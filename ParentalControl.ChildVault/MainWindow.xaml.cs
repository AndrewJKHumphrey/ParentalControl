using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Data;
using ParentalControl.Core.Helpers;
using ParentalControl.Core.Models;

namespace ParentalControl.ChildVault;

public partial class MainWindow : Window
{
    // Profile resolved at unlock — remembered so refresh doesn't need the PIN again
    private int _unlockedProfileId = -1;

    // Auto-refresh timer (30 s) — only runs while the vault panel is visible
    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30)
    };

    // ── Convenience: read a brush from the shared resource dictionary ─────────
    private static SolidColorBrush Br(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    public MainWindow()
    {
        InitializeComponent();
        _refreshTimer.Tick += (_, _) => RefreshEntries();
        Loaded += (_, _) => CheckProfileOnLoad();
    }

    // ── Profile resolution ────────────────────────────────────────────────────

    private static UserProfile? ResolveProfile(AppDbContext db)
    {
        var username = Environment.UserName;

        return db.UserProfiles.AsNoTracking()
                   .FirstOrDefault(p => p.WindowsUsername.ToLower() == username.ToLower())
               ?? db.UserProfiles.AsNoTracking()
                   .FirstOrDefault(p => p.WindowsUsername == "");
    }

    private void CheckProfileOnLoad()
    {
        try
        {
            using var db = new AppDbContext();
            var profile = ResolveProfile(db);

            if (profile == null || string.IsNullOrEmpty(profile.VaultPin))
            {
                PinBox.IsEnabled    = false;
                UnlockBtn.IsEnabled = false;
                PinHint.Text = profile == null
                    ? "No profile found for your Windows account. Please ask your parent to set one up in the ParentGuard app."
                    : "Your parent hasn't set up a vault PIN for your account yet. Please ask them to set one in the ParentGuard app.";
                PinHint.Foreground = new SolidColorBrush(Color.FromRgb(0xFA, 0xB3, 0x87));
            }
        }
        catch (Exception ex)
        {
            PinError.Text = $"Could not read the ParentGuard database: {ex.Message}";
        }

        PinBox.Focus();
    }

    // ── PIN unlock ────────────────────────────────────────────────────────────

    private void PinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryUnlock();
    }

    private void Unlock_Click(object sender, RoutedEventArgs e) => TryUnlock();

    private void TryUnlock()
    {
        var pin = PinBox.Password;
        if (string.IsNullOrEmpty(pin)) return;

        PinError.Text       = "";
        UnlockBtn.IsEnabled = false;
        PinBox.IsEnabled    = false;

        try
        {
            using var db = new AppDbContext();
            var profile = ResolveProfile(db);

            if (profile == null || string.IsNullOrEmpty(profile.VaultPin))
            {
                ShowError("No PIN has been configured for your account.");
                return;
            }

            if (!BCrypt.Net.BCrypt.Verify(pin, profile.VaultPin))
            {
                ShowError("Incorrect PIN. Please try again.");
                return;
            }

            _unlockedProfileId = profile.Id;

            var entries = LoadEntries(db, profile.Id);
            RenderEntries(entries);

            PinPanel.Visibility   = Visibility.Collapsed;
            VaultPanel.Visibility = Visibility.Visible;
            _refreshTimer.Start();
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        PinError.Text       = message;
        UnlockBtn.IsEnabled = true;
        PinBox.IsEnabled    = true;
        PinBox.Clear();
        PinBox.Focus();
    }

    // ── Refresh (auto + manual) ───────────────────────────────────────────────

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshEntries();

    private void RefreshEntries()
    {
        if (_unlockedProfileId < 0) return;

        try
        {
            using var db  = new AppDbContext();
            var entries   = LoadEntries(db, _unlockedProfileId);
            RenderEntries(entries);
        }
        catch { /* silently ignore transient DB errors on background refresh */ }
    }

    private static List<VaultEntry> LoadEntries(AppDbContext db, int profileId) =>
        db.VaultEntries
          .AsNoTracking()
          .Where(v => v.UserProfileId == profileId)
          .OrderBy(v => v.SiteName)
          .ToList();

    // ── Vault rendering ───────────────────────────────────────────────────────

    private void RenderEntries(List<VaultEntry> entries)
    {
        EntriesPanel.Children.Clear();

        if (entries.Count == 0)
        {
            EntriesPanel.Children.Add(new TextBlock
            {
                Text                = "No passwords have been saved for your account yet.",
                Foreground          = Br("TextSecondary"),
                FontSize            = 13,
                TextWrapping        = TextWrapping.Wrap,
                TextAlignment       = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 32, 0, 0)
            });
        }
        else
        {
            foreach (var entry in entries)
                EntriesPanel.Children.Add(BuildEntryCard(entry));
        }

        LastRefreshedText.Text = $"Updated {DateTime.Now:HH:mm:ss}";
    }

    private UIElement BuildEntryCard(VaultEntry entry)
    {
        var plain = string.IsNullOrEmpty(entry.EncryptedPassword)
            ? ""
            : VaultCrypto.Decrypt(entry.EncryptedPassword);

        bool revealed = false;

        var panel = new StackPanel();

        // Site name
        panel.Children.Add(new TextBlock
        {
            Text       = entry.SiteName,
            FontSize   = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br("TextPrimary"),
            Margin     = new Thickness(0, 0, 0, 8)
        });

        // Divider
        panel.Children.Add(new Border
        {
            Height     = 1,
            Background = Br("BorderColor"),
            Margin     = new Thickness(0, 0, 0, 8)
        });

        // Username row
        if (!string.IsNullOrEmpty(entry.Username))
            panel.Children.Add(MakeStaticRow("Username", entry.Username));

        // Password row
        if (!string.IsNullOrEmpty(plain))
        {
            var pwdDisplay = new TextBlock
            {
                Text              = "••••••••",
                Foreground        = Br("TextSecondary"),
                FontSize          = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin            = new Thickness(0, 0, 8, 0)
            };

            var toggleBtn = MakeSmallBtn("Show");
            toggleBtn.Click += (_, _) =>
            {
                revealed              = !revealed;
                pwdDisplay.Text       = revealed ? plain : "••••••••";
                pwdDisplay.Foreground = revealed ? Br("TextPrimary") : Br("TextSecondary");
                toggleBtn.Content     = revealed ? "Hide" : "Show";
            };

            var copyBtn = MakeSmallBtn("Copy");
            copyBtn.Click += (_, _) => CopyToClipboard(plain, copyBtn);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text              = "Password",
                FontSize          = 11,
                Foreground        = Br("TextSecondary"),
                Width             = 68,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(pwdDisplay);
            row.Children.Add(toggleBtn);
            row.Children.Add(new Border { Width = 6 });
            row.Children.Add(copyBtn);
            panel.Children.Add(row);
        }

        return new Border
        {
            Background      = Br("CardBg"),
            CornerRadius    = new CornerRadius(8),
            BorderBrush     = Br("BorderColor"),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(14, 12, 14, 12),
            Margin          = new Thickness(0, 0, 0, 10),
            Child           = panel
        };
    }

    private UIElement MakeStaticRow(string label, string value)
    {
        var copyBtn = MakeSmallBtn("Copy");
        copyBtn.Click += (_, _) => CopyToClipboard(value, copyBtn);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 6)
        };
        row.Children.Add(new TextBlock
        {
            Text              = label,
            FontSize          = 11,
            Foreground        = Br("TextSecondary"),
            Width             = 68,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text              = value,
            FontSize          = 12,
            Foreground        = Br("TextPrimary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 8, 0)
        });
        row.Children.Add(copyBtn);
        return row;
    }

    private Button MakeSmallBtn(string text)
    {
        var btn = new Button
        {
            Content         = text,
            FontSize        = 11,
            Height          = 24,
            Padding         = new Thickness(9, 0, 9, 0),
            Cursor          = Cursors.Hand,
            Background      = Br("CardHover"),
            Foreground      = Br("TextPrimary"),
            BorderThickness = new Thickness(0)
        };

        // Rounded template (ControlTemplate built in code since styles aren't
        // shared with parent UI resources in this separate app)
        var template  = new ControlTemplate(typeof(Button));
        var bdFactory = new FrameworkElementFactory(typeof(Border));
        bdFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        bdFactory.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
        bdFactory.SetValue(Border.PaddingProperty, new Thickness(9, 0, 9, 0));
        var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        cpFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cpFactory.SetValue(ContentPresenter.VerticalAlignmentProperty,   VerticalAlignment.Center);
        bdFactory.AppendChild(cpFactory);
        template.VisualTree = bdFactory;
        btn.Template        = template;

        btn.MouseEnter += (_, _) => btn.Background = Br("BorderColor");
        btn.MouseLeave += (_, _) => btn.Background = Br("CardHover");

        return btn;
    }

    private static void CopyToClipboard(string text, Button btn)
    {
        try
        {
            Clipboard.SetText(text);
            var original   = btn.Content?.ToString() ?? "Copy";
            btn.Content    = "Copied!";
            btn.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xE3, 0xA1)); // green

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                btn.Content    = original;
                btn.Foreground = Br("TextPrimary");
            };
            timer.Start();
        }
        catch { }
    }

    // ── Lock ──────────────────────────────────────────────────────────────────

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _refreshTimer.Stop();
        _unlockedProfileId = -1;

        EntriesPanel.Children.Clear();
        LastRefreshedText.Text = "";

        VaultPanel.Visibility = Visibility.Collapsed;
        PinPanel.Visibility   = Visibility.Visible;

        PinBox.IsEnabled      = true;
        UnlockBtn.IsEnabled   = true;
        PinBox.Clear();
        PinError.Text = "";
        PinBox.Focus();
    }
}
