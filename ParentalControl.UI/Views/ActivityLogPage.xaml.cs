using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

namespace ParentalControl.UI.Views;

public partial class ActivityLogPage : Page
{
    private DispatcherTimer? _refreshTimer;

    public ActivityLogPage()
    {
        InitializeComponent();
        Loaded += (_, _) => InitPage();
        Unloaded += (_, _) => _refreshTimer?.Stop();
    }

    private void InitPage()
    {
        FromDate.SelectedDate = DateTime.Today.AddDays(-7);
        TypeFilter.Items.Add("All");
        foreach (var t in Enum.GetNames<ActivityType>())
            TypeFilter.Items.Add(t);
        TypeFilter.SelectedIndex = 0;
        LoadData();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => LoadData();
        _refreshTimer.Start();
    }

    private void LoadData()
    {
        try
        {
            using var db = new AppDbContext();
            var from = FromDate.SelectedDate ?? DateTime.Today.AddDays(-7);
            var search = SearchBox.Text.Trim().ToLower();

            var query = db.ActivityEntries
                .Where(a => a.Timestamp >= from);

            if (TypeFilter.SelectedItem is string type && type != "All"
                && Enum.TryParse<ActivityType>(type, out var actType))
                query = query.Where(a => a.Type == actType);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(a => a.Detail.ToLower().Contains(search));

            LogGrid.ItemsSource = query
                .OrderByDescending(a => a.Timestamp)
                .Take(500)
                .ToList();
        }
        catch { }
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e) => LoadData();

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "This will permanently delete all activity log entries. Continue?",
            "Clear Log", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var db = new AppDbContext();
            db.ActivityEntries.RemoveRange(db.ActivityEntries);
            db.SaveChanges();
            LogGrid.ItemsSource = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear log: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
