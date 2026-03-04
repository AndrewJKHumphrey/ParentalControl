using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core.Data;
using ParentalControl.Core.Helpers;
using ParentalControl.Core.Services;

namespace ParentalControl.UI.Views;

public partial class SettingsPage : Page
{
    private bool _loaded = false;

    private static readonly string[] _allGenres =
    {
        "Action", "Adventure", "Arcade", "Board Games", "Card", "Casual",
        "Educational", "Family", "Fighting", "Indie", "Massively Multiplayer",
        "Platformer", "Puzzle", "Racing", "RPG", "Shooter", "Simulation",
        "Sports", "Strategy"
    };

    private static readonly string[] _allTags =
    {
        "blood", "dark-themes", "drugs", "gambling", "gore",
        "horror", "mature", "nudity", "sexual-content", "violence"
    };

    public SettingsPage()
    {
        InitializeComponent();
#if DEBUG
        DebugSection.Visibility  = System.Windows.Visibility.Visible;
        DebugThemeItem.Visibility = System.Windows.Visibility.Visible;
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
                Format12hToggle.IsChecked = settings.TimeFormat12Hour;
                DarkModeToggle.IsChecked = settings.ThemeIsDark;
                DebugStopServiceToggle.IsChecked = settings.DebugStopServiceAfterLock;
                DebugAppTimeOverrideToggle.IsChecked = settings.DebugAppTimeOverride;

                // Notifications
                NotificationsEnabledToggle.IsChecked = settings.NotificationsEnabled;
                ModeEmailRadio.IsChecked             = settings.NotificationMode == 0;
                ModeTextRadio.IsChecked              = settings.NotificationMode == 1;
                ModePushRadio.IsChecked              = settings.NotificationMode == 2;
                EmailDestPanel.Visibility            = settings.NotificationMode == 0
                                                       ? Visibility.Visible : Visibility.Collapsed;
                TextDestPanel.Visibility             = settings.NotificationMode == 1
                                                       ? Visibility.Visible : Visibility.Collapsed;
                NtfyDestPanel.Visibility             = settings.NotificationMode == 2
                                                       ? Visibility.Visible : Visibility.Collapsed;
                SendingAccountPanel.Visibility       = settings.NotificationMode == 2
                                                       ? Visibility.Collapsed : Visibility.Visible;
                NotificationAddressBox.Text          = settings.NotificationMode == 0 ? settings.NotificationAddress : string.Empty;
                PhoneNumberBox.Text                  = settings.PhoneNumber;
                NtfyTopicBox.Text                    = settings.NtfyTopic;

                // Select saved carrier
                CarrierBox.SelectedIndex = 0;
                foreach (System.Windows.Controls.ComboBoxItem ci in CarrierBox.Items)
                {
                    if (ci.Tag?.ToString() == settings.CarrierGateway)
                    { CarrierBox.SelectedItem = ci; break; }
                }

                SmtpUsernameBox.Text             = settings.SmtpUsername;
                SmtpPasswordBox.Password         = settings.SmtpPassword;
                SmtpHostBox.Text                 = settings.SmtpHost;
                SmtpPortBox.Text                 = settings.SmtpPort.ToString();
                SmtpSslToggle.IsChecked          = settings.SmtpUseSsl;
                NotifyScreenLockToggle.IsChecked = settings.NotifyOnScreenLock;
                NotifyAppBlockToggle.IsChecked   = settings.NotifyOnAppBlock;

                // Auto-migrate plain-text key to encrypted storage on first load
                if (!string.IsNullOrEmpty(settings.RawgApiKey) &&
                    !ApiKeyHelper.IsEncrypted(settings.RawgApiKey))
                {
                    settings.RawgApiKey = ApiKeyHelper.EncryptForStorage(settings.RawgApiKey);
                    db.SaveChanges();
                }
                // Always show censored mask — never expose the actual key
                RawgApiKeyBox.Text = string.IsNullOrEmpty(settings.RawgApiKey)
                    ? string.Empty : "••••••••••••••••";

                // Scan blocking rules
                var ratingItems = ScanBlockRatingBox.Items.Cast<ComboBoxItem>()
                                                    .Select(i => i.Content?.ToString()).ToList();
                var ratingIdx = ratingItems.IndexOf(settings.ScanBlockRating);
                ScanBlockRatingBox.SelectedIndex = ratingIdx >= 0 ? ratingIdx : 4; // default M
                PopulateCheckBoxPanel(GenresPanel, _allGenres, settings.ScanBlockedGenres, ScanGenreTag_Changed);
                PopulateCheckBoxPanel(TagsPanel,   _allTags,   settings.ScanBlockedTags,   ScanGenreTag_Changed);

                // Scan App Time rules
                var appTimeIdx = ratingItems.IndexOf(settings.ScanAppTimeRating);
                ScanAppTimeRatingBox.SelectedIndex = appTimeIdx >= 0 ? appTimeIdx : 0;
                PopulateCheckBoxPanel(AppTimeGenresPanel, _allGenres, settings.ScanAppTimeGenres, ScanAppTimeTag_Changed);
                PopulateCheckBoxPanel(AppTimeTagsPanel,   _allTags,   settings.ScanAppTimeTags,   ScanAppTimeTag_Changed);

                // Scan Focus Mode rules
                var focusIdx = ratingItems.IndexOf(settings.ScanFocusModeRating);
                ScanFocusModeRatingBox.SelectedIndex = focusIdx >= 0 ? focusIdx : 0;
                PopulateCheckBoxPanel(FocusModeGenresPanel, _allGenres, settings.ScanFocusModeGenres, ScanFocusModeTag_Changed);
                PopulateCheckBoxPanel(FocusModeTagsPanel,   _allTags,   settings.ScanFocusModeTags,   ScanFocusModeTag_Changed);

                // Show auto-detect hint if SMTP was already configured
                ShowSmtpHint(settings.SmtpUsername);

                // UI Scale
                var scaleItems = UiScaleBox.Items.Cast<ComboBoxItem>()
                                            .Select(i => i.Content?.ToString()).ToList();
                var scaleIdx = scaleItems.IndexOf(settings.UiScale);
                UiScaleBox.SelectedIndex = scaleIdx >= 0 ? scaleIdx : 0;

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

    private void UiScale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var label = (UiScaleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "1080p";
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.UiScale = label;
        db.SaveChanges();
        if (Application.Current.MainWindow is MainWindow mw)
            mw.ApplyScale(label);
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

    private void RawgKeyEdit_Click(object sender, RoutedEventArgs e)
    {
        RawgApiKeyBox.IsReadOnly = false;
        RawgApiKeyBox.SetResourceReference(ForegroundProperty, "TextPrimary");
        RawgApiKeyBox.Text = string.Empty; // clear mask — user types new key
        RawgKeyEditButton.Visibility  = Visibility.Collapsed;
        RawgKeySaveButton.Visibility  = Visibility.Visible;
        RawgKeyCancelButton.Visibility = Visibility.Visible;
        RawgApiKeyBox.Focus();
    }

    private void RawgKeySave_Click(object sender, RoutedEventArgs e)
    {
        var newKey = RawgApiKeyBox.Text.Trim();
        if (!string.IsNullOrEmpty(newKey))
        {
            try
            {
                using var db = new AppDbContext();
                var settings = db.Settings.FirstOrDefault();
                if (settings != null)
                {
                    settings.RawgApiKey = ApiKeyHelper.EncryptForStorage(newKey);
                    db.SaveChanges();
                }
            }
            catch { }
        }
        RawgKeyLock(string.IsNullOrEmpty(newKey) ? null : "••••••••••••••••");
    }

    private void RawgKeyCancel_Click(object sender, RoutedEventArgs e)
    {
        // Discard typed text — reload mask from DB so display is accurate
        string mask = string.Empty;
        try
        {
            using var db = new AppDbContext();
            var stored = db.Settings.FirstOrDefault()?.RawgApiKey ?? string.Empty;
            mask = string.IsNullOrEmpty(stored) ? string.Empty : "••••••••••••••••";
        }
        catch { }
        RawgKeyLock(mask);
    }

    private void RawgKeyLock(string? displayText)
    {
        RawgApiKeyBox.IsReadOnly = true;
        RawgApiKeyBox.Text = displayText ?? string.Empty;
        RawgApiKeyBox.SetResourceReference(ForegroundProperty, "TextSecondary");
        RawgKeyEditButton.Visibility   = Visibility.Visible;
        RawgKeySaveButton.Visibility   = Visibility.Collapsed;
        RawgKeyCancelButton.Visibility = Visibility.Collapsed;
    }

    // -------------------------------------------------------------------------
    // Scan blocking rules helpers
    // -------------------------------------------------------------------------

    private void PopulateCheckBoxPanel(WrapPanel panel, string[] allItems, string stored,
                                       RoutedEventHandler onChanged)
    {
        panel.Children.Clear();
        var selected = stored.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                        StringSplitOptions.TrimEntries)
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in allItems)
        {
            var cb = new CheckBox
            {
                Content   = item,
                IsChecked = selected.Contains(item),
                Margin    = new System.Windows.Thickness(0, 0, 12, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary"),
            };
            cb.Checked   += onChanged;
            cb.Unchecked += onChanged;
            panel.Children.Add(cb);
        }
    }

    private static string GetCheckedValues(WrapPanel panel) =>
        string.Join(",", panel.Children.OfType<CheckBox>()
                             .Where(cb => cb.IsChecked == true)
                             .Select(cb => cb.Content.ToString()!));

    private void ScanBlockRating_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanBlockRating = (ScanBlockRatingBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "M";
        db.SaveChanges();
    }

    private void ScanGenreTag_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanBlockedGenres = GetCheckedValues(GenresPanel);
        settings.ScanBlockedTags   = GetCheckedValues(TagsPanel);
        db.SaveChanges();
    }

