using System.Runtime.InteropServices;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Services;

namespace ParentalControl.Service.Services;

public class ScreenTimeEnforcer
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
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

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string lpszUsername, string lpszDomain,
        string lpszPassword, int dwLogonType, int dwLogonProvider, out IntPtr phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private readonly ActivityLogger _logger;
    private readonly NotificationService _notifier;
    private DateTime _lastTickTime  = DateTime.UtcNow;
    private DateTime _nextDebugDump = DateTime.MinValue;
    private double _pendingMinutes  = 0;

    private bool _lockedDueToTimeWindow  = false;
    private bool _lockedDueToDailyLimit  = false;
    private bool _lockStateRestored      = false;   // true after first tick
    private bool _activeAccountHasPassword = false; // auto-detected; cached per user
    private bool _lastSessionWasActive   = false;   // tracks session active→inactive transition

    private int    _activeProfileId = 0;
    private string _activeUsername  = string.Empty;

    private static readonly string _debugLog =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     "ParentalControl", "debug.log");

    private static void WriteDebug(string msg)
    {
        try
        {
            File.AppendAllText(_debugLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

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
            if (settings == null) { WriteDebug("EXIT: settings row missing"); return; }

            bool debug = settings.DebugStopServiceAfterLock;

            // Resolve active profile
            var activeUsername = GetActiveWindowsUsername();
            if (!string.Equals(activeUsername, _activeUsername, StringComparison.OrdinalIgnoreCase))
            {
                _activeUsername          = activeUsername;
                _activeProfileId         = ResolveProfileId(activeUsername, db);
                _pendingMinutes          = 0;
                _lockedDueToTimeWindow   = false;
                _lockedDueToDailyLimit   = false;
                _activeAccountHasPassword = AccountHasPassword(activeUsername);
                _lastSessionWasActive    = false;
                if (debug) WriteDebug($"User changed → '{activeUsername}', profileId={_activeProfileId}, hasPassword={_activeAccountHasPassword}");
            }
            if (_activeProfileId == 0)
                _activeProfileId = ResolveProfileId(activeUsername, db);

            // Detect password on first tick
            if (!_lockStateRestored && string.IsNullOrEmpty(_activeUsername) == false)
                _activeAccountHasPassword = AccountHasPassword(activeUsername);

            // Periodic debug dump (once per minute when debug flag is on)
            if (debug && DateTime.UtcNow >= _nextDebugDump)
            {
                _nextDebugDump = DateTime.UtcNow.AddMinutes(1);
                WriteDebug($"TICK user='{activeUsername}' profileId={_activeProfileId} " +
                           $"ScreenTimeEnabled={settings.ScreenTimeEnabled} hasPassword={_activeAccountHasPassword}");
            }

            var profile = db.UserProfiles.Find(_activeProfileId);
            if (profile == null || !profile.IsEnabled)
            {
                if (debug) WriteDebug($"EXIT: profile null or disabled (id={_activeProfileId})");
                return;
            }

            // Reset daily counter if date changed
            if (profile.UsageDate.Date != DateTime.Now.Date)
            {
                profile.UsageDate           = DateTime.Now.Date;
                profile.TodayUsedMinutes    = 0;
                profile.TodayBonusMinutes   = 0;
                profile.IsScreenTimeLocked  = false;
                _pendingMinutes            = 0;
                _lockedDueToTimeWindow     = false;
                _lockedDueToDailyLimit     = false;
            }

            // ONE-TIME startup restore: if the service restarted while the screen was locked,
            // pre-arm the in-memory lock flags. IsUserActive() prevents double-locking on
            // an already-locked screen; session-transition detection below handles re-locking
            // when the user logs back in (no-password accounts only).
            if (!_lockStateRestored)
            {
                _lockStateRestored = true;
                if (profile.IsScreenTimeLocked)
                {
                    _lockedDueToTimeWindow = true;
                    _lockedDueToDailyLimit = true;
                }
            }

            // For no-password accounts: when the session transitions from inactive (locked)
            // to active (user logged back in), clear the lock flags so the enforcement checks
            // below will immediately re-lock.
            bool sessionActive = IsUserActive();
            bool shouldRelock  = !_activeAccountHasPassword || profile.AlwaysRelock;
            if (shouldRelock
                && (_lockedDueToTimeWindow || _lockedDueToDailyLimit)
                && !_lastSessionWasActive && sessionActive)
            {
                if (debug) WriteDebug($"SESSION TRANSITION: account logged back in — re-arming lock checks (hasPassword={_activeAccountHasPassword}, alwaysRelock={profile.AlwaysRelock})");
                _lockedDueToTimeWindow = false;
                _lockedDueToDailyLimit = false;
            }
            _lastSessionWasActive = sessionActive;

            // Accumulate fractional minutes
            if (sessionActive)
                _pendingMinutes += elapsed;

            var wholeMinutes = (int)_pendingMinutes;
            if (wholeMinutes > 0)
            {
                _pendingMinutes -= wholeMinutes;
                profile.TodayUsedMinutes += wholeMinutes;
            }

            db.SaveChanges();

            if (!settings.ScreenTimeEnabled)
            {
                if (debug) WriteDebug("EXIT: ScreenTimeEnabled=false");
                return;
            }

            var limit = db.ScreenTimeLimits.FirstOrDefault(
                l => l.DayOfWeek == now.DayOfWeek && l.UserProfileId == _activeProfileId);
            if (limit == null || !limit.IsEnabled)
            {
                if (debug) WriteDebug($"EXIT: limit null or disabled for day={now.DayOfWeek} profileId={_activeProfileId}");
                return;
            }

            var currentTime = TimeOnly.FromDateTime(now);

            if (debug) WriteDebug($"TIME CHECK: current={currentTime} allowedFrom={limit.AllowedFrom} allowedUntil={limit.AllowedUntil} " +
                                   $"outside={currentTime < limit.AllowedFrom || currentTime > limit.AllowedUntil} " +
                                   $"sessionActive={sessionActive} alreadyLocked={_lockedDueToTimeWindow}");

            // --- Enforce allowed time window ---
            if (currentTime < limit.AllowedFrom || currentTime > limit.AllowedUntil)
            {
                if (sessionActive && !_lockedDueToTimeWindow)
                {
                    _lockedDueToTimeWindow     = true;
                    profile.IsScreenTimeLocked = true;
                    db.SaveChanges();
                    if (debug) WriteDebug("LOCKING: outside allowed time window");
                    if (settings.DebugStopServiceAfterLock) TriggeredDebugStop = true;
                    SendWarning(
                        $"You are outside the allowed hours " +
                        $"({limit.AllowedFrom.ToString("HH:mm")}–{limit.AllowedUntil.ToString("HH:mm")}). " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _lastSessionWasActive = false;   // screen is now locked
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
                if (sessionActive && !_lockedDueToDailyLimit)
                {
                    _lockedDueToDailyLimit     = true;
                    profile.IsScreenTimeLocked = true;
                    db.SaveChanges();
                    if (debug) WriteDebug($"LOCKING: daily limit {limit.DailyLimitMinutes} min reached");
                    if (settings.DebugStopServiceAfterLock) TriggeredDebugStop = true;
                    SendWarning(
                        $"Daily screen time limit of {limit.DailyLimitMinutes} minutes has been reached. " +
                        $"The screen will lock in 30 seconds.");
                    Thread.Sleep(30000);
                    SessionLock.LockActive();
                    _lastSessionWasActive = false;   // screen is now locked
                    _logger.Log(ActivityType.ScreenTimeLimitReached,
                        $"Daily limit of {limit.DailyLimitMinutes} min reached");
                    _notifier.SendScreenLockNotification(
                        $"Daily screen time limit of {limit.DailyLimitMinutes} minutes reached.");
                }
            }
            else
            {
                _lockedDueToDailyLimit = false;
                if (profile.IsScreenTimeLocked)
                {
                    profile.IsScreenTimeLocked = false;
                    db.SaveChanges();
                }
            }
        }
        catch (Exception ex)
        {
            WriteDebug($"EXCEPTION in Tick: {ex.GetType().Name}: {ex.Message}");
        }
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
                p.WindowsUsername.ToLower() == username.ToLower() && p.IsEnabled);
            return profile?.Id ?? 0;
        }
        catch { return 0; }
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

    private static bool AccountHasPassword(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        try
        {
            // LOGON32_LOGON_NETWORK (3), LOGON32_PROVIDER_DEFAULT (0)
            // If we can log on with an empty password the account has none.
            bool noPassword = LogonUser(username, Environment.MachineName, "",
                                        3, 0, out var token);
            if (noPassword) CloseHandle(token);
            return !noPassword;   // true → account requires a password
        }
        catch { return false; }
    }
}
