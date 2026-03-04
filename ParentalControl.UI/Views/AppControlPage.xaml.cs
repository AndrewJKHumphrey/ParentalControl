using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Helpers;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public class AppTimeScheduleRow
{
    public int       Id                { get; set; }
    public DayOfWeek DayOfWeek         { get; set; }
    public string    DayLabel          { get; set; } = "";
    public bool      IsEnabled         { get; set; }
    public int       DailyLimitMinutes { get; set; }
}

public partial class AppControlPage : Page
{
    private readonly IpcClient _ipc = new();
    private int _selectedProfileId = 1;
    private List<AppTimeScheduleRow> _scheduleRows = new();
    private static readonly HttpClient _http = new();

    public AppControlPage()
    {
        InitializeComponent();
        Loaded += (_, _) => InitPage();
    }

    private void InitPage()
    {
        PopulateDriveSelector();
        try
        {
            using var db = new AppDbContext();
            var profiles = db.UserProfiles.OrderBy(p => p.Id).ToList();
            ProfileSelector.ItemsSource   = profiles;
            ProfileSelector.SelectedIndex = 0;
            // SelectionChanged fires and calls LoadData()
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
            AppRulesGrid.ItemsSource = db.AppRules
                .Where(r => r.UserProfileId == _selectedProfileId)
                .OrderBy(r => r.ProcessName)
                .ToList();

            if (!db.AppTimeSchedules.Any(s => s.UserProfileId == _selectedProfileId))
            {
                foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
                {
                    db.AppTimeSchedules.Add(new AppTimeSchedule
                    {
                        UserProfileId     = _selectedProfileId,
                        DayOfWeek         = day,
                        IsEnabled         = false,
                        DailyLimitMinutes = 60,
                    });
                }
                db.SaveChanges();
            }

            _scheduleRows = db.AppTimeSchedules
                .Where(s => s.UserProfileId == _selectedProfileId)
                .OrderBy(s => s.DayOfWeek)
                .Select(s => new AppTimeScheduleRow
                {
                    Id                = s.Id,
                    DayOfWeek         = s.DayOfWeek,
                    DayLabel          = s.DayOfWeek.ToString(),
                    IsEnabled         = s.IsEnabled,
                    DailyLimitMinutes = s.DailyLimitMinutes,
                })
                .ToList();
            DayRangeBox.SelectedIndex = 0;
            AppTimeScheduleGrid.ItemsSource = _scheduleRows;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
        }
    }

