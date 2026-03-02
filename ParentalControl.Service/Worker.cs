using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Core.Services;
using ParentalControl.Service.Services;

namespace ParentalControl.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly IHostApplicationLifetime _lifetime;
    private ActivityLogger? _activityLogger;
    private ProcessMonitor? _processMonitor;
    private ScreenTimeEnforcer? _screenTimeEnforcer;
    private WebsiteFilter? _websiteFilter;
    private IpcServer? _ipcServer;

    public Worker(ILogger<Worker> log, IHostApplicationLifetime lifetime)
    {
        _log      = log;
        _lifetime = lifetime;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        EnsureDatabase();

        _activityLogger = new ActivityLogger();
        var notifier = new NotificationService();
        _processMonitor = new ProcessMonitor(_activityLogger, notifier);
        _screenTimeEnforcer = new ScreenTimeEnforcer(_activityLogger, notifier);
        _websiteFilter = new WebsiteFilter();
        _ipcServer = new IpcServer(_processMonitor, _screenTimeEnforcer, _websiteFilter, _activityLogger);

        _websiteFilter.SyncHostsFile();
        _ipcServer.Start();

        _activityLogger.Log(ActivityType.ServiceStarted, "ParentalControl service started");
        _log.LogInformation("ParentalControl service started.");

        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _screenTimeEnforcer!.Tick();
                _processMonitor!.EnforceRules(_screenTimeEnforcer.IsScreenTimeLocked);

                if (_screenTimeEnforcer.TriggeredDebugStop)
                {
                    _log.LogInformation("Debug stop flag triggered — stopping service.");
                    _lifetime.StopApplication();
                    return;
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error in enforcement tick");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        _activityLogger?.Log(ActivityType.ServiceStopped, "ParentalControl service stopped");
        _activityLogger?.Flush();
        _ipcServer?.Dispose();
        _websiteFilter?.Dispose();
        _activityLogger?.Dispose();
        _log.LogInformation("ParentalControl service stopped.");
        await base.StopAsync(ct);
    }

    private static void EnsureDatabase()
    {
        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();

            // Add columns introduced after initial release (safe on existing DBs)
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ChildAccountHasPassword INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TimeFormat12Hour INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppTheme TEXT NOT NULL DEFAULT 'Default'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsAllowMode INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE WebsiteRules ADD COLUMN IsAllowed INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScreenTimeEnabled INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppControlEnabled INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN WebFilterEnabled INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ThemeIsDark INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayBonusMinutes INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN DebugStopServiceAfterLock INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN DebugAppTimeOverride INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppTimeLimitMinutes INTEGER NOT NULL DEFAULT 60"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayAppTimeUsedMinutes INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayAppTimeBonusMinutes INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN AccessMode INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationsEnabled INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationMode INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationAddress TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN PhoneNumber TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN CarrierGateway TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpHost TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpPort INTEGER NOT NULL DEFAULT 587"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpUsername TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpPassword TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpUseSsl INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotifyOnScreenLock INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotifyOnAppBlock INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN TodayPlaytimeSeconds INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE AppRules SET AccessMode = 2 WHERE IsBlocked = 1 AND AccessMode = 0"); } catch { }

            // Focus Mode + Profiles additions
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN AllowedInFocusMode INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN UserProfileId INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE ScreenTimeLimits ADD COLUMN UserProfileId INTEGER NOT NULL DEFAULT 1"); } catch { }

            // FocusSchedules table
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS FocusSchedules (
                        Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        DayOfWeek     INTEGER NOT NULL,
                        IsEnabled     INTEGER NOT NULL DEFAULT 0,
                        FocusFrom     TEXT    NOT NULL DEFAULT '15:00:00',
                        FocusUntil    TEXT    NOT NULL DEFAULT '21:00:00',
                        UserProfileId INTEGER NOT NULL DEFAULT 1
                    )");
            }
            catch { }
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    INSERT INTO FocusSchedules (DayOfWeek, IsEnabled, FocusFrom, FocusUntil, UserProfileId)
                    SELECT v.day, 0, '15:00:00', '21:00:00', 1
                    FROM (VALUES (0),(1),(2),(3),(4),(5),(6)) AS v(day)
                    WHERE NOT EXISTS (SELECT 1 FROM FocusSchedules WHERE UserProfileId = 1)");
            }
            catch { }

            // UserProfiles table
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS UserProfiles (
                        Id                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        WindowsUsername          TEXT    NOT NULL DEFAULT '',
                        DisplayName              TEXT    NOT NULL DEFAULT 'Default',
                        IsEnabled                INTEGER NOT NULL DEFAULT 1,
                        TodayUsedMinutes         INTEGER NOT NULL DEFAULT 0,
                        UsageDate                TEXT    NOT NULL DEFAULT '',
                        TodayBonusMinutes        INTEGER NOT NULL DEFAULT 0,
                        TodayAppTimeUsedMinutes  INTEGER NOT NULL DEFAULT 0,
                        TodayAppTimeBonusMinutes INTEGER NOT NULL DEFAULT 0,
                        AppTimeLimitMinutes      INTEGER NOT NULL DEFAULT 60,
                        FocusModeEnabled         INTEGER NOT NULL DEFAULT 0
                    )");
            }
            catch { }
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    INSERT INTO UserProfiles (Id, WindowsUsername, DisplayName, IsEnabled, UsageDate)
                    SELECT 1, '', 'Default', 1, date('now')
                    WHERE NOT EXISTS (SELECT 1 FROM UserProfiles WHERE Id = 1)");
            }
            catch { }
            // One-time migration: copy existing usage from AppSettings into Default profile
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    UPDATE UserProfiles
                    SET TodayUsedMinutes         = (SELECT TodayUsedMinutes FROM Settings WHERE Id = 1),
                        UsageDate                = (SELECT UsageDate FROM Settings WHERE Id = 1),
                        TodayBonusMinutes        = (SELECT TodayBonusMinutes FROM Settings WHERE Id = 1),
                        TodayAppTimeUsedMinutes  = (SELECT TodayAppTimeUsedMinutes FROM Settings WHERE Id = 1),
                        TodayAppTimeBonusMinutes = (SELECT TodayAppTimeBonusMinutes FROM Settings WHERE Id = 1),
                        AppTimeLimitMinutes      = (SELECT AppTimeLimitMinutes FROM Settings WHERE Id = 1)
                    WHERE Id = 1 AND TodayUsedMinutes = 0");
            }
            catch { }

            // Seed common browsers (unblocked, AccessMode=0)
            foreach (var (proc, name) in new[]
            {
                ("msedge.exe",  "Microsoft Edge"),
                ("chrome.exe",  "Google Chrome"),
                ("firefox.exe", "Mozilla Firefox"),
                ("opera.exe",   "Opera"),
                ("brave.exe",   "Brave Browser"),
                ("vivaldi.exe", "Vivaldi"),
            })
            {
                var procNoExt = proc[..^4];
                try
                {
                    db.Database.ExecuteSqlRaw(
                        "INSERT INTO AppRules (ProcessName, DisplayName, IsBlocked, AccessMode, CreatedAt) " +
                        "SELECT ?, ?, 0, 0, datetime('now') " +
                        "WHERE NOT EXISTS (SELECT 1 FROM AppRules WHERE ProcessName = ? OR ProcessName = ?)",
                        proc, name, proc, procNoExt);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "Application",
                $"ParentalControl DB init failed: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Error);
        }
    }
}
