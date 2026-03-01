using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core.Data;

namespace ParentalControl.UI.Views;

public partial class SettingsPage : Page
{
    private bool _loaded = false;

    public SettingsPage()
    {
        InitializeComponent();
#if DEBUG
        DebugSection.Visibility = System.Windows.Visibility.Visible;
#endif
        Loaded += (_, _) => LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                ChildHasPasswordBox.IsChecked = settings.ChildAccountHasPassword;
                Format12hToggle.IsChecked = settings.TimeFormat12Hour;
                DarkModeToggle.IsChecked = settings.ThemeIsDark;
                DebugStopServiceToggle.IsChecked = settings.DebugStopServiceAfterLock;
                DebugAppTimeOverrideToggle.IsChecked = settings.DebugAppTimeOverride;

                var themeName = settings.AppTheme;
                foreach (ComboBoxItem item in ThemeBox.Items)
                {
                    if (item.Content?.ToString() == themeName)
                    {
                        ThemeBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        catch { }

        _loaded = true;
    }

    private void ChildHasPassword_Changed(object sender, RoutedEventArgs e)
    {
        var hasPassword = ChildHasPasswordBox.IsChecked == true;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.ChildAccountHasPassword = hasPassword;
            db.SaveChanges();
        }
        catch { }
    }

    private void TimeFormat_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.TimeFormat12Hour = Format12hToggle.IsChecked == true;
            db.SaveChanges();
        }
        catch { }
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        ApplyAndSaveTheme();
    }

    private void DarkMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ApplyAndSaveTheme();
    }

    private void ApplyAndSaveTheme()
    {
        if (ThemeBox.SelectedItem is not ComboBoxItem item) return;
        var name = item.Content?.ToString() ?? "Default";
        bool isDark = DarkModeToggle.IsChecked == true;

        App.ApplyTheme(name, isDark);

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.AppTheme = name;
            settings.ThemeIsDark = isDark;
            db.SaveChanges();
        }
        catch { }
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentPasswordBox.Password;
        var newPwd = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(newPwd))
        {
            ShowStatus("Please fill in all fields.", isError: true);
            return;
        }

        if (newPwd != confirm)
        {
            ShowStatus("New passwords do not match.", isError: true);
            return;
        }

        if (newPwd.Length < 6)
        {
            ShowStatus("Password must be at least 6 characters.", isError: true);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) { ShowStatus("Settings not found.", isError: true); return; }

            if (!BCrypt.Net.BCrypt.Verify(current, settings.PasswordHash))
            {
                ShowStatus("Current password is incorrect.", isError: true);
                return;
            }

            settings.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPwd);
            db.SaveChanges();

            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();

            ShowStatus("Password changed successfully.", isError: false);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        PwdStatus.Text = message;
        PwdStatus.Foreground = isError
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 139, 168))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(166, 227, 161));
        PwdStatus.Visibility = Visibility.Visible;
    }

    // -------------------------------------------------------------------------
    // Reset handlers
    // -------------------------------------------------------------------------

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will restore all settings (time limits, toggles, theme, App Time) to their defaults.\n\nYour password and all rules are kept.\n\nContinue?",
            "Reset Settings", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            settings.ScreenTimeEnabled          = true;
            settings.AppControlEnabled          = true;
            settings.WebFilterEnabled           = true;
            settings.TimeFormat12Hour           = false;
            settings.AppTheme                   = "Default";
            settings.ThemeIsDark                = true;
            settings.IsAllowMode                = false;
            settings.ChildAccountHasPassword    = false;
            settings.TodayUsedMinutes           = 0;
            settings.TodayBonusMinutes          = 0;
            settings.AppTimeLimitMinutes        = 60;
            settings.TodayAppTimeUsedMinutes    = 0;
            settings.TodayAppTimeBonusMinutes   = 0;
            settings.DebugStopServiceAfterLock  = false;
            db.SaveChanges();

            App.ApplyTheme("Default", true);
            LoadSettings();
            MessageBox.Show("Settings reset to defaults.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reset failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetData_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will permanently delete:\n• All app rules\n• All website rules\n• All screen time schedules\n• All activity logs\n\nYour settings and password are kept.\n\nContinue?",
            "Reset Data", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            db.Database.ExecuteSqlRaw("DELETE FROM AppRules");
            db.Database.ExecuteSqlRaw("DELETE FROM WebsiteRules");
            db.Database.ExecuteSqlRaw("DELETE FROM ActivityEntries");
            db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET IsEnabled = 0, DailyLimitMinutes = 0, AllowedFrom = '00:00:00', AllowedUntil = '23:59:00'");
            App.SeedBrowsers(db);
            MessageBox.Show("All data cleared.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reset failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FullReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will reset ALL settings and delete ALL data.\n\nYour password will be kept.\n\nThis cannot be undone. Continue?",
            "Full Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        // Second confirmation for the destructive action
        var confirm = MessageBox.Show(
            "Are you absolutely sure? All rules, schedules, logs and settings will be wiped.",
            "Full Reset — Final Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Stop);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();

            // Clear all data tables
            db.Database.ExecuteSqlRaw("DELETE FROM AppRules");
            db.Database.ExecuteSqlRaw("DELETE FROM WebsiteRules");
            db.Database.ExecuteSqlRaw("DELETE FROM ActivityEntries");
            db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET IsEnabled = 0, DailyLimitMinutes = 0, AllowedFrom = '00:00:00', AllowedUntil = '23:59:00'");
            App.SeedBrowsers(db);

            // Reset settings, keep PasswordHash
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                settings.ScreenTimeEnabled          = true;
                settings.AppControlEnabled          = true;
                settings.WebFilterEnabled           = true;
                settings.TimeFormat12Hour           = false;
                settings.AppTheme                   = "Default";
                settings.ThemeIsDark                = true;
                settings.IsAllowMode                = false;
                settings.ChildAccountHasPassword    = false;
                settings.TodayUsedMinutes           = 0;
                settings.TodayBonusMinutes          = 0;
                settings.AppTimeLimitMinutes        = 60;
                settings.TodayAppTimeUsedMinutes    = 0;
                settings.TodayAppTimeBonusMinutes   = 0;
                settings.DebugStopServiceAfterLock  = false;
                db.SaveChanges();
            }

            App.ApplyTheme("Default", true);
            LoadSettings();
            MessageBox.Show("Full reset complete.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Reset failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Event handlers always compiled (methods must exist for XAML);
    // section is hidden in Release builds so they cannot be triggered there.
    private void DebugStopService_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.DebugStopServiceAfterLock = DebugStopServiceToggle.IsChecked == true;
            db.SaveChanges();
        }
        catch { }
    }

    private void DebugAppTimeOverride_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.DebugAppTimeOverride = DebugAppTimeOverrideToggle.IsChecked == true;
            db.SaveChanges();
        }
        catch { }
    }
}
