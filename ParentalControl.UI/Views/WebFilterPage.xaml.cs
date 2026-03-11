using System.IO;
using System.Net.Http;
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

public class TagRow
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceUrl   { get; set; } = "";
    public bool   IsEnabled   { get; set; }
    public int    DomainCount { get; set; }
    public string CountLabel  => DomainCount > 0
        ? $"({DomainCount:N0})"
        : !string.IsNullOrEmpty(SourceUrl)
            ? "(not synced)"
            : "(0)";
}

public partial class WebFilterPage : Page
{
    private readonly IpcClient _ipc = new();
    private int  _selectedProfileId = 1;
    private bool _suppressTagCheck  = false;
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
        _ = SyncEmptyTagsAsync();
    }

    /// <summary>
    /// Silently downloads domains for any source-backed tag that currently has no entries.
    /// Shows the progress bar while running. Runs fire-and-forget from InitPage.
    /// </summary>
    private async Task SyncEmptyTagsAsync()
    {
        List<(int Id, string Name, string SourceUrl)> tagsToSync;
        try
        {
            using var db = new AppDbContext();
            var hasDomains = db.WebFilterTagDomains
                               .Select(d => d.TagId)
                               .Distinct()
                               .ToHashSet();
            tagsToSync = db.WebFilterTags
                           .Where(t => t.SourceUrl != null && t.SourceUrl != "")
                           .ToList()
                           .Where(t => !hasDomains.Contains(t.Id))
                           .Select(t => (t.Id, t.Name, t.SourceUrl))
                           .ToList();
        }
        catch { return; }

        if (tagsToSync.Count == 0) return;

        SyncProgressPanel.Visibility = Visibility.Visible;
        SyncAllBtn.IsEnabled = false;

        for (int i = 0; i < tagsToSync.Count; i++)
        {
            var (tagId, tagName, sourceUrl) = tagsToSync[i];
            SyncProgressLabel.Text = $"Syncing {tagName}… ({i + 1} of {tagsToSync.Count})";
            SyncProgressBar.Value  = (double)i / tagsToSync.Count * 100;

            try
            {
                await SyncTagFromSourceAsync(tagId, sourceUrl);
            }
            catch { /* best-effort — user can sync manually */ }

            await Dispatcher.InvokeAsync(LoadTags);
        }

        SyncProgressBar.Value  = 100;
        SyncProgressLabel.Text = $"Sync complete — {tagsToSync.Count} tag(s) updated.";

        await Task.Delay(2000);
        SyncProgressPanel.Visibility = Visibility.Collapsed;
        SyncAllBtn.IsEnabled = true;
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

            var profile   = db.UserProfiles.Find(_selectedProfileId);
            var allowMode = profile?.WebFilterAllowMode ?? false;

            AllowModeToggle.Checked   -= AllowModeToggle_Changed;
            AllowModeToggle.Unchecked -= AllowModeToggle_Changed;
            AllowModeToggle.IsChecked  = allowMode;
            AllowModeToggle.Checked   += AllowModeToggle_Changed;
            AllowModeToggle.Unchecked += AllowModeToggle_Changed;
            UpdateAllowModeLabel(allowMode);

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

            DomainsGrid.ItemsSource = rules;
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }

        LoadTags();
    }

    // ── Allow Mode ──────────────────────────────────────────────────────────

    private void AllowModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAllowModeLabel(AllowModeToggle.IsChecked == true);
    }

    private void UpdateAllowModeLabel(bool allowMode)
    {
        if (AllowModeLabel == null) return;
        AllowModeLabel.Text = allowMode
            ? "On — only Allowed entries are accessible; everything else is blocked"
            : "Off — only Blocked entries are blocked; everything else is accessible";
    }

    // ── Individual domain rules ──────────────────────────────────────────────

    private void AddDomain_Click(object sender, RoutedEventArgs e) => TryAddDomain();

    private void DomainBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryAddDomain();
    }

    private void TryAddDomain()
    {
        var raw = DomainBox.Text.Trim();
        if (string.IsNullOrEmpty(raw) || raw == PlaceholderText) return;

        raw = raw.Replace("https://", "").Replace("http://", "").TrimEnd('/');

        bool isBlocked = (EntryTypeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() != "Allowed";

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
                IsBlocked     = isBlocked,
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
        BumpWebRulesVersion();
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

        BumpWebRulesVersion();
        LoadData();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool allowMode = AllowModeToggle.IsChecked == true;
            using (var db = new AppDbContext())
            {
                var profile = db.UserProfiles.Find(_selectedProfileId);
                if (profile != null)
                {
                    profile.WebFilterAllowMode = allowMode;
                    db.SaveChanges();
                }
            }

            BumpWebRulesVersion();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            StatusText.Text = "Rules saved.";
            LoadData();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    private async void SaveToAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool allowMode = AllowModeToggle.IsChecked == true;
            using (var db = new AppDbContext())
            {
                var sourceRules = db.WebsiteRules
                                    .Where(r => r.UserProfileId == _selectedProfileId)
                                    .ToList();
                var sourceTags  = db.ProfileWebFilterTags
                                    .Where(pt => pt.UserProfileId == _selectedProfileId)
                                    .ToList();

                var otherProfiles = db.UserProfiles
                                      .Where(p => p.Id != _selectedProfileId)
                                      .ToList();

                foreach (var profile in otherProfiles)
                {
                    profile.WebFilterAllowMode = allowMode;

                    var existing = db.WebsiteRules
                                     .Where(r => r.UserProfileId == profile.Id)
                                     .ToList();
                    db.WebsiteRules.RemoveRange(existing);

                    foreach (var src in sourceRules)
                    {
                        db.WebsiteRules.Add(new WebsiteRule
                        {
                            Pattern       = src.Pattern,
                            IsBlocked     = src.IsBlocked,
                            UserProfileId = profile.Id,
                        });
                    }

                    var existingTags = db.ProfileWebFilterTags
                                         .Where(pt => pt.UserProfileId == profile.Id)
                                         .ToList();
                    db.ProfileWebFilterTags.RemoveRange(existingTags);

                    foreach (var src in sourceTags)
                    {
                        db.ProfileWebFilterTags.Add(new ProfileWebFilterTag
                        {
                            TagId         = src.TagId,
                            UserProfileId = profile.Id,
                        });
                    }
                }
                db.SaveChanges();
            }

            BumpWebRulesVersion();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            StatusText.Text = "Rules saved to all profiles.";
            LoadData();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    // ── Content Tags ─────────────────────────────────────────────────────────

    private void LoadTags()
    {
        try
        {
            using var db = new AppDbContext();
            var enabledIds = db.ProfileWebFilterTags
                               .Where(pt => pt.UserProfileId == _selectedProfileId)
                               .Select(pt => pt.TagId)
                               .ToHashSet();

            var counts = db.WebFilterTagDomains
                           .GroupBy(d => d.TagId)
                           .Select(g => new { g.Key, Count = g.Count() })
                           .ToDictionary(x => x.Key, x => x.Count);

            var rows = db.WebFilterTags
                         .OrderBy(t => t.Name)
                         .Select(t => new TagRow
                         {
                             Id          = t.Id,
                             Name        = t.Name,
                             Description = t.Description,
                             SourceUrl   = t.SourceUrl,
                         })
                         .ToList();

            foreach (var row in rows)
            {
                row.IsEnabled   = enabledIds.Contains(row.Id);
                row.DomainCount = counts.GetValueOrDefault(row.Id, 0);
            }

            _suppressTagCheck = true;
            TagsListBox.ItemsSource = rows;
            _suppressTagCheck = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading tags: {ex.Message}";
        }
    }

    private void TagEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTagCheck) return;
        if (sender is not CheckBox cb || cb.DataContext is not TagRow tag) return;

        // Read the new state directly from the CheckBox — the TwoWay binding has not yet
        // written back to tag.IsEnabled when Checked/Unchecked fires (WPF fires the routed
        // event from the PropertyChangedCallback, before the binding source update runs).
        bool isNowEnabled = cb.IsChecked == true;

        try
        {
            using var db = new AppDbContext();
            var existing = db.ProfileWebFilterTags
                              .FirstOrDefault(pt => pt.TagId == tag.Id && pt.UserProfileId == _selectedProfileId);

            if (isNowEnabled && existing == null)
            {
                db.ProfileWebFilterTags.Add(new ProfileWebFilterTag
                {
                    TagId         = tag.Id,
                    UserProfileId = _selectedProfileId,
                });
                db.SaveChanges();
            }
            else if (!isNowEnabled && existing != null)
            {
                db.ProfileWebFilterTags.Remove(existing);
                db.SaveChanges();
            }

            BumpWebRulesVersion();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error updating tag: {ex.Message}";
        }
    }

    private async void SyncAll_Click(object sender, RoutedEventArgs e)
    {
        List<(int Id, string Name, string SourceUrl)> allTags;
        try
        {
            using var db = new AppDbContext();
            allTags = db.WebFilterTags
                        .OrderBy(t => t.Name)
                        .Select(t => new { t.Id, t.Name, t.SourceUrl })
                        .ToList()
                        .Select(t => (t.Id, t.Name, t.SourceUrl ?? ""))
                        .ToList();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            return;
        }

        if (allTags.Count == 0) return;

        SyncAllBtn.IsEnabled         = false;
        SyncProgressPanel.Visibility = Visibility.Visible;

        int synced = 0;
        for (int i = 0; i < allTags.Count; i++)
        {
            var (tagId, tagName, sourceUrl) = allTags[i];
            SyncProgressBar.Value = (double)i / allTags.Count * 100;

            if (!string.IsNullOrEmpty(sourceUrl))
            {
                SyncProgressLabel.Text = $"Syncing {tagName}… ({i + 1} of {allTags.Count})";
                try
                {
                    await SyncTagFromSourceAsync(tagId, sourceUrl);
                    synced++;
                }
                catch (Exception ex)
                {
                    SyncProgressLabel.Text = $"Error syncing {tagName}: {ex.Message}";
                    await Task.Delay(1500);
                }
            }
            else
            {
                // Manually curated list — no external source; domains are already seeded.
                SyncProgressLabel.Text = $"{tagName} — curated list ({i + 1} of {allTags.Count})";
                await Task.Delay(400);
            }

            LoadTags();
        }

        SyncProgressBar.Value  = 100;
        SyncProgressLabel.Text = $"Complete — {synced} tag(s) refreshed from source.";
        BumpWebRulesVersion();

        await Task.Delay(2000);
        SyncProgressPanel.Visibility = Visibility.Collapsed;
        SyncAllBtn.IsEnabled = true;
    }

    private void ViewDomains_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not TagRow tag) return;
        var viewer = new TagDomainViewerWindow(tag.Id, tag.Name)
        {
            Owner = Window.GetWindow(this)
        };
        viewer.Show();
    }

    private void CopyTagsToAllProfiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var db = new AppDbContext();
            var sourceTags = db.ProfileWebFilterTags
                               .Where(pt => pt.UserProfileId == _selectedProfileId)
                               .ToList();

            var otherProfileIds = db.UserProfiles
                                    .Where(p => p.Id != _selectedProfileId)
                                    .Select(p => p.Id)
                                    .ToList();

            foreach (var profileId in otherProfileIds)
            {
                db.ProfileWebFilterTags.RemoveRange(
                    db.ProfileWebFilterTags.Where(pt => pt.UserProfileId == profileId));
                foreach (var src in sourceTags)
                {
                    db.ProfileWebFilterTags.Add(new ProfileWebFilterTag
                    {
                        TagId         = src.TagId,
                        UserProfileId = profileId,
                    });
                }
            }

            db.SaveChanges();
            BumpWebRulesVersion();
            StatusText.Text = "Tag settings copied to all profiles.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    // ── Shared sync helper ───────────────────────────────────────────────────

    private static async Task SyncTagFromSourceAsync(int tagId, string sourceUrl)
    {
        string content;
        using (var http = new HttpClient())
        {
            http.Timeout = TimeSpan.FromSeconds(60);
            http.DefaultRequestHeaders.Add("User-Agent", "ParentGuard/1.0");
            content = await http.GetStringAsync(sourceUrl);
        }

        var domains = ParseDomainList(content).ToList();

        await Task.Run(() =>
        {
            using var db = new AppDbContext();
            db.WebFilterTagDomains.RemoveRange(
                db.WebFilterTagDomains.Where(d => d.TagId == tagId));
            db.SaveChanges();

            const int batchSize = 500;
            for (int i = 0; i < domains.Count; i += batchSize)
            {
                foreach (var d in domains.Skip(i).Take(batchSize))
                    db.WebFilterTagDomains.Add(new WebFilterTagDomain { TagId = tagId, Domain = d });
                db.SaveChanges();
            }
        });
    }

    // ── Domain list parser ───────────────────────────────────────────────────

    private static IEnumerable<string> ParseDomainList(string content)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            var ci = line.IndexOf('#');
            if (ci >= 0) line = line[..ci].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Hosts format: "0.0.0.0 domain.com" or "127.0.0.1 domain.com"
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string domain = parts.Length >= 2 && IsIpLike(parts[0]) ? parts[1] : parts[0];

            domain = domain.ToLowerInvariant().TrimStart('*').TrimStart('.');

            if (!domain.Contains('.') || domain.Contains('/') || domain.Length > 253) continue;
            if (domain is "localhost" or "broadcasthost" or "local") continue;

            if (seen.Add(domain)) yield return domain;
        }
    }

    private static bool IsIpLike(string s) =>
        s == "0.0.0.0" || s == "127.0.0.1" || s.Split('.').All(p => int.TryParse(p, out _));

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void BumpWebRulesVersion()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ParentalControl");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "web_rules_version.txt"),
                DateTime.UtcNow.Ticks.ToString());
        }
        catch { }
    }

    private void DomainBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (DomainBox.Text == PlaceholderText)
        {
            DomainBox.Text       = "";
            DomainBox.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
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
