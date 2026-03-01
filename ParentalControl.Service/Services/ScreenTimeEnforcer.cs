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

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WTSSendMessage(
        IntPtr hServer, int sessionId,
        string pTitle, int titleLength,
        string pMessage, int messageLength,
        uint style, int timeout, out int pResponse, bool bWait);

    private readonly ActivityLogger _logger;
    private DateTime _lastTickTime = DateTime.UtcNow;
    private double _pendingMinutes = 0;

    private bool _lockedDueToTimeWindow = false;
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
                settings.TodayBonusMinutes = 0;
                _pendingMinutes = 0;
                _lockedDueToTimeWindow = false;
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

            // Skip enforcement if Screen Time is disabled from the dashboard
            if (!settings.ScreenTimeEnabled) return;

            // Enforce limits only if enabled for today
            var limit = db.ScreenTimeLimits.FirstOrDefault(l => l.DayOfWeek == now.DayOfWeek);
            if (limit == null || !limit.IsEnabled) return;

            var currentTime = TimeOnly.FromDateTime(now);

            // --- Enforce allowed time window ---
            if (currentTime < limit.AllowedFrom || currentTime > limit.AllowedUntil)
            {
                // Guard flag prevents re-locking every tick — lock fires once per time-window event
                if (IsUserLoggedIn() && !_lockedDueToTimeWindow)
                {
                    _lockedDueToTimeWindow = true;
                    SendWarning(
                        $"You are outside the allowed hours " +
                        $"({limit.AllowedFrom.ToString("HH:mm")}–{limit.AllowedUntil.ToString("HH:mm")}). " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _logger.Log(ActivityType.ScreenLocked,
                        $"Outside allowed hours ({limit.AllowedFrom}-{limit.AllowedUntil})");
#if DEBUG
                    DebugStopIfFlagSet();
#endif
                }
                return; // don't evaluate daily limit while outside allowed hours
            }

            // Back within allowed hours — reset time-window lock flag
            _lockedDueToTimeWindow = false;

            // --- Enforce daily limit ---
            if (limit.DailyLimitMinutes > 0 &&
                settings.TodayUsedMinutes >= limit.DailyLimitMinutes + settings.TodayBonusMinutes)
            {
                // Guard flag prevents re-locking every tick — lock fires once per day's limit event
                if (IsUserLoggedIn() && !_lockedDueToDailyLimit)
                {
                    _lockedDueToDailyLimit = true;
                    SendWarning(
                        $"Daily screen time limit of {limit.DailyLimitMinutes} minutes has been reached. " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _logger.Log(ActivityType.ScreenTimeLimitReached,
                        $"Daily limit of {limit.DailyLimitMinutes} min reached");
#if DEBUG
                    DebugStopIfFlagSet();
#endif
                }
            }
            else
            {
                _lockedDueToDailyLimit = false;
            }
        }
        catch { }
    }

    private static void SendWarning(string message)
    {
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId == -1) return;

        const string title = "Screen Time Warning";
        WTSSendMessage(IntPtr.Zero, sessionId,
            title, title.Length * 2,
            message, message.Length * 2,
            0x00000030u /* MB_OK | MB_ICONWARNING */, 0, out _, false);
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

#if DEBUG
    // Exits the service process immediately after a lock event when the developer has
    // enabled the "stop service after lock" safety flag in Settings. This prevents a
    // testing mishap from locking the developer out of their own machine.
    private static void DebugStopIfFlagSet()
    {
        try
        {
            using var db = new AppDbContext();
            if (db.Settings.FirstOrDefault()?.DebugStopServiceAfterLock == true)
                Environment.Exit(0);
        }
        catch { }
    }
#endif

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
