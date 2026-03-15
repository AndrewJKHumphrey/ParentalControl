namespace ParentalControl.Core.Models;

public class VaultEntry
{
    public int Id { get; set; }
    public int UserProfileId { get; set; }
    public string SiteName { get; set; } = "";
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
