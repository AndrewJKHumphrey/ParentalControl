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
    private readonly HashSet<string> _blockedProcesses        = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _screenTimeOnlyProcesses = new(StringComparer.OrdinalIgnoreCase);

    // Shared App Time pool — tracks seconds any ScreenTimeOnly app was running this session
    private double   _appTimePendingSeconds = 0;
    private DateTime _lastTickTime          = DateTime.UtcNow;
    private DateTime _lastDbFlush           = DateTime.UtcNow;
    private DateOnly _lastResetDate         = DateOnly.FromDateTime(DateTime.Today);

    // Warning state: maps process name → when the 5-min warning was issued
    private readonly Dictionary<string, DateTime> _warningIssuedAt = new(StringComparer.OrdinalIgnoreCase);

    // Processes observed running while enforcement was not active (i.e. before the limit was hit)
    // Used to distinguish "was playing when time ran out" from "opened after time already ran out"
    private readonly HashSet<string> _ranBeforeStop = new(StringComparer.OrdinalIgnoreCase);

    public ProcessMonitor(ActivityLogger logger, NotificationService notifier)
    {
        _logger   = logger;
        _notifier = notifier;
        LoadRules();
    }

    public void LoadRules()
    {
        lock (_rulesLock)
        {
            _blockedProcesses.Clear();
            _screenTimeOnlyProcesses.Clear();

            try
            {
                using var db = new AppDbContext();
                var rules = db.AppRules.Select(r => new { r.ProcessName, r.AccessMode }).ToList();

                foreach (var r in rules)
                {
                    var name = StripExe(r.ProcessName);
                    if (r.AccessMode == (int)AppAccessMode.Blocked)
                        _blockedProcesses.Add(name);
                    else if (r.AccessMode == (int)AppAccessMode.ScreenTimeOnly)
                        _screenTimeOnlyProcesses.Add(name);
                }
            }
            catch { }
        }
    }

    public void EnforceRules(bool screenTimeLocked)
    {
        // Check master app-control switch
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null && !settings.AppControlEnabled) return;
        }
        catch { }

        // Midnight reset
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today != _lastResetDate)
        {
            _lastResetDate = today;
            _appTimePendingSeconds = 0;
            _warningIssuedAt.Clear();
            _ranBeforeStop.Clear();
            try
            {
                using var db = new AppDbContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings != null)
                {
                    settings.TodayAppTimeUsedMinutes  = 0;
                    settings.TodayAppTimeBonusMinutes = 0;
                    db.SaveChanges();
                }
            }
            catch { }
        }

        // Elapsed time since last tick
        var now     = DateTime.UtcNow;
        var elapsed = (now - _lastTickTime).TotalSeconds;
        _lastTickTime = now;

        // Read App Time limit and current usage once per tick
        int limitMinutes = 0, usedMinutes = 0, bonusMinutes = 0;
        bool debugOverride = false;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                limitMinutes = settings.AppTimeLimitMinutes;
                usedMinutes  = settings.TodayAppTimeUsedMinutes;
                bonusMinutes = settings.TodayAppTimeBonusMinutes;
                if (settings.DebugAppTimeOverride && limitMinutes != 0)
                {
                    limitMinutes = 6;
                    debugOverride = true;
                }
            }
        }
        catch { }

        bool appTimeLimitActive = limitMinutes > 0;
        // Use effective minutes (DB value + in-memory pending) so enforcement fires
        // as soon as the limit is hit, not up to 60 s later when the DB is next flushed.
        int effectiveUsedMinutes = usedMinutes + (int)(_appTimePendingSeconds / 60.0);
        bool appTimeLimitExceeded = appTimeLimitActive &&
            (effectiveUsedMinutes >= limitMinutes + bonusMinutes);

        // Take local copies of rule sets
        HashSet<string> blocked, screenTimeOnly;
        lock (_rulesLock)
        {
            blocked       = new HashSet<string>(_blockedProcesses,        StringComparer.OrdinalIgnoreCase);
            screenTimeOnly = new HashSet<string>(_screenTimeOnlyProcesses, StringComparer.OrdinalIgnoreCase);
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
                        // Always-blocked: kill immediately
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
                                // Was running when limit hit — check if grace period has elapsed
                                double gracePeriodMinutes = debugOverride ? (30.0 / 60.0) : 5.0;
                                if ((now - warnedAt).TotalMinutes >= gracePeriodMinutes)
                                {
                                    process.Kill(entireProcessTree: true);
                                    _logger.Log(ActivityType.AppBlocked, pName);
                                    _notifier.SendAppBlockNotification(pName, "App time limit reached — grace period expired.");
                                    _warningIssuedAt.Remove(pName);
                                    _ranBeforeStop.Remove(pName);
                                }
                                // else: inside grace window — leave running
                            }
                            else if (_ranBeforeStop.Contains(pName))
                            {
                                // Was running before limit hit but not yet warned — send warning
                                SendWarning(pName, debugOverride);
                                _warningIssuedAt[pName] = now;
                            }
                            else
                            {
                                // Opened after limit was already exceeded — kill immediately, no warning
                                process.Kill(entireProcessTree: true);
                                _logger.Log(ActivityType.AppBlocked, pName);
                                _notifier.SendAppBlockNotification(pName, "App time limit already reached.");
                            }
                        }
                        else
                        {
                            // Conditions clear — track as running, cancel any pending warning
                            _ranBeforeStop.Add(pName);
                            _warningIssuedAt.Remove(pName);
                        }
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

        // Accumulate shared App Time pool if any ScreenTimeOnly app was running
        if (anyScreenTimeOnlyRunning && !appTimeLimitExceeded)
            _appTimePendingSeconds += elapsed;

        // Flush App Time usage to DB every ~60 seconds, or immediately when limit is hit
        bool flushNow = (now - _lastDbFlush).TotalSeconds >= 60 ||
                        (appTimeLimitExceeded && _appTimePendingSeconds > 0);
        if (flushNow)
        {
            _lastDbFlush = now;
            FlushAppTime();
        }
    }

    // -------------------------------------------------------------------------

    private void FlushAppTime()
    {
        var wholeMinutes = (int)(_appTimePendingSeconds / 60.0);
        if (wholeMinutes <= 0) return;

        _appTimePendingSeconds -= wholeMinutes * 60.0;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.TodayAppTimeUsedMinutes += wholeMinutes;
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
