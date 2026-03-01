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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
