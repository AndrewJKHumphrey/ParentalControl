namespace ParentalControl.Core.Models;

public class AppTimeSchedule
{
    public int       Id                { get; set; }
    public DayOfWeek DayOfWeek         { get; set; }
    public bool      IsEnabled         { get; set; } = false;
    public int       DailyLimitMinutes { get; set; } = 60;
    public int       UserProfileId     { get; set; } = 1;
}
