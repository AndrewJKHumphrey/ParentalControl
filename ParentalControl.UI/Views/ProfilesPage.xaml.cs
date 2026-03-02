using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public partial class ProfilesPage : Page
{
    private readonly IpcClient _ipc = new();

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            ProfilesGrid.ItemsSource = db.UserProfiles.OrderBy(p => p.Id).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load profiles: {ex.Message}");
        }
    }

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var displayName = DisplayNameBox.Text.Trim();
        var username    = UsernameBox.Text.Trim();

        if (string.IsNullOrEmpty(displayName))
        {
            MessageBox.Show("Display Name is required.", "Validation",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var newProfile = new UserProfile
            {
                DisplayName     = displayName,
                WindowsUsername = username,
                IsEnabled       = true,
                UsageDate       = DateTime.Now.Date,
            };
            db.UserProfiles.Add(newProfile);
            db.SaveChanges();

            // Seed 7 ScreenTimeLimits for the new profile
            for (int i = 0; i < 7; i++)
            {
                db.ScreenTimeLimits.Add(new ScreenTimeLimit
                {
                    DayOfWeek         = (DayOfWeek)i,
                    IsEnabled         = false,
                    DailyLimitMinutes = 0,
                    AllowedFrom       = new TimeOnly(0, 0),
                    AllowedUntil      = new TimeOnly(23, 59),
                    UserProfileId     = newProfile.Id,
                });
            }

            // Seed 7 FocusSchedules for the new profile
            for (int i = 0; i < 7; i++)
            {
                db.FocusSchedules.Add(new FocusSchedule
                {
                    DayOfWeek     = (DayOfWeek)i,
                    IsEnabled     = false,
                    FocusFrom     = new TimeOnly(15, 0),
                    FocusUntil    = new TimeOnly(21, 0),
                    UserProfileId = newProfile.Id,
                });
            }
            db.SaveChanges();

            DisplayNameBox.Clear();
            UsernameBox.Clear();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            LoadData();

            StatusText.Text = $"Profile '{displayName}' added.";
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add profile: {ex.Message}");
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            if (id == 1)
            {
                MessageBox.Show("Cannot remove the Default profile.", "Not Allowed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "This will delete the profile and all its rules. Continue?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                using var db = new AppDbContext();
                var rules     = db.AppRules.Where(r => r.UserProfileId == id).ToList();
                db.AppRules.RemoveRange(rules);

                var limits    = db.ScreenTimeLimits.Where(l => l.UserProfileId == id).ToList();
                db.ScreenTimeLimits.RemoveRange(limits);

                var schedules = db.FocusSchedules.Where(s => s.UserProfileId == id).ToList();
                db.FocusSchedules.RemoveRange(schedules);

                var profile = db.UserProfiles.Find(id);
                if (profile != null) db.UserProfiles.Remove(profile);

                db.SaveChanges();
                await _ipc.SendAsync(IpcCommand.ReloadRules);
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete profile: {ex.Message}");
            }
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesGrid.ItemsSource is not List<UserProfile> profiles) return;

        try
        {
            using var db = new AppDbContext();
            foreach (var p in profiles)
            {
                var existing = db.UserProfiles.Find(p.Id);
                if (existing == null) continue;
                existing.DisplayName     = p.DisplayName;
                existing.WindowsUsername = p.WindowsUsername;
                existing.IsEnabled       = p.IsEnabled;
            }
            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);

            StatusText.Text = "Saved.";
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }
}
