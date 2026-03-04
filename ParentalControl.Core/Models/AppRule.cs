namespace ParentalControl.Core.Models;

public enum AppAccessMode
{
    Unrestricted   = 0,
    ScreenTimeOnly = 1,
    Blocked        = 2,
}

public class AppRule
{
    public int    Id                  { get; set; }
    public string ProcessName         { get; set; } = string.Empty;
    public string DisplayName         { get; set; } = string.Empty;
    /// <summary>Legacy column kept in DB; use AccessMode for logic.</summary>
    public bool   IsBlocked           { get; set; }
    public int    AccessMode          { get; set; } = (int)AppAccessMode.Unrestricted;
    public bool   AllowedInFocusMode  { get; set; } = false;
    public int    UserProfileId       { get; set; } = 1;
    public string EsrbRating          { get; set; } = string.Empty;  // "E","E10+","T","M","AO", or ""
    public string Genres              { get; set; } = string.Empty;  // e.g. "Shooter, Action"
    public string Tags                { get; set; } = string.Empty;  // e.g. "violence, gore"
    /// <summary>Set when the user manually saves changes; prevents scan from overwriting.</summary>
    public bool   IsManuallyModified  { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
