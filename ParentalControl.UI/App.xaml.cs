using System.Diagnostics;
using System.IO;
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
    private const string ServiceName    = "ParentalControlService";
    private const string WatchdogServiceName = "ParentalControlWatchdog";

    private void App_Startup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Ensure DB exists
        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();

            // Add columns introduced after initial release (safe on existing DBs)
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN TimeFormat12Hour INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppTheme TEXT NOT NULL DEFAULT 'Dark'"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsAllowMode INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS WebsiteRules (
                    Id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Pattern       TEXT    NOT NULL DEFAULT '',
                    IsBlocked     INTEGER NOT NULL DEFAULT 1,
                    UserProfileId INTEGER NOT NULL DEFAULT 1
                )"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScreenTimeEnabled INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AppControlEnabled INTEGER NOT NULL DEFAULT 1"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN FocusModeEnabled  INTEGER NOT NULL DEFAULT 1"); } catch { }
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN NtfyTopic TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN RawgApiKey TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("UPDATE Settings SET RawgApiKey = 'cfe4cb325b754875b944b7dc3f80628d' WHERE RawgApiKey = ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockRating   TEXT NOT NULL DEFAULT 'M'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockedGenres TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanBlockedTags   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN UiScale           TEXT NOT NULL DEFAULT '1080p'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanRatedDefault   INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanUnratedDefault INTEGER NOT NULL DEFAULT 0"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeRating   TEXT NOT NULL DEFAULT 'None'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeGenres   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanAppTimeTags     TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeRating TEXT NOT NULL DEFAULT 'None'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeGenres TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ScanFocusModeTags   TEXT NOT NULL DEFAULT ''"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN CustomPrimaryColor   TEXT NOT NULL DEFAULT '#1E1E2E'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN CustomSecondaryColor TEXT NOT NULL DEFAULT '#585B70'"); } catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN CustomTertiaryColor  TEXT NOT NULL DEFAULT '#CDD6F4'"); } catch { }

            // New columns for access categorisation
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN AccessMode INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN TodayPlaytimeSeconds INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN EsrbRating TEXT NOT NULL DEFAULT ''"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN Genres TEXT NOT NULL DEFAULT ''"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN Tags TEXT NOT NULL DEFAULT ''"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE AppRules ADD COLUMN IsManuallyModified INTEGER NOT NULL DEFAULT 0"); }
            catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN AlwaysRelock INTEGER NOT NULL DEFAULT 0"); }
            catch { }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE UserProfiles ADD COLUMN IsScreenTimeLocked INTEGER NOT NULL DEFAULT 0"); }
            catch { }

            // ScanCache table
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ScanCache (
                        Id                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ProcessName        TEXT    NOT NULL DEFAULT '',
                        UserProfileId      INTEGER NOT NULL DEFAULT 1,
                        AccessMode         INTEGER NOT NULL DEFAULT 0,
                        AllowedInFocusMode INTEGER NOT NULL DEFAULT 0
                    )");
            }
            catch { }

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
            ApplyTheme(settings?.AppTheme ?? "Default", settings?.ThemeIsDark ?? true,
                settings?.CustomPrimaryColor   ?? "#1E1E2E",
                settings?.CustomSecondaryColor ?? "#585B70",
                settings?.CustomTertiaryColor  ?? "#CDD6F4");
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

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var log = $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n{e.Exception.StackTrace}";
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pc_crash.log");
        try { System.IO.File.WriteAllText(logPath, log); } catch { }
        MessageBox.Show($"Unhandled exception:\n\n{log}", "Crash Details", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void ApplyTheme(string name, bool isDark = true,
        string customPrimary   = "#1E1E2E",
        string customSecondary = "#585B70",
        string customTertiary  = "#CDD6F4")
    {
        // Custom theme: derive palette from 3 user-defined colours
        if (name == "Custom")
        {
            static Color ParseC(string hex) => (Color)ColorConverter.ConvertFromString(hex);
            static Color Darken(Color c, double a)  => Color.FromRgb((byte)(c.R*(1-a)), (byte)(c.G*(1-a)), (byte)(c.B*(1-a)));
            static Color Lighten(Color c, double a) => Color.FromRgb((byte)Math.Min(255, c.R+(255-c.R)*a), (byte)Math.Min(255, c.G+(255-c.G)*a), (byte)Math.Min(255, c.B+(255-c.B)*a));
            static Color Blend(Color a, Color b, double t) => Color.FromRgb((byte)(a.R*t+b.R*(1-t)), (byte)(a.G*t+b.G*(1-t)), (byte)(a.B*t+b.B*(1-t)));

            var p = ParseC(customPrimary);
            var s = ParseC(customSecondary);
            var t = ParseC(customTertiary);
            var customRes = Application.Current.Resources;
            customRes["AppBg"]         = new SolidColorBrush(p);
            customRes["SidebarBg"]     = new SolidColorBrush(Darken(p, 0.10));
            customRes["CardBg"]        = new SolidColorBrush(Lighten(p, 0.12));
            customRes["CardHover"]     = new SolidColorBrush(Lighten(p, 0.22));
            customRes["AltRowBg"]      = new SolidColorBrush(Lighten(p, 0.05));
            customRes["BorderColor"]   = new SolidColorBrush(s);
            customRes["TextPrimary"]   = new SolidColorBrush(t);
            customRes["TextSecondary"] = new SolidColorBrush(Blend(t, p, 0.65));
            static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            WriteThemeJson(ToHex(p), ToHex(Lighten(p, 0.12)), ToHex(s), ToHex(t), ToHex(Blend(t, p, 0.65)), ToHex(Lighten(p, 0.22)));
            return;
        }

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

            // ── Quaternary: Scarlet (Red → Vermilion) ─────────────────────────
            ["Scarlet"]   = (("#1E0A06", "#160604", "#2C1008", "#3E1A12", "#240C06", "#DD1100", "#F5C4B0", "#CC7E60"),
                              ("#FFF2EE", "#FFE4DC", "#FFFBFA", "#FFD0C0", "#FFF6F2", "#CC2200", "#3A0800", "#882010")),

            // ── Quinary: Coral (Scarlet → Vermilion) ──────────────────────────
            ["Coral"]     = (("#1E0C06", "#150806", "#2C1408", "#3C1E12", "#220E06", "#DD4422", "#F5CAB0", "#CC8866"),
                              ("#FFF6F2", "#FFEAE0", "#FFFBF8", "#FFD8C8", "#FFF0E8", "#BB2200", "#441010", "#882222")),

            // ── Tertiary: Vermilion (Red-Orange) ───────────────────────────────
            ["Vermilion"] = (("#1E1208", "#150D05", "#2E1E0E", "#3E2A18", "#241808", "#993322", "#F5CCB8", "#CC8860"),
                              ("#FFF3EE", "#FFE5D8", "#FFFAF8", "#FFD8C0", "#FFF6F2", "#CC4422", "#3A1005", "#883322")),

            // ── Secondary: Orange ──────────────────────────────────────────────
            ["Orange"]    = (("#1E1508", "#150F05", "#2E2010", "#3E2C18", "#241A08", "#AA5522", "#F5D4AA", "#CC9055"),
                              ("#FFF6EE", "#FFEAD8", "#FFFBF7", "#FFDCB8", "#FFF8F2", "#CC5511", "#3A1800", "#884411")),

            // ── Tertiary: Amber (Yellow-Orange) ───────────────────────────────
            ["Amber"]     = (("#1A1800", "#131200", "#282400", "#363200", "#1E1C00", "#887700", "#F0DD77", "#C8AA30"),
                              ("#FFFBEE", "#FFF5D8", "#FFFEF8", "#FFEEB8", "#FFFDF0", "#CC8800", "#3A2A00", "#886600")),

            // ── Quinary: Gold (Amber → Yellow) ────────────────────────────────
            ["Gold"]      = (("#1C1A00", "#141200", "#2A2600", "#3A3400", "#201E00", "#CC9900", "#FFEE88", "#DDCC44"),
                              ("#FEFDE8", "#FAF8D0", "#FDFCE0", "#F8F480", "#FCFCE8", "#996600", "#443300", "#664400")),

            // ── Primary: Yellow ────────────────────────────────────────────────
            ["Yellow"]    = (("#1A1A00", "#131300", "#282800", "#363600", "#1E1E00", "#888800", "#F0EE77", "#C8C030"),
                              ("#FEFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FCFCE8", "#888800", "#2A2800", "#666600")),

            // ── Quaternary: Chartreuse (Yellow → Lime) ────────────────────────
            ["Chartreuse"] = (("#0C1400", "#080E00", "#142000", "#1E2E00", "#101800", "#88CC00", "#DEFF88", "#AACC44"),
                               ("#F6FFE8", "#EAFFD0", "#FBFFF5", "#D8FF80", "#F0FFE0", "#558800", "#182400", "#3A6600")),

            // ── Tertiary: Lime (Yellow-Green) ─────────────────────────────────
            ["Lime"]      = (("#0E1800", "#0A1200", "#182800", "#223800", "#121E00", "#447800", "#BBFF44", "#88CC20"),
                              ("#F4FFEE", "#E8FFD8", "#FBFFFA", "#D0FF99", "#F8FFE8", "#448800", "#1A3A00", "#336600")),

            // ── Quinary: Mint (Lime → Green) ──────────────────────────────────
            ["Mint"]      = (("#061C0E", "#041208", "#0C2A14", "#143A1C", "#081E10", "#00CC88", "#AAFFDD", "#55DDAA"),
                              ("#EEFFF6", "#D8FFF0", "#F5FFF8", "#B0FFD8", "#E4FFF0", "#009966", "#003322", "#005544")),

            // ── Secondary: Green ───────────────────────────────────────────────
            ["Green"]     = (("#0A1E0C", "#081410", "#102C14", "#1C3C20", "#0E2010", "#2A7A30", "#AAEEBB", "#70BB80"),
                              ("#EEFAF0", "#DCF4E0", "#F8FFFB", "#C8ECC8", "#F2FCF4", "#228844", "#0A2A10", "#1A6030")),

            // ── Tertiary: Teal (Blue-Green) ────────────────────────────────────
            ["Teal"]      = (("#081E1C", "#061412", "#102E2A", "#1A3E38", "#0C2422", "#1A7070", "#80FFEE", "#40C0B0"),
                              ("#EDFFFE", "#D8FFFA", "#F5FFFF", "#B0FFF5", "#F0FFFD", "#1A7070", "#003830", "#1A6060")),

            // ── Quaternary: Cyan (Teal → Blue) ────────────────────────────────
            ["Cyan"]      = (("#001C22", "#001418", "#002C34", "#003A44", "#002028", "#00AACC", "#88EEFF", "#44BBCC"),
                              ("#EEFFFF", "#D8FFFF", "#F5FFFF", "#B0FFFF", "#E0FFFF", "#007799", "#002830", "#006070")),

            // ── Quinary: Sky (Cyan → Blue) ────────────────────────────────────
            ["Sky"]       = (("#061826", "#041018", "#0C2436", "#103046", "#081C2C", "#0088DD", "#AADDFF", "#66AADD"),
                              ("#EAF4FF", "#D4ECFF", "#F2F8FF", "#C0E4FF", "#E4F0FF", "#0055BB", "#001844", "#003388")),

            // ── Primary: Blue ──────────────────────────────────────────────────
            ["Blue"]      = (("#0C1224", "#080D1A", "#101E38", "#18304C", "#0E162C", "#2255AA", "#88BBFF", "#5588CC"),
                              ("#EEF2FF", "#DCE6FF", "#F8FAFF", "#BCCEFF", "#F2F6FF", "#2244BB", "#080E30", "#1A2E88")),

            // ── Tertiary: Indigo (Blue-Purple) ────────────────────────────────
            ["Indigo"]    = (("#100C22", "#0C0818", "#1A1234", "#261A46", "#140E28", "#4433AA", "#AAAAFF", "#7766CC"),
                              ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#CCC8FF", "#F5F4FF", "#4433AA", "#100830", "#2A1888")),

            // ── Quinary: Lavender (Indigo → Violet) ───────────────────────────
            ["Lavender"]  = (("#120E26", "#0C0A1C", "#1C1638", "#26204A", "#16122E", "#7766BB", "#CCBBFF", "#9988DD"),
                              ("#F2F0FF", "#E8E4FF", "#F8F6FF", "#DDD8FF", "#EEE8FF", "#4433AA", "#2A1A6A", "#3A2288")),

            // ── Quaternary: Violet (Indigo → Purple) ──────────────────────────
            ["Violet"]    = (("#120820", "#0C0418", "#1C1030", "#281840", "#160C28", "#6622CC", "#CCAAFF", "#9966CC"),
                              ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#5518AA", "#180830", "#3A1888")),

            // ── Secondary: Purple ──────────────────────────────────────────────
            ["Purple"]    = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C28", "#5A2A99", "#D4AAFF", "#A070D0"),
                              ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#5522AA", "#200840", "#3A1088")),

            // ── Tertiary: Magenta (Red-Purple) ────────────────────────────────
            ["Magenta"]   = (("#1E0818", "#170512", "#2E1028", "#3E183A", "#240C1E", "#882288", "#FFB0EE", "#CC70CC"),
                              ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8F0", "#FFF5FB", "#AA2299", "#3A0030", "#880888")),

            // ── Quinary: Pink (Magenta → Rose) ────────────────────────────────
            ["Pink"]      = (("#1E0A16", "#160610", "#2E1222", "#3E1A30", "#240E1C", "#DD2266", "#FFB8D8", "#DD7799"),
                              ("#FFF0F8", "#FFE0F0", "#FFFBFE", "#FFC8E8", "#FFF5FA", "#BB0044", "#441022", "#882244")),

            // ── Quaternary: Rose (Magenta → Red, closes wheel) ────────────────
            ["Rose"]      = (("#1E0810", "#160508", "#2E1020", "#3E1830", "#240C18", "#CC1155", "#F5BBCC", "#CC7788"),
                              ("#FFF0F5", "#FFE0EC", "#FFFBFD", "#FFD0E0", "#FFF5F8", "#AA1144", "#380010", "#880830")),

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
            ["Cherry Grove"]  = (("#141A08", "#0C1004", "#1E2810", "#2A3818", "#181E0C", "#E0408C", "#FFE8F4", "#C890A8"),
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
            ["Steampunk"]       = (("#140E04", "#0C0800", "#201608", "#302210", "#1A1206", "#B86020", "#F0D880", "#C09040"),
                                    ("#FFF8E8", "#FFF0D0", "#FFFDF5", "#FFE4A8", "#FFF8EC", "#5C2800", "#1A0C00", "#603010")),

            // ── Retro / Terminal ──────────────────────────────────────────────

            // Classic green phosphor CRT monitor
            ["CRT Green"]       = (("#0A1A0A", "#061206", "#102A10", "#183818", "#0C1E0C", "#00FF41", "#00FF41", "#00AA30"),
                                    ("#F0FFF0", "#E0FFE0", "#D0FFD0", "#B0FFB0", "#E8FFE8", "#009910", "#0A2A0A", "#1A6A1A")),

            // Classic amber phosphor CRT monitor
            ["CRT Amber"]       = (("#1A1200", "#110C00", "#281C00", "#382800", "#201400", "#FFB300", "#FFB300", "#CC8800"),
                                    ("#FFFBEE", "#FFF5D8", "#FFEEB8", "#FFE080", "#FFF8E4", "#885500", "#2C1A00", "#6A4000")),

            // 80s Miami Vice neon — deep magenta-black + hot pink + electric blue
            ["Retrowave"]       = (("#0D0018", "#08000F", "#18002C", "#240040", "#120022", "#FF007F", "#FF80FF", "#00CFFF"),
                                    ("#FFF0FF", "#FFE0FE", "#FFD0FC", "#FFC0F8", "#FFECFF", "#CC0066", "#1A0030", "#6600AA")),

            // ── Editor Themes ─────────────────────────────────────────────────

            // Cool arctic slate — the popular developer palette
            ["Nord"]            = (("#2E3440", "#242932", "#3B4252", "#434C5E", "#333847", "#5E81AC", "#ECEFF4", "#D8DEE9"),
                                    ("#ECEFF4", "#E5E9F0", "#D8DEE9", "#C8D0DC", "#DDE2EA", "#5E81AC", "#2E3440", "#4C566A")),

            // Deep purple-black + bright pink/green/orange — the famous dark theme
            ["Dracula"]         = (("#282A36", "#1E2029", "#44475A", "#525568", "#373A4A", "#BD93F9", "#F8F8F2", "#6272A4"),
                                    ("#F8F8F2", "#F0F0E8", "#EEEEE5", "#E4E4D5", "#F4F4EC", "#BD93F9", "#282A36", "#6272A4")),

            // Warm earthy retro — tan bg, muted olive/brick accents
            ["Gruvbox"]         = (("#282828", "#1D2021", "#3C3836", "#504945", "#32302F", "#D79921", "#EBDBB2", "#A89984"),
                                    ("#FBF1C7", "#F2E5BC", "#EBDBB2", "#D5C4A1", "#F2EAC0", "#AF3A03", "#3C3836", "#7C6F64")),

            // Charcoal black + vivid teal/orange/rose
            ["Monokai"]         = (("#272822", "#1E1F1A", "#3E3D32", "#4E4D42", "#2E2D28", "#F92672", "#F8F8F2", "#75715E"),
                                    ("#F9F8F5", "#F0EFE8", "#E8E7DA", "#DEDDC8", "#F4F3EC", "#F92672", "#272822", "#75715E")),

            // Deep blue-purple bg + pink and bright teal highlights
            ["Tokyo Night"]     = (("#1A1B26", "#16161E", "#292E42", "#2F3549", "#1E1F2E", "#7AA2F7", "#C0CAF5", "#565F89"),
                                    ("#E1E2E7", "#D5D6DC", "#C8C9D0", "#BABBC3", "#DCDDEA", "#2E7DE9", "#3760BF", "#6172B0")),

            // Precision-balanced warm amber + cool teal
            ["Solarized"]       = (("#002B36", "#00212B", "#073642", "#0D4350", "#052F3A", "#268BD2", "#93A1A1", "#657B83"),
                                    ("#FDF6E3", "#F5EDDB", "#EEE8D5", "#E4DDCA", "#F5EED8", "#268BD2", "#657B83", "#93A1A1")),

            // ── Soft / Pastel ─────────────────────────────────────────────────

            // Dark lilac bg + soft violet accent  (Purple ↔ Violet)
            ["Twilight"]        = (("#1C1520", "#160F1A", "#2A1E30", "#382840", "#201828", "#C77DFF", "#E8D8F8", "#B8A0CC"),
                                    ("#F5F0FF", "#EDE8F8", "#E0D8F5", "#D0C8EC", "#EAE4F5", "#9B59B6", "#2D1B4E", "#6B4A8A")),

            // Dark dusty rose bg + vivid rose accent  (Rose ↔ Crimson)
            ["Midnight Rose"]   = (("#201218", "#180C10", "#301A24", "#402030", "#28161E", "#FF8FAB", "#F8E0E8", "#CCA0B0"),
                                    ("#FFF0F5", "#FFE4EC", "#FFD8E4", "#FFC8D8", "#FFEAF0", "#D4437A", "#3D1420", "#8A3050")),

            // Dark teal-green bg + bright aqua accent  (Teal ↔ Aqua)
            ["Lagoon"]          = (("#0E1C18", "#0A1412", "#162C26", "#1E3830", "#12221E", "#00E5B0", "#D0F5EC", "#80C8B0"),
                                    ("#F0FFF8", "#E4FFF2", "#D4FFEC", "#C4FFE4", "#ECFFF5", "#00A878", "#0A2E20", "#2A6A50")),

            // ── Cozy / Warm Neutral ───────────────────────────────────────────

            // Deep espresso brown bg + warm cream text
            ["Espresso"]        = (("#1A0E08", "#110804", "#281808", "#3A2410", "#201206", "#C07030", "#F5E8D8", "#C0A080"),
                                    ("#FFF8F0", "#F5EDE0", "#EEE0CC", "#E4D0B8", "#FAF0E4", "#6F3800", "#2C1400", "#7A4820")),

            // Dark burgundy-wood bg + champagne gold accent
            ["Rosewood"]        = (("#1A0810", "#120508", "#2A1018", "#3A1828", "#200C14", "#C89050", "#F0E0D0", "#C09080"),
                                    ("#FFF5F0", "#FFEAE0", "#FFD8C8", "#FFC8B0", "#FFEDE4", "#8B0020", "#280008", "#6A2030")),

            // Aged paper tan bg + warm ink accent
            ["Parchment"]       = (("#1C1610", "#140E08", "#2A2018", "#382E22", "#221C14", "#A07840", "#EED8A8", "#B09060"),
                                    ("#F8EDD0", "#F0E4C0", "#E8D8A8", "#DCC890", "#F4E4C4", "#806030", "#2A1C08", "#7A5830")),

            // ── Seasonal ─────────────────────────────────────────────────────

            // Deep rust bg + burnt orange accent
            ["Autumn"]          = (("#1A0C08", "#120604", "#2C1408", "#3C2010", "#20100A", "#D06020", "#F5D0A0", "#C08050"),
                                    ("#FFF5E8", "#FFE8D0", "#FFD8B8", "#FFC8A0", "#FFEDE0", "#C03800", "#300800", "#803020")),

            // Near-black cool slate bg + ice blue accent
            ["Winter"]          = (("#0A0E14", "#060810", "#0E1824", "#162230", "#0C141C", "#88CCFF", "#E8F4FF", "#90B0CC"),
                                    ("#F0F8FF", "#E8F4FF", "#DFF0FF", "#D0E8FF", "#EBF5FF", "#2288CC", "#0A1E30", "#4080A8")),

            // Deep warm navy bg + blazing sun-gold accent  (Navy ↔ Gold)
            ["Summer"]          = (("#060E1C", "#030811", "#0C1A32", "#142244", "#0A1628", "#EDAB00", "#FFF5CC", "#C8A840"),
                                    ("#FFFBEE", "#FFF5D8", "#FEFEF0", "#FFEEA0", "#FFFCE0", "#005599", "#020E2C", "#0A3A70")),

            // Deep forest bg + cherry blossom pink accent
            ["Spring"]          = (("#0C1810", "#081008", "#142818", "#1E3820", "#101E14", "#FF80C0", "#D0F0D0", "#80B880"),
                                    ("#F4FFF0", "#EBF8E0", "#DCF0D0", "#CCE8C0", "#EEF8E8", "#C0287A", "#0A2A10", "#3A6A30")),

            // ── Other ─────────────────────────────────────────────────────────

            // Near-black bg + nebula purple accent
            ["Galaxy"]          = (("#080410", "#040208", "#100818", "#180C24", "#0C0614", "#A040FF", "#F0E8FF", "#9080C0"),
                                    ("#F5F0FF", "#EEE8FF", "#E4D8FF", "#D8C8FF", "#F0E8FF", "#7000DD", "#1A0040", "#5010A0")),

            // Pure black bg + vivid yellow accent — maximum readability
            ["High Contrast"]   = (("#000000", "#000000", "#0A0A0A", "#151515", "#060606", "#FFFF00", "#FFFFFF", "#E0E0E0"),
                                    ("#FFFFFF", "#FFFFFF", "#F0F0F0", "#E0E0E0", "#F8F8F8", "#0000CC", "#000000", "#202020")),

            // Warm aged-paper brown bg + amber-gold ink accent  (Umber ↔ Amber)
            ["Antique"]         = (("#180E08", "#100804", "#261808", "#342410", "#1E1208", "#C89050", "#EED890", "#B09050"),
                                    ("#FEF8EC", "#F8EED8", "#F0E4C0", "#E8D8A8", "#FAF2DC", "#8B6020", "#2C1808", "#7A5030")),

            // ── Three-colour  (sorted: bg hue → accent hue) ───────────────────
            //    Each entry: bg-colour family │ accent colour │ text colour

            // Red bg │ Teal accent │ Amber text
            ["Bloodrite"]      = (("#1A0608", "#120404", "#2C0E10", "#3A1418", "#1E080C", "#009999", "#FFBB44", "#CC8810"),
                                   ("#FFF0F2", "#FFE4E8", "#FFFBFC", "#FFD0D8", "#FFF5F6", "#006666", "#996600", "#664400")),

            // Red bg │ Blue accent │ Gold text
            ["Inferno"]        = (("#1E0808", "#140404", "#2E1010", "#3E1818", "#240C0C", "#2255CC", "#FFD966", "#C4A020"),
                                   ("#FFF4F4", "#FFEAEA", "#FFF9F9", "#FFD8D8", "#FFF0F0", "#1A3DBB", "#7A5500", "#553300")),

            // Vermilion bg │ Indigo accent │ Gold text
            ["Magma"]          = (("#1E0E06", "#160A04", "#2C1A0A", "#3C2810", "#221404", "#4433AA", "#FFD966", "#C4A020"),
                                   ("#FFF6EE", "#FFEAD8", "#FFFCF8", "#FFDDBB", "#FFF2E8", "#3322AA", "#664400", "#442200")),

            // Orange bg │ Blue accent │ Lime text
            ["Citrus Storm"]   = (("#1E1206", "#160E04", "#2C1C0A", "#3C2C14", "#221606", "#2266DD", "#AAFF33", "#77CC11"),
                                   ("#FFF8EE", "#FFEEDD", "#FFFDF8", "#FFDDAA", "#FFF4E8", "#1144CC", "#334400", "#1A2800")),

            // Amber bg │ Teal accent │ Rose text
            ["Desert Bloom"]   = (("#1A1200", "#120C00", "#281A00", "#382600", "#1E1600", "#00AAAA", "#FF88BB", "#CC4477"),
                                   ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE4B8", "#FFF5E8", "#007777", "#662233", "#441122")),

            // Amber bg │ Purple accent │ Cyan text
            ["Amber Waves"]    = (("#1A1000", "#120A00", "#281A00", "#382600", "#1E1400", "#9933CC", "#44DDEE", "#20AACC"),
                                   ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE4B8", "#FFF5E8", "#6611AA", "#005566", "#003344")),

            // Yellow bg │ Magenta accent │ Blue text
            ["Bumblebee"]      = (("#1A1A00", "#121200", "#282800", "#383600", "#1E1C00", "#CC22AA", "#44AAFF", "#2266CC"),
                                   ("#FEFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FBFBE8", "#AA1188", "#0033AA", "#001188")),

            // Olive bg │ Crimson accent │ Blue text
            ["Venom"]          = (("#141400", "#0C0C00", "#201E00", "#2E2A00", "#181600", "#CC1122", "#44AAFF", "#2266CC"),
                                   ("#FFFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FBFBE8", "#AA0011", "#0044AA", "#002277")),

            // Lime bg │ Violet accent │ Orange text
            ["Acid Wash"]      = (("#0E1800", "#0A1200", "#182800", "#223800", "#121E00", "#8833EE", "#FF9933", "#CC6600"),
                                   ("#F0FFF0", "#E4FFE0", "#F8FFF6", "#D0FF99", "#EBFFE8", "#6600CC", "#AA4400", "#882200")),

            // Chartreuse bg │ Crimson accent │ Cyan text
            ["Toxic"]          = (("#0C1400", "#080E00", "#142200", "#1E3000", "#101A00", "#CC1122", "#44DDEE", "#20AACC"),
                                   ("#F4FFE4", "#EAFFD0", "#F8FFF2", "#D8FF88", "#EEFFE0", "#AA0011", "#003344", "#002233")),

            // Green bg │ Orange accent │ Blue text
            ["Jungle Fire"]    = (("#0A1A0A", "#061006", "#122814", "#1A3A1C", "#0E200E", "#EE6600", "#66BBFF", "#3388CC"),
                                   ("#EEFFF0", "#DAFFF5", "#F5FFF8", "#CAFFEA", "#E4FFF4", "#BB4400", "#003388", "#001A55")),

            // Green bg │ Gold accent │ Violet text
            ["Emerald Crown"]  = (("#081808", "#041004", "#102810", "#183A18", "#0C1E0C", "#DDAA00", "#BB88FF", "#8844DD"),
                                   ("#EEFFF0", "#DCFFEA", "#F5FFF7", "#C8FFCC", "#E8FFEC", "#AA7700", "#440088", "#330066")),

            // Green bg │ Violet accent │ Gold text
            ["Poison"]         = (("#081608", "#040C04", "#102410", "#183018", "#0C1C0C", "#9933DD", "#FFCC33", "#CC9900"),
                                   ("#EEFFF0", "#DCFFEA", "#F5FFF7", "#C8FFCC", "#E8FFEC", "#6600AA", "#775500", "#553300")),

            // Teal bg │ Crimson accent │ Amber text
            ["Dragon Jade"]    = (("#051E1A", "#031410", "#092E28", "#0F3C34", "#072422", "#DD1122", "#FFCC44", "#DD9900"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#AA0011", "#664400", "#443300")),

            // Teal bg │ Gold accent │ Rose text
            ["Mermaid"]        = (("#041C18", "#021210", "#082C26", "#0E3A32", "#062020", "#DDAA00", "#FF88BB", "#DD4488"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#AA7700", "#881133", "#660022")),

            // Teal bg │ Magenta accent │ Yellow text
            ["Carnival"]       = (("#041C18", "#021210", "#082C26", "#0E3A32", "#062020", "#EE1188", "#FFEE44", "#DDB820"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E8FFFA", "#AA0066", "#886600", "#665500")),

            // Cyan bg │ Purple accent │ Gold text
            ["Arctic Aurora"]  = (("#001C22", "#001418", "#002C34", "#003A44", "#002028", "#8833DD", "#FFCC33", "#CC9900"),
                                   ("#EEFFFF", "#D8FFFF", "#F5FFFF", "#B0FFFF", "#E0FFFF", "#6600AA", "#664400", "#442200")),

            // Cyan bg │ Red accent │ Lime text
            ["Frozen Fire"]    = (("#001C22", "#001418", "#002C34", "#003A44", "#002028", "#EE1122", "#88FF44", "#55CC11"),
                                   ("#EEFFFF", "#D8FFFF", "#F5FFFF", "#B0FFFF", "#E0FFFF", "#CC0011", "#224400", "#1A3300")),

            // Blue bg │ Gold accent │ Teal text
            ["Monarch"]        = (("#080E1C", "#050A12", "#0E1830", "#162040", "#0A1224", "#DDAA00", "#33DDCC", "#22AAAA"),
                                   ("#EEF4FF", "#DDEAFF", "#F5FAFF", "#C8DEFF", "#E8F0FF", "#AA7700", "#005566", "#003344")),

            // Blue bg │ Lime accent │ Orange text
            ["Circuit"]        = (("#080E20", "#060A16", "#0E1834", "#162244", "#0A1228", "#88DD00", "#FF9944", "#CC6600"),
                                   ("#EEF2FF", "#DDEAFF", "#F5F8FF", "#C8D8FF", "#E8F0FF", "#447700", "#AA4400", "#882200")),

            // Blue bg │ Orange accent │ Lime text
            ["Blueprint"]      = (("#080E20", "#060A16", "#0E1834", "#162244", "#0A1228", "#EE6600", "#AAFF33", "#77CC11"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#CC4400", "#334400", "#223300")),

            // Blue bg │ Red accent │ Amber text
            ["Sapphire Flame"] = (("#080E1C", "#060A12", "#0E1830", "#162040", "#0A1224", "#EE1122", "#FFD966", "#CC9920"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#CC0011", "#664400", "#443300")),

            // Indigo bg │ Amber accent │ Cyan text
            ["Starlight"]      = (("#0C0A20", "#080616", "#141030", "#201840", "#100E28", "#DDAA00", "#44DDEE", "#20AACC"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0C8FF", "#E8E4FF", "#886600", "#003344", "#002233")),

            // Indigo bg │ Lime accent │ Rose text
            ["Nebula"]         = (("#0C0A20", "#080616", "#141030", "#201840", "#100E28", "#44DD22", "#FF88AA", "#CC4466"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0C8FF", "#E8E4FF", "#22AA00", "#881133", "#660022")),

            // Purple bg │ Gold accent │ Mint text
            ["Twilight Fire"]  = (("#100818", "#0C0410", "#1C1028", "#281838", "#140C1E", "#DDAA00", "#44FFAA", "#22BB77"),
                                   ("#F5EEFF", "#EDE0FF", "#FBFAFF", "#E0CCFF", "#F2EAFF", "#886600", "#006644", "#004433")),

            // Purple bg │ Lime accent │ Rose text
            ["Witchcraft"]     = (("#0E0618", "#08040E", "#180C28", "#221236", "#120820", "#44DD22", "#FF88AA", "#CC4466"),
                                   ("#F2EEFF", "#E8E0FF", "#FAF8FF", "#DDD0FF", "#F0ECFF", "#228800", "#881133", "#660022")),

            // Violet bg │ Crimson accent │ Lime text
            ["Hex"]            = (("#100820", "#0C0418", "#1C1030", "#281840", "#140C28", "#DD1122", "#88FF44", "#55CC11"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#BB0011", "#224400", "#1A3300")),

            // Magenta bg │ Gold accent │ Blue text
            ["Flamingo"]       = (("#1E0818", "#160510", "#2E1028", "#3E183A", "#240C1E", "#DDAA00", "#44AAFF", "#2266CC"),
                                   ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8EE", "#FFF5FB", "#AA7700", "#0033AA", "#001188")),

            // Magenta bg │ Teal accent │ Lime text
            ["Cherry Bomb"]    = (("#1E0818", "#160510", "#2E1028", "#3E183A", "#240C1E", "#00AAAA", "#AAFF44", "#77CC11"),
                                   ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8EE", "#FFF5FB", "#007777", "#334400", "#1A3300")),

            // Dark bg │ Cyan accent │ Pink text
            ["Neon Trinity"]   = (("#080810", "#050508", "#101018", "#181820", "#0C0C14", "#00CCDD", "#FF66AA", "#CC3377"),
                                   ("#F0F0FF", "#E8E8F8", "#FAFAFF", "#DCDCE8", "#F5F5FF", "#006677", "#881155", "#660033")),

            // ── New two-colour themes ─────────────────────────────────────────
            // Red bg │ Electric blue accent
            ["Blood Moon"]     = (("#200606", "#160404", "#301010", "#401818", "#280A0A", "#1144CC", "#FFCCCC", "#CC8888"),
                                   ("#FFF2F2", "#FFE8E8", "#FFFAFA", "#FFD8D8", "#FFF5F5", "#0033BB", "#300808", "#882222")),

            // Deep wine red bg │ Amber-gold accent
            ["Garnet"]         = (("#1E0C0C", "#150808", "#2E1818", "#3E2626", "#221212", "#BB8800", "#FFDDAA", "#CC9944"),
                                   ("#FFF4F0", "#FFEAE0", "#FFFBF8", "#FFE0CC", "#FFF2E8", "#996600", "#3A1008", "#882210")),

            // Scorched ember bg │ Cyan accent
            ["Ember"]          = (("#1C0A04", "#140602", "#2A1008", "#3A1C10", "#220C06", "#00BBCC", "#FFD0AA", "#CC8855"),
                                   ("#FFF4EE", "#FFE8DC", "#FFFBF8", "#FFE0CC", "#FFF8F2", "#007788", "#381008", "#882210")),

            // Warm dark amber bg │ Seafoam accent
            ["Cinder"]         = (("#1C1400", "#140E00", "#2A2000", "#3A2E00", "#201800", "#00AAAA", "#FFE8AA", "#CCBB55"),
                                   ("#FFF8EC", "#FFF0D8", "#FFFDF8", "#FFE8C0", "#FFF4EC", "#007777", "#381E00", "#886600")),

            // Dark amber-brown bg │ Indigo accent
            ["Harvest"]        = (("#1A1200", "#120C00", "#281A00", "#382800", "#1E1600", "#4444BB", "#FFEEBB", "#CCBB66"),
                                   ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE8C0", "#FFF4EA", "#2233AA", "#382000", "#886600")),

            // Dark forest green bg │ Rose accent
            ["Fern"]           = (("#081E08", "#041408", "#102C10", "#1A3C1A", "#0C2210", "#FF5599", "#AAFFBB", "#66CC77"),
                                   ("#F0FFF0", "#E4FFE4", "#F8FFF8", "#D0FFD0", "#EAFFF0", "#CC1155", "#082808", "#226622")),

            // Dark jungle green bg │ Purple accent
            ["Canopy"]         = (("#0A1C0A", "#061206", "#102A10", "#183818", "#0E2010", "#8833CC", "#AAFFAA", "#66BB55"),
                                   ("#F0FFF0", "#E4FFE4", "#F8FFF8", "#D0FFD0", "#EAFFEA", "#5511AA", "#0A2A0A", "#226622")),

            // Dark teal bg │ Violet accent
            ["Seafoam"]        = (("#061C1A", "#041210", "#0C2A28", "#123836", "#081E1C", "#8833CC", "#AAFFEE", "#55CCBB"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#5511AA", "#031C18", "#1A6060")),

            // Deep ocean teal bg │ Amber accent
            ["Abyss"]          = (("#021810", "#011008", "#062418", "#0A3022", "#041A12", "#DDAA00", "#88FFDD", "#44BBAA"),
                                   ("#EDFFF8", "#DAFFF0", "#F5FFFC", "#C0FFE8", "#E8FFF5", "#AA7700", "#021C10", "#006044")),

            // Deep navy bg │ Rose accent
            ["Neptune"]        = (("#040A1E", "#020614", "#081030", "#0E1840", "#060C24", "#FF4488", "#AACCFF", "#6688CC"),
                                   ("#EEF2FF", "#DDEAFF", "#F5F8FF", "#C8DEFF", "#E8F0FF", "#CC0055", "#040E2E", "#1A2E88")),

            // Icy steel-blue bg │ Crimson accent
            ["Frostbite"]      = (("#061624", "#04101A", "#0C2236", "#122E48", "#081C2C", "#CC1133", "#BBDDFF", "#7799BB"),
                                   ("#EEF4FF", "#E0EBFF", "#F5FAFF", "#CCDEFF", "#E8F2FF", "#AA0022", "#061830", "#1A3A70")),

            // Deep indigo bg │ Crimson accent
            ["Smoke"]          = (("#0E0C22", "#090818", "#161430", "#201E40", "#12102A", "#CC1133", "#CCBBFF", "#8877AA"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0C8FF", "#E8E4FF", "#AA0022", "#120A30", "#3A2088")),

            // Deep indigo bg │ Amber accent
            ["Phantom"]        = (("#0E0A1E", "#090616", "#16102C", "#20183C", "#120C24", "#DDAA22", "#CCBBFF", "#8877AA"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0C8FF", "#E8E4FF", "#AA7700", "#100A28", "#3A2088")),

            // Violet bg │ Teal accent
            ["Iris"]           = (("#120820", "#0C0418", "#1C1030", "#281840", "#160C28", "#00BBAA", "#CCAAFF", "#9966CC"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#008878", "#180830", "#3A1888")),

            // Purple bg │ Cyan accent
            ["Amethyst"]       = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C2A", "#00CCDD", "#D4AAFF", "#A070D0"),
                                   ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#008899", "#200840", "#3A1088")),

            // ── New three-colour themes ───────────────────────────────────────
            // Scarlet bg │ Teal accent │ Gold text
            ["Sanguine"]       = (("#1A0606", "#120404", "#2A0E0E", "#3A1616", "#1E0808", "#009999", "#FFCC33", "#CC9900"),
                                   ("#FFF0F2", "#FFE4E8", "#FFFBFC", "#FFD0D8", "#FFF5F6", "#006666", "#7A5500", "#5A3A00")),

            // Yellow-green bg │ Orange accent │ Violet text
            ["Swamp Gas"]      = (("#0E1600", "#0A1000", "#182400", "#223200", "#121C00", "#EE7700", "#BB88FF", "#8844DD"),
                                   ("#F4FFE4", "#E8FFD0", "#F8FFF2", "#D8FF80", "#EEFFE0", "#AA4400", "#440088", "#330066")),

            // Orange-amber bg │ Blue accent │ Rose text
            ["Sundial"]        = (("#1C1004", "#140A02", "#2A1808", "#3A2410", "#201408", "#2244CC", "#FFB8AA", "#DD7755"),
                                   ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE0C0", "#FFF4E8", "#1133AA", "#661122", "#441122")),

            // Yellow-orange bg │ Teal accent │ Rose text
            ["Prairie"]        = (("#1A1600", "#120E00", "#282200", "#383000", "#1E1C00", "#00AAAA", "#FF88BB", "#CC4477"),
                                   ("#FFF8EE", "#FFF0D8", "#FFFDF8", "#FFE8B8", "#FFF5E8", "#007777", "#882233", "#661122")),

            // Blue-purple bg │ Amber accent │ Lime text
            ["Deep Space"]     = (("#0C1028", "#080C1E", "#14183A", "#1E224E", "#101430", "#DDAA00", "#AAFF44", "#77CC11"),
                                   ("#F0EEFF", "#E4E0FF", "#F8F6FF", "#D4CCFF", "#ECEAFF", "#AA7700", "#2A4400", "#1A3300")),

            // Blue-purple bg │ Rose accent │ Cyan text
            ["Cosmos"]         = (("#0A0C24", "#060818", "#121436", "#1C1E48", "#0C1028", "#FF4488", "#44DDEE", "#20AACC"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0CCFF", "#E8E4FF", "#CC0055", "#003344", "#005566")),

            // Blue bg │ Crimson accent │ Gold text
            ["Opal"]           = (("#060E24", "#040A1A", "#0C1836", "#122246", "#08102A", "#CC1133", "#FFDD66", "#CCAA22"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#AA0022", "#664400", "#443300")),

            // Blue bg │ Purple accent │ Rose text
            ["Cascade"]        = (("#080E20", "#060A16", "#0E1834", "#162244", "#0A1228", "#9933CC", "#FF88AA", "#DD4477"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#6611AA", "#661133", "#440022")),

            // Violet bg │ Amber accent │ Teal text
            ["Wraith"]         = (("#100820", "#0C0418", "#1C1030", "#281840", "#140C28", "#DDAA00", "#44DDCC", "#22AAAA"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#AA7700", "#003344", "#004455")),

            // Violet bg │ Cyan accent │ Gold text
            ["Seraph"]         = (("#120A22", "#0C0618", "#1E1234", "#2A1C44", "#160E2C", "#00CCDD", "#FFCC44", "#DDA820"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#008899", "#664400", "#443300")),

            // Purple bg │ Crimson accent │ Cyan text
            ["Sorcerer"]       = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C2A", "#DD1122", "#44DDEE", "#20AACC"),
                                   ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#AA0011", "#003344", "#002233")),

            // Purple bg │ Orange accent │ Teal text
            ["Specter"]        = (("#140A20", "#0E0616", "#201030", "#2C1840", "#180C28", "#EE7700", "#44DDCC", "#22AAAA"),
                                   ("#F5F0FF", "#EDE4FF", "#FDFBFF", "#E0D4FF", "#F8F4FF", "#AA4400", "#003344", "#002233")),

            // Magenta bg │ Blue accent │ Lime text
            ["Vivid"]          = (("#1E0818", "#160510", "#2E1028", "#3E183A", "#240C1E", "#2244DD", "#88FF44", "#55CC11"),
                                   ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8EE", "#FFF5FB", "#1133BB", "#2A4400", "#1A3300")),

            // Deep magenta-purple bg │ Cyan accent │ Yellow text
            ["Candy"]          = (("#1A0820", "#120516", "#2A1030", "#3A1840", "#200C28", "#00CCEE", "#FFEE44", "#DDCC11"),
                                   ("#FFF0FC", "#FFE0F8", "#FFFCFF", "#FFC8F4", "#FFF4FC", "#007788", "#664400", "#443300")),

            // Near-black bg │ Gold accent │ Violet text
            ["Midnight Purple"]= (("#0A080C", "#060408", "#121018", "#1C1824", "#0E0C10", "#DDAA00", "#CC88FF", "#9944DD"),
                                   ("#F5F4F8", "#ECEAF0", "#FDFCFF", "#E4E0EC", "#F8F6FC", "#AA7700", "#440088", "#330066")),

            // ── Additional two-colour themes ──────────────────────────────────

            // Dark wine-rose bg │ Electric blue accent
            ["Dusk"]           = (("#1E0C14", "#140810", "#2E1820", "#3E2030", "#22101A", "#1166DD", "#FFBBCC", "#CC7788"),
                                   ("#FFF0F5", "#FFE4EC", "#FFFBFD", "#FFD0E0", "#FFF5F8", "#0044BB", "#380010", "#880030")),

            // Red bg │ Lime accent
            ["Crimson"]        = (("#1E0808", "#150606", "#2E1010", "#3E1818", "#241010", "#88FF00", "#FFCCCC", "#CC8888"),
                                   ("#FFF0F0", "#FFDDE0", "#FFFBFB", "#FFD0D0", "#FFF5F5", "#559900", "#3A0A0A", "#882222")),

            // Yellow bg │ Blue accent
            ["Canary"]         = (("#1A1A00", "#131300", "#282600", "#363400", "#1E1C00", "#2244BB", "#F0EE88", "#C8C040"),
                                   ("#FEFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FCFCE8", "#1133AA", "#2A2800", "#666600")),

            // Yellow bg │ Violet accent
            ["Sunflower"]      = (("#1A1A00", "#131200", "#282600", "#363400", "#1E1C00", "#7722CC", "#F0EE88", "#C8C040"),
                                   ("#FEFEE8", "#FAFAD0", "#FEFEFC", "#F8F880", "#FCFCE8", "#5511AA", "#2A2800", "#666600")),

            // Chartreuse bg │ Crimson accent
            ["Acid"]           = (("#0C1400", "#080E00", "#142000", "#1E2E00", "#101800", "#CC1133", "#DDFF88", "#AACC44"),
                                   ("#F6FFE8", "#EAFFD0", "#FBFFF5", "#D8FF80", "#F0FFE0", "#AA0022", "#1A2800", "#3A6600")),

            // Chartreuse bg │ Purple accent
            ["Neon Moss"]      = (("#0A1200", "#060C00", "#121E00", "#1C2A00", "#0E1600", "#8833CC", "#CCFF88", "#88BB44"),
                                   ("#F4FFE8", "#E8FFD0", "#FAFFEE", "#D0FF88", "#EEFFD8", "#5511AA", "#162800", "#336600")),

            // Lime bg │ Rose accent
            ["Slime"]          = (("#0E1800", "#0A1200", "#182800", "#223800", "#121E00", "#FF4499", "#BBFF44", "#88CC20"),
                                   ("#F4FFEE", "#E8FFD8", "#FBFFFA", "#D0FF99", "#F8FFE8", "#CC1155", "#1A3A00", "#336600")),

            // Lime bg │ Royal blue accent
            ["Kiwi"]           = (("#0C1600", "#080E00", "#162400", "#203200", "#101C00", "#2244CC", "#AAFF44", "#77CC20"),
                                   ("#F2FFEE", "#E4FFD8", "#FAFFEE", "#CCFF88", "#EEFFE0", "#1133AA", "#182A00", "#2A5000")),

            // Dark cool blue-grey bg │ Rose accent
            ["Slate"]          = (("#0C1018", "#080C12", "#121820", "#1C2430", "#0E141C", "#FF4488", "#C8D8E8", "#7898B8"),
                                   ("#F0F4FF", "#E4EAFF", "#F8FAFF", "#D0DCFF", "#ECF2FF", "#CC0055", "#0C1828", "#2A4060")),

            // Violet bg │ Rose accent
            ["Plum"]           = (("#120820", "#0C0418", "#1C1030", "#281840", "#160C28", "#FF4488", "#CCAAFF", "#9966CC"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#CC0055", "#180830", "#3A1888")),

            // Violet bg │ Lime accent
            ["Ultraviolet"]    = (("#100A22", "#0C0618", "#1A1232", "#261A42", "#140E2C", "#88FF00", "#CCAAFF", "#9966CC"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#55AA00", "#180830", "#3A1888")),

            // Rich purple bg │ Gold accent
            ["Velvet"]         = (("#180A22", "#120618", "#241034", "#301A46", "#1C0E2C", "#DDAA00", "#D4AAFF", "#A070D0"),
                                   ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#AA7700", "#200840", "#3A1088")),

            // Purple bg │ Orange accent
            ["Reverie"]        = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C2A", "#EE7700", "#D4AAFF", "#A070D0"),
                                   ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#BB4400", "#200840", "#3A1088")),

            // Deep magenta bg │ Teal accent
            ["Orchid"]         = (("#1E0818", "#160510", "#2E1028", "#3E183A", "#240C1E", "#00BBAA", "#FFAAEE", "#CC66BB"),
                                   ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8EE", "#FFF5FB", "#008878", "#380030", "#880888")),

            // Deep fuchsia bg │ Gold accent
            ["Fuchsia"]        = (("#1C0618", "#140410", "#2C1028", "#3C1838", "#200A20", "#DDAA00", "#FFAAEE", "#CC66BB"),
                                   ("#FFF0FA", "#FFE0F4", "#FFFCFE", "#FFC8EE", "#FFF4FA", "#AA7700", "#380030", "#880888")),

            // ── Additional three-colour themes ────────────────────────────────

            // Red bg │ Purple accent │ Lime text
            ["Tempest"]        = (("#1E0808", "#140404", "#2E1010", "#3E1818", "#240C0C", "#9933CC", "#AAFF44", "#77CC11"),
                                   ("#FFF4F4", "#FFEAEA", "#FFF9F9", "#FFD8D8", "#FFF0F0", "#6611AA", "#334400", "#1A3300")),

            // Orange bg │ Indigo accent │ Mint text
            ["Bonfire"]        = (("#1E1508", "#150F05", "#2E2010", "#3E2C18", "#241A08", "#4433AA", "#AAFFCC", "#66CCAA"),
                                   ("#FFF6EE", "#FFEAD8", "#FFFBF7", "#FFDCB8", "#FFF8F2", "#2222AA", "#006644", "#004433")),

            // Amber bg │ Crimson accent │ Blue text
            ["Gilded"]         = (("#1A1800", "#131200", "#282400", "#363200", "#1E1C00", "#CC1133", "#44AAFF", "#2266CC"),
                                   ("#FFFBEE", "#FFF5D8", "#FFFEF8", "#FFEEB8", "#FFFDF0", "#AA0022", "#003388", "#002266")),

            // Lime bg │ Gold accent │ Purple text
            ["Wildgrass"]      = (("#0E1800", "#0A1200", "#182800", "#223800", "#121E00", "#DDAA00", "#BB88FF", "#8844DD"),
                                   ("#F4FFEE", "#E8FFD8", "#FBFFFA", "#D0FF99", "#F8FFE8", "#AA7700", "#440088", "#330066")),

            // Dark green bg │ Rose accent │ Gold text
            ["Bayou"]          = (("#081E08", "#041408", "#102C10", "#1A3C1A", "#0C2210", "#FF4488", "#FFCC33", "#CC9900"),
                                   ("#EEFAF0", "#DCFFF0", "#F8FFFB", "#C8ECC8", "#F2FCF4", "#CC0055", "#7A5500", "#5A3A00")),

            // Dark teal bg │ Blue accent │ Yellow text
            ["Gale"]           = (("#041C1A", "#021210", "#082A28", "#0E3836", "#061E1C", "#2266CC", "#FFEE44", "#DDCC11"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#1144AA", "#665500", "#443300")),

            // Dark cyan bg │ Violet accent │ Rose text
            ["Prismatic"]      = (("#001C22", "#001418", "#002C34", "#003A44", "#002028", "#8833CC", "#FF88AA", "#DD4477"),
                                   ("#EEFFFF", "#D8FFFF", "#F5FFFF", "#B0FFFF", "#E0FFFF", "#5511AA", "#881133", "#660022")),

            // Blue bg │ Teal accent │ Rose text
            ["Horizon"]        = (("#0C1224", "#080D1A", "#101E38", "#18304C", "#0E162C", "#00AAAA", "#FF88AA", "#DD4477"),
                                   ("#EEF2FF", "#DCE6FF", "#F8FAFF", "#BCCEFF", "#F2F6FF", "#007777", "#881133", "#660022")),

            // Indigo bg │ Orange accent │ Rose text
            ["Vortex"]         = (("#100C22", "#0C0818", "#1A1234", "#261A46", "#140E28", "#EE7700", "#FF88AA", "#DD4477"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#CCC8FF", "#F5F4FF", "#AA4400", "#881133", "#660022")),

            // Indigo bg │ Red accent │ Gold text
            ["Phantasm"]       = (("#0E0A1E", "#090616", "#16102C", "#20183C", "#120C24", "#DD1122", "#FFCC33", "#DDAA11"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0C8FF", "#E8E4FF", "#BB0011", "#7A5500", "#5A3A00")),

            // Violet bg │ Blue accent │ Gold text
            ["Cryptic"]        = (("#120820", "#0C0418", "#1C1030", "#281840", "#160C28", "#2255CC", "#FFCC33", "#DDAA11"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#1133AA", "#7A5500", "#5A3A00")),

            // Purple bg │ Teal accent │ Gold text
            ["Coven"]          = (("#160A22", "#100618", "#221034", "#2E1846", "#1A0C2A", "#00AAAA", "#FFCC33", "#DDAA11"),
                                   ("#F8F0FF", "#EEE0FF", "#FDFAFF", "#E0C8FF", "#FAF4FF", "#007777", "#7A5500", "#5A3A00")),

            // Magenta bg │ Lime accent │ Orange text
            ["Disco"]          = (("#1E0818", "#160510", "#2E1028", "#3E183A", "#240C1E", "#88FF00", "#FFAA22", "#CC7700"),
                                   ("#FFF0FA", "#FFE0F5", "#FFFCFE", "#FFC8EE", "#FFF5FB", "#55AA00", "#994400", "#773300")),

            // Near-black bg │ Purple accent │ Teal text
            ["Void"]           = (("#080810", "#050508", "#101018", "#181820", "#0C0C14", "#9933CC", "#44DDCC", "#22AAAA"),
                                   ("#F0F0FF", "#E8E8F8", "#FAFAFF", "#DCDCE8", "#F5F5FF", "#6611AA", "#003344", "#004455")),

            // Dark warm-grey bg │ Crimson accent │ Amber text
            ["Forge"]          = (("#181410", "#100E0C", "#242018", "#302C24", "#1C1814", "#CC1133", "#FFCC44", "#DDAA11"),
                                   ("#FFF8F0", "#FFF0E0", "#FFFCF8", "#FFE8D0", "#FFF4EC", "#AA0022", "#7A5500", "#5A3A00")),

            // ── Neutral single-colour themes ──────────────────────────────────

            // Warm brown neutral
            ["Cocoa"]          = (("#1C1008", "#140A06", "#2A1A10", "#3A2820", "#201410", "#AA6633", "#F5DCC4", "#CCAA88"),
                                   ("#FFF8F2", "#FFEEDD", "#FFFCF8", "#FFE0C8", "#FFF4EA", "#884422", "#3A1A10", "#662210")),

            // Cool blue-grey neutral
            ["Silver"]         = (("#0E1014", "#080A0E", "#161820", "#20222A", "#121416", "#8899BB", "#CCD8EE", "#9AAABB"),
                                   ("#F0F2F8", "#E4E8F0", "#F8FAFF", "#D4D8E4", "#ECEEF8", "#445577", "#1A2030", "#2A3040")),

            // Warm sandy neutral
            ["Sand"]           = (("#1E1A10", "#161208", "#2C2618", "#3C3428", "#221E14", "#AA9955", "#F5E8C8", "#CCBB88"),
                                   ("#FFF8EC", "#FFF0D8", "#FFFDF8", "#FFE8C0", "#FFF4E8", "#886633", "#3A2E10", "#664400")),

            // ── New two-colour themes (spectrum order: Red → Rose) ────────────

            // Deep red bg │ Cyan accent
            ["Ruby"]           = (("#200808", "#160404", "#301010", "#401818", "#280C0C", "#00BBDD", "#FFCCCC", "#CC8888"),
                                   ("#FFF2F2", "#FFE8E8", "#FFFBFB", "#FFD0D0", "#FFF5F5", "#007799", "#380808", "#882222")),

            // Scorched deep red bg │ Yellow-green acid accent
            ["Dragon Blood"]   = (("#200306", "#160204", "#30060A", "#400C10", "#280406", "#AADD00", "#FFBBBB", "#DD7777"),
                                   ("#FFF0F2", "#FFE0E8", "#FFFBFC", "#FFC8D0", "#FFF5F6", "#667700", "#380808", "#882222")),

            // Orange bg │ Teal accent
            ["Tangerine"]      = (("#1E1208", "#150C06", "#2C1C10", "#3A2818", "#221608", "#00BBAA", "#FFD4AA", "#CC8855"),
                                   ("#FFF6EC", "#FFEAD8", "#FFFBF7", "#FFD8B8", "#FFF0E8", "#007777", "#441A00", "#884400")),

            // Orange bg │ Violet accent
            ["Pumpkin"]        = (("#1C1006", "#140A04", "#2A1808", "#3A2410", "#201408", "#AA33CC", "#FFD4AA", "#CC8855"),
                                   ("#FFF6EE", "#FFEADC", "#FFFBF8", "#FFD8B8", "#FFF2E8", "#771199", "#441A00", "#884400")),

            // Deep amber bg │ Navy accent
            ["Honey"]          = (("#1C1800", "#141200", "#2A2400", "#3A3200", "#201C00", "#2244AA", "#FFEE88", "#DDCC44"),
                                   ("#FEFDE8", "#FAF8D0", "#FDFCE0", "#F8F488", "#FCFCE8", "#112299", "#442200", "#886600")),

            // Dark forest green bg │ Amber accent
            ["Woodland"]       = (("#081A08", "#041206", "#102810", "#183818", "#0C1E0C", "#CC8800", "#AAEEAA", "#66BB66"),
                                   ("#F0FFF0", "#E4FFE4", "#F8FFF8", "#D0FFD0", "#EAFFF0", "#996600", "#0A2A0A", "#226622")),

            // Soft green bg │ Rose accent
            ["Meadow"]         = (("#0A1A0A", "#061206", "#102A10", "#183A18", "#0E1E0E", "#FF4499", "#AAFFAA", "#66CC66"),
                                   ("#F0FFF0", "#E4FFE4", "#F8FFF8", "#D0FFD0", "#EAFFEA", "#CC1155", "#0A2A0A", "#226622")),

            // Dark teal bg │ Coral/salmon accent
            ["Sea Coral"]      = (("#061A18", "#041210", "#0C2826", "#123634", "#081C1A", "#FF6644", "#AAFFEE", "#55CCBB"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#CC3311", "#031C18", "#1A6060")),

            // Deep navy bg │ Gold accent
            ["Royal"]          = (("#080E22", "#060A18", "#0E1634", "#141E44", "#0A102A", "#DDAA00", "#AACCFF", "#6688CC"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#AA7700", "#040E2E", "#1A2E88")),

            // Very dark navy bg │ Silver-blue accent
            ["Inkwell"]        = (("#060810", "#040608", "#0C101A", "#141822", "#08090E", "#CCDDFF", "#AABBEE", "#7788BB"),
                                   ("#F0F4FF", "#E4EBFF", "#F8FAFF", "#D4DDFF", "#ECF0FF", "#334488", "#0A0C1E", "#1A2060")),

            // Deep violet-grey bg │ Hot pink accent
            ["Twilight Bloom"] = (("#0E0820", "#080418", "#160E30", "#201640", "#120A28", "#FF77BB", "#CCBBFF", "#9977CC"),
                                   ("#F2EEFF", "#E8E0FF", "#F8F5FF", "#DACCFF", "#EEEAFF", "#CC0066", "#180830", "#3A1888")),

            // Deep blue-purple bg │ Amber accent
            ["Topaz"]          = (("#0A0C1E", "#060810", "#121430", "#1A1E40", "#0C1028", "#DDAA33", "#AABBFF", "#7788CC"),
                                   ("#EEF0FF", "#E0E6FF", "#F5F8FF", "#C8D4FF", "#E8EEFF", "#AA7700", "#060E30", "#1A2A88")),

            // Deep rose bg │ Teal accent
            ["Valentine"]      = (("#200616", "#160410", "#300A22", "#401232", "#28081C", "#00AAAA", "#FFBBDD", "#DD77AA"),
                                   ("#FFF0F8", "#FFE0EE", "#FFFCFE", "#FFC8E0", "#FFF5FA", "#007777", "#380010", "#880030")),

            // Deep rose-pink bg │ Lavender accent
            ["Cherry Blossom"] = (("#1E0810", "#160608", "#2E1020", "#3E1A2C", "#221020", "#9966DD", "#FFAABB", "#DD6688"),
                                   ("#FFF0F5", "#FFE0EC", "#FFFBFE", "#FFC8D8", "#FFF5F8", "#6633BB", "#380010", "#880030")),

            // ── New three-colour themes (spectrum order: bg hue → accent hue) ─

            // Deep red-black bg │ Orange-red accent │ Bright yellow text
            ["Hellfire"]       = (("#1A0202", "#120000", "#280806", "#38100A", "#200404", "#EE4400", "#FFEE44", "#DDCC00"),
                                   ("#FFF5F5", "#FFECEC", "#FFFBFB", "#FFD8D8", "#FFF0F0", "#CC2200", "#664400", "#443300")),

            // Deep amber-orange bg │ Lime accent │ Red text
            ["Citrus"]         = (("#1C1200", "#140C00", "#2A1C00", "#3A2A00", "#201600", "#AAEE00", "#FF6644", "#DD3311"),
                                   ("#FFF8EC", "#FFEECC", "#FFFDF8", "#FFE8B8", "#FFF4E8", "#667700", "#881100", "#660000")),

            // Dark amber bg │ Sienna accent │ Teal text
            ["Beekeeper"]      = (("#1C1800", "#141200", "#2A2400", "#3A3200", "#201C00", "#995533", "#44DDCC", "#22AAAA"),
                                   ("#FEFDE8", "#FAF8D0", "#FDFCE0", "#F8F488", "#FCFCE8", "#773300", "#003344", "#002233")),

            // Dark forest green bg │ Sky blue accent │ Gold text
            ["Grassland"]      = (("#081A08", "#041206", "#102810", "#183818", "#0C1E0C", "#2299CC", "#FFDD55", "#CCAA22"),
                                   ("#F0FFF0", "#E4FFE4", "#F8FFF8", "#D0FFD0", "#EAFFF0", "#116688", "#665500", "#443300")),

            // Dark teal bg │ Rose accent │ Amber text
            ["Tidepools"]      = (("#061C1A", "#041210", "#0C2A28", "#123836", "#081E1C", "#FF4499", "#FFCC55", "#DDAA22"),
                                   ("#EDFFFE", "#DAFFF8", "#F5FFFC", "#B0FFEE", "#E0FFFA", "#CC0055", "#775500", "#553300")),

            // Very dark blue-teal bg │ Ice-cyan accent │ Ember-orange text
            ["Fjord"]          = (("#040E1A", "#020A12", "#08162A", "#0E2038", "#060C1E", "#44CCEE", "#FF8844", "#DD5522"),
                                   ("#EAF4FF", "#D4ECFF", "#F2F8FF", "#C0E4FF", "#E4F0FF", "#0077AA", "#883300", "#661100")),

            // Deep dark cyan bg │ Lime accent │ Hot pink text
            ["Aquifer"]        = (("#021818", "#011010", "#062424", "#0A3030", "#041A1A", "#88DD00", "#FF6699", "#DD2266"),
                                   ("#EAFFFF", "#D0FFFF", "#F5FFFF", "#A8FFFF", "#DEFFFF", "#226600", "#881133", "#660022")),

            // Dark blue bg │ Yellow accent │ Violet text
            ["Prism"]          = (("#060E22", "#040A18", "#0C1434", "#121E44", "#08102A", "#FFDD00", "#CC88FF", "#9944DD"),
                                   ("#EEF2FF", "#DCE6FF", "#F5F8FF", "#C8D8FF", "#E4EEFF", "#AA8800", "#440088", "#330066")),

            // Near-black bg │ Silver-cyan accent │ Warm amber text
            ["Lunar"]          = (("#080A0C", "#040608", "#101418", "#181C22", "#0C0E10", "#88CCDD", "#FFD066", "#CCAA33"),
                                   ("#F4F8FC", "#E8F0F8", "#FAFCFF", "#D8E8F4", "#F0F4FA", "#3366AA", "#664400", "#443300")),

            // Deep indigo bg │ Gold accent │ Rose text
            ["Oracle"]         = (("#0C0A20", "#080618", "#141232", "#1E1A42", "#100E28", "#DDAA00", "#FF88BB", "#DD4477"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D4CCFF", "#ECEAFF", "#AA7700", "#881133", "#660022")),

            // Dark purple-blue bg │ Orange-red accent │ Cyan text
            ["Dusk Storm"]     = (("#0A0C22", "#060818", "#121436", "#1A1C46", "#0C1028", "#EE5500", "#44DDEE", "#22AACC"),
                                   ("#F0EEFF", "#E4E0FF", "#FAFAFF", "#D0CCFF", "#E8E4FF", "#CC2200", "#003344", "#002233")),

            // Deep violet-purple bg │ Rose-magenta accent │ Pale gold text
            ["Prophecy"]       = (("#120A22", "#0C0618", "#1C1034", "#261844", "#160C2A", "#EE2288", "#FFDD66", "#DDBB33"),
                                   ("#F4EEFF", "#EAE0FF", "#FAFAFF", "#D8CCFF", "#F0EAFF", "#CC0066", "#887700", "#665500")),

            // Deep dark violet bg │ Amber accent │ Mint-teal text
            ["Mirage"]         = (("#0E0A20", "#080616", "#161030", "#201840", "#120C28", "#BB8800", "#44DDBB", "#22AAAA"),
                                   ("#F2EEFF", "#E8E0FF", "#F8F6FF", "#DDD8FF", "#EEE8FF", "#996600", "#003344", "#002233")),

            // Very dark near-black bg │ Silver accent │ Crimson text
            ["Obsidian"]       = (("#080808", "#040404", "#101010", "#181818", "#0C0C0C", "#AABBCC", "#FF1133", "#CC0022"),
                                   ("#F5F5F5", "#EEEEEE", "#FAFAFA", "#E0E0E0", "#F2F2F2", "#556677", "#880011", "#660011")),
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

        WriteThemeJson(palette.Item1, palette.Item3, palette.Item6, palette.Item7, palette.Item8, palette.Item4);

        static SolidColorBrush Brush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));
    }

    private static void WriteThemeJson(
        string appBg, string cardBg, string borderColor,
        string textPrimary, string textSecondary, string cardHover)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                bg       = appBg,
                cardBg,
                border   = borderColor,
                text     = textPrimary,
                textSub  = textSecondary,
                btnBg    = cardHover,
                btnHover = borderColor,
                accent   = borderColor,
                urlColor = borderColor,
            });
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ParentalControl");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "theme.json"), json);
        }
        catch { }
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
            // Start watchdog first — it will start the main service at its own startup.
            using var wdog = new ServiceController(WatchdogServiceName);
            if (wdog.Status != ServiceControllerStatus.Running &&
                wdog.Status != ServiceControllerStatus.StartPending)
            {
                wdog.Start();
                wdog.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        }
        catch { /* Watchdog may not be installed yet — fall through to start main directly */ }

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
