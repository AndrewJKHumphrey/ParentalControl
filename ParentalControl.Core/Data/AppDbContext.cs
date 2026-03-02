using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Models;

namespace ParentalControl.Core.Data;

public class AppDbContext : DbContext
{
    public DbSet<AppRule> AppRules { get; set; }
    public DbSet<WebsiteRule> WebsiteRules { get; set; }
    public DbSet<ScreenTimeLimit> ScreenTimeLimits { get; set; }
    public DbSet<ActivityEntry> ActivityEntries { get; set; }
    public DbSet<AppSettings> Settings { get; set; }
    public DbSet<FocusSchedule> FocusSchedules { get; set; }
    public DbSet<UserProfile> UserProfiles { get; set; }

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
        // Seed default profile (Id=1, catch-all for any user without a specific profile)
        modelBuilder.Entity<UserProfile>().HasData(new UserProfile
        {
            Id              = 1,
            WindowsUsername = "",
            DisplayName     = "Default",
            IsEnabled       = true,
            UsageDate       = new DateTime(2000, 1, 1)
        });

        // Seed default screen time limits (one per day, all disabled / unlimited)
        for (int i = 0; i < 7; i++)
        {
            modelBuilder.Entity<ScreenTimeLimit>().HasData(new ScreenTimeLimit
            {
                Id = i + 1,
                DayOfWeek = (DayOfWeek)i,
                DailyLimitMinutes = 0,
                AllowedFrom = new TimeOnly(0, 0),
                AllowedUntil = new TimeOnly(23, 59),
                IsEnabled = false,
                UserProfileId = 1
            });
        }

        // Seed default focus schedules (one per day, all disabled)
        for (int i = 0; i < 7; i++)
        {
            modelBuilder.Entity<FocusSchedule>().HasData(new FocusSchedule
            {
                Id = i + 1,
                DayOfWeek = (DayOfWeek)i,
                IsEnabled = false,
                FocusFrom  = new TimeOnly(15, 0),
                FocusUntil = new TimeOnly(21, 0),
                UserProfileId = 1
            });
        }

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
