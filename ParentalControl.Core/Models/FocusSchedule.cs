namespace ParentalControl.Core.Models;

public class FocusSchedule
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsEnabled { get; set; }
    public TimeOnly FocusFrom  { get; set; } = new TimeOnly(15, 0);
    public TimeOnly FocusUntil { get; set; } = new TimeOnly(21, 0);
    public int UserProfileId { get; set; } = 1;
}
