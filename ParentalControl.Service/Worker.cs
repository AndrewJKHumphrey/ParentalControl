using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.Service.Services;

namespace ParentalControl.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private ActivityLogger? _activityLogger;
    private ProcessMonitor? _processMonitor;
    private ScreenTimeEnforcer? _screenTimeEnforcer;
    private WebsiteFilter? _websiteFilter;
    private IpcServer? _ipcServer;

    public Worker(ILogger<Worker> log)
    {
        _log = log;
    }

    public override async Task StartAsync(CancellationToken ct)
    {
        EnsureDatabase();

        _activityLogger = new ActivityLogger();
        _processMonitor = new ProcessMonitor(_activityLogger);
        _screenTimeEnforcer = new ScreenTimeEnforcer(_activityLogger);
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN TodayPlaytimeSeconds INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE AppRules SET AccessMode = 2 WHERE IsBlocked = 1 AND AccessMode = 0"); } catch { }

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
