using System.Runtime.InteropServices;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Service.Services;

public class ScreenTimeEnforcer
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll")]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer, int sessionId, int wtsInfoClass,
        out IntPtr ppBuffer, out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);

    private readonly ActivityLogger _logger;
    private DateTime _lastTickTime = DateTime.UtcNow;
    private double _pendingMinutes = 0;

    // Time-window violation state (outside AllowedFrom–AllowedUntil)
    private DateTime? _timeWindowViolationAt = null;
    private bool _lockedDueToTimeWindow = false;

    // Daily-limit violation state (TodayUsedMinutes >= DailyLimitMinutes)
    private DateTime? _dailyLimitViolationAt = null;
    private bool _lockedDueToDailyLimit = false;

    public ScreenTimeEnforcer(ActivityLogger logger)
    {
        _logger = logger;
    }

    public void LoadRules()
    {
        // Rules are read fresh from DB each tick, no caching needed
    }

    public void Tick()
    {
        var now = DateTime.Now;

        // Always advance the clock so elapsed doesn't balloon after a gap
        var elapsed = (DateTime.UtcNow - _lastTickTime).TotalMinutes;
        _lastTickTime = DateTime.UtcNow;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            // Reset daily counter if date changed
            if (settings.UsageDate.Date != DateTime.Now.Date)
            {
                settings.UsageDate = DateTime.Now.Date;
                settings.TodayUsedMinutes = 0;
                _pendingMinutes = 0;
                _timeWindowViolationAt = null;
                _lockedDueToTimeWindow = false;
                _dailyLimitViolationAt = null;
                _lockedDueToDailyLimit = false;
            }

            // Accumulate fractional minutes; ticks are 5s so elapsed is ~0.083 —
            // casting directly to int would always truncate to 0.
            if (IsUserActive())
                _pendingMinutes += elapsed;

            var wholeMinutes = (int)_pendingMinutes;
            if (wholeMinutes > 0)
            {
                _pendingMinutes -= wholeMinutes;
                settings.TodayUsedMinutes += wholeMinutes;
            }

            db.SaveChanges();

            // Enforce limits only if enabled for today
            var limit = db.ScreenTimeLimits.FirstOrDefault(l => l.DayOfWeek == now.DayOfWeek);
            if (limit == null || !limit.IsEnabled) return;

            var currentTime = TimeOnly.FromDateTime(now);

            bool childHasPassword = settings.ChildAccountHasPassword;
            var lockDelay = TimeSpan.FromSeconds(
                Math.Clamp(settings.LockDelaySeconds, 20, 180));

            // --- Enforce allowed time window ---
            if (currentTime < limit.AllowedFrom || currentTime > limit.AllowedUntil)
            {
                _timeWindowViolationAt ??= now;

                if (now - _timeWindowViolationAt >= lockDelay && IsUserLoggedIn())
                {
                    if (childHasPassword)
                    {
                        if (!_lockedDueToTimeWindow)
                        {
                            _lockedDueToTimeWindow = true;
                            SessionLock.LockActive();
                            _logger.Log(ActivityType.ScreenLocked,
                                $"Outside allowed hours ({limit.AllowedFrom}-{limit.AllowedUntil})");
                        }
                    }
                    else
                    {
                        SessionLock.LockActive();
                    }
                }
                return; // don't evaluate daily limit while outside allowed hours
            }

            // Back within allowed hours — reset time-window state only
            _timeWindowViolationAt = null;
            _lockedDueToTimeWindow = false;

            // --- Enforce daily limit ---
            // Uses its own independent timer so the time-window reset above can't clobber it.
            if (limit.DailyLimitMinutes > 0 &&
                settings.TodayUsedMinutes >= limit.DailyLimitMinutes)
            {
                _dailyLimitViolationAt ??= now;

                if (now - _dailyLimitViolationAt >= lockDelay && IsUserLoggedIn())
                {
                    if (childHasPassword)
                    {
                        if (!_lockedDueToDailyLimit)
                        {
                            _lockedDueToDailyLimit = true;
                            SessionLock.LockActive();
                            _logger.Log(ActivityType.ScreenTimeLimitReached,
                                $"Daily limit of {limit.DailyLimitMinutes} min reached");
                        }
                    }
                    else
                    {
                        SessionLock.LockActive();
                    }
                }
            }
            else
            {
                _dailyLimitViolationAt = null;
                _lockedDueToDailyLimit = false;
            }
        }
        catch { }
    }

    private static bool IsUserActive()
    {
        // Use the console session (the interactive desktop), not the service's session 0
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId == -1) return false; // 0xFFFFFFFF = no active session

        if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 8 /* WTSConnectState */,
            out var pBuffer, out _))
        {
            int state = Marshal.ReadInt32(pBuffer);
            WTSFreeMemory(pBuffer);
            return state == 0; // WTSActive
        }
        return true; // assume active if query fails — better to over-count than miss time
    }

    // Distinct from IsUserActive: at the Windows login screen the session state is still
    // WTSActive (Winlogon owns it), so we check for a non-empty username to confirm a real
    // user account is actually logged in before attempting to disconnect.
    private static bool IsUserLoggedIn()
    {
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId == -1) return false;

        if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 5 /* WTSUserName */,
            out var pBuffer, out _))
        {
            var username = Marshal.PtrToStringUni(pBuffer) ?? string.Empty;
            WTSFreeMemory(pBuffer);
            return !string.IsNullOrWhiteSpace(username);
        }
        return true; // assume user is logged in if query fails — better to try locking than miss
    }
}
