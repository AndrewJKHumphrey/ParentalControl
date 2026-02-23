using System.Windows.Controls;
using System.Windows.Threading;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public partial class DashboardPage : Page
{
    private readonly IpcClient _ipc = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public DashboardPage()
    {
        InitializeComponent();
        _refreshTimer.Tick += async (_, _) => await LoadDataAsync();
        Loaded += async (_, _) =>
        {
            await LoadDataAsync();
            _refreshTimer.Start();
        };
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    private async Task LoadDataAsync()
    {
        var status = await _ipc.SendAsync(IpcCommand.GetStatus);
        ServiceStatusText.Text = status.Success ? "Running" : "Stopped";
        ServiceStatusText.Foreground = status.Success
            ? System.Windows.Media.Brushes.LightGreen
            : System.Windows.Media.Brushes.Salmon;

        try
        {
            using var db = new AppDbContext();

            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
                ScreenTimeText.Text = $"{settings.TodayUsedMinutes} min";

            var today = DateTime.Now.Date;

            var appsBlocked = db.ActivityEntries
                .Count(a => a.Type == ActivityType.AppBlocked && a.Timestamp >= today);
            AppsBlockedText.Text = appsBlocked.ToString();

            var webBlocked = db.ActivityEntries
                .Count(a => a.Type == ActivityType.WebsiteBlocked && a.Timestamp >= today);
            WebsitesBlockedText.Text = webBlocked.ToString();

            var recent = db.ActivityEntries
                .OrderByDescending(a => a.Timestamp)
                .Take(50)
                .ToList();
            RecentGrid.ItemsSource = recent;
        }
        catch { }
    }
}
