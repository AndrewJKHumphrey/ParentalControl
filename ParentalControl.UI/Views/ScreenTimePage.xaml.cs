using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ParentalControl.Core;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;
using ParentalControl.UI.Services;

namespace ParentalControl.UI.Views;

public class ScreenTimeLimitRow
{
    public int Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsEnabled { get; set; }
    public int DailyLimitMinutes { get; set; }
    public string AllowedFromStr { get; set; } = "00:00";
    public string AllowedUntilStr { get; set; } = "23:59";
}

public partial class ScreenTimePage : Page
{
    private readonly IpcClient _ipc = new();
    private List<ScreenTimeLimitRow> _rows = new();
    private bool _use12h = false;
    private int _selectedProfileId = 1;

    public ScreenTimePage()
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

    private string FormatTime(TimeOnly t)
    {
        if (!_use12h) return t.ToString("HH:mm");
        int h12 = t.Hour % 12;
        if (h12 == 0) h12 = 12;
        return $"{h12}:{t.Minute:D2} {(t.Hour < 12 ? "AM" : "PM")}";
    }

    // Culture-invariant parser for our own display strings ("HH:mm" or "h:mm AM/PM").
    // TimeOnly.TryParse is culture-sensitive and can misinterpret "00:00" or our
    // hardcoded AM/PM strings on non-English Windows regional settings.
    private static bool TryParseDisplayTime(string s, out TimeOnly result)
    {
        // 24h invariant: "0:00" – "23:59" (single or double-digit hour)
        if (TimeOnly.TryParseExact(s.Trim(), new[] { "H:mm", "HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        // Custom 12h: "h:mm AM/PM" with or without a space before the suffix
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

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            _rows = db.ScreenTimeLimits
                .Where(l => l.UserProfileId == _selectedProfileId)
                .OrderBy(l => l.DayOfWeek)
                .Select(l => new ScreenTimeLimitRow
                {
                    Id = l.Id,
                    DayOfWeek = l.DayOfWeek,
                    IsEnabled = l.IsEnabled,
                    DailyLimitMinutes = l.DailyLimitMinutes,
                    AllowedFromStr = l.AllowedFrom.ToString("HH:mm"),
                    AllowedUntilStr = l.AllowedUntil.ToString("HH:mm")
                })
                .ToList();

            // Convert to display format
            if (_use12h)
            {
                foreach (var row in _rows)
                {
                    if (TryParseDisplayTime(row.AllowedFromStr, out var f))
                        row.AllowedFromStr = FormatTime(f);
                    if (TryParseDisplayTime(row.AllowedUntilStr, out var u))
                        row.AllowedUntilStr = FormatTime(u);
                }
            }

            LimitsGrid.ItemsSource = _rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load: {ex.Message}");
        }
    }

    private void LimitsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.EditingElement is not TextBox tb) return;

        var header = e.Column.Header?.ToString() ?? "";
        if (!header.StartsWith("Allowed From") && !header.StartsWith("Allowed Until")) return;

        var normalized = NormalizeTimeInput(tb.Text);
        if (normalized != null)
        {
            tb.Text = normalized;
            StatusText.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusText.Text = $"Invalid time \"{tb.Text}\" — use formats like: 0800, 800PM, 8:30 AM, 20:00";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168));
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private string? NormalizeTimeInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        // Use culture-invariant parser to avoid locale-dependent misinterpretation
        // (TimeOnly.TryParse is culture-sensitive and can shift AM times by 12h).
        if (TryParseDisplayTime(input, out var t))
            return FormatTime(t);

        // Digit-only shorthand with optional AM/PM suffix: "930pm", "9pm", "2000"
        var lower = input.ToLower();
        bool isPm = lower.EndsWith("pm");
        bool isAm = lower.EndsWith("am");
        var digits = new string(input.Where(char.IsDigit).ToArray());
        string candidate = digits.Length switch
        {
            1 or 2 => $"{digits.PadLeft(2, '0')}:00",   // "9"    → "09:00"
            3      => $"0{digits[0]}:{digits[1..]}",      // "930"  → "09:30"
            4      => $"{digits[..2]}:{digits[2..]}",     // "2000" → "20:00"
            _      => ""
        };

        if (!TimeOnly.TryParseExact(candidate, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out t)) return null;

        // Apply AM/PM for digit-only shorthand (e.g. "900pm" → 21:00, "1200am" → 00:00)
        if (isPm && t.Hour < 12) t = t.AddHours(12);
        if (isAm && t.Hour == 12) t = t.AddHours(-12);

        return FormatTime(t);
    }

    private async void SaveToAllButton_Click(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        foreach (var row in _rows)
        {
            var fromNorm  = NormalizeTimeInput(row.AllowedFromStr);
            var untilNorm = NormalizeTimeInput(row.AllowedUntilStr);
            if (fromNorm  == null) errors.Add($"{row.DayOfWeek}: invalid 'Allowed From' value \"{row.AllowedFromStr}\"");
            else row.AllowedFromStr  = fromNorm;
            if (untilNorm == null) errors.Add($"{row.DayOfWeek}: invalid 'Allowed Until' value \"{row.AllowedUntilStr}\"");
            else row.AllowedUntilStr = untilNorm;
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
                    var limit = db.ScreenTimeLimits.FirstOrDefault(
                        l => l.UserProfileId == profileId && l.DayOfWeek == row.DayOfWeek);
                    if (limit == null) continue;
                    limit.IsEnabled         = row.IsEnabled;
                    limit.DailyLimitMinutes = row.DailyLimitMinutes;
                    if (TryParseDisplayTime(row.AllowedFromStr,  out var from))  limit.AllowedFrom  = from;
                    if (TryParseDisplayTime(row.AllowedUntilStr, out var until)) limit.AllowedUntil = until;
                }
            }
            db.SaveChanges();
            await _ipc.SendAsync(IpcCommand.ReloadRules);
            StatusText.Text       = "Saved to all profiles.";
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
        // Pre-validate all time fields before touching the DB
        var errors = new List<string>();
        foreach (var row in _rows)
        {
            var fromNorm = NormalizeTimeInput(row.AllowedFromStr);
            var untilNorm = NormalizeTimeInput(row.AllowedUntilStr);
            if (fromNorm == null)
                errors.Add($"{row.DayOfWeek}: invalid 'Allowed From' value \"{row.AllowedFromStr}\"");
            else
                row.AllowedFromStr = fromNorm;
            if (untilNorm == null)
                errors.Add($"{row.DayOfWeek}: invalid 'Allowed Until' value \"{row.AllowedUntilStr}\"");
            else
                row.AllowedUntilStr = untilNorm;
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
                var limit = db.ScreenTimeLimits.Find(row.Id);
                if (limit == null) continue;

                limit.IsEnabled = row.IsEnabled;
                limit.DailyLimitMinutes = row.DailyLimitMinutes;

                if (TryParseDisplayTime(row.AllowedFromStr, out var from))
                    limit.AllowedFrom = from;
                if (TryParseDisplayTime(row.AllowedUntilStr, out var until))
                    limit.AllowedUntil = until;
            }
            db.SaveChanges();

            await _ipc.SendAsync(IpcCommand.ReloadRules);

            StatusText.Text = "Saved successfully.";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
            StatusText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed: {ex.Message}");
        }
    }
}
