namespace ParentalControl.Core.Models;

public class UserProfile
{
    public int      Id                       { get; set; }
    public string   WindowsUsername          { get; set; } = string.Empty;
    public string   DisplayName              { get; set; } = string.Empty;
    public bool     IsEnabled                { get; set; } = true;

    public int      TodayUsedMinutes         { get; set; } = 0;
    public DateTime UsageDate                { get; set; } = DateTime.Now.Date;
    public int      TodayBonusMinutes        { get; set; } = 0;
    public int      TodayAppTimeUsedMinutes  { get; set; } = 0;
    public int      TodayAppTimeBonusMinutes { get; set; } = 0;
    public int      AppTimeLimitMinutes      { get; set; } = 60;
    public bool     FocusModeEnabled         { get; set; } = false;
}
