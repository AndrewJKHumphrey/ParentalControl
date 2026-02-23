using System.ServiceProcess;
using System.Windows;
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

            // Fix AllowedFrom rows that were seeded as noon (12:00) instead of midnight (00:00)
            try { db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET AllowedFrom = '00:00:00' WHERE AllowedFrom LIKE '12:00:00%'"); }
            catch { }
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
