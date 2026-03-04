using System.Diagnostics;
using System.Runtime.InteropServices;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Services;

namespace ParentalControl.Service.Services;

public class ProcessMonitor
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool WTSSendMessage(
        IntPtr hServer, int sessionId,
        string pTitle, int titleLength,
        string pMessage, int messageLength,
        uint style, int timeout, out int pResponse, bool bWait);

    private readonly ActivityLogger _logger;
    private readonly NotificationService _notifier;
    private readonly Lock _rulesLock = new();

    // Enforcement sets (process names without .exe)
    private readonly HashSet<string> _blockedProcesses         = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _screenTimeOnlyProcesses  = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _focusRestrictedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private List<FocusSchedule> _focusSchedules = new();

    // Profile tracking
    private int    _activeProfileId = 0;
    private string _activeUsername  = string.Empty;

    // Shared App Time pool
    private double   _appTimePendingSeconds = 0;
    private DateTime _lastTickTime          = DateTime.UtcNow;
    private DateTime _lastDbFlush           = DateTime.UtcNow;
    private DateOnly _lastResetDate         = DateOnly.FromDateTime(DateTime.Today);

    // Warning state: maps process name → when the 5-min warning was issued
    private readonly Dictionary<string, DateTime> _warningIssuedAt = new(StringComparer.OrdinalIgnoreCase);

    // Processes running before the limit was hit (eligible for grace period)
    private readonly HashSet<string> _ranBeforeStop = new(StringComparer.OrdinalIgnoreCase);

    public ProcessMonitor(ActivityLogger logger, NotificationService notifier)
    {
        _logger   = logger;
        _notifier = notifier;
    }

    // Called externally (IPC ReloadRules) — reloads for the currently active profile
    public void LoadRules()
    {
        var username  = ScreenTimeEnforcer.GetActiveWindowsUsername();
        var profileId = ScreenTimeEnforcer.ResolveProfileId(username);
        _activeUsername  = username;
        _activeProfileId = profileId;
        LoadRulesForProfile(profileId);
    }

    private void LoadRulesForProfile(int profileId)
    {
        lock (_rulesLock)
        {
            _blockedProcesses.Clear();
            _screenTimeOnlyProcesses.Clear();
            _focusRestrictedProcesses.Clear();

            try
            {
                using var db = new AppDbContext();
                var rules = db.AppRules
                    .Where(r => r.UserProfileId == profileId)
                    .Select(r => new { r.ProcessName, r.AccessMode, r.AllowedInFocusMode })
                    .ToList();

                foreach (var r in rules)
                {
                    var name = StripExe(r.ProcessName);
                    if (r.AccessMode == (int)AppAccessMode.Blocked)
                        _blockedProcesses.Add(name);
                    else if (r.AccessMode == (int)AppAccessMode.ScreenTimeOnly)
                        _screenTimeOnlyProcesses.Add(name);

                    if (!r.AllowedInFocusMode)
                        _focusRestrictedProcesses.Add(name);
                }

                _focusSchedules = db.FocusSchedules
                    .Where(s => s.UserProfileId == profileId)
                    .ToList();
            }
            catch { }
        }
    }

    public void EnforceRules(bool screenTimeLocked)
    {
        // Always advance the clock first so elapsed is never inflated by early returns
        var now     = DateTime.UtcNow;
        var elapsed = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null && !settings.AppControlEnabled) return;
        }
        catch { }

        // Resolve active profile; reload rules if the Windows user changed
        var activeUsername = ScreenTimeEnforcer.GetActiveWindowsUsername();
        if (!string.Equals(activeUsername, _activeUsername, StringComparison.OrdinalIgnoreCase))
        {
            _activeUsername = activeUsername;
            int newProfileId = ScreenTimeEnforcer.ResolveProfileId(activeUsername);
            if (newProfileId != _activeProfileId)
            {
                _activeProfileId       = newProfileId;
                _appTimePendingSeconds = 0;
                _warningIssuedAt.Clear();
                _ranBeforeStop.Clear();
                LoadRulesForProfile(_activeProfileId);
            }
        }
        if (_activeProfileId == 0)
        {
            _activeProfileId = ScreenTimeEnforcer.ResolveProfileId(activeUsername);
            LoadRulesForProfile(_activeProfileId);
        }

        // Midnight reset
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today != _lastResetDate)
        {
            _lastResetDate         = today;
            _appTimePendingSeconds = 0;
            _warningIssuedAt.Clear();
            _ranBeforeStop.Clear();
            try
            {
                using var db = new AppDbContext();
                var profile = db.UserProfiles.Find(_activeProfileId);
                if (profile != null)
                {
                    profile.TodayAppTimeUsedMinutes  = 0;
                    profile.TodayAppTimeBonusMinutes = 0;
                    db.SaveChanges();
                }
            }
            catch { }
        }

        // Read App Time limit and usage from active UserProfile
        int limitMinutes = 0, usedMinutes = 0, bonusMinutes = 0;
        bool debugOverride = false, focusModeEnabled = false;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            var profile  = db.UserProfiles.Find(_activeProfileId);
            if (profile != null)
            {
                var todaySchedule = db.AppTimeSchedules.FirstOrDefault(
                    s => s.DayOfWeek == DateTime.Now.DayOfWeek && s.UserProfileId == _activeProfileId);
                limitMinutes     = (todaySchedule?.IsEnabled == true) ? todaySchedule.DailyLimitMinutes : 0;
                usedMinutes      = profile.TodayAppTimeUsedMinutes;
                bonusMinutes     = profile.TodayAppTimeBonusMinutes;
                focusModeEnabled = profile.FocusModeEnabled;
            }
            if (settings?.DebugAppTimeOverride == true && limitMinutes != 0)
            {
                limitMinutes  = 6;
                debugOverride = true;
            }
        }
        catch { }

        bool appTimeLimitActive   = limitMinutes > 0;
        int  effectiveUsedMinutes = usedMinutes + (int)(_appTimePendingSeconds / 60.0);
        bool appTimeLimitExceeded = appTimeLimitActive &&
            (effectiveUsedMinutes >= limitMinutes + bonusMinutes);

        bool focusModeActive = focusModeEnabled && IsFocusModeActive();

        HashSet<string> blocked, screenTimeOnly, focusRestricted;
        lock (_rulesLock)
        {
            blocked         = new HashSet<string>(_blockedProcesses,         StringComparer.OrdinalIgnoreCase);
            screenTimeOnly  = new HashSet<string>(_screenTimeOnlyProcesses,  StringComparer.OrdinalIgnoreCase);
            focusRestricted = new HashSet<string>(_focusRestrictedProcesses, StringComparer.OrdinalIgnoreCase);
        }

        bool anyScreenTimeOnlyRunning = false;

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var pName = process.ProcessName;

                    if (blocked.Contains(pName))
                    {
                        process.Kill(entireProcessTree: true);
                        _logger.Log(ActivityType.AppBlocked, pName);
                        _notifier.SendAppBlockNotification(pName, "App is on the blocked list.");
                    }
                    else if (screenTimeOnly.Contains(pName))
                    {
                        anyScreenTimeOnlyRunning = true;

                        bool shouldStop = screenTimeLocked || appTimeLimitExceeded;

                        if (shouldStop)
                        {
                            if (_warningIssuedAt.TryGetValue(pName, out var warnedAt))
                            {
                                double gracePeriodMinutes = debugOverride ? (30.0 / 60.0) : 5.0;
                                if ((now - warnedAt).TotalMinutes >= gracePeriodMinutes)
                                {
                                    process.Kill(entireProcessTree: true);
                                    _logger.Log(ActivityType.AppBlocked, pName);
                                    _notifier.SendAppBlockNotification(pName, "App time limit reached — grace period expired.");
                                    _warningIssuedAt.Remove(pName);
                                    _ranBeforeStop.Remove(pName);
                                }
                            }
                            else if (_ranBeforeStop.Contains(pName))
                            {
                                SendWarning(pName, debugOverride);
                                _warningIssuedAt[pName] = now;
                            }
                            else
                            {
                                process.Kill(entireProcessTree: true);
                                _logger.Log(ActivityType.AppBlocked, pName);
                                _notifier.SendAppBlockNotification(pName, "App time limit already reached.");
                            }
                        }
                        else
                        {
                            _ranBeforeStop.Add(pName);
                            _warningIssuedAt.Remove(pName);
                        }
                    }
                    else if (focusModeActive && focusRestricted.Contains(pName))
                    {
                        process.Kill(entireProcessTree: true);
                        _logger.Log(ActivityType.AppBlocked, pName);
                        _notifier.SendAppBlockNotification(pName, "Focus mode is active.");
                    }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch { }

        if (anyScreenTimeOnlyRunning && !appTimeLimitExceeded)
            _appTimePendingSeconds += elapsed;

        bool flushNow = (now - _lastDbFlush).TotalSeconds >= 60 ||
                        (appTimeLimitExceeded && _appTimePendingSeconds > 0);
        if (flushNow)
        {
            _lastDbFlush = now;
            FlushAppTime();
        }
    }

    // -------------------------------------------------------------------------

    private bool IsFocusModeActive()
    {
        try
        {
            var now       = TimeOnly.FromDateTime(DateTime.Now);
            var dayOfWeek = DateTime.Now.DayOfWeek;
            List<FocusSchedule> schedules;
            lock (_rulesLock)
                schedules = _focusSchedules.ToList();

            var schedule = schedules.FirstOrDefault(s => s.DayOfWeek == dayOfWeek);
            if (schedule == null || !schedule.IsEnabled) return false;
            return now >= schedule.FocusFrom && now <= schedule.FocusUntil;
        }
        catch { return false; }
    }

    private void FlushAppTime()
    {
        var wholeMinutes = (int)(_appTimePendingSeconds / 60.0);
        if (wholeMinutes <= 0) return;

        _appTimePendingSeconds -= wholeMinutes * 60.0;

        try
        {
            using var db = new AppDbContext();
            var profile = db.UserProfiles.Find(_activeProfileId);
            if (profile == null) return;
            profile.TodayAppTimeUsedMinutes += wholeMinutes;
            db.SaveChanges();
        }
        catch { }
    }

    private void SendWarning(string processName, bool debugOverride)
    {
        string timeStr  = debugOverride ? "30 seconds" : "5 minutes";
        string message  = $"'{processName}' will close in {timeStr} — app time limit reached.";
        const string title = "App Time Warning";

        try
        {
            var sessionId = (int)WTSGetActiveConsoleSessionId();
            if (sessionId == -1) return;
            WTSSendMessage(IntPtr.Zero, sessionId,
                title,   title.Length   * 2,
                message, message.Length * 2,
                0x00000030u /* MB_OK | MB_ICONWARNING */, 0, out _, false);
        }
        catch { }

        _logger.Log(ActivityType.AppBlocked, $"Warning sent for '{processName}' ({timeStr} grace)");
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
