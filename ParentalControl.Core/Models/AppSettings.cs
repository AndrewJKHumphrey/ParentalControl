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
    public bool ScreenTimeEnabled { get; set; } = true;
    public bool AppControlEnabled { get; set; } = true;
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

    // Game scanning
    public string RawgApiKey          { get; set; } = string.Empty;

    // Scan blocking rules
    public string ScanBlockRating   { get; set; } = "M";          // "None","E","E10+","T","M","AO"
    public string ScanBlockedGenres { get; set; } = string.Empty; // comma-separated genre names
    public string ScanBlockedTags   { get; set; } = string.Empty; // comma-separated tag names

    // Scan App Time rules (AccessMode = ScreenTimeOnly)
    public string ScanAppTimeRating  { get; set; } = "None";
    public string ScanAppTimeGenres  { get; set; } = string.Empty;
    public string ScanAppTimeTags    { get; set; } = string.Empty;

    // Scan Focus Mode rules (AllowedInFocusMode = true)
    public string ScanFocusModeRating  { get; set; } = "None";
    public string ScanFocusModeGenres  { get; set; } = string.Empty;
    public string ScanFocusModeTags    { get; set; } = string.Empty;

    // UI scale / target resolution
    public string UiScale { get; set; } = "1080p"; // "1080p","1440p","2160p"
}
