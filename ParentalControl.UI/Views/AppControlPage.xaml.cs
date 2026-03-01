using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public partial class AppControlPage : Page
{
    private readonly IpcClient _ipc = new();

    public AppControlPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            AppRulesGrid.ItemsSource = db.AppRules.OrderBy(r => r.ProcessName).ToList();
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
            if (!db.AppRules.Any(r => r.ProcessName == processName))
            {
                db.AppRules.Add(new AppRule
                {
                    ProcessName = processName.ToLower().Replace(".exe", ""),
                    DisplayName = string.IsNullOrEmpty(DisplayNameBox.Text) ? processName : DisplayNameBox.Text.Trim(),
                    IsBlocked = true
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
        if (AppRulesGrid.ItemsSource is List<AppRule> rules)
        {
            try
            {
                using var db = new AppDbContext();
                foreach (var r in rules)
                {
                    var existing = db.AppRules.Find(r.Id);
                    if (existing != null)
                    {
                        existing.IsBlocked = r.IsBlocked;
                        existing.DisplayName = r.DisplayName;
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
            if (db.AppRules.Any(r => r.ProcessName == processName))
            {
                skipped++;
                continue;
            }
            db.AppRules.Add(new AppRule
            {
                DisplayName = name,
                ProcessName = processName,
                IsBlocked = false,
            });
            added++;
        }
        db.SaveChanges();

        ScanStatusText.Text = games.Count == 0
            ? "No games found."
            : $"Found {games.Count} game(s): {added} added, {skipped} already in list.";
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

                    var exe = FindGameExe(gamePath, installDir);
                    if (exe == null) continue;

                    results.Add((name, Path.GetFileNameWithoutExtension(exe).ToLower()));
                }
                catch { }
            }
        }

        return results;
    }

    private static string? FindGameExe(string gamePath, string installDir)
    {
        IEnumerable<string> candidates;
        try { candidates = Directory.EnumerateFiles(gamePath, "*.exe"); }
        catch { return null; }

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

        if (exes.Count == 0) return null;
        if (exes.Count == 1) return exes[0];

        // Prefer an exe whose name matches or contains the install directory name
        var dirLower = installDir.ToLower().Replace(" ", "");
        var match = exes.FirstOrDefault(f =>
        {
            var n = Path.GetFileNameWithoutExtension(f).ToLower().Replace(" ", "");
            return n == dirLower || dirLower.Contains(n) || n.Contains(dirLower);
        });
        if (match != null) return match;

        // Fall back to the largest exe (usually the game binary)
        return exes.OrderByDescending(f => new FileInfo(f).Length).First();
    }
}
