namespace ParentalControl.Core.Models;

public class WebsiteRule
{
    public int    Id            { get; set; }
    public string Pattern       { get; set; } = "";   // e.g. "example.com" or "*.example.com"
    public bool   IsBlocked     { get; set; } = true; // true = block, false = allow-exception
    public int    UserProfileId { get; set; } = 1;
}
