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
                _processMonitor!.EnforceRules();
                _screenTimeEnforcer!.Tick();
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
