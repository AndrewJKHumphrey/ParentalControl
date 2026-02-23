namespace ParentalControl.Core.Models;

public class WebsiteRule
{
    public int Id { get; set; }
    public string Domain { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
