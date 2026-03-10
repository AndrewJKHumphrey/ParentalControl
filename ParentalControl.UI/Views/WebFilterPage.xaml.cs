using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public class WebsiteRuleRow
{
    public int    Id        { get; set; }
    public string Pattern   { get; set; } = "";
    public bool   IsBlocked { get; set; } = true;
    public string ModeLabel => IsBlocked ? "Blocked" : "Allowed";
}

public partial class WebFilterPage : Page
{
    private readonly IpcClient _ipc = new();
    private int  _selectedProfileId = 1;
    private bool _allowMode         = false;
    private const string PlaceholderText = "Domain (e.g. example.com)";

    public WebFilterPage()
    {
        InitializeComponent();
        DomainBox.Text       = PlaceholderText;
        DomainBox.Foreground = System.Windows.Media.Brushes.Gray;
        Loaded += (_, _) => InitPage();
    }

    private void InitPage()
    {
        try
        {
            using var db = new AppDbContext();
            var profiles = db.UserProfiles.OrderBy(p => p.Id).ToList();
            ProfileSelector.ItemsSource   = profiles;
            ProfileSelector.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load profiles: {ex.Message}");
        }
    }

    private void ProfileSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is UserProfile p)
        {
            _selectedProfileId = p.Id;
            LoadData();
        }
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            var rules = db.WebsiteRules
                          .Where(r => r.UserProfileId == _selectedProfileId)
                          .OrderBy(r => r.Pattern)
                          .Select(r => new WebsiteRuleRow
                          {
                              Id        = r.Id,
                              Pattern   = r.Pattern,
                              IsBlocked = r.IsBlocked,
                          })
                          .ToList();

            // Detect mode from existing rules: if any allow-exceptions exist, we're in allow mode
            _allowMode = rules.Any(r => !r.IsBlocked);
            BlockModeRadio.IsChecked = !_allowMode;
            AllowModeRadio.IsChecked =  _allowMode;

            DomainsGrid.ItemsSource = rules;
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        _allowMode = AllowModeRadio.IsChecked == true;
    }

    private void AddDomain_Click(object sender, RoutedEventArgs e) => TryAddDomain();

    private void DomainBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAddDomain();
    }

    private void TryAddDomain()
    {
        var raw = DomainBox.Text.Trim();
        if (string.IsNullOrEmpty(raw) || raw == PlaceholderText) return;

        // Normalise: strip scheme and trailing slashes
        raw = raw.Replace("https://", "").Replace("http://", "").TrimEnd('/');

        try
        {
            using var db = new AppDbContext();

            if (db.WebsiteRules.Any(r => r.Pattern == raw && r.UserProfileId == _selectedProfileId))
            {
                StatusText.Text = $"'{raw}' is already in the list.";
                return;
            }

            db.WebsiteRules.Add(new WebsiteRule
            {
                Pattern       = raw,
                IsBlocked     = !_allowMode,
                UserProfileId = _selectedProfileId,
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            return;
        }

        DomainBox.Text       = PlaceholderText;
        DomainBox.Foreground = System.Windows.Media.Brushes.Gray;
        LoadData();
    }

    private void RemoveDomain_Click(object sender, RoutedEventArgs e)
    {
        var selected = DomainsGrid.SelectedItems.Cast<WebsiteRuleRow>().ToList();
        if (selected.Count == 0) return;

        try
        {
            using var db = new AppDbContext();
            var ids = selected.Select(r => r.Id).ToHashSet();
            var toRemove = db.WebsiteRules.Where(r => ids.Contains(r.Id)).ToList();
            db.WebsiteRules.RemoveRange(toRemove);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            return;
        }

        LoadData();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Update IsBlocked flag on all existing rules for this profile to match current mode
            using (var db = new AppDbContext())
            {
                var rules = db.WebsiteRules
                              .Where(r => r.UserProfileId == _selectedProfileId)
                              .ToList();
                foreach (var r in rules)
                    r.IsBlocked = !_allowMode;
                db.SaveChanges();
            }

            await _ipc.SendAsync(IpcCommand.ReloadRules);
            StatusText.Text = "Rules saved.";
            LoadData();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    // ── Placeholder text helpers ─────────────────────────────────────────────

    private void DomainBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DomainBox.Text == PlaceholderText)
        {
            DomainBox.Text       = "";
            DomainBox.Foreground = (System.Windows.Media.Brush)
                FindResource("TextPrimary");
        }
    }

    private void DomainBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DomainBox.Text))
        {
            DomainBox.Text       = PlaceholderText;
            DomainBox.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }
}
