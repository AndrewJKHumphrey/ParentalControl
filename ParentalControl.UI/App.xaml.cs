using System.Diagnostics;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Data;
using ParentalControl.UI.Views;

namespace ParentalControl.UI;

public partial class App : Application
{
    private const string ServiceName = "ParentalControlService";

    private void App_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Ensure DB exists
        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();

            // Add columns introduced after initial release (safe on existing DBs)
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ChildAccountHasPassword INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TimeFormat12Hour INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppTheme TEXT NOT NULL DEFAULT 'Dark'"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsAllowMode INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE WebsiteRules ADD COLUMN IsAllowed INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScreenTimeEnabled INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppControlEnabled INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN WebFilterEnabled INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ThemeIsDark INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayBonusMinutes INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN DebugStopServiceAfterLock INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }

            // Seed common browsers into AppRules (unblocked by default)
            foreach (var (proc, name) in new[]
            {
                ("msedge", "Microsoft Edge"),
                ("chrome", "Google Chrome"),
                ("firefox", "Mozilla Firefox"),
                ("opera", "Opera"),
                ("brave", "Brave Browser"),
            })
            {
                try
                {
                    db.Database.ExecuteSqlRaw(
                        "INSERT INTO AppRules (ProcessName, DisplayName, IsBlocked, CreatedAt) " +
                        "SELECT ?, ?, 0, datetime('now') " +
                        "WHERE NOT EXISTS (SELECT 1 FROM AppRules WHERE ProcessName = ?)",
                        proc, name, proc);
                }
                catch { }
            }

            // Migrate legacy "Dark"/"Light" AppTheme values to "Default" + ThemeIsDark flag
            try { db.Database.ExecuteSqlRaw("UPDATE Settings SET AppTheme = 'Default' WHERE AppTheme = 'Dark'"); }
            catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE Settings SET AppTheme = 'Default', ThemeIsDark = 0 WHERE AppTheme = 'Light'"); }
            catch { }

