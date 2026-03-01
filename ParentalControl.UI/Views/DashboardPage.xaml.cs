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
    private bool _enforcementLoaded;

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
            {
                ScreenTimeText.Text = $"{settings.TodayUsedMinutes} min";

                int appUsed  = settings.TodayAppTimeUsedMinutes;
                int appLimit = settings.AppTimeLimitMinutes + settings.TodayAppTimeBonusMinutes;
                AppTimeUsedText.Text = $"{appUsed} min";
#if DEBUG
                if (settings.DebugAppTimeOverride && settings.AppTimeLimitMinutes != 0)
                {
                    AppTimeLimitText.Text = $"of 6 min limit ({Math.Max(0, 6 - appUsed)} min remaining) [DEBUG OVERRIDE]";
                }
                else
#endif
                AppTimeLimitText.Text = appLimit == 0
                    ? "no daily limit set"
                    : $"of {appLimit} min limit ({Math.Max(0, appLimit - appUsed)} min remaining)";

                // Load enforcement toggles (only once to avoid re-triggering the changed handler)
                if (!_enforcementLoaded)
                {
                    _enforcementLoaded = false; // keep false until all three are set
                    ScreenTimeToggle.IsChecked = settings.ScreenTimeEnabled;
                    AppControlToggle.IsChecked = settings.AppControlEnabled;
                    WebFilterToggle.IsChecked  = settings.WebFilterEnabled;
                    _enforcementLoaded = true;
                }
            }

            var today = DateTime.Now.Date;

            var limit = db.ScreenTimeLimits.FirstOrDefault(l => l.DayOfWeek == DateTime.Now.DayOfWeek);
            if (limit != null && limit.IsEnabled)
            {
                bool use12h = settings?.TimeFormat12Hour == true;
                AllowedRangeText.Text = $"{FormatTime(limit.AllowedFrom, use12h)} – {FormatTime(limit.AllowedUntil, use12h)}";
                int effective = limit.DailyLimitMinutes + (settings?.TodayBonusMinutes ?? 0);
                DailyLimitText.Text = limit.DailyLimitMinutes == 0 ? "Unlimited" : $"{effective} min";
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

    private async void Enforcement_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_enforcementLoaded) return;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                settings.ScreenTimeEnabled = ScreenTimeToggle.IsChecked == true;
                settings.AppControlEnabled = AppControlToggle.IsChecked == true;
                settings.WebFilterEnabled  = WebFilterToggle.IsChecked  == true;
            }
            db.SaveChanges();
        }
        catch { }

        await _ipc.SendAsync(ParentalControl.Core.IpcCommand.ReloadRules);
    }

        private async void AdminToggle_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_enforcementLoaded) return;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                bool flag = !(AdminToggle.IsChecked == true);
                settings.ScreenTimeEnabled = flag;
                settings.AppControlEnabled = flag;
                settings.WebFilterEnabled  = flag;

                ScreenTimeToggle.IsEnabled = flag;
                AppControlToggle.IsEnabled = flag;
                WebFilterToggle.IsEnabled = flag; 
                db.SaveChanges();
            }
        }
        catch { }

        await _ipc.SendAsync(ParentalControl.Core.IpcCommand.ReloadRules);
    }

    private async void ExtendAppTime_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        int minutes = 15;
        if (ExtendAppTimeBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int parsed))
            minutes = parsed;

        ExtendAppTimeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings == null) return;
                settings.TodayAppTimeBonusMinutes += minutes;
                db.SaveChanges();
            });
            await LoadDataAsync();
        }
        catch { }
        finally
        {
            ExtendAppTimeButton.IsEnabled = true;
        }
    }

    private async void ResetScreenTime_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        int minutes = 15;
        if (ExtendMinutesBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int parsed))
            minutes = parsed;

        ResetScreenTimeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings == null) return;
                settings.TodayBonusMinutes += minutes;
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
