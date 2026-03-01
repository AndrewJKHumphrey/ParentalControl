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

    // Event handler always compiled (method must exist for XAML);
    // section is hidden in Release builds so it cannot be triggered there.
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
}
