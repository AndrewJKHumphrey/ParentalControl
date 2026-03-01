using System.Diagnostics;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Service.Services;

public class ProcessMonitor
{
    private readonly ActivityLogger _logger;
    private readonly HashSet<string> _blockedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _rulesLock = new();

    public ProcessMonitor(ActivityLogger logger)
    {
        _logger = logger;
        LoadRules();
    }

    public void LoadRules()
    {
        lock (_rulesLock)
        {
            _blockedProcesses.Clear();
            try
            {
                using var db = new AppDbContext();
                var blocked = db.AppRules.Where(r => r.IsBlocked).Select(r => r.ProcessName).ToList();
                foreach (var p in blocked)
                    _blockedProcesses.Add(p);
            }
            catch { }
        }
    }

    public void EnforceRules()
    {
        // Check settings before enforcing
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null && !settings.AppControlEnabled) return;
        }
        catch { }

        HashSet<string> blocked;
        lock (_rulesLock)
        {
            blocked = new HashSet<string>(_blockedProcesses, StringComparer.OrdinalIgnoreCase);
        }

        if (blocked.Count == 0) return;

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (blocked.Contains(process.ProcessName))
                    {
                        process.Kill(entireProcessTree: true);
                        _logger.Log(ActivityType.AppBlocked, process.ProcessName);
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
    }
}
