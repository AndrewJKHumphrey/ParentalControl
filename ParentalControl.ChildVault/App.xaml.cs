using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace ParentalControl.ChildVault;

public partial class App : Application
{
    private static readonly string ThemeFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ParentalControl", "child_vault_theme.json");

    private void App_Startup(object sender, StartupEventArgs e)
    {
        ApplyThemeFromFile();
        new MainWindow().Show();
    }

    /// <summary>
    /// Reads theme.json written by the parent UI and updates the shared resource
    /// keys so every DynamicResource binding in the window reflects the parent's
    /// currently active theme.  Falls back to the Catppuccin Mocha dark defaults
    /// declared in App.xaml if the file is absent or unreadable.
    /// </summary>
    internal static void ApplyThemeFromFile()
    {
        try
        {
            if (!File.Exists(ThemeFile)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(ThemeFile));
            var root = doc.RootElement;

            static SolidColorBrush? TryBrush(JsonElement r, string key)
            {
                if (!r.TryGetProperty(key, out var prop)) return null;
                var hex = prop.GetString();
                if (string.IsNullOrEmpty(hex)) return null;
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { return null; }
            }

            var res = Current.Resources;
            if (TryBrush(root, "bg")      is { } appBg)         res["AppBg"]         = appBg;
            if (TryBrush(root, "cardBg")  is { } cardBg)        res["CardBg"]        = cardBg;
            if (TryBrush(root, "btnBg")   is { } cardHover)     res["CardHover"]     = cardHover;
            if (TryBrush(root, "border")  is { } borderColor)   res["BorderColor"]   = borderColor;
            if (TryBrush(root, "text")    is { } textPrimary)   res["TextPrimary"]   = textPrimary;
            if (TryBrush(root, "textSub") is { } textSecondary) res["TextSecondary"] = textSecondary;
        }
        catch { /* leave defaults in place */ }
    }
}
