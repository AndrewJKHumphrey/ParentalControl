using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            if (id == 1)
            {
                MessageBox.Show("Cannot remove the Default (catch-all) profile.", "Not Allowed",
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
                existing.DisplayName = p.DisplayName;
                existing.IsEnabled   = p.IsEnabled;
            }
            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);

            StatusText.Text = "Saved.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }
}
