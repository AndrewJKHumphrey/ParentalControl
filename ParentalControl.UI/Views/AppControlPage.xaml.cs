using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public class AppTimeScheduleRow
{
    public int       Id                { get; set; }
    public DayOfWeek DayOfWeek         { get; set; }
    public bool      IsEnabled         { get; set; }
    public int       DailyLimitMinutes { get; set; }
}

public partial class AppControlPage : Page
{
    private readonly IpcClient _ipc = new();
    private int _selectedProfileId = 1;
    private List<AppTimeScheduleRow> _scheduleRows = new();

    public AppControlPage()
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
            // For non-Default profiles: show Default rules + profile-specific rules combined.
            // Default rules act as an inherited baseline visible alongside profile overrides.
            var showDefault = _selectedProfileId != 1;
            AppRulesGrid.ItemsSource = db.AppRules
                .Where(r => r.UserProfileId == _selectedProfileId
                         || (showDefault && r.UserProfileId == 1))
                .OrderBy(r => r.ProcessName)
                .ToList();

            _scheduleRows = db.AppTimeSchedules
                .Where(s => s.UserProfileId == _selectedProfileId)
                .OrderBy(s => s.DayOfWeek)
                .Select(s => new AppTimeScheduleRow
                {
                    Id                = s.Id,
                    DayOfWeek         = s.DayOfWeek,
                    IsEnabled         = s.IsEnabled,
                    DailyLimitMinutes = s.DailyLimitMinutes,
                })
                .ToList();
            AppTimeScheduleGrid.ItemsSource = _scheduleRows;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
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

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (AppRulesGrid.ItemsSource is not List<AppRule> rules) return;

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
    // Steam scanning
    // -------------------------------------------------------------------------

    private async void QuickScanSteam_Click(object sender, RoutedEventArgs e)
    {
        SetScanButtonsEnabled(false);
        ScanStatusText.Text = "Scanning Steam library...";
        ScanStatusText.Visibility = Visibility.Visible;

        try
        {
            var steamAppsDirs = await Task.Run(FindSteamAppsDirs_Quick);
            await RunScan(steamAppsDirs);
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetScanButtonsEnabled(true);
        }
    }

    private async void DeepScanSteam_Click(object sender, RoutedEventArgs e)
    {
        SetScanButtonsEnabled(false);
        ScanStatusText.Text = "Scanning all drives for Steam games — please wait...";
        ScanStatusText.Visibility = Visibility.Visible;

        try
        {
            var steamAppsDirs = await Task.Run(FindSteamAppsDirs_Deep);
            await RunScan(steamAppsDirs);
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            SetScanButtonsEnabled(true);
        }
    }

    private void SetScanButtonsEnabled(bool enabled)
    {
        QuickScanButton.IsEnabled = enabled;
        DeepScanButton.IsEnabled = enabled;
    }

    private async Task RunScan(List<string> steamAppsDirs)
    {
        if (steamAppsDirs.Count == 0)
        {
            ScanStatusText.Text = "No Steam library folders found.";
            return;
        }

        var games = await Task.Run(() => ScanSteamAppsDirs(steamAppsDirs));

        int added = 0, skipped = 0;
        using var db = new AppDbContext();
        foreach (var (name, processName) in games)
        {
            if (db.AppRules.Any(r => r.ProcessName == processName && r.UserProfileId == _selectedProfileId))
            {
                skipped++;
                continue;
            }
            db.AppRules.Add(new AppRule
            {
                DisplayName        = name,
                ProcessName        = processName,
                IsBlocked          = false,
                UserProfileId      = _selectedProfileId,
                AllowedInFocusMode = false,
            });
            added++;
        }
        db.SaveChanges();

        ScanStatusText.Text = games.Count == 0
            ? "No games found."
            : $"Found {games.Count} executable(s): {added} added, {skipped} already in list.";
        LoadData();
    }

    // -- Quick scan: default Steam install + registered library folders via VDF --

    private static List<string> FindSteamAppsDirs_Quick()
    {
        var dirs = new List<string>();

        // Check the well-known default Steam installation paths
        var defaultRoots = new[]
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
        };

        string? steamRoot = defaultRoots.FirstOrDefault(Directory.Exists);
        if (steamRoot == null) return dirs;

        var defaultApps = Path.Combine(steamRoot, "steamapps");
        if (Directory.Exists(defaultApps))
            dirs.Add(defaultApps);

