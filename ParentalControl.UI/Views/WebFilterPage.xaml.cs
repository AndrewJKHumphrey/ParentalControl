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

    public WebFilterPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            WebRulesGrid.ItemsSource = db.WebsiteRules.OrderBy(r => r.Domain).ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
        }
    }

    private void BlockDomain_Click(object sender, RoutedEventArgs e)
    {
        var domain = DomainBox.Text.Trim()
            .ToLower()
            .Replace("https://", "").Replace("http://", "")
            .Replace("www.", "").TrimEnd('/');

        if (string.IsNullOrEmpty(domain)) return;

        try
        {
            using var db = new AppDbContext();
            if (!db.WebsiteRules.Any(r => r.Domain == domain))
            {
                db.WebsiteRules.Add(new WebsiteRule { Domain = domain, IsBlocked = true });
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
                    if (existing != null) existing.IsBlocked = r.IsBlocked;
                }
                db.SaveChanges();
                await _ipc.SendAsync(IpcCommand.ReloadRules);
                StatusText.Text = "Applied to hosts file.";
                StatusText.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}");
            }
        }
    }
}
