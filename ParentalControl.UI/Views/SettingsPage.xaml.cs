using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core.Data;

namespace ParentalControl.UI.Views;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadSettings();
    }

    private bool _settingsLoaded = false;

    private void LoadSettings()
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                ChildHasPasswordBox.IsChecked = settings.ChildAccountHasPassword;
                LockDelaySlider.Value = Math.Clamp(settings.LockDelaySeconds, 20, 180);
                UpdateLockDelayLabel((int)LockDelaySlider.Value);
            }
        }
        catch { }
        _settingsLoaded = true;
    }

    private void ChildHasPassword_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.ChildAccountHasPassword = ChildHasPasswordBox.IsChecked == true;
            db.SaveChanges();
        }
        catch { }
    }

    private void LockDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var seconds = (int)LockDelaySlider.Value;
        UpdateLockDelayLabel(seconds);

        if (!_settingsLoaded) return;

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;
            settings.LockDelaySeconds = seconds;
            db.SaveChanges();
        }
        catch { }
    }

    private void UpdateLockDelayLabel(int seconds)
    {
        if (LockDelayLabel == null) return;
        if (seconds < 60)
            LockDelayLabel.Text = $"{seconds} sec";
        else
        {
            int mins = seconds / 60;
            int secs = seconds % 60;
            LockDelayLabel.Text = secs == 0 ? $"{mins} min" : $"{mins} min {secs} sec";
        }
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
}
