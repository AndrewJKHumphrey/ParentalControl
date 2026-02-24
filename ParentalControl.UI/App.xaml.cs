using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN EnforceForAdmins INTEGER NOT NULL DEFAULT 1"); }
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
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsAdminSession INTEGER NOT NULL DEFAULT 0"); }
            catch { /* Column already exists */ }
            try { db.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN AdminSessionId INTEGER NOT NULL DEFAULT -1"); }
            catch { /* Column already exists */ }

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

        // Show login
        var login = new LoginWindow();
        if (login.ShowDialog() != true)
        {
            Shutdown(0);
            return;
        }

        // Ensure service is running
        EnsureServiceRunning();

        // Record whether this UI session is running as an admin so the service
        // can skip enforcement without needing privileged P/Invoke.
        SetAdminSessionFlag();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        Current.MainWindow = main;
        main.Show();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle, int tokenInformationClass,
        out int tokenInformation, int tokenInformationLength,
        out int returnLength);

    private static bool IsCurrentUserAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();

            // GetTokenInformation with TokenElevationType (18) correctly identifies
            // admins even when UAC is active and the token is filtered:
            //   TokenElevationTypeFull    (1) = running elevated
            //   TokenElevationTypeLimited (3) = admin account, filtered by UAC
            //   TokenElevationTypeDefault (2) = standard user, or admin with UAC disabled
            if (GetTokenInformation(identity.Token, 18,
                    out int elevType, sizeof(int), out _))
            {
                if (elevType == 1 || elevType == 3) return true;
            }

            // Fallback: direct role check handles UAC-disabled systems where
            // the full admin token is used without splitting
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void SetAdminSessionFlag()
    {
        try
        {
            bool isAdmin = IsCurrentUserAdmin();

            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            settings.IsAdminSession = isAdmin;
            settings.AdminSessionId = isAdmin ? Process.GetCurrentProcess().SessionId : -1;
            db.SaveChanges();
        }
        catch { }
    }

    private static void ClearAdminSessionFlag()
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            settings.IsAdminSession = false;
            settings.AdminSessionId = -1;
            db.SaveChanges();
        }
        catch { }
    }

    private void App_Exit(object sender, ExitEventArgs e)
    {
        ClearAdminSessionFlag();
    }

    public static void ApplyTheme(string name, bool isDark = true)
    {
        // Each entry: (dark palette, light palette)
        // Palette order: AppBg, SidebarBg, CardBg, CardHover, AltRowBg, BorderColor, TextPrimary, TextSecondary
        var themes = new Dictionary<string, (
            (string, string, string, string, string, string, string, string) dark,
            (string, string, string, string, string, string, string, string) light)>
        {
            ["Default"]    = (("#1E1E2E", "#181825", "#313244", "#45475A", "#2A2A3E", "#585B70", "#CDD6F4", "#A6ADC8"),
                               ("#EFF1F5", "#E6E9EF", "#DCE0E8", "#BCC0CC", "#CCD0DA", "#9CA0B0", "#4C4F69", "#6C6F85")),
            ["Red"]        = (("#1C1014", "#150B0F", "#2D1820", "#3E2430", "#241018", "#7D4455", "#F2CDCD", "#C99AA8"),
                               ("#FFF0F0", "#FFE4E4", "#FFFFFF", "#FFD0D0", "#FFF5F5", "#CC3344", "#4A1020", "#992233")),
            ["Green"]      = (("#141E18", "#0E1812", "#1E2E24", "#2E4038", "#182820", "#4A7060", "#C3E8D0", "#8DB8A0"),
                               ("#F0FAF4", "#E4F4EA", "#FFFFFF", "#C8ECD4", "#F5FCF7", "#2A7050", "#0A2E18", "#226644")),
            ["Lime"]       = (("#111A08", "#0C1306", "#1C2D10", "#283F18", "#16240C", "#4A7820", "#BBFF44", "#88C030"),
                               ("#F4FFE8", "#EAFFD0", "#FFFFFF", "#D0FF99", "#F8FFEE", "#4A8810", "#1A3A00", "#3A7010")),
            ["Teal"]       = (("#0D1A1A", "#081212", "#122424", "#1C3434", "#0F1E1E", "#2E6666", "#80FFEE", "#50C0B0"),
                               ("#EDFFFE", "#DAFFF8", "#FFFFFF", "#B8FFF0", "#F0FFFD", "#1A7070", "#003030", "#1A5A5A")),
            ["Purple"]     = (("#160E2A", "#100820", "#221640", "#302058", "#1A1230", "#5A3A90", "#D4AAFF", "#A078D0"),
                               ("#F8F0FF", "#F0E4FF", "#FFFFFF", "#E0CCFF", "#FBF5FF", "#6633BB", "#2A0860", "#5528AA")),
            ["Orange"]     = (("#1C1208", "#140D05", "#2C1E0C", "#3E2C14", "#201608", "#7A4A18", "#FFB860", "#CC8830"),
                               ("#FFF6EE", "#FFE8D4", "#FFFFFF", "#FFD0AA", "#FFFBF5", "#AA5510", "#3C1400", "#884410")),
            ["Yellow"]     = (("#1A1800", "#131100", "#2A2600", "#3C3600", "#201E00", "#706800", "#FFEE44", "#CCB820"),
                               ("#FFFEE8", "#FFFBD0", "#FFFFFF", "#FFF899", "#FFFFF0", "#887800", "#2A2400", "#666200")),
            ["Cyan"]       = (("#081018", "#040C14", "#102030", "#183040", "#0C1820", "#1888B0", "#00DDFF", "#1899CC"),
                               ("#EDFAFF", "#D8F4FF", "#FFFFFF", "#AAEEFF", "#F0FAFE", "#0077AA", "#001830", "#005A88")),
            ["Blue"]      = (("#0A1220", "#060D18", "#102030", "#182E44", "#0C1828", "#2860A0", "#60B8FF", "#3888D0"),
                               ("#EEF4FF", "#DCE9FF", "#FFFFFF", "#BBDDFF", "#F2F7FF", "#1155BB", "#001440", "#0044AA")),
            ["Pink"]       = (("#200A18", "#180612", "#301228", "#44203C", "#280E20", "#883060", "#FFB0D8", "#D070A8"),
                               ("#FFF0F8", "#FFE0F2", "#FFFFFF", "#FFC8E8", "#FFF8FC", "#BB3377", "#440020", "#992266")),
            ["Rocket Pop"] = (("#0C0E20", "#080A18", "#1E0A14", "#2E1020", "#101424", "#CC2233", "#F4F4FF", "#8899CC"),
                               ("#FAFCFF", "#EEF3FF", "#FFFFFF", "#FFE8EE", "#EDF2FF", "#CC2233", "#0B1F7A", "#AA1122")),
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
