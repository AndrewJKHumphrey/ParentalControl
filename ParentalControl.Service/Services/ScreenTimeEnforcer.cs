using System.Runtime.InteropServices;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Services;

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
    private readonly NotificationService _notifier;
    private DateTime _lastTickTime = DateTime.UtcNow;
    private double _pendingMinutes = 0;

    private bool _lockedDueToTimeWindow = false;
    private bool _lockedDueToDailyLimit = false;

    private int    _activeProfileId = 0;
    private string _activeUsername  = string.Empty;

    public bool IsScreenTimeLocked  => _lockedDueToTimeWindow || _lockedDueToDailyLimit;
    public bool TriggeredDebugStop  { get; private set; }

    public ScreenTimeEnforcer(ActivityLogger logger, NotificationService notifier)
    {
        _logger   = logger;
        _notifier = notifier;
    }

    public void LoadRules()
    {
        // Rules are read fresh from DB each tick; also refresh profile on explicit reload
        var username  = GetActiveWindowsUsername();
        _activeUsername  = username;
        _activeProfileId = ResolveProfileId(username);
    }

    public void Tick()
    {
        var now = DateTime.Now;

        var elapsed = (DateTime.UtcNow - _lastTickTime).TotalMinutes;
        _lastTickTime = DateTime.UtcNow;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            // Resolve active profile
            var activeUsername = GetActiveWindowsUsername();
            if (!string.Equals(activeUsername, _activeUsername, StringComparison.OrdinalIgnoreCase))
            {
                _activeUsername  = activeUsername;
                _activeProfileId = ResolveProfileId(activeUsername, db);
                _pendingMinutes  = 0;
                _lockedDueToTimeWindow = false;
                _lockedDueToDailyLimit = false;
            }
            if (_activeProfileId == 0)
                _activeProfileId = ResolveProfileId(activeUsername, db);

            var profile = db.UserProfiles.Find(_activeProfileId);
            if (profile == null || !profile.IsEnabled) return;

            // Reset daily counter if date changed
            if (profile.UsageDate.Date != DateTime.Now.Date)
            {
                profile.UsageDate       = DateTime.Now.Date;
                profile.TodayUsedMinutes  = 0;
                profile.TodayBonusMinutes = 0;
                _pendingMinutes = 0;
                _lockedDueToTimeWindow = false;
                _lockedDueToDailyLimit = false;
            }

            // Accumulate fractional minutes
            if (IsUserActive())
                _pendingMinutes += elapsed;

            var wholeMinutes = (int)_pendingMinutes;
            if (wholeMinutes > 0)
            {
                _pendingMinutes -= wholeMinutes;
                profile.TodayUsedMinutes += wholeMinutes;
            }

            db.SaveChanges();

            if (!settings.ScreenTimeEnabled) return;

            var limit = db.ScreenTimeLimits.FirstOrDefault(
                l => l.DayOfWeek == now.DayOfWeek && l.UserProfileId == _activeProfileId);
            if (limit == null || !limit.IsEnabled) return;

            var currentTime = TimeOnly.FromDateTime(now);

            // --- Enforce allowed time window ---
            if (currentTime < limit.AllowedFrom || currentTime > limit.AllowedUntil)
            {
                if (IsUserLoggedIn() && !_lockedDueToTimeWindow)
                {
                    _lockedDueToTimeWindow = true;
                    if (settings.DebugStopServiceAfterLock) TriggeredDebugStop = true;
                    SendWarning(
                        $"You are outside the allowed hours " +
                        $"({limit.AllowedFrom.ToString("HH:mm")}–{limit.AllowedUntil.ToString("HH:mm")}). " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _logger.Log(ActivityType.ScreenLocked,
                        $"Outside allowed hours ({limit.AllowedFrom}-{limit.AllowedUntil})");
                    _notifier.SendScreenLockNotification(
                        $"Outside allowed hours ({limit.AllowedFrom:HH\\:mm}–{limit.AllowedUntil:HH\\:mm}).");
                }
                return;
            }

            _lockedDueToTimeWindow = false;

            // --- Enforce daily limit ---
            if (limit.DailyLimitMinutes > 0 &&
                profile.TodayUsedMinutes >= limit.DailyLimitMinutes + profile.TodayBonusMinutes)
            {
                if (IsUserLoggedIn() && !_lockedDueToDailyLimit)
                {
                    _lockedDueToDailyLimit = true;
                    if (settings.DebugStopServiceAfterLock) TriggeredDebugStop = true;
                    SendWarning(
                        $"Daily screen time limit of {limit.DailyLimitMinutes} minutes has been reached. " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _logger.Log(ActivityType.ScreenTimeLimitReached,
                        $"Daily limit of {limit.DailyLimitMinutes} min reached");
                    _notifier.SendScreenLockNotification(
                        $"Daily screen time limit of {limit.DailyLimitMinutes} minutes reached.");
                }
            }
            else
            {
                _lockedDueToDailyLimit = false;
            }
        }
        catch { }
    }

    internal static string GetActiveWindowsUsername()
    {
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId == -1) return string.Empty;

        if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 5 /* WTSUserName */,
            out var pBuffer, out _))
        {
            var username = Marshal.PtrToStringUni(pBuffer) ?? string.Empty;
            WTSFreeMemory(pBuffer);
            return username.Trim();
        }
        return string.Empty;
    }

    internal static int ResolveProfileId(string username, AppDbContext? db = null)
    {
        bool owned = db == null;
        db ??= new AppDbContext();
        try
        {
            var profile = db.UserProfiles.FirstOrDefault(p =>
                p.WindowsUsername.ToLower() == username.ToLower() && p.IsEnabled)
                ?? db.UserProfiles.FirstOrDefault(p => p.WindowsUsername == "" && p.IsEnabled);
            return profile?.Id ?? 1;
        }
        catch { return 1; }
        finally { if (owned) db.Dispose(); }
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
        var sessionId = (int)WTSGetActiveConsoleSessionId();
        if (sessionId == -1) return false;

        if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, 8 /* WTSConnectState */,
            out var pBuffer, out _))
        {
            int state = Marshal.ReadInt32(pBuffer);
            WTSFreeMemory(pBuffer);
            return state == 0; // WTSActive
        }
        return true;
    }

    private static bool IsUserLoggedIn()
        => !string.IsNullOrWhiteSpace(GetActiveWindowsUsername());
}
