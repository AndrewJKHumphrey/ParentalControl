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
    public string AppTheme { get; set; } = "Default";
    public bool ThemeIsDark { get; set; } = true;
    public bool IsAllowMode { get; set; } = false;
    public bool ScreenTimeEnabled { get; set; } = true;
    public bool AppControlEnabled { get; set; } = true;
    public bool WebFilterEnabled { get; set; } = true;
    public int TodayBonusMinutes { get; set; } = 0;
    public bool DebugStopServiceAfterLock { get; set; } = false;
    public bool DebugAppTimeOverride { get; set; } = false;
    public int AppTimeLimitMinutes { get; set; } = 60;
    public int TodayAppTimeUsedMinutes { get; set; } = 0;
    public int TodayAppTimeBonusMinutes { get; set; } = 0;

    // Notifications
    public bool   NotificationsEnabled { get; set; } = false;
    public int    NotificationMode     { get; set; } = 0;  // 0 = email, 1 = text, 2 = push (ntfy.sh)
    public string NotificationAddress  { get; set; } = string.Empty;
    public string PhoneNumber          { get; set; } = string.Empty;
    public string CarrierGateway       { get; set; } = string.Empty;
    public string SmtpHost             { get; set; } = string.Empty;
    public int    SmtpPort             { get; set; } = 587;
    public string SmtpUsername         { get; set; } = string.Empty;
    public string SmtpPassword         { get; set; } = string.Empty;
    public bool   SmtpUseSsl           { get; set; } = true;
    public bool   NotifyOnScreenLock   { get; set; } = true;
    public bool   NotifyOnAppBlock     { get; set; } = true;
    public string NtfyTopic           { get; set; } = string.Empty;
}
