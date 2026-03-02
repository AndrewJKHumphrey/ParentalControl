namespace ParentalControl.Core.Models;

public class ScreenTimeLimit
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public int DailyLimitMinutes { get; set; }       // 0 = unlimited
    public TimeOnly AllowedFrom { get; set; } = new TimeOnly(0, 0);
    public TimeOnly AllowedUntil { get; set; } = new TimeOnly(23, 59);
    public bool IsEnabled { get; set; }
    public int UserProfileId { get; set; } = 1;
}
