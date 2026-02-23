using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.UI.Views;

namespace ParentalControl.UI;

public partial class MainWindow : Window
{
    private const string ServiceName = "ParentalControlService";

    public MainWindow()
    {
        InitializeComponent();
        Navigate("Dashboard");
        Loaded += (_, _) => RefreshServiceButton();
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
            Navigate(tag);
    }

    private void Navigate(string page)
    {
        ContentFrame.Content = page switch
        {
            "Dashboard"   => (object)new DashboardPage(),
            "ScreenTime"  => new ScreenTimePage(),
            "AppControl"  => new AppControlPage(),
            "WebFilter"   => new WebFilterPage(),
            "ActivityLog" => new ActivityLogPage(),
            "Settings"    => new SettingsPage(),
            _ => null!
        };
    }

    private void RefreshServiceButton()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            var running = sc.Status == ServiceControllerStatus.Running ||
                          sc.Status == ServiceControllerStatus.StartPending;
            ServiceButton.Content    = running ? "Shutdown Service" : "Start Service";
            ServiceButton.Background = running
                ? new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87))  // orange
                : new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // green
        }
        catch { }
    }

    private async void ServiceButton_Click(object sender, RoutedEventArgs e)
    {
        ServiceButton.IsEnabled = false;
        string? message = null;
        try
        {
            bool wasRunning;
            using (var sc = new ServiceController(ServiceName))
                wasRunning = sc.Status == ServiceControllerStatus.Running ||
                             sc.Status == ServiceControllerStatus.StartPending;

            await Task.Run(() =>
            {
                using var sc = new ServiceController(ServiceName);
                if (wasRunning)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                else
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                }
            });

            message = wasRunning
                ? "Service stopped. Parental controls are now disabled."
                : "Service started. Parental controls are now active.";
        }
        catch (Exception ex)
        {
            message = $"Could not change service state: {ex.Message}";
        }
        finally
        {
            RefreshServiceButton();
            ServiceButton.IsEnabled = true;
        }

        if (message != null)
            MessageBox.Show(message, "Service", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