    private void ScanAppTimeRating_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanAppTimeRating = (ScanAppTimeRatingBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        db.SaveChanges();
    }

    private void ScanAppTimeTag_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanAppTimeGenres = GetCheckedValues(AppTimeGenresPanel);
        settings.ScanAppTimeTags   = GetCheckedValues(AppTimeTagsPanel);
        db.SaveChanges();
    }

    private void ScanFocusModeRating_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanFocusModeRating = (ScanFocusModeRatingBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        db.SaveChanges();
    }

    private void ScanFocusModeTag_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        using var db = new AppDbContext();
        var settings = db.Settings.FirstOrDefault();
        if (settings == null) return;
        settings.ScanFocusModeGenres = GetCheckedValues(FocusModeGenresPanel);
        settings.ScanFocusModeTags   = GetCheckedValues(FocusModeTagsPanel);
        db.SaveChanges();
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
            settings.TimeFormat12Hour           = false;
            settings.AppTheme                   = "Default";
            settings.ThemeIsDark                = true;
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
            db.Database.ExecuteSqlRaw("DELETE FROM ActivityEntries");
            db.Database.ExecuteSqlRaw("UPDATE ScreenTimeLimits SET IsEnabled = 0, DailyLimitMinutes = 0, AllowedFrom = '00:00:00', AllowedUntil = '23:59:00'");
            App.SeedBrowsers(db);

            // Reset settings, keep PasswordHash
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                settings.ScreenTimeEnabled          = true;
                settings.AppControlEnabled          = true;
                settings.TimeFormat12Hour           = false;
                settings.AppTheme                   = "Default";
                settings.ThemeIsDark                = true;
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

    // -------------------------------------------------------------------------
    // Notification handlers
    // -------------------------------------------------------------------------

    private void Notifications_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void NotifyMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        bool isEmail = ModeEmailRadio.IsChecked == true;
        bool isPush  = ModePushRadio.IsChecked  == true;
        EmailDestPanel.Visibility      = isEmail             ? Visibility.Visible   : Visibility.Collapsed;
        TextDestPanel.Visibility       = (!isEmail && !isPush) ? Visibility.Visible : Visibility.Collapsed;
        NtfyDestPanel.Visibility       = isPush              ? Visibility.Visible   : Visibility.Collapsed;
        SendingAccountPanel.Visibility = isPush              ? Visibility.Collapsed : Visibility.Visible;
        SaveNotificationSettings();
    }

    private void NotificationAddress_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void PhoneNumber_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void NtfyTopic_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void Carrier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void SmtpEmail_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        ShowSmtpHint(SmtpUsernameBox.Text);
        SaveNotificationSettings();
    }

    private void SmtpPassword_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void AdvancedSmtp_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        SaveNotificationSettings();
    }

    private void ShowSmtpHint(string email)
    {
        var detected = TryDetectSmtp(email);
        if (detected.HasValue)
        {
            var (host, port, ssl, name) = detected.Value;
            // Auto-fill the advanced SMTP fields
            SmtpHostBox.Text        = host;
            SmtpPortBox.Text        = port.ToString();
            SmtpSslToggle.IsChecked = ssl;
            SmtpAutoDetectHint.Text = $"Auto-detected: {name} ({host}:{port}, SSL {(ssl ? "On" : "Off")})";
            SmtpAutoDetectHint.Visibility = Visibility.Visible;
        }
        else
        {
            SmtpAutoDetectHint.Visibility = Visibility.Collapsed;
        }
    }

    private static (string host, int port, bool ssl, string name)? TryDetectSmtp(string email)
    {
        if (!email.Contains('@')) return null;
        var domain = email.Split('@')[1].Trim().ToLowerInvariant();
        return domain switch
        {
            "gmail.com" or "googlemail.com"
                => ("smtp.gmail.com", 587, true, "Gmail"),
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" or "windowslive.com"
                => ("smtp-mail.outlook.com", 587, true, "Outlook / Hotmail"),
            "yahoo.com" or "yahoo.co.uk" or "ymail.com"
                => ("smtp.mail.yahoo.com", 587, true, "Yahoo"),
            "icloud.com" or "me.com" or "mac.com"
                => ("smtp.mail.me.com", 587, true, "iCloud"),
            _ => null
        };
    }

    private void SaveNotificationSettings()
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings == null) return;

            settings.NotificationsEnabled = NotificationsEnabledToggle.IsChecked == true;
            settings.NotificationMode     = ModeEmailRadio.IsChecked == true ? 0 :
                                            ModeTextRadio.IsChecked  == true ? 1 : 2;
            settings.NtfyTopic            = NtfyTopicBox.Text.Trim();
            settings.PhoneNumber          = PhoneNumberBox.Text.Trim();

            // Compute carrier gateway from selected item's Tag
            var carrierGateway = string.Empty;
            if (CarrierBox.SelectedItem is System.Windows.Controls.ComboBoxItem ci)
                carrierGateway = ci.Tag?.ToString() ?? string.Empty;
            settings.CarrierGateway = carrierGateway;

            // Final delivery address
            settings.NotificationAddress = settings.NotificationMode == 0
                ? NotificationAddressBox.Text.Trim()
                : (string.IsNullOrWhiteSpace(settings.PhoneNumber) || string.IsNullOrWhiteSpace(carrierGateway)
                    ? string.Empty
                    : $"{settings.PhoneNumber}@{carrierGateway}");

            settings.SmtpUsername       = SmtpUsernameBox.Text.Trim();
            settings.SmtpPassword       = SmtpPasswordBox.Password;
            settings.SmtpHost           = SmtpHostBox.Text.Trim();
            settings.SmtpPort           = int.TryParse(SmtpPortBox.Text, out var port) ? port : 587;
            settings.SmtpUseSsl         = SmtpSslToggle.IsChecked == true;
            settings.NotifyOnScreenLock = NotifyScreenLockToggle.IsChecked == true;
            settings.NotifyOnAppBlock   = NotifyAppBlockToggle.IsChecked == true;

            db.SaveChanges();
        }
        catch { }
    }

    private async void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        SaveNotificationSettings();

        TestNotificationButton.IsEnabled = false;
        TestNotificationButton.Content   = "Sending...";
        NotificationStatus.Visibility    = Visibility.Collapsed;

        var notifier = new NotificationService();
        var (success, message) = await Task.Run(() => notifier.SendTest());

        NotificationStatus.Text       = message;
        NotificationStatus.Foreground = success
            ? new SolidColorBrush(Color.FromRgb(166, 227, 161))
            : new SolidColorBrush(Color.FromRgb(243, 139, 168));
        NotificationStatus.Visibility    = Visibility.Visible;
        TestNotificationButton.IsEnabled = true;
        TestNotificationButton.Content   = "Send Test Notification";
    }
}