        // Parse libraryfolders.vdf to find additional library locations
        var vdfPath = Path.Combine(defaultApps, "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return dirs;

        // VDF lines look like:   "path"    "D:\\SteamLibrary"
        // Older format:           "1"       "D:\\SteamLibrary"
        var pathLine = new Regex(@"""(?:path|\d+)""\s+""([^""]+)""");
        foreach (var line in File.ReadLines(vdfPath))
        {
            var m = pathLine.Match(line);
            if (!m.Success) continue;

            var libRoot = m.Groups[1].Value.Replace(@"\\", @"\");
            var libApps = Path.Combine(libRoot, "steamapps");
            if (Directory.Exists(libApps) &&
                !dirs.Contains(libApps, StringComparer.OrdinalIgnoreCase))
                dirs.Add(libApps);
        }

        return dirs;
    }

    // -- Deep scan: try common locations on every drive + limited recursive search --

    private static List<string> FindSteamAppsDirs_Deep()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Start with quick-scan results (covers the default install + VDF-registered libraries)
        foreach (var d in FindSteamAppsDirs_Quick())
            dirs.Add(d);

        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d => d.RootDirectory.FullName);

        // Common relative paths where Steam (or a Steam library) might live on any drive
        var relativeGuesses = new[]
        {
            @"Steam\steamapps",
            @"SteamLibrary\steamapps",
            @"Program Files\Steam\steamapps",
            @"Program Files (x86)\Steam\steamapps",
            @"Games\Steam\steamapps",
            @"Games\SteamLibrary\steamapps",
        };

        foreach (var drive in drives)
        {
            // Try the known relative paths first (fast)
            foreach (var rel in relativeGuesses)
            {
                var candidate = Path.Combine(drive, rel);
                if (Directory.Exists(candidate))
                    dirs.Add(candidate);
            }

            // Do a bounded-depth search for any folder literally named "steamapps"
            // that contains appmanifest files (max 5 levels deep to stay fast)
            FindSteamAppsDirsRecursive(drive, dirs, maxDepth: 5);
        }

        return dirs.ToList();
    }

    private static void FindSteamAppsDirsRecursive(
        string path, HashSet<string> found, int maxDepth)
    {
        if (maxDepth <= 0) return;

        IEnumerable<string> subdirs;
        try { subdirs = Directory.EnumerateDirectories(path); }
        catch { return; }

        foreach (var dir in subdirs)
        {
            var name = Path.GetFileName(dir);

            // Skip system/hidden noise
            if (name.StartsWith('.') ||
                string.Equals(name, "Windows", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(name, "steamapps", StringComparison.OrdinalIgnoreCase) &&
                Directory.EnumerateFiles(dir, "appmanifest_*.acf").Any())
            {
                found.Add(dir);
                continue; // no need to recurse inside steamapps
            }

            FindSteamAppsDirsRecursive(dir, found, maxDepth - 1);
        }
    }

    // -- Parse ACF files and detect main exe --

    private static List<(string Name, string ProcessName)> ScanSteamAppsDirs(
        IEnumerable<string> steamAppsDirs)
    {
        var results = new List<(string, string)>();

        foreach (var appsDir in steamAppsDirs)
        {
            IEnumerable<string> acfFiles;
            try { acfFiles = Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var acf in acfFiles)
            {
                try
                {
                    string name = "", installDir = "";
                    foreach (var line in File.ReadLines(acf))
                    {
                        var m = Regex.Match(line,
                            @"""(name|installdir)""\s+""([^""]+)""");
                        if (!m.Success) continue;
                        if (m.Groups[1].Value == "name")       name       = m.Groups[2].Value;
                        if (m.Groups[1].Value == "installdir") installDir = m.Groups[2].Value;
                        if (name != "" && installDir != "") break;
                    }

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir))
                        continue;

                    var gamePath = Path.Combine(appsDir, "common", installDir);
                    if (!Directory.Exists(gamePath)) continue;

                    var exes = FindGameExes(gamePath, installDir);
                    if (exes.Count == 0) continue;

                    foreach (var exe in exes)
                    {
                        var exeName = Path.GetFileName(exe).ToLower();
                        var displayName = exes.Count == 1
                            ? name
                            : $"{name} ({Path.GetFileName(exe)})";
                        results.Add((displayName, exeName));
                    }
                }
                catch { }
            }
        }

        return results;
    }

    private static List<string> FindGameExes(string gamePath, string installDir)
    {
        IEnumerable<string> candidates;
        try { candidates = Directory.EnumerateFiles(gamePath, "*.exe"); }
        catch { return []; }

        // Filter out installers, redistributables, and crash handlers
        var exes = candidates.Where(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f).ToLower();
            return !n.Contains("unins") &&
                   !n.Contains("setup") &&
                   !n.Contains("redist") &&
                   !n.Contains("crash") &&
                   !n.Contains("report") &&
                   !n.Contains("helper") &&
                   !n.Contains("install") &&
                   !n.StartsWith("vc_") &&
                   !n.StartsWith("dotnet");
        }).ToList();

        if (exes.Count == 0) return [];

        // Sort: prefer exes whose name matches the install directory, then by size descending
        var dirLower = installDir.ToLower().Replace(" ", "");
        return exes.OrderByDescending(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f).ToLower().Replace(" ", "");
            return n == dirLower || dirLower.Contains(n) || n.Contains(dirLower) ? 1 : 0;
        }).ThenByDescending(f => new FileInfo(f).Length).ToList();
    }
}
