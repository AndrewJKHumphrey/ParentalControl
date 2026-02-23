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
}
