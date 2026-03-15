using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<AppRule> AppRules { get; set; }
    public DbSet<ScreenTimeLimit> ScreenTimeLimits { get; set; }
    public DbSet<ActivityEntry> ActivityEntries { get; set; }
    public DbSet<AppSettings> Settings { get; set; }
    public DbSet<FocusSchedule> FocusSchedules { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }
    public DbSet<AppTimeSchedule> AppTimeSchedules { get; set; }
    public DbSet<ScanCacheEntry>  ScanCache         { get; set; }
    public DbSet<WebsiteRule>          WebsiteRules         { get; set; }
    public DbSet<WebFilterTag>         WebFilterTags        { get; set; }
    public DbSet<WebFilterTagDomain>   WebFilterTagDomains  { get; set; }
    public DbSet<ProfileWebFilterTag>  ProfileWebFilterTags { get; set; }
    public DbSet<VaultEntry>           VaultEntries         { get; set; }

    public static string DbPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ParentalControl");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "data.db");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Seed default settings (password = "parent1234" pre-hashed)
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings
        {
            Id = 1,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("parent1234"),
            IsFirstRun = true,
            TodayUsedMinutes = 0,
            UsageDate = DateTime.Now.Date
        });
    }
}
