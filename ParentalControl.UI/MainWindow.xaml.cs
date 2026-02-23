using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.UI.Services;
using ParentalControl.UI.Views;

namespace ParentalControl.UI;

public partial class MainWindow : Window
{
    private readonly IpcClient _ipc = new();

    public MainWindow()
    {
        InitializeComponent();
        Navigate("Dashboard");
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

    private async void LockNow_Click(object sender, RoutedEventArgs e)
    {
        var result = await _ipc.SendAsync(IpcCommand.LockNow);
        if (!result.Success)
            MessageBox.Show($"Could not lock: {result.Error}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
