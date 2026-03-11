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
    /// <summary>When true, re-locks on login even if the account has a password.</summary>
    public bool     AlwaysRelock             { get; set; } = false;
    /// <summary>Persisted across service restarts to prevent double-locking on the same day.</summary>
    public bool     IsScreenTimeLocked       { get; set; } = false;
    /// <summary>When true, only explicitly allowed domains are accessible; everything else is blocked.</summary>
    public bool     WebFilterAllowMode       { get; set; } = false;

    /// <summary>Runtime-only: true when the Windows account requires a password to log in.</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool     AccountIsPasswordProtected { get; set; }

    public override string ToString() => DisplayName;
}
