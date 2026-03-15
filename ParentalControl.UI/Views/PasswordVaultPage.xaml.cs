using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core.Data;
using ParentalControl.Core.Helpers;
using ParentalControl.Core.Models;

namespace ParentalControl.UI.Views;

public partial class PasswordVaultPage : Page
{
    public PasswordVaultPage()
    {
        InitializeComponent();
        Loaded += (_, _) => InitPage();
    }

    private void InitPage()
    {
        try
        {
            using var db = new AppDbContext();
            var profiles = db.UserProfiles.OrderBy(p => p.Id).ToList();
            ProfilePicker.ItemsSource    = profiles;
            PinProfileSelector.ItemsSource = profiles;
            if (profiles.Count > 0)
            {
                ProfilePicker.SelectedIndex      = 0;
                PinProfileSelector.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load profiles: {ex.Message}");
        }

        LoadData();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            var entries = db.VaultEntries
                .Join(db.UserProfiles,
                      v => v.UserProfileId,
                      p => p.Id,
                      (v, p) => new VaultRow
                      {
                          Id                = v.Id,
                          ProfileName       = p.DisplayName,
                          SiteName          = v.SiteName,
                          Username          = v.Username,
                          EncryptedPassword = v.EncryptedPassword
                      })
                .OrderBy(r => r.ProfileName)
                .ThenBy(r => r.SiteName)
                .ToList();

            VaultGrid.ItemsSource = entries;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load vault: {ex.Message}");
        }
    }

    private void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = Visibility.Visible;
        SiteBox.Clear();
        UsernameBox.Clear();
        PasswordBox.Clear();
    }

    private void CancelAdd_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = Visibility.Collapsed;
    }

    private void SaveEntry_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilePicker.SelectedItem is not UserProfile profile)
        {
            MessageBox.Show("Please select a child profile.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var site     = SiteBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(site))
        {
            MessageBox.Show("Please enter a site or service name.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            db.VaultEntries.Add(new VaultEntry
            {
                UserProfileId     = profile.Id,
                SiteName          = site,
                Username          = username,
                EncryptedPassword = string.IsNullOrEmpty(password)
                                        ? ""
                                        : VaultCrypto.Encrypt(password),
                CreatedAt         = DateTime.UtcNow
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save entry: {ex.Message}");
            return;
        }

        AddForm.Visibility = Visibility.Collapsed;
        LoadData();
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int id) return;

        var result = MessageBox.Show("Delete this entry?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var entry = db.VaultEntries.Find(id);
            if (entry != null) { db.VaultEntries.Remove(entry); db.SaveChanges(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete entry: {ex.Message}");
        }

        LoadData();
    }

    private void ShowPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string enc) return;

        var plain = string.IsNullOrEmpty(enc) ? "(no password stored)" : VaultCrypto.Decrypt(enc);
        if (string.IsNullOrEmpty(plain)) plain = "(decryption failed)";

        MessageBox.Show(plain, "Password", MessageBoxButton.OK, MessageBoxImage.None);
    }

    private void CopyUsername_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string username)
            Clipboard.SetText(username);
    }

    private void CopyPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string enc) return;

        var plain = string.IsNullOrEmpty(enc) ? "" : VaultCrypto.Decrypt(enc);
        if (!string.IsNullOrEmpty(plain))
            Clipboard.SetText(plain);
    }

    private void OpenChildVault_Click(object sender, RoutedEventArgs e)
    {
        // Look for the exe next to the parent UI, then next to the install location
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ParentalControl.ChildVault.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         "ParentalControl", "ParentalControl.ChildVault.exe")
        };

        var exePath = candidates.FirstOrDefault(File.Exists);
        if (exePath is null)
        {
            MessageBox.Show(
                "ParentalControl.ChildVault.exe was not found.\n\n" +
                "Make sure the Child Vault has been published alongside this application.",
                "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Child Vault: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PinProfileSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdatePinStatus();
    }

    private void UpdatePinStatus()
    {
        if (PinProfileSelector.SelectedItem is not UserProfile profile)
        {
            PinStatusText.Text       = "";
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var stored = db.UserProfiles.Find(profile.Id);
            bool hasPин = stored != null && !string.IsNullOrEmpty(stored.VaultPin);
            PinStatusText.Text       = hasPин ? "PIN set" : "No PIN — child cannot access vault";
            PinStatusText.Foreground = hasPин
                ? new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1))   // green
                : new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87));   // amber
        }
        catch { }
    }

    private void SetPin_Click(object sender, RoutedEventArgs e)
    {
        if (PinProfileSelector.SelectedItem is not UserProfile profile) return;

        var pin = PinBox.Password;
        if (!Regex.IsMatch(pin, @"^\d{4,8}$"))
        {
            MessageBox.Show("PIN must be 4–8 digits (numbers only).", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var stored = db.UserProfiles.Find(profile.Id);
            if (stored == null) return;
            stored.VaultPin = BCrypt.Net.BCrypt.HashPassword(pin);
            db.SaveChanges();
            PinBox.Clear();
            UpdatePinStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to set PIN: {ex.Message}");
        }
    }

    private void ClearPin_Click(object sender, RoutedEventArgs e)
    {
        if (PinProfileSelector.SelectedItem is not UserProfile profile) return;

        var result = MessageBox.Show(
            $"Clear the vault PIN for {profile.DisplayName}? They will no longer be able to access the extension vault.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            var stored = db.UserProfiles.Find(profile.Id);
            if (stored == null) return;
            stored.VaultPin = "";
            db.SaveChanges();
            UpdatePinStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear PIN: {ex.Message}");
        }
    }

    // DTO for DataGrid binding
    private class VaultRow
    {
        public int    Id                { get; set; }
        public string ProfileName       { get; set; } = "";
        public string SiteName          { get; set; } = "";
        public string Username          { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
    }
}
