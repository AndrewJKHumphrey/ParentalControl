using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Security.Principal;
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
        _ipcServer = new IpcServer(_processMonitor, _screenTimeEnforcer, _activityLogger);
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScreenTimeEnabled INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppControlEnabled INTEGER NOT NULL DEFAULT 1"); } catch { }
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NtfyTopic TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN RawgApiKey TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE Settings SET RawgApiKey = 'cfe4cb325b754875b944b7dc3f80628d' WHERE RawgApiKey = ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockRating   TEXT NOT NULL DEFAULT 'M'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockedGenres TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockedTags   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN UiScale           TEXT NOT NULL DEFAULT '1080p'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeRating   TEXT NOT NULL DEFAULT 'None'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeGenres   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeTags     TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeRating TEXT NOT NULL DEFAULT 'None'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeGenres TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeTags   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN TodayPlaytimeSeconds INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN EsrbRating TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN Genres TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN Tags TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE AppRules SET AccessMode = 2 WHERE IsBlocked = 1 AND AccessMode = 0"); } catch { }

            // Focus Mode + Profiles additions
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN AllowedInFocusMode INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN UserProfileId INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN IsManuallyModified INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE ScreenTimeLimits ADD COLUMN UserProfileId INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE FocusSchedules ADD COLUMN UserProfileId INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN ChildAccountHasPassword INTEGER NOT NULL DEFAULT 0"); } catch { }

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

            // AppTimeSchedules table
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS AppTimeSchedules (
                        Id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        DayOfWeek         INTEGER NOT NULL,
                        IsEnabled         INTEGER NOT NULL DEFAULT 0,
                        DailyLimitMinutes INTEGER NOT NULL DEFAULT 60,
                        UserProfileId     INTEGER NOT NULL DEFAULT 1
                    )");
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

            // Auto-seed a UserProfile for every Windows user account on this machine.
            // Runs every startup so newly added accounts are picked up automatically.
            SeedWindowsUserProfiles(db);
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "Application",
                $"ParentalControl DB init failed: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Error);
        }
    }

    private static void SeedWindowsUserProfiles(AppDbContext db)
    {
        var windowsUsers = GetLocalWindowsUsers();
        foreach (var username in windowsUsers)
        {
            // Skip if a profile already exists for this account (case-insensitive)
            if (db.UserProfiles.Any(p =>
                    p.WindowsUsername.ToLower() == username.ToLower()))
                continue;

            var profile = new UserProfile
            {
                WindowsUsername = username,
                DisplayName     = username,   // default display name = account name
                IsEnabled       = true,
                UsageDate       = DateTime.Now.Date,
            };
            db.UserProfiles.Add(profile);
            db.SaveChanges();

            // Seed 7 ScreenTimeLimits for the new profile
            for (int i = 0; i < 7; i++)
            {
                db.ScreenTimeLimits.Add(new ScreenTimeLimit
                {
                    DayOfWeek         = (DayOfWeek)i,
                    IsEnabled         = false,
                    DailyLimitMinutes = 0,
                    AllowedFrom       = new TimeOnly(0, 0),
                    AllowedUntil      = new TimeOnly(23, 59),
                    UserProfileId     = profile.Id,
                });
            }

            // Seed 7 FocusSchedules for the new profile
            for (int i = 0; i < 7; i++)
            {
                db.FocusSchedules.Add(new FocusSchedule
                {
                    DayOfWeek     = (DayOfWeek)i,
                    IsEnabled     = false,
                    FocusFrom     = new TimeOnly(15, 0),
                    FocusUntil    = new TimeOnly(21, 0),
                    UserProfileId = profile.Id,
                });
            }

            // Seed 7 AppTimeSchedules for the new profile
            for (int i = 0; i < 7; i++)
            {
                db.AppTimeSchedules.Add(new AppTimeSchedule
                {
                    DayOfWeek         = (DayOfWeek)i,
                    IsEnabled         = false,
                    DailyLimitMinutes = 60,
                    UserProfileId     = profile.Id,
                });
            }
            db.SaveChanges();
        }
    }

    // Returns the username for every S-1-5-21-* SID in the registry ProfileList.
    // Falls back to the profile folder name if SID translation fails.
    private static List<string> GetLocalWindowsUsers()
    {
        var users = new List<string>();
        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            if (profileList == null) return users;

            foreach (var sidStr in profileList.GetSubKeyNames())
            {
                // Only process standard user SIDs; skip built-in system accounts
                if (!sidStr.StartsWith("S-1-5-21-")) continue;

                try
                {
                    using var subKey = profileList.OpenSubKey(sidStr);
                    var profilePath = subKey?.GetValue("ProfileImagePath") as string;
                    if (string.IsNullOrWhiteSpace(profilePath)) continue;

                    string username;
                    try
                    {
                        var sid     = new SecurityIdentifier(sidStr);
                        var account = (NTAccount)sid.Translate(typeof(NTAccount));
                        var full    = account.Value; // "MACHINE\Username" or "DOMAIN\Username"
                        username = full.Contains('\\')
                            ? full[(full.LastIndexOf('\\') + 1)..]
                            : full;
                    }
                    catch
                    {
                        // Fall back to the profile folder name (e.g. C:\Users\Alice → Alice)
                        username = Path.GetFileName(profilePath.TrimEnd('\\'));
                    }

                    if (!string.IsNullOrWhiteSpace(username))
                        users.Add(username);
                }
                catch { }
            }
        }
        catch { }
        return users;
    }
}
