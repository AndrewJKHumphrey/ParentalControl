namespace ParentalControl.Core.Models;

public class ScanCacheEntry
{
    public int    Id                 { get; set; }
    public string ProcessName        { get; set; } = string.Empty;
    public int    UserProfileId      { get; set; } = 1;
    /// <summary>AccessMode the scanner last computed for this game.</summary>
    public int    AccessMode         { get; set; }
    /// <summary>AllowedInFocusMode the scanner last computed for this game.</summary>
    public bool   AllowedInFocusMode { get; set; }
}
