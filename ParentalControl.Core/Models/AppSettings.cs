namespace ParentalControl.Core.Models;

public class AppSettings
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsFirstRun { get; set; } = true;
    public int TodayUsedMinutes { get; set; }
    public DateTime UsageDate { get; set; } = DateTime.Now.Date;
    public bool ChildAccountHasPassword { get; set; } = false;
    public bool TimeFormat12Hour { get; set; } = false;
    public bool EnforceForAdmins { get; set; } = true;
    public string AppTheme { get; set; } = "Dark";
}
