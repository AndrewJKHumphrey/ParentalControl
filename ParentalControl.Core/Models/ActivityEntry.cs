namespace ParentalControl.Core.Models;

public enum ActivityType
{
    AppLaunched,
    AppBlocked,
    WebsiteBlocked,
    ScreenTimeLimitReached,
    ScreenLocked,
    ScreenUnlocked,
    ServiceStarted,
    ServiceStopped
}

public class ActivityEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public ActivityType Type { get; set; }
    public string Detail { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
}
