using System.Windows.Controls;
using System.Windows.Media;
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

    private static UserProfile? ResolveActiveProfile(AppDbContext db)
    {
        var username = Environment.UserName;
        return db.UserProfiles.AsEnumerable()
            .FirstOrDefault(p => p.IsEnabled &&
                string.Equals(p.WindowsUsername, username, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadDataAsync()
    {
        try
        {
            using var db = new AppDbContext();

            var settings = db.Settings.FirstOrDefault();
            var profile  = ResolveActiveProfile(db);

            if (profile != null)
            {
                ActiveProfileText.Text = profile.DisplayName;
                ScreenTimeText.Text    = $"{profile.TodayUsedMinutes} min";

                int appUsed = profile.TodayAppTimeUsedMinutes;
                var todayAppSchedule = db.AppTimeSchedules.FirstOrDefault(
                    s => s.DayOfWeek == DateTime.Now.DayOfWeek && s.UserProfileId == profile.Id);
                int baseLimit = (todayAppSchedule?.IsEnabled == true) ? todayAppSchedule.DailyLimitMinutes : 0;
                int appLimit  = baseLimit > 0 ? baseLimit + profile.TodayAppTimeBonusMinutes : 0;

                AppTimeUsedText.Text = $"{appUsed} min";
                if (appLimit == 0)
                {
                    AppTimeRemainingText.Text = "No limit";
                    AppTimeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                }
                else
                {
                    int appRemaining = Math.Max(0, appLimit - appUsed);
                    AppTimeRemainingText.Text = FormatDuration(appRemaining);
                    AppTimeRemainingText.Foreground = appRemaining > 30
                        ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                        : appRemaining > 10
                            ? new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF))
                            : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                }
            }

            if (settings != null)
            {
                // Load enforcement toggles (only once to avoid re-triggering the changed handler)
                if (!_enforcementLoaded)
                {
                    _enforcementLoaded = false;
                    ScreenTimeToggle.IsChecked = settings.ScreenTimeEnabled;
                    AppControlToggle.IsChecked = settings.AppControlEnabled;
                    FocusModeToggle.IsChecked  = settings.FocusModeEnabled;
                    WebFilterToggle.IsChecked  = settings.WebFilterEnabled;
                    _enforcementLoaded = true;
                }
            }

            var today     = DateTime.Now.Date;
            var profileId = profile?.Id ?? 1;

            var limit = db.ScreenTimeLimits.FirstOrDefault(
                l => l.DayOfWeek == DateTime.Now.DayOfWeek && l.UserProfileId == profileId);

            if (limit != null && limit.IsEnabled)
            {
                bool use12h = settings?.TimeFormat12Hour == true;
                AllowedRangeText.Text = $"{FormatTime(limit.AllowedFrom, use12h)} – {FormatTime(limit.AllowedUntil, use12h)}";
                var nowTime = TimeOnly.FromDateTime(DateTime.Now);

                // Time Until Window Begins
                if (nowTime < limit.AllowedFrom)
                {
                    int minsUntilStart = (int)(limit.AllowedFrom - nowTime).TotalMinutes;
                    AllowedRangeStartText.Text = FormatDuration(minsUntilStart);
                    AllowedRangeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                }
                else if (nowTime < limit.AllowedUntil)
                {
                    AllowedRangeStartText.Text = "Window active";
                    AllowedRangeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                }
                else
                {
                    AllowedRangeStartText.Text = "Outside Window";
                    AllowedRangeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                }

                // Time Until Window Ends
                if (nowTime < limit.AllowedUntil)
                {
                    int minsUntilEnd = (int)(limit.AllowedUntil - nowTime).TotalMinutes;
                    AllowedRangeRemainingText.Text = FormatDuration(minsUntilEnd);
                    AllowedRangeRemainingText.Foreground = minsUntilEnd > 30
                        ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                        : minsUntilEnd > 10
                            ? new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF))
                            : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                }
                else
                {
                    AllowedRangeRemainingText.Text = "0m";
                    AllowedRangeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                }

                int todayBonus = profile?.TodayBonusMinutes ?? 0;
                int effective  = limit.DailyLimitMinutes + todayBonus;

                if (limit.DailyLimitMinutes == 0)
                {
                    ScreenTimeLimitText.Text = "Unlimited";
                    ScreenTimeLimitText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                    TimeRemainingText.Text = "Unlimited";
                    TimeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                }
                else
                {
                    ScreenTimeLimitText.Text = FormatDuration(effective);
                    ScreenTimeLimitText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                    int remaining = Math.Max(0, effective - (profile?.TodayUsedMinutes ?? 0));
                    TimeRemainingText.Text = FormatDuration(remaining);
                    TimeRemainingText.Foreground = remaining > 30
                        ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                        : remaining > 10
                            ? new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF))
                            : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                }
            }
            else
            {
                AllowedRangeText.Text = "No limit today";
                AllowedRangeStartText.Text = "--";
                AllowedRangeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                AllowedRangeRemainingText.Text = "--";
                AllowedRangeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                ScreenTimeLimitText.Text = "No limit";
                ScreenTimeLimitText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                TimeRemainingText.Text = "No limit";
                TimeRemainingText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
            }

            // Focus Mode status
            {
                if (settings?.FocusModeEnabled == true)
                {
                    int focusProfileId = profile?.Id ?? 1;
                    var todaySchedule = db.FocusSchedules.FirstOrDefault(
                        s => s.DayOfWeek == DateTime.Now.DayOfWeek && s.UserProfileId == focusProfileId);
                    var nowTime = TimeOnly.FromDateTime(DateTime.Now);
                    bool use12h = settings?.TimeFormat12Hour == true;

                    if (todaySchedule?.IsEnabled == true)
                    {
                        FocusModeRangeText.Text = $"{FormatTime(todaySchedule.FocusFrom, use12h)} – {FormatTime(todaySchedule.FocusUntil, use12h)}";

                        // Time Until Window Begins
                        if (nowTime < todaySchedule.FocusFrom)
                        {
                            int minsUntilStart = (int)(todaySchedule.FocusFrom - nowTime).TotalMinutes;
                            FocusModeStartText.Text       = FormatDuration(minsUntilStart);
                            FocusModeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                        }
                        else if (nowTime <= todaySchedule.FocusUntil)
                        {
                            FocusModeStartText.Text       = "Window active";
                            FocusModeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                        }
                        else
                        {
                            FocusModeStartText.Text       = "Window ended";
                            FocusModeStartText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                        }

                        // Time Until Window Ends
                        if (nowTime < todaySchedule.FocusUntil)
                        {
                            int minsUntilEnd = (int)(todaySchedule.FocusUntil - nowTime).TotalMinutes;
                            FocusModeEndText.Text       = FormatDuration(minsUntilEnd);
                            FocusModeEndText.Foreground = minsUntilEnd > 30
                                ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))
                                : minsUntilEnd > 10
                                    ? new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF))
                                    : new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                        }
                        else
                        {
                            FocusModeEndText.Text       = "0m";
                            FocusModeEndText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x8B, 0xA8));
                        }

                        if (nowTime >= todaySchedule.FocusFrom && nowTime <= todaySchedule.FocusUntil)
                        {
                            FocusModeStatusText.Text       = "Active";
                            FocusModeStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xF9, 0xE2, 0xAF));
                        }
                        else
                        {
                            FocusModeStatusText.Text       = "Enabled";
                            FocusModeStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1));
                        }
                    }
                    else
                    {
                        FocusModeStatusText.Text       = "Off";
                        FocusModeStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
                        FocusModeRangeText.Text        = "Not scheduled";
                        FocusModeRangeText.Foreground  = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
                        FocusModeStartText.Text        = "--";
                        FocusModeStartText.Foreground  = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                        FocusModeEndText.Text          = "--";
                        FocusModeEndText.Foreground    = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                    }
                }
                else
                {
                    FocusModeStatusText.Text       = "Off";
                    FocusModeStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
                    FocusModeRangeText.Text        = "--:-- – --:--";
                    FocusModeRangeText.Foreground  = new SolidColorBrush(Color.FromRgb(0x6C, 0x70, 0x86));
                    FocusModeStartText.Text        = "--";
                    FocusModeStartText.Foreground  = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                    FocusModeEndText.Text          = "--";
                    FocusModeEndText.Foreground    = new SolidColorBrush(Color.FromRgb(0xCB, 0xA6, 0xF7));
                }
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

    private static string FormatDuration(int minutes)
    {
        if (minutes == 0) return "0m";
        int h = minutes / 60;
        int m = minutes % 60;
        return h > 0 ? (m > 0 ? $"{h}h {m}m" : $"{h}h") : $"{m}m";
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
                settings.FocusModeEnabled  = FocusModeToggle.IsChecked  == true;
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
                settings.FocusModeEnabled  = flag;
                settings.WebFilterEnabled  = flag;

                ScreenTimeToggle.IsEnabled = flag;
                AppControlToggle.IsEnabled = flag;
                FocusModeToggle.IsEnabled  = flag;
                WebFilterToggle.IsEnabled  = flag;
                db.SaveChanges();
            }
        }
        catch { }

        await _ipc.SendAsync(ParentalControl.Core.IpcCommand.ReloadRules);
    }

    private async void ExtendAppTime_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        int minutes = 0;
        if (ExtendAppTimeBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int parsed))
            minutes = parsed;

        ExtendAppTimeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var profile = ResolveActiveProfile(db);
                if (profile == null) return;
                profile.TodayAppTimeBonusMinutes += minutes;
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

    private async void ExtendRange_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        int minutes = 0;
        if (ExtendRangeBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int parsed))
            minutes = parsed;

        ExtendRangeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var profile = ResolveActiveProfile(db);
                int profileId = profile?.Id ?? 1;
                var limit = db.ScreenTimeLimits.FirstOrDefault(
                    l => l.DayOfWeek == DateTime.Now.DayOfWeek && l.UserProfileId == profileId);
                if (limit == null) return;
                limit.AllowedUntil = limit.AllowedUntil.AddMinutes(minutes);
                db.SaveChanges();
            });
            await LoadDataAsync();
        }
        catch { }
        finally
        {
            ExtendRangeButton.IsEnabled = true;
        }
    }

    private async void ResetScreenTime_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        int minutes = 0;
        if (ExtendMinutesBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out int parsed))
            minutes = parsed;

        ResetScreenTimeButton.IsEnabled = false;
        try
        {
            await Task.Run(() =>
            {
                using var db = new AppDbContext();
                var profile = ResolveActiveProfile(db);
                if (profile == null) return;
                profile.TodayBonusMinutes += minutes;
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
