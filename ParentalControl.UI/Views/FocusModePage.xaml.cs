using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public class FocusScheduleRow
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public string DayLabel { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string FocusFromStr  { get; set; } = "15:00";
    public string FocusUntilStr { get; set; } = "21:00";
}

public partial class FocusModePage : Page
{
    private readonly IpcClient _ipc = new();
    private List<FocusScheduleRow> _rows = new();
    private bool _use12h = false;
    private int _selectedProfileId = 1;
    private bool _profileLoaded = false;

    public FocusModePage()
    {
        InitializeComponent();
        Loaded += (_, _) => InitPage();
    }

    private void InitPage()
    {
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            if (settings?.TimeFormat12Hour == true)
                _use12h = true;

            var profiles = db.UserProfiles.OrderBy(p => p.Id).ToList();
            ProfileSelector.ItemsSource   = profiles;
            ProfileSelector.SelectedIndex = 0;
            // SelectionChanged fires → LoadData()
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
            _rows = db.FocusSchedules
                .Where(s => s.UserProfileId == _selectedProfileId)
                .OrderBy(s => s.DayOfWeek)
                .Select(s => new FocusScheduleRow
                {
                    Id            = s.Id,
                    DayOfWeek     = s.DayOfWeek,
                    DayLabel      = s.DayOfWeek.ToString(),
                    IsEnabled     = s.IsEnabled,
                    FocusFromStr  = s.FocusFrom.ToString("HH:mm"),
                    FocusUntilStr = s.FocusUntil.ToString("HH:mm"),
                })
                .ToList();

            if (_use12h)
            {
                foreach (var row in _rows)
                {
                    if (TryParseDisplayTime(row.FocusFromStr, out var f))
                        row.FocusFromStr = FormatTime(f);
                    if (TryParseDisplayTime(row.FocusUntilStr, out var u))
                        row.FocusUntilStr = FormatTime(u);
                }
            }

            DayRangeBox.SelectedIndex = 0;
            ScheduleGrid.ItemsSource = _rows;

            // Load FocusModeEnabled toggle for the selected profile
            _profileLoaded = false;
            var profile = db.UserProfiles.Find(_selectedProfileId);
            FocusModeToggle.IsChecked = profile?.FocusModeEnabled ?? false;
            _profileLoaded = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
        }
    }

    private void FocusModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_profileLoaded) return;
        try
        {
            using var db = new AppDbContext();
            var profile = db.UserProfiles.Find(_selectedProfileId);
            if (profile != null)
            {
                profile.FocusModeEnabled = FocusModeToggle.IsChecked == true;
                db.SaveChanges();
            }
        }
        catch { }
    }

    private string FormatTime(TimeOnly t)
    {
        if (!_use12h) return t.ToString("HH:mm");
        int h12 = t.Hour % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12}:{t.Minute:D2} {(t.Hour < 12 ? "AM" : "PM")}";
    }

    private static bool TryParseDisplayTime(string s, out TimeOnly result)
    {
        if (TimeOnly.TryParseExact(s.Trim(), new[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        var upper = s.Trim().ToUpperInvariant();
        bool pm = upper.EndsWith(" PM") || upper.EndsWith("PM");
        bool am = upper.EndsWith(" AM") || upper.EndsWith("AM");
        if (pm || am)
        {
            int suffixLen = (upper.EndsWith(" PM") || upper.EndsWith(" AM")) ? 3 : 2;
            var timePart = upper[..^suffixLen].Trim();
            if (TimeOnly.TryParseExact(timePart, new[] { "h:mm", "hh:mm" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                if (pm && result.Hour < 12) result = result.AddHours(12);
                if (am && result.Hour == 12) result = result.AddHours(-12);
                return true;
            }
        }

        result = default;
        return false;
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
        if (_rows.Count == 0) return;
        var range = GetSelectedDays();
        if (range.Count == 0) { ScheduleGrid.ItemsSource = _rows; return; }

        var label  = (DayRangeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var source = _rows.FirstOrDefault(r => range.Contains(r.DayOfWeek)) ?? _rows[0];
        var tmpl   = new FocusScheduleRow
        {
            Id            = source.Id,
            DayOfWeek     = source.DayOfWeek,
            DayLabel      = label,
            IsEnabled     = source.IsEnabled,
            FocusFromStr  = source.FocusFromStr,
            FocusUntilStr = source.FocusUntilStr,
        };
        ScheduleGrid.ItemsSource = new List<FocusScheduleRow> { tmpl };
    }

    private void BroadcastToRange()
    {
        var range = GetSelectedDays();
        if (range.Count == 0) return;
        if (ScheduleGrid.ItemsSource is not List<FocusScheduleRow> shown || shown.Count != 1) return;
        var tmpl = shown[0];
        foreach (var row in _rows.Where(r => range.Contains(r.DayOfWeek)))
        {
            row.IsEnabled     = tmpl.IsEnabled;
            row.FocusFromStr  = tmpl.FocusFromStr;
            row.FocusUntilStr = tmpl.FocusUntilStr;
        }
    }

    private void ScheduleGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.EditingElement is not TextBox tb) return;

        var header = e.Column.Header?.ToString() ?? "";
        if (!header.StartsWith("Focus From") && !header.StartsWith("Focus Until")) return;

        var normalized = NormalizeTimeInput(tb.Text);
        if (normalized != null)
        {
            tb.Text = normalized;
            StatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusText.Text = $"Invalid time \"{tb.Text}\" — use formats like: 1500, 3pm, 15:00";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168));
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private string? NormalizeTimeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        if (TryParseDisplayTime(input, out var t))
            return FormatTime(t);

        var lower = input.ToLower();
        bool isPm = lower.EndsWith("pm");
        bool isAm = lower.EndsWith("am");
        var digits = new string(input.Where(char.IsDigit).ToArray());
        string candidate = digits.Length switch
        {
            1 or 2 => $"{digits.PadLeft(2, '0')}:00",
            3      => $"0{digits[0]}:{digits[1..]}",
            4      => $"{digits[..2]}:{digits[2..]}",
            _      => ""
        };

        if (!TimeOnly.TryParseExact(candidate, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out t)) return null;

        if (isPm && t.Hour < 12) t = t.AddHours(12);
        if (isAm && t.Hour == 12) t = t.AddHours(-12);

        return FormatTime(t);
    }

    private async void SaveToAllButton_Click(object sender, RoutedEventArgs e)
    {
        BroadcastToRange();
        var errors = new List<string>();
        foreach (var row in _rows)
        {
            var fromNorm  = NormalizeTimeInput(row.FocusFromStr);
            var untilNorm = NormalizeTimeInput(row.FocusUntilStr);
            if (fromNorm  == null) errors.Add($"{row.DayOfWeek}: invalid 'Focus From' value \"{row.FocusFromStr}\"");
            else row.FocusFromStr  = fromNorm;
            if (untilNorm == null) errors.Add($"{row.DayOfWeek}: invalid 'Focus Until' value \"{row.FocusUntilStr}\"");
            else row.FocusUntilStr = untilNorm;
        }
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Invalid Time Values",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            var allProfileIds = db.UserProfiles.Select(p => p.Id).ToList();
            foreach (var profileId in allProfileIds)
            {
                foreach (var row in _rows)
                {
                    var schedule = db.FocusSchedules.FirstOrDefault(
                        s => s.UserProfileId == profileId && s.DayOfWeek == row.DayOfWeek);
                    if (schedule == null) continue;
                    schedule.IsEnabled = row.IsEnabled;
                    if (TryParseDisplayTime(row.FocusFromStr,  out var from))  schedule.FocusFrom  = from;
                    if (TryParseDisplayTime(row.FocusUntilStr, out var until)) schedule.FocusUntil = until;
                }
            }
            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            var rangeLabel = (DayRangeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "day";
            StatusText.Text       = DayRangeBox.SelectedIndex == 0
                ? "Saved to all profiles."
                : $"Saved to all profiles — applied to all {rangeLabel.ToLower()}.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        BroadcastToRange();
        var errors = new List<string>();
        foreach (var row in _rows)
        {
            var fromNorm  = NormalizeTimeInput(row.FocusFromStr);
            var untilNorm = NormalizeTimeInput(row.FocusUntilStr);
            if (fromNorm == null)
                errors.Add($"{row.DayOfWeek}: invalid 'Focus From' value \"{row.FocusFromStr}\"");
            else
                row.FocusFromStr = fromNorm;
            if (untilNorm == null)
                errors.Add($"{row.DayOfWeek}: invalid 'Focus Until' value \"{row.FocusUntilStr}\"");
            else
                row.FocusUntilStr = untilNorm;
        }
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Invalid Time Values",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new AppDbContext();
            foreach (var row in _rows)
            {
                var schedule = db.FocusSchedules.Find(row.Id);
                if (schedule == null) continue;

                schedule.IsEnabled = row.IsEnabled;
                if (TryParseDisplayTime(row.FocusFromStr,  out var from))
                    schedule.FocusFrom  = from;
                if (TryParseDisplayTime(row.FocusUntilStr, out var until))
                    schedule.FocusUntil = until;
            }
            db.SaveChanges();

            await _ipc.SendAsync(IpcCommand.ReloadRules);

            var rangeLabel = (DayRangeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "day";
            StatusText.Text = DayRangeBox.SelectedIndex == 0
                ? "Saved successfully."
                : $"Saved — applied to all {rangeLabel.ToLower()}.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }
}
