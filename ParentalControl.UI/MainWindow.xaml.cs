using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ParentalControl.UI.Views;

using ParentalControl.UI.Services;
using ParentalControl.Core;
using ParentalControl.Core.Data;

namespace ParentalControl.UI;

public partial class MainWindow : Window
{
    private const string ServiceName        = "ParentalControlService";
    private const string WatchdogServiceName = "ParentalControlWatchdog";
    private readonly IpcClient _ipc = new();

    // Base design dimensions at 1080p (1.0 scale)
    private const double BaseWidth  = 960;
    private const double BaseHeight = 640;

    public MainWindow()
    {
        InitializeComponent();

        using (var db = new AppDbContext())
        {
            var scale = db.Settings.FirstOrDefault()?.UiScale ?? "1080p";
            ApplyScale(scale);
        }

        Navigate("Dashboard");
        Loaded += (_, _) =>
        {
            RefreshServiceButton();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => RefreshServiceButton();
            timer.Start();
        };
    }

    public void ApplyScale(string label)
    {
        // Scale factors = target vertical res / 1080 base
        double scale = label switch
        {
            "1200p" => 1200.0 / 1080.0,   // ≈ 1.111  WUXGA
            "1440p" => 1440.0 / 1080.0,   // ≈ 1.333  QHD
            "1600p" => 1600.0 / 1080.0,   // ≈ 1.481  WQXGA
            "1800p" => 1800.0 / 1080.0,   // ≈ 1.667  QHD+
            "2160p" => 2160.0 / 1080.0,   //   2.000  4K UHD
            _       => 1.0,                //   1.000  1080p FHD (default)
        };
        RootGrid.LayoutTransform = new ScaleTransform(scale, scale);
        Width  = Math.Round(BaseWidth  * scale);
        Height = Math.Round(BaseHeight * scale);
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
            "Dashboard"   => new DashboardPage(),
            "ScreenTime"  => new ScreenTimePage(),
            "AppControl"  => new AppControlPage(),
            "FocusMode"   => new FocusModePage(),
            "Profiles"    => new ProfilesPage(),
            "ActivityLog" => new ActivityLogPage(),
            "WebFilter"   => new WebFilterPage(),
            "Settings"    => new SettingsPage(),
            _ => null
        };
    }

    private async void RefreshServiceButton()
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

            var status = await _ipc.SendAsync(IpcCommand.GetStatus);
            ServiceStatusText.Text = status.Success ? "Running" : "Stopped";
            ServiceStatusText.Foreground = status.Success
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Salmon;
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
                if (wasRunning)
                {
                    // Stop watchdog first so it doesn't restart the main service
                    try
                    {
                        using var wdog = new ServiceController(WatchdogServiceName);
                        if (wdog.Status == ServiceControllerStatus.Running ||
                            wdog.Status == ServiceControllerStatus.StartPending)
                        {
                            wdog.Stop();
                            wdog.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                        }
                    }
                    catch { /* Watchdog not installed — continue to stop main service */ }

                    using var sc = new ServiceController(ServiceName);
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                }
                else
                {
                    // Start watchdog first — it will start the main service
                    try
                    {
                        using var wdog = new ServiceController(WatchdogServiceName);
                        if (wdog.Status != ServiceControllerStatus.Running &&
                            wdog.Status != ServiceControllerStatus.StartPending)
                        {
                            wdog.Start();
                            wdog.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                        }
                    }
                    catch { /* Watchdog not installed — start main service directly */ }

                    using var sc = new ServiceController(ServiceName);
                    if (sc.Status != ServiceControllerStatus.Running &&
                        sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
                    }
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
