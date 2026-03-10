using System.Runtime.InteropServices;
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
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(string u, string d, string p, int t, int prov, out IntPtr tok);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    private static bool AccountHasPassword(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        try
        {
            bool noPassword = LogonUser(username, Environment.MachineName, "", 3, 0, out var tok);
            if (noPassword) CloseHandle(tok);
            return !noPassword;
        }
        catch { return false; }
    }

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
            var profiles = db.UserProfiles.OrderBy(p => p.Id).ToList();
            foreach (var p in profiles)
                p.AccountIsPasswordProtected = AccountHasPassword(p.WindowsUsername);
            ProfilesGrid.ItemsSource = profiles;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load profiles: {ex.Message}");
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
                existing.DisplayName  = p.DisplayName;
                existing.IsEnabled   = p.IsEnabled;
                existing.AlwaysRelock = p.AlwaysRelock;
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
