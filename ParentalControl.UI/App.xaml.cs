using System.Diagnostics;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN DebugAppTimeOverride INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }

            // App Time pool fields
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppTimeLimitMinutes INTEGER NOT NULL DEFAULT 60"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayAppTimeUsedMinutes INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TodayAppTimeBonusMinutes INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }

            // Notification settings
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationsEnabled INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationMode INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotificationAddress TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN PhoneNumber TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN CarrierGateway TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpHost TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpPort INTEGER NOT NULL DEFAULT 587"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpUsername TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpPassword TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN SmtpUseSsl INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotifyOnScreenLock INTEGER NOT NULL DEFAULT 1"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NotifyOnAppBlock INTEGER NOT NULL DEFAULT 1"); } catch { }

            // New columns for access categorisation
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN AccessMode INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN TodayPlaytimeSeconds INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }

            // Migrate legacy IsBlocked=1 rows to AccessMode=2 (Blocked)
            try { db.Database.ExecuteSqlRaw("UPDATE AppRules SET AccessMode = 2 WHERE IsBlocked = 1 AND AccessMode = 0"); }
            catch { }

            // Reset today's playtime on startup (service is not yet running at this point)
            try { db.Database.ExecuteSqlRaw("UPDATE AppRules SET TodayPlaytimeSeconds = 0"); }
            catch { }

            SeedBrowsers(db);

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

            // ── Ice Blue — Azure / White ──────────────────────────────────────
            ["Ice Blue"]      = (("#0A1018", "#060C12", "#10202E", "#182C3E", "#0C1A26", "#0090FF", "#F0F8FF", "#8BBCDC"),
                                  ("#FAFCFF", "#F2F8FF", "#FFFFFF", "#E8F4FF", "#F5FAFF", "#0078D4", "#0A0E14", "#2A4A6A")),

            // ── Complementary pairs ───────────────────────────────────────────

            // Navy blue bg + Warm orange accent  (Blue ↔ Orange)
            ["Sunset"]        = (("#0E1528", "#0A0F1E", "#182040", "#243060", "#141A34", "#E07028", "#FFD0A0", "#C07840"),
                                  ("#FFF5EC", "#FFEEDD", "#FFFBF7", "#FFE4C8", "#FFF0E4", "#2244AA", "#0A1A40", "#3355AA")),

            // Deep teal bg + Vivid magenta accent  (Teal ↔ Magenta)
            ["Aurora"]        = (("#001A14", "#001010", "#002820", "#003828", "#001E18", "#C0308C", "#AAFFD8", "#60C898"),
                                  ("#F0FFF8", "#E0FFF0", "#F8FFFC", "#C8FFEA", "#E8FFF4", "#B02888", "#002018", "#006040")),

            // Deep violet bg + Chartreuse accent  (Violet ↔ Yellow-green)
            ["Eclipse"]       = (("#120A22", "#0C0618", "#1E1030", "#2C1848", "#180E2C", "#90C020", "#E8D0FF", "#A088C8"),
                                  ("#F8F5FF", "#EEE8FF", "#FDFBFF", "#E0D4FF", "#F4F0FF", "#6A9000", "#200A40", "#5020A0")),

            // Dark cyan bg + Red-orange accent  (Cyan ↔ Red-orange)
            ["Coral Reef"]    = (("#041C1C", "#021414", "#082C2C", "#0E3C3C", "#062424", "#E04820", "#A0F0E8", "#60C0B8"),
                                  ("#F0FFFE", "#E0FFFC", "#F8FFFF", "#C8FFF8", "#E8FFFD", "#CC3800", "#021C18", "#005048")),

            // Dark emerald bg + Vivid crimson accent  (Green ↔ Red)
            ["Forest Fire"]   = (("#081A08", "#041004", "#102810", "#183818", "#0C2010", "#CC2020", "#FFE8F4", "#C890A8"),
                                  ("#FFF0F0", "#FFE0E0", "#FFFAFA", "#FFD0D0", "#FFF5F5", "#1A6010", "#0A1A08", "#2A5020")),

            // Near-black blue bg + Rich gold accent  (Midnight blue ↔ Amber)
            ["Midnight Gold"] = (("#08080E", "#04040A", "#10101A", "#18182A", "#0C0C14", "#D4A000", "#FFE880", "#C0A040"),
                                  ("#FFFBEE", "#FFF5D8", "#FFFEF8", "#FFEEB8", "#FFFBF0", "#0A0A3A", "#080820", "#1A1A60")),

            // Dark wine/burgundy bg + Electric aqua accent  (Red ↔ Cyan)
            ["Volcanic"]      = (("#1E0808", "#140404", "#2C1010", "#3C1818", "#240C0C", "#00C8C0", "#FFD0D0", "#C08080"),
                                  ("#EEFFFF", "#D8FFFE", "#F5FFFF", "#C0FFFC", "#E8FFFF", "#880010", "#1A0408", "#600010")),

            // Dark olive/forest bg + Cherry blossom pink accent  (Green ↔ Rose)
            ["Sakura"]        = (("#141A08", "#0C1004", "#1E2810", "#2A3818", "#181E0C", "#E0408C", "#FFE8F4", "#C890A8"),
                                  ("#FFF0F8", "#FFE0F0", "#FFFCFE", "#FFD0E8", "#FFF5FB", "#2A5010", "#101A08", "#3A6020")),

            // Dark rust/burnt orange bg + Electric blue accent  (Orange ↔ Blue)
            ["Wildfire"]      = (("#1E0E04", "#140804", "#2C1808", "#3C2410", "#241008", "#2060E8", "#FFE0C8", "#C08060"),
                                  ("#EEF2FF", "#E0E8FF", "#F8FAFF", "#C8D8FF", "#EEF4FF", "#B84400", "#080E30", "#1A2A80")),

            // Dark warm brown bg + Verdigris/seafoam accent  (Red-orange ↔ Blue-green)
            ["Copper Patina"] = (("#1A0E08", "#110804", "#281810", "#38241A", "#20120C", "#30A888", "#D8C0A8", "#A08060"),
                                  ("#EEFFF8", "#DCFFF0", "#F5FFFC", "#C0FFE8", "#E8FFF4", "#883010", "#180E04", "#602010")),

            // Dark amber bg + Deep indigo accent  (Yellow-orange ↔ Blue-violet)
            ["Solstice"]      = (("#1A1000", "#100A00", "#281800", "#382400", "#201400", "#5830D0", "#FFE8A0", "#C0A850"),
                                  ("#F5F0FF", "#EEE5FF", "#FDFBFF", "#E0D0FF", "#F8F4FF", "#AA6600", "#180800", "#604000")),

            // ── Nature ───────────────────────────────────────────────────────

            // Near-black deep blue bg + Bioluminescent green accent
            ["Deep Ocean"]    = (("#020A12", "#010608", "#041018", "#082028", "#030C16", "#00E890", "#B0F0E0", "#50C0A0"),
                                  ("#EDFFF8", "#DAFFF2", "#F5FFFC", "#C0FFE8", "#E8FFF6", "#004A30", "#010C08", "#006040")),

            // Dark warm ash/grey bg + Lava orange accent
            ["Volcanic Ash"]  = (("#1A1410", "#120E0A", "#261E18", "#342824", "#201814", "#FF5500", "#F0E0D0", "#B09080"),
                                  ("#FFF5F0", "#FFEBE0", "#FFFAF8", "#FFD8C0", "#FFF0E8", "#4A3020", "#1A1008", "#604030")),

            // Dark cool slate bg + Aurora green accent
            ["Arctic Night"]  = (("#0A0E14", "#060810", "#10161E", "#182028", "#0C1218", "#00E8A0", "#C8E8F8", "#78A8C8"),
                                  ("#F0FFFE", "#E0FFFC", "#F8FFFF", "#C0FFF8", "#E8FFFD", "#006050", "#080E12", "#204050")),

            // Very dark forest bg + Golden canopy light accent
            ["Rainforest"]    = (("#061008", "#040A05", "#0A1A0C", "#102414", "#081410", "#E8C020", "#D0F0B8", "#80B860"),
                                  ("#FDFEE8", "#FAFAD0", "#FEFEF5", "#F8F8A0", "#FBFBE8", "#1A4010", "#081008", "#2A5018")),

            // Dark blue-grey bg + Electric lightning yellow accent
            ["Storm"]         = (("#0C1018", "#080C12", "#121820", "#1C2430", "#0E141C", "#E8D800", "#D8E8F8", "#7898B8"),
                                  ("#FDFEF0", "#FAFAD8", "#FEFEF8", "#F8F8B0", "#FCFCE8", "#2A3040", "#080C14", "#2A3850")),

            // Dark warm sand/earth bg + Terracotta accent
            ["Desert"]        = (("#1C1408", "#140E04", "#2A1E10", "#3A2A1A", "#22180C", "#CC5020", "#F0E0C0", "#C0A070"),
                                  ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE4C0", "#FFF5E8", "#883010", "#1C1004", "#604020")),

            // ── Aesthetic / Style ─────────────────────────────────────────────

            // Deep purple-black bg + Neon hot pink accent
            ["Synthwave"]     = (("#0D0518", "#080310", "#140828", "#1E0C3A", "#100620", "#FF1090", "#E0D0FF", "#8060C8"),
                                  ("#FEF0FF", "#FCE0FF", "#FEFAFF", "#F8C8FF", "#FCF0FF", "#8800CC", "#1A0028", "#600090")),

            // Near-black bg + Neon yellow-green accent
            ["Cyberpunk"]     = (("#0E0E04", "#080800", "#181808", "#242410", "#10100A", "#CCFF00", "#F0FF80", "#90CC00"),
                                  ("#F8FFEE", "#F0FFD8", "#FBFFEE", "#E0FF80", "#F4FFE8", "#304000", "#0A0E00", "#405010")),

            // Very dark near-black bg + Deep crimson accent
            ["Gothic"]        = (("#0A0404", "#060202", "#140808", "#200C0C", "#0E0606", "#8B0000", "#E8C8C8", "#A06868"),
                                  ("#FFF0F0", "#FFE4E4", "#FFFBFB", "#FFD0D0", "#FFF5F5", "#4A0000", "#0A0404", "#400808")),

            // Near-pure black bg + Silver/white accent
            ["Noir"]          = (("#080808", "#040404", "#101010", "#181818", "#0C0C0C", "#C8C8C8", "#F8F8F8", "#A0A0A0"),
                                  ("#FAFAFA", "#F0F0F0", "#FFFFFF", "#E8E8E8", "#F5F5F5", "#181818", "#080808", "#505050")),

            // Dark aged brass/bronze bg + Copper accent
            ["Steampunk"]     = (("#140E04", "#0C0800", "#201608", "#302210", "#1A1206", "#B86020", "#F0D880", "#C09040"),
                                  ("#FFF8E8", "#FFF0D0", "#FFFDF5", "#FFE4A8", "#FFF8EC", "#5C2800", "#1A0C00", "#603010")),
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

    public static void SeedBrowsers(AppDbContext db)
    {
        foreach (var (proc, displayName) in new[]
        {
            ("msedge.exe",  "Microsoft Edge"),
            ("chrome.exe",  "Google Chrome"),
            ("firefox.exe", "Mozilla Firefox"),
            ("opera.exe",   "Opera"),
            ("brave.exe",   "Brave Browser"),
            ("vivaldi.exe", "Vivaldi"),
        })
        {
            var procNoExt = proc[..^4];
            if (!db.AppRules.Any(r => r.ProcessName == proc || r.ProcessName == procNoExt))
            {
                db.AppRules.Add(new AppRule
                {
                    ProcessName = proc,
                    DisplayName = displayName,
                    IsBlocked   = false,
                    AccessMode  = (int)AppAccessMode.Unrestricted,
                });
            }
        }
        db.SaveChanges();
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
