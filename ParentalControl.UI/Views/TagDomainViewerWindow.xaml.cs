using System.Windows;
using System.Windows.Controls;
using ParentalControl.Core.Data;

namespace ParentalControl.UI.Views;

public partial class TagDomainViewerWindow : Window
{
    private readonly int    _tagId;
    private List<string>    _allDomains = [];

    public TagDomainViewerWindow(int tagId, string tagName)
    {
        InitializeComponent();
        _tagId = tagId;
        Title = $"Domains in: {tagName}";
        TagNameLabel.Text = $"Domains in: {tagName}";
        SearchBox.Text = "";
        Loaded += (_, _) => LoadDomains();
    }

    private void LoadDomains()
    {
        try
        {
            using var db = new AppDbContext();
            _allDomains = db.WebFilterTagDomains
                            .Where(d => d.TagId == _tagId)
                            .OrderBy(d => d.Domain)
                            .Select(d => d.Domain)
                            .ToList();
        }
        catch
        {
            _allDomains = [];
        }

        ApplyFilter("");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text.Trim());
    }

    private void ApplyFilter(string query)
    {
        List<string> filtered = string.IsNullOrEmpty(query)
            ? _allDomains
            : _allDomains.Where(d => d.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        DomainListBox.ItemsSource = filtered;
        CountLabel.Text = string.IsNullOrEmpty(query)
            ? $"{_allDomains.Count:N0} domains"
            : $"Showing {filtered.Count:N0} of {_allDomains.Count:N0} domains";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
