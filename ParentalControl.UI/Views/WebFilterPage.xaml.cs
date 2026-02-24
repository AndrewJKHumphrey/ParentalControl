using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public partial class WebFilterPage : Page
{
    private readonly IpcClient _ipc = new();
    private bool _loaded;

    public WebFilterPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        try
        {
            _loaded = false;
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            bool isAllowMode = settings?.IsAllowMode ?? false;

            AllowModeToggle.IsChecked = isAllowMode;
            UpdateModeUi(isAllowMode);

            WebRulesGrid.ItemsSource = db.WebsiteRules.OrderBy(r => r.Domain).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
        }
        finally
        {
            _loaded = true;
        }
    }

    private void UpdateModeUi(bool isAllowMode)
    {
        if (isAllowMode)
        {
            ModeDescText.Text = "Allow Mode: only listed domains are accessible; all other sites are blocked.";
            PageDescText.Text = "Allow websites by domain. All other domains will be blocked. Enter domain only (e.g. google.com).";
            AddDomainButton.Content = "Allow Domain";
            AddDomainButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#A6E3A1"));
        }
        else
        {
            ModeDescText.Text = "Block Mode: listed domains are blocked; all other sites are accessible.";
            PageDescText.Text = "Block websites by domain. Blocked domains are added to the system hosts file. Enter domain only (e.g. tiktok.com).";
            AddDomainButton.Content = "Block Domain";
            AddDomainButton.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F38BA8"));
        }
    }

    private void FilterMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        bool isAllowMode = AllowModeToggle.IsChecked == true;
        UpdateModeUi(isAllowMode);

        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings != null)
            {
                settings.IsAllowMode = isAllowMode;
                db.SaveChanges();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save mode: {ex.Message}");
        }
    }

    private void AddDomain_Click(object sender, RoutedEventArgs e)
    {
        var domain = DomainBox.Text.Trim()
            .ToLower()
            .Replace("https://", "").Replace("http://", "")
            .Replace("www.", "").TrimEnd('/');

        if (string.IsNullOrEmpty(domain)) return;

        bool isAllowMode = AllowModeToggle.IsChecked == true;

        try
        {
            using var db = new AppDbContext();
            var existing = db.WebsiteRules.FirstOrDefault(r => r.Domain == domain);
            if (existing == null)
            {
                db.WebsiteRules.Add(new WebsiteRule
                {
                    Domain = domain,
                    IsBlocked = !isAllowMode,
                    IsAllowed = isAllowMode,
                });
                db.SaveChanges();
            }
            else if (isAllowMode && !existing.IsAllowed)
            {
                existing.IsAllowed = true;
                db.SaveChanges();
            }
            else if (!isAllowMode && !existing.IsBlocked)
            {
                existing.IsBlocked = true;
                db.SaveChanges();
            }

            DomainBox.Clear();
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add: {ex.Message}");
        }
    }

    private void DeleteDomain_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            try
            {
                using var db = new AppDbContext();
                var rule = db.WebsiteRules.Find(id);
                if (rule != null) { db.WebsiteRules.Remove(rule); db.SaveChanges(); }
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete: {ex.Message}");
            }
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (WebRulesGrid.ItemsSource is List<WebsiteRule> rules)
        {
            try
            {
                using var db = new AppDbContext();
                foreach (var r in rules)
                {
                    var existing = db.WebsiteRules.Find(r.Id);
                    if (existing != null)
                    {
                        existing.IsBlocked = r.IsBlocked;
                        existing.IsAllowed = r.IsAllowed;
                    }
                }
                db.SaveChanges();
                await _ipc.SendAsync(IpcCommand.ReloadWebFilter);
                StatusText.Text = "Applied. Browser rules and hosts file updated; Edge and Chrome were restarted.";
                StatusText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}");
            }
        }
    }
}
