using System.Windows;
using System.Windows.Input;
using ParentalControl.Core.Data;

namespace ParentalControl.UI.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) TryLogin();
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e) => TryLogin();

    private void TryLogin()
    {
        var password = PasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError("Please enter the password.");
            return;
        }

        try
        {
            using var db = new AppDbContext();
            db.Database.EnsureCreated();
            var settings = db.Settings.FirstOrDefault();

            if (settings == null)
            {
                ShowError("No settings found. Database may not be initialized.");
                return;
            }

            if (BCrypt.Net.BCrypt.Verify(password, settings.PasswordHash))
            {
                DialogResult = true;
                Close();
            }
            else
            {
                ShowError("Incorrect password. Please try again.");
                PasswordBox.Clear();
                PasswordBox.Focus();
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