    private List<DayOfWeek> GetSelectedDays() => DayRangeBox.SelectedIndex switch
    {
        1 => new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                     DayOfWeek.Thursday, DayOfWeek.Friday },
        2 => new() { DayOfWeek.Saturday, DayOfWeek.Sunday },
        3 => Enum.GetValues<DayOfWeek>().ToList(),
        _ => new()
    };

    private void DayRangeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_scheduleRows.Count == 0) return;
        var range = GetSelectedDays();
        if (range.Count == 0) { AppTimeScheduleGrid.ItemsSource = _scheduleRows; return; }

        var label  = (DayRangeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var source = _scheduleRows.FirstOrDefault(r => range.Contains(r.DayOfWeek)) ?? _scheduleRows[0];
        var tmpl   = new AppTimeScheduleRow
        {
            Id                = source.Id,
            DayOfWeek         = source.DayOfWeek,
            DayLabel          = label,
            IsEnabled         = source.IsEnabled,
            DailyLimitMinutes = source.DailyLimitMinutes,
        };
        AppTimeScheduleGrid.ItemsSource = new List<AppTimeScheduleRow> { tmpl };
    }

    private void BroadcastToRange()
    {
        var range = GetSelectedDays();
        if (range.Count == 0) return;
        if (AppTimeScheduleGrid.ItemsSource is not List<AppTimeScheduleRow> shown || shown.Count != 1) return;
        var tmpl = shown[0];
        foreach (var row in _scheduleRows.Where(r => range.Contains(r.DayOfWeek)))
        {
            row.IsEnabled         = tmpl.IsEnabled;
            row.DailyLimitMinutes = tmpl.DailyLimitMinutes;
        }
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrEmpty(processName)) return;

        try
        {
            using var db = new AppDbContext();
            if (!db.AppRules.Any(r => r.ProcessName == processName && r.UserProfileId == _selectedProfileId))
            {
                db.AppRules.Add(new AppRule
                {
                    ProcessName     = processName.ToLower(),
                    DisplayName     = string.IsNullOrEmpty(DisplayNameBox.Text) ? processName : DisplayNameBox.Text.Trim(),
                    AccessMode      = (int)AppAccessMode.Blocked,
                    UserProfileId   = _selectedProfileId,
                    AllowedInFocusMode = false,
                });
                db.SaveChanges();
            }
            ProcessNameBox.Clear();
            DisplayNameBox.Clear();
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to add rule: {ex.Message}");
        }
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            try
            {
                using var db = new AppDbContext();
                var rule = db.AppRules.Find(id);
                if (rule != null) { db.AppRules.Remove(rule); db.SaveChanges(); }
                LoadData();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete: {ex.Message}");
            }
        }
    }

    private async void SaveToAllButton_Click(object sender, RoutedEventArgs e)
    {
        BroadcastToRange();
        var scheduleErrors = new List<string>();
        foreach (var row in _scheduleRows)
        {
            if (row.IsEnabled && row.DailyLimitMinutes < 1)
                scheduleErrors.Add($"{row.DayOfWeek}: limit must be at least 1 minute when enabled.");
        }
        if (scheduleErrors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", scheduleErrors), "Invalid App Time Schedule",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var allProfileIds = db.UserProfiles.Select(p => p.Id).ToList();

            // App time schedule: copy to all profiles
            foreach (var profileId in allProfileIds)
            {
                foreach (var row in _scheduleRows)
                {
                    var schedule = db.AppTimeSchedules.FirstOrDefault(
                        s => s.UserProfileId == profileId && s.DayOfWeek == row.DayOfWeek);
                    if (schedule == null) continue;
                    schedule.IsEnabled         = row.IsEnabled;
                    schedule.DailyLimitMinutes = row.DailyLimitMinutes;
                }
            }

            // App rules: update matching rules in other profiles by process name
            if (AppRulesGrid.ItemsSource is List<AppRule> rules)
            {
                foreach (var profileId in allProfileIds.Where(id => id != _selectedProfileId))
                {
                    foreach (var r in rules)
                    {
                        var target = db.AppRules.FirstOrDefault(
                            x => x.UserProfileId == profileId && x.ProcessName == r.ProcessName);
                        if (target != null)
                        {
                            target.AccessMode         = r.AccessMode;
                            target.AllowedInFocusMode = r.AllowedInFocusMode;
                        }
                        else
                        {
                            db.AppRules.Add(new AppRule
                            {
                                ProcessName        = r.ProcessName,
                                DisplayName        = r.DisplayName,
                                AccessMode         = r.AccessMode,
                                AllowedInFocusMode = r.AllowedInFocusMode,
                                UserProfileId      = profileId,
                            });
                        }
                    }
                }
            }

            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            MessageBox.Show("Saved to all profiles.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppRulesGrid.ItemsSource is not List<AppRule> rules) return;

        BroadcastToRange();
        // Validate schedule rows before touching DB
        var scheduleErrors = new List<string>();
        foreach (var row in _scheduleRows)
        {
            if (row.IsEnabled && row.DailyLimitMinutes < 1)
                scheduleErrors.Add($"{row.DayOfWeek}: limit must be at least 1 minute when enabled.");
        }
        if (scheduleErrors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", scheduleErrors), "Invalid App Time Schedule",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();

            // Save app rules
            foreach (var r in rules)
            {
                var existing = db.AppRules.Find(r.Id);
                if (existing != null)
                {
                    existing.AccessMode         = r.AccessMode;
                    existing.DisplayName        = r.DisplayName;
                    existing.ProcessName        = r.ProcessName;
                    existing.AllowedInFocusMode = r.AllowedInFocusMode;
                }
            }

            // Save app time schedule rows
            foreach (var row in _scheduleRows)
            {
                var schedule = db.AppTimeSchedules.Find(row.Id);
                if (schedule != null)
                {
                    schedule.IsEnabled         = row.IsEnabled;
                    schedule.DailyLimitMinutes = row.DailyLimitMinutes;
                }
            }

            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            MessageBox.Show("Saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Game scanning
    // -------------------------------------------------------------------------

    private void PopulateDriveSelector()
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => d.RootDirectory.FullName)
            .ToList();
        DriveSelector.ItemsSource = drives;
        if (drives.Count > 0) DriveSelector.SelectedIndex = 0;
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        if (DriveSelector.SelectedItem is not string driveRoot) return;

        ScanButton.IsEnabled = false;
        ScanStatusText.Text = "Scanning for games...";
        ScanStatusText.Visibility = Visibility.Visible;

        try
        {
            // Phase 1 — scan drive for game executables
            var games = await Task.Run(() => ScanDrive(driveRoot));

            // Phase 2 — ESRB lookup via RAWG API (skipped if no key configured)
            string rawgKey = "";
            using (var db0 = new AppDbContext())
                rawgKey = ApiKeyHelper.DecryptFromStorage(
                    db0.Settings.FirstOrDefault()?.RawgApiKey ?? "");

            var ratings = new Dictionary<string, (string Rating, string Genres, string Tags)>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(rawgKey) && games.Count > 0)
            {
                ScanStatusText.Text = $"Found {games.Count} game(s), looking up ratings & genres...";
                ratings = await LookupRatingsAsync(games, rawgKey);
            }

            // Load configurable scan rules from settings
            string[] ratingOrder = { "E", "E10+", "T", "M", "AO" };
            int blockThresholdIdx, appTimeThresholdIdx, focusModeThresholdIdx;
            HashSet<string> blockedGenres, blockedTags;
            HashSet<string> appTimeGenres, appTimeTags;
            HashSet<string> focusModeGenres, focusModeTags;
            using (var dbSettings = new AppDbContext())
            {
                var s = dbSettings.Settings.FirstOrDefault();
                static HashSet<string> ParseSet(string? v) =>
                    (v ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
                blockThresholdIdx    = Array.IndexOf(ratingOrder, s?.ScanBlockRating    ?? "M");
                appTimeThresholdIdx  = Array.IndexOf(ratingOrder, s?.ScanAppTimeRating  ?? "None");
                focusModeThresholdIdx = Array.IndexOf(ratingOrder, s?.ScanFocusModeRating ?? "None");
                blockedGenres    = ParseSet(s?.ScanBlockedGenres);
                blockedTags      = ParseSet(s?.ScanBlockedTags);
                appTimeGenres    = ParseSet(s?.ScanAppTimeGenres);
                appTimeTags      = ParseSet(s?.ScanAppTimeTags);
                focusModeGenres  = ParseSet(s?.ScanFocusModeGenres);
                focusModeTags    = ParseSet(s?.ScanFocusModeTags);
            }

            // Returns true when rating/genre/tag matches the given rule threshold + sets
            bool MatchesRule(string rating, string genres, string tags,
                             int thresholdIdx, bool atOrBelow,
                             HashSet<string> ruleGenres, HashSet<string> ruleTags)
            {
                int ri = Array.IndexOf(ratingOrder, rating);
                if (thresholdIdx >= 0 && ri >= 0 &&
                    (atOrBelow ? ri <= thresholdIdx : ri >= thresholdIdx)) return true;
                var gs = genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ruleGenres.Count > 0 && gs.Any(g => ruleGenres.Contains(g))) return true;
                var ts = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ruleTags.Count > 0 && ts.Any(t => ruleTags.Contains(t))) return true;
                return false;
            }

            // Phase 3 — upsert into DB: update existing (rescan), insert new
            int added = 0, updated = 0, autoBlocked = 0;
            using var db = new AppDbContext();
            foreach (var (name, processName) in games)
            {
                if (!ratings.TryGetValue(processName, out var info))
                    info = ("", "", "");

                // Block takes priority over App Time; Focus Mode is independent
                bool block   = MatchesRule(info.Rating, info.Genres, info.Tags, blockThresholdIdx,   false, blockedGenres,   blockedTags);
                bool appTime = !block && MatchesRule(info.Rating, info.Genres, info.Tags, appTimeThresholdIdx,  false, appTimeGenres,   appTimeTags);
                bool focus   = MatchesRule(info.Rating, info.Genres, info.Tags, focusModeThresholdIdx, true,  focusModeGenres, focusModeTags);

                int accessMode = block   ? (int)AppAccessMode.Blocked
                               : appTime ? (int)AppAccessMode.ScreenTimeOnly
                               :           (int)AppAccessMode.Unrestricted;

                var existing = db.AppRules.FirstOrDefault(
                    r => r.ProcessName == processName && r.UserProfileId == _selectedProfileId);
                if (existing != null)
                {
                    existing.EsrbRating        = info.Rating;
                    existing.Genres            = info.Genres;
                    existing.Tags              = info.Tags;
                    existing.IsBlocked         = block;
                    existing.AccessMode        = accessMode;
                    existing.AllowedInFocusMode = focus;
                    updated++;
                }
                else
                {
                    db.AppRules.Add(new AppRule
                    {
                        DisplayName        = name,
                        ProcessName        = processName,
                        IsBlocked          = block,
                        AccessMode         = accessMode,
                        EsrbRating         = info.Rating,
                        Genres             = info.Genres,
                        Tags               = info.Tags,
                        UserProfileId      = _selectedProfileId,
                        AllowedInFocusMode = focus,
                    });
                    added++;
                }
                if (block) autoBlocked++;
            }
            db.SaveChanges();

            var msg = games.Count == 0
                ? "No games found."
                : $"Found {games.Count}: {added} added, {updated} updated" +
                  (autoBlocked > 0 ? $", {autoBlocked} blocked by scan rules" : "") + ".";
            ScanStatusText.Text = msg;
            LoadData();
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private static async Task<Dictionary<string, (string Rating, string Genres, string Tags)>>
        LookupRatingsAsync(List<(string Name, string ProcessName)> games, string apiKey)
    {
        var result = new ConcurrentDictionary<string, (string, string, string)>(
            StringComparer.OrdinalIgnoreCase);
        var sem = new SemaphoreSlim(4);

        await Task.WhenAll(games.Select(async g =>
        {
            await sem.WaitAsync();
            try
            {
                var info = await LookupGameInfoAsync(g.Name, apiKey);
                result[g.ProcessName] = info;
            }
            finally { sem.Release(); }
        }));

        return new Dictionary<string, (string, string, string)>(result,
            StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<(string Rating, string Genres, string Tags)> LookupGameInfoAsync(
        string gameName, string apiKey)
    {
        try
        {
            var url = "https://api.rawg.io/api/games" +
                      $"?key={Uri.EscapeDataString(apiKey)}" +
                      $"&search={Uri.EscapeDataString(gameName)}&page_size=1";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return ("", "", "");
            var first = results[0];

            // ESRB rating
            string rating = "";
            if (first.TryGetProperty("esrb_rating", out var esrb) &&
                esrb.ValueKind != JsonValueKind.Null)
            {
                rating = (esrb.GetProperty("name").GetString() ?? "") switch
                {
                    "Everyone"     => "E",
                    "Everyone 10+" => "E10+",
                    "Teen"         => "T",
                    "Mature"       => "M",
                    "Adults Only"  => "AO",
                    _              => ""
                };
            }

            // Genres
            string genres = "";
            if (first.TryGetProperty("genres", out var genresEl) &&
                genresEl.ValueKind == JsonValueKind.Array)
            {
                genres = string.Join(", ", genresEl.EnumerateArray()
                    .Select(g => g.GetProperty("name").GetString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n)));
            }

            // Tags (English only, first 10)
            string tags = "";
            if (first.TryGetProperty("tags", out var tagsEl) &&
                tagsEl.ValueKind == JsonValueKind.Array)
            {
                tags = string.Join(", ", tagsEl.EnumerateArray()
                    .Where(t => (t.GetProperty("language").GetString() ?? "") == "eng")
                    .Take(10)
                    .Select(t => t.GetProperty("name").GetString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n)));
            }

            return (rating, genres, tags);
        }
        catch { return ("", "", ""); }
    }

    private static List<(string Name, string ProcessName)> ScanDrive(string driveRoot)
    {
        var all = new List<(string Name, string ProcessName)>();

        // Steam (ACF manifest-based)
        foreach (var d in FindSteamAppsDirs(driveRoot))
            all.AddRange(ScanSteamDir(d));

        // GOG Games
        foreach (var root in FindGogRoots(driveRoot))
            all.AddRange(ScanLibraryRoot(root));

        // Epic Games (manifest JSON)
        all.AddRange(ScanEpicGames(driveRoot));

        // EA / Origin
        foreach (var root in FindEaRoots(driveRoot))
            all.AddRange(ScanLibraryRoot(root));

        // Battle.net / Blizzard
        foreach (var root in FindBattleNetRoots(driveRoot))
            all.AddRange(ScanLibraryRoot(root));

        // Ubisoft Connect
        foreach (var root in FindUbisoftRoots(driveRoot))
            all.AddRange(ScanLibraryRoot(root));

        // Deduplicate by process name (case-insensitive)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return all.Where(r => seen.Add(r.ProcessName)).ToList();
    }

    // -- Steam ----------------------------------------------------------------

    private static List<string> FindSteamAppsDirs(string driveRoot)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownPaths = new[]
        {
            Path.Combine(driveRoot, "Program Files (x86)", "Steam",        "steamapps"),
            Path.Combine(driveRoot, "Program Files",        "Steam",        "steamapps"),
            Path.Combine(driveRoot, "Steam",                               "steamapps"),
            Path.Combine(driveRoot, "SteamLibrary",                        "steamapps"),
            Path.Combine(driveRoot, "Games", "Steam",                      "steamapps"),
            Path.Combine(driveRoot, "Games", "SteamLibrary",               "steamapps"),
        };
        foreach (var p in knownPaths.Where(Directory.Exists))
            dirs.Add(p);

        // Parse libraryfolders.vdf to discover additional library paths
        foreach (var p in dirs.ToList())
        {
            var vdfPath = Path.Combine(p, "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;
            var re = new Regex(@"""(?:path|\d+)""\s+""([^""]+)""");
            foreach (var line in File.ReadLines(vdfPath))
            {
                var m = re.Match(line);
                if (!m.Success) continue;
                var libApps = Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), "steamapps");
                if (Directory.Exists(libApps)) dirs.Add(libApps);
            }
            break;
        }

        // Recursive fallback: any folder named "steamapps" containing ACF files
        SearchFolderRecursive(driveRoot, "steamapps", dirs, maxDepth: 5,
            validate: d => Directory.EnumerateFiles(d, "appmanifest_*.acf").Any());

        return dirs.ToList();
    }

    private static IEnumerable<(string Name, string ProcessName)> ScanSteamDir(string appsDir)
    {
        IEnumerable<string> acfFiles;
        try { acfFiles = Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"); }
        catch { yield break; }

        foreach (var acf in acfFiles)
        {
            string name = "", installDir = "";
            try
            {
                foreach (var line in File.ReadLines(acf))
                {
                    var m = Regex.Match(line, @"""(name|installdir)""\s+""([^""]+)""");
                    if (!m.Success) continue;
                    if (m.Groups[1].Value == "name")       name       = m.Groups[2].Value;
                    if (m.Groups[1].Value == "installdir") installDir = m.Groups[2].Value;
                    if (name != "" && installDir != "") break;
                }
            }
            catch { continue; }

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir)) continue;
            var gamePath = Path.Combine(appsDir, "common", installDir);
            if (!Directory.Exists(gamePath)) continue;

            var exes = FindGameExes(gamePath, installDir);
            foreach (var exe in exes)
            {
                var displayName = exes.Count == 1 ? name : $"{name} ({Path.GetFileName(exe)})";
                yield return (displayName, Path.GetFileName(exe).ToLower());
            }
        }
    }

    // -- GOG ------------------------------------------------------------------

    private static List<string> FindGogRoots(string driveRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new[]
        {
            Path.Combine(driveRoot, "GOG Games"),
            Path.Combine(driveRoot, "Program Files (x86)", "GOG Games"),
            Path.Combine(driveRoot, "Program Files (x86)", "GOG Galaxy", "Games"),
            Path.Combine(driveRoot, "Games", "GOG Games"),
        };
        foreach (var p in knownPaths.Where(Directory.Exists)) roots.Add(p);
        SearchFolderRecursive(driveRoot, "GOG Games", roots, maxDepth: 4);
        return roots.ToList();
    }

    // -- Epic Games -----------------------------------------------------------

    private static List<(string Name, string ProcessName)> ScanEpicGames(string driveRoot)
    {
        var results = new List<(string Name, string ProcessName)>();

        // Epic stores manifests in %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestsDir)) return results;

        IEnumerable<string> items;
        try { items = Directory.EnumerateFiles(manifestsDir, "*.item"); }
        catch { return results; }

        foreach (var item in items)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;

                if (!root.TryGetProperty("InstallLocation", out var locProp)) continue;
                var installDir = locProp.GetString();
                if (string.IsNullOrEmpty(installDir)) continue;
                if (!installDir.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (!Directory.Exists(installDir)) continue;

                var displayName = root.TryGetProperty("DisplayName", out var dnProp)
                    ? (dnProp.GetString() ?? Path.GetFileName(installDir))
                    : Path.GetFileName(installDir);

                // Use LaunchExecutable when available for a precise match
                if (root.TryGetProperty("LaunchExecutable", out var leProp))
                {
                    var exeRel = leProp.GetString();
                    if (!string.IsNullOrEmpty(exeRel))
                    {
                        var exeFull = Path.Combine(installDir, exeRel);
                        if (File.Exists(exeFull))
                        {
                            results.Add((displayName!, Path.GetFileName(exeFull).ToLower()));
                            continue;
                        }
                    }
                }

                var exes = FindGameExes(installDir, Path.GetFileName(installDir));
                if (exes.Count > 0)
                    results.Add((displayName!, Path.GetFileName(exes[0]).ToLower()));
            }
            catch { }
        }

        return results;
    }

    // -- EA / Origin ----------------------------------------------------------

    private static List<string> FindEaRoots(string driveRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new[]
        {
            Path.Combine(driveRoot, "Program Files",       "EA Games"),
            Path.Combine(driveRoot, "Program Files (x86)", "Origin Games"),
            Path.Combine(driveRoot, "EA Games"),
            Path.Combine(driveRoot, "Origin Games"),
            Path.Combine(driveRoot, "Games", "EA Games"),
        };
        foreach (var p in knownPaths.Where(Directory.Exists)) roots.Add(p);
        SearchFolderRecursive(driveRoot, "EA Games",     roots, maxDepth: 4);
        SearchFolderRecursive(driveRoot, "Origin Games", roots, maxDepth: 4);
        return roots.ToList();
    }

    // -- Battle.net / Blizzard ------------------------------------------------

    private static List<string> FindBattleNetRoots(string driveRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new[]
        {
            Path.Combine(driveRoot, "Program Files (x86)", "Blizzard Entertainment"),
            Path.Combine(driveRoot, "Program Files",       "Blizzard Entertainment"),
            Path.Combine(driveRoot, "Blizzard Entertainment"),
        };
        foreach (var p in knownPaths.Where(Directory.Exists)) roots.Add(p);
        SearchFolderRecursive(driveRoot, "Blizzard Entertainment", roots, maxDepth: 4);
        return roots.ToList();
    }

    // -- Ubisoft Connect ------------------------------------------------------

    private static List<string> FindUbisoftRoots(string driveRoot)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new[]
        {
            Path.Combine(driveRoot, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games"),
            Path.Combine(driveRoot, "Program Files",       "Ubisoft", "Ubisoft Game Launcher", "games"),
            Path.Combine(driveRoot, "Ubisoft", "games"),
        };
        foreach (var p in knownPaths.Where(Directory.Exists)) roots.Add(p);
        return roots.ToList();
    }

    // -- Generic library root scanner (each subdir = one game) ----------------

    private static List<(string Name, string ProcessName)> ScanLibraryRoot(string libraryRoot)
    {
        var results = new List<(string Name, string ProcessName)>();
        IEnumerable<string> gameDirs;
        try { gameDirs = Directory.EnumerateDirectories(libraryRoot); }
        catch { return results; }

        foreach (var gameDir in gameDirs)
        {
            var gameName = Path.GetFileName(gameDir);
            var exes = FindGameExes(gameDir, gameName);
            if (exes.Count == 0) continue;
            results.Add((gameName, Path.GetFileName(exes[0]).ToLower()));
        }
        return results;
    }

    // -- Shared utilities -----------------------------------------------------

    private static void SearchFolderRecursive(
        string path, string targetName, HashSet<string> found,
        int maxDepth, Func<string, bool>? validate = null)
    {
        if (maxDepth <= 0) return;
        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(path); }
        catch { return; }

        foreach (var dir in subdirs)
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') ||
                string.Equals(name, "Windows",                   StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "$Recycle.Bin",              StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "WindowsApps",               StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                if (validate == null || validate(dir))
                {
                    found.Add(dir);
                    continue; // don't recurse inside a matched folder
                }
            }

            SearchFolderRecursive(dir, targetName, found, maxDepth - 1, validate);
        }
    }

    private static List<string> FindGameExes(string gamePath, string installDir)
    {
        IEnumerable<string> candidates;
        try { candidates = Directory.EnumerateFiles(gamePath, "*.exe"); }
        catch { return []; }

        var exes = candidates.Where(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f).ToLower();
            return !n.Contains("unins")   &&
                   !n.Contains("setup")   &&
                   !n.Contains("redist")  &&
                   !n.Contains("crash")   &&
                   !n.Contains("report")  &&
                   !n.Contains("helper")  &&
                   !n.Contains("install") &&
                   !n.StartsWith("vc_")   &&
                   !n.StartsWith("dotnet");
        }).ToList();

        if (exes.Count == 0) return [];

        var dirLower = installDir.ToLower().Replace(" ", "");
        return exes.OrderByDescending(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f).ToLower().Replace(" ", "");
            return n == dirLower || dirLower.Contains(n) || n.Contains(dirLower) ? 1 : 0;
        }).ThenByDescending(f => new FileInfo(f).Length).ToList();
    }
}
