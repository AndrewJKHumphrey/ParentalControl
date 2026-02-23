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

            // Fix AllowedFrom rows that were seeded as noon (12:00) instead of midnight (00:00)
            try { db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET AllowedFrom = '00:00:00' WHERE AllowedFrom LIKE '12:00:00%'"); }
            catch { }

            // Apply saved theme before showing any window
            var settings = db.Settings.FirstOrDefault();
            ApplyTheme(settings?.AppTheme ?? "Dark");
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

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var main = new MainWindow();
        Current.MainWindow = main;
        main.Show();
    }

    public static void ApplyTheme(string name)
    {
        // Each tuple: AppBg, SidebarBg, CardBg, CardHover, AltRowBg, BorderColor, TextPrimary, TextSecondary
        var themes = new Dictionary<string, (string, string, string, string, string, string, string, string)>
        {
            ["Dark"]  = ("#1E1E2E", "#181825", "#313244", "#45475A", "#2A2A3E", "#585B70", "#CDD6F4", "#A6ADC8"),
            ["Light"] = ("#EFF1F5", "#E6E9EF", "#DCE0E8", "#BCC0CC", "#CCD0DA", "#9CA0B0", "#4C4F69", "#6C6F85"),
            ["Red"]   = ("#1C1014", "#150B0F", "#2D1820", "#3E2430", "#241018", "#7D4455", "#F2CDCD", "#C99AA8"),
            ["Blue"]  = ("#1A1E2E", "#131728", "#252A40", "#373D5A", "#1E2338", "#4C5580", "#C5CCEF", "#8F9ABD"),
            ["Green"] = ("#141E18", "#0E1812", "#1E2E24", "#2E4038", "#182820", "#4A7060", "#C3E8D0", "#8DB8A0"),
        };

        if (!themes.TryGetValue(name, out var t))
            t = themes["Dark"];

        var res = Application.Current.Resources;
        res["AppBg"]         = Brush(t.Item1);
        res["SidebarBg"]     = Brush(t.Item2);
        res["CardBg"]        = Brush(t.Item3);
        res["CardHover"]     = Brush(t.Item4);
        res["AltRowBg"]      = Brush(t.Item5);
        res["BorderColor"]   = Brush(t.Item6);
        res["TextPrimary"]   = Brush(t.Item7);
        res["TextSecondary"] = Brush(t.Item8);

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