            // Fix AllowedFrom rows that were seeded as noon (12:00) instead of midnight (00:00)
            try { db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET AllowedFrom = '00:00:00' WHERE AllowedFrom LIKE '12:00:00%'"); }
            catch { }

            // Apply saved theme before showing any window
            var settings = db.Settings.FirstOrDefault();
            ApplyTheme(settings?.AppTheme ?? "Default", settings?.ThemeIsDark ?? true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to initialize database:\n{ex.Message}", "Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Show login (skipped in Debug builds for faster development)
#if !DEBUG
        var login = new LoginWindow();
        if (login.ShowDialog() != true)
        {
            Shutdown(0);
            return;
        }
#endif

        // Ensure service is running
        EnsureServiceRunning();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        Current.MainWindow = main;
        main.Show();
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {

    }

    public static void ApplyTheme(string name, bool isDark = true)
    {
        // Each entry: (dark palette, light palette)
        // Palette order: AppBg, SidebarBg, CardBg, CardHover, AltRowBg, BorderColor, TextPrimary, TextSecondary
        // Themes follow the RYB color wheel: primary (Red, Yellow, Blue),
        // secondary (Orange, Green, Purple), and tertiary colors — sorted Red → Magenta.
        var themes = new Dictionary<string, (
            (string, string, string, string, string, string, string, string) dark,
            (string, string, string, string, string, string, string, string) light)>
        {
            // Default — Catppuccin-inspired blue-purple base
            ["Default"]   = (("#1E1E2E", "#181825", "#313244", "#45475A", "#2A2A3E", "#585B70", "#CDD6F4", "#A6ADC8"),
                              ("#EFF1F5", "#E6E9EF", "#DCE0E8", "#BCC0CC", "#CCD0DA", "#9CA0B0", "#4C4F69", "#6C6F85")),

            // ── Primary: Red ───────────────────────────────────────────────────
            ["Red"]       = (("#1E1010", "#150B0B", "#2E1818", "#3E2222", "#241212", "#882233", "#F5CCCC", "#CC8888"),
                              ("#FFF0F0", "#FFDDE0", "#FFFBFB", "#FFD0D0", "#FFF5F5", "#BB2233", "#3A0A0A", "#882222")),

            // ── Tertiary: Vermilion (Red-Orange) ───────────────────────────────
            ["Vermilion"] = (("#1E1208", "#150D05", "#2E1E0E", "#3E2A18", "#241808", "#993322", "#F5CCB8", "#CC8860"),
                              ("#FFF3EE", "#FFE5D8", "#FFFAF8", "#FFD8C0", "#FFF6F2", "#CC4422", "#3A1005", "#883322")),

            // ── Secondary: Orange ──────────────────────────────────────────────
            ["Orange"]    = (("#1E1508", "#150F05", "#2E2010", "#3E2C18", "#241A08", "#AA5522", "#F5D4AA", "#CC9055"),
                              ("#FFF6EE", "#FFEAD8", "#FFFBF7", "#FFDCB8", "#FFF8F2", "#CC5511", "#3A1800", "#884411")),

            // ── Tertiary: Amber (Yellow-Orange) ───────────────────────────────
            ["Amber"]     = (("#1A1800", "#131200", "#282400", "#363200", "#1E1C00", "#887700", "#F0DD77", "#C8AA30"),
                              ("#FFFBEE", "#FFF5D8", "#FFFEF8", "#FFEEB8", "#FFFDF0", "#CC8800", "#3A2A00", "#886600")),

            // ── Primary: Yellow ────────────────────────────────────────────────
            ["Yellow"]    = (("#1A1A00", "#131300", "#282800", "#363600", "#1E1E00", "#888800", "#F0EE77", "#C8C030"),
                              ("#FEFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FCFCE8", "#888800", "#2A2800", "#666600")),

            // ── Tertiary: Lime (Yellow-Green) ─────────────────────────────────
            ["Lime"]      = (("#0E1800", "#0A1200", "#182800", "#223800", "#121E00", "#447800", "#BBFF44", "#88CC20"),
                              ("#F4FFEE", "#E8FFD8", "#FBFFFA", "#D0FF99", "#F8FFE8", "#448800", "#1A3A00", "#336600")),

            // ── Secondary: Green ───────────────────────────────────────────────
            ["Green"]     = (("#0A1E0C", "#081410", "#102C14", "#1C3C20", "#0E2010", "#2A7A30", "#AAEEBB", "#70BB80"),
                              ("#EEFAF0", "#DCF4E0", "#F8FFFB", "#C8ECC8", "#F2FCF4", "#228844", "#0A2A10", "#1A6030")),

            // ── Tertiary: Teal (Blue-Green) ────────────────────────────────────
            ["Teal"]      = (("#081E1C", "#061412", "#102E2A", "#1A3E38", "#0C2422", "#1A7070", "#80FFEE", "#40C0B0"),
                              ("#EDFFFE", "#D8FFFA", "#F5FFFF", "#B0FFF5", "#F0FFFD", "#1A7070", "#003830", "#1A6060")),

            // ── Primary: Blue ──────────────────────────────────────────────────
            ["Blue"]      = (("#0C1224", "#080D1A", "#101E38", "#18304C", "#0E162C", "#2255AA", "#88BBFF", "#5588CC"),
                              ("#EEF2FF", "#DCE6FF", "#F8FAFF", "#BCCEFF", "#F2F6FF", "#2244BB", "#080E30", "#1A2E88")),

            // ── Tertiary: Indigo (Blue-Purple) ────────────────────────────────
            ["Indigo"]    = (("#100C22", "#0C0818", "#1A1234", "#261A46", "#140E28", "#4433AA", "#AAAAFF", "#7766CC"),
                              ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#CCC8FF", "#F5F4FF", "#4433AA", "#100830", "#2A1888")),

            // ── Secondary: Purple ──────────────────────────────────────────────
            ["Purple"]    = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C28", "#5A2A99", "#D4AAFF", "#A070D0"),
                              ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#5522AA", "#200840", "#3A1088")),

            // ── Tertiary: Magenta (Red-Purple) ────────────────────────────────
            ["Magenta"]   = (("#1E0818", "#170512", "#2E1028", "#3E183A", "#240C1E", "#882288", "#FFB0EE", "#CC70CC"),
                              ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8F0", "#FFF5FB", "#AA2299", "#3A0030", "#880888")),
        };

        // Fallback for legacy names
        if (!themes.ContainsKey(name)) name = "Default";

        var palette = isDark ? themes[name].dark : themes[name].light;

        var res = Application.Current.Resources;
        res["AppBg"]         = Brush(palette.Item1);
        res["SidebarBg"]     = Brush(palette.Item2);
        res["CardBg"]        = Brush(palette.Item3);
        res["CardHover"]     = Brush(palette.Item4);
        res["AltRowBg"]      = Brush(palette.Item5);
        res["BorderColor"]   = Brush(palette.Item6);
        res["TextPrimary"]   = Brush(palette.Item7);
        res["TextSecondary"] = Brush(palette.Item8);

        static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }

    private static void EnsureServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status != ServiceControllerStatus.Running &&
                sc.Status != ServiceControllerStatus.StartPending)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start the ParentalControl service:\n{ex.Message}\n\nSome features may not work.",
                "Service Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
