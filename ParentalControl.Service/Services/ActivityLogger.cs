using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.Service.Services;

public class ActivityLogger : IDisposable
{
    private readonly List<ActivityEntry> _buffer = new();
    private readonly Lock _lock = new();
    private readonly Timer _flushTimer;

    public ActivityLogger()
    {
        _flushTimer = new Timer(_ => Flush(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Log(ActivityType type, string detail, int durationSeconds = 0)
    {
        lock (_lock)
        {
            _buffer.Add(new ActivityEntry
            {
                Timestamp = DateTime.Now,
                Type = type,
                Detail = detail,
                DurationSeconds = durationSeconds
            });
        }
    }

    public void Flush()
    {
        List<ActivityEntry> toWrite;
        lock (_lock)
        {
            if (_buffer.Count == 0) return;
            toWrite = new List<ActivityEntry>(_buffer);
            _buffer.Clear();
        }

        try
        {
            using var db = new AppDbContext();
            db.ActivityEntries.AddRange(toWrite);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            // If DB write fails, log to Windows Event Log
            System.Diagnostics.EventLog.WriteEntry(
                "ParentalControl",
                $"ActivityLogger flush failed: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Warning);
        }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
    }
}
