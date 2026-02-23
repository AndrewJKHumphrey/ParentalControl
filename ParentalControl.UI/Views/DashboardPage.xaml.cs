using System.Windows.Controls;
using System.Windows.Threading;
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
        try
        {
            using var db = new AppDbContext();

            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
                ScreenTimeText.Text = $"{settings.TodayUsedMinutes} min";

            var today = DateTime.Now.Date;

            var limit = db.ScreenTimeLimits.FirstOrDefault(l => l.DayOfWeek == DateTime.Now.DayOfWeek);
            if (limit != null && limit.IsEnabled)
            {
                bool use12h = settings?.TimeFormat12Hour == true;
                AllowedRangeText.Text = $"{FormatTime(limit.AllowedFrom, use12h)} – {FormatTime(limit.AllowedUntil, use12h)}";
                DailyLimitText.Text = limit.DailyLimitMinutes == 0 ? "Unlimited" : $"{limit.DailyLimitMinutes} min";
            }
            else
            {
                AllowedRangeText.Text = "No limit today";
                DailyLimitText.Text = "No limit today";
            }

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

    private static string FormatTime(TimeOnly t, bool use12h)
    {
        if (!use12h) return t.ToString("HH:mm");
        int hour = t.Hour % 12;
        if (hour == 0) hour = 12;
        return $"{hour}:{t.Minute:D2} {(t.Hour < 12 ? "AM" : "PM")}";
    }

    private async void ResetScreenTime_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ResetScreenTimeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings == null) return;
                settings.TodayUsedMinutes = 0;
                settings.UsageDate = DateTime.Now.Date;
                db.SaveChanges();
            });
            await LoadDataAsync();
        }
        catch { }
        finally
        {
            ResetScreenTimeButton.IsEnabled = true;
        }
    }
}
