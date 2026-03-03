using System.Net.Http;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using ParentalControl.Core.Data;

namespace ParentalControl.Core.Services;

public class NotificationService
{
    private static readonly HttpClient _http = new();

    // Per-process rate-limiting: don't flood the parent with repeated blocked-app emails.
    // One notification per process per 10 minutes maximum.
    private readonly Dictionary<string, DateTime> _lastSent = new(StringComparer.OrdinalIgnoreCase);

    public void SendScreenLockNotification(string reason)
    {
        Send(
            subject: "ParentGuard: Screen Locked",
            body:    $"The screen was locked at {DateTime.Now:t} on {DateTime.Now:D}.\n\nReason: {reason}",
            checkScreenLock: true,
            processName: null);
    }

    public void SendAppBlockNotification(string processName, string reason)
    {
        Send(
            subject: $"ParentGuard: App Blocked — {processName}",
            body:    $"'{processName}' was blocked at {DateTime.Now:t} on {DateTime.Now:D}.\n\nReason: {reason}",
            checkScreenLock: false,
            processName: processName);
    }

    // -------------------------------------------------------------------------

    private void Send(string subject, string body, bool checkScreenLock, string? processName)
    {
        try
        {
            using var db = new AppDbContext();
            var s = db.Settings.FirstOrDefault();
            if (s == null || !s.NotificationsEnabled) return;

            // Check per-event-type toggle
            if (checkScreenLock  && !s.NotifyOnScreenLock) return;
            if (!checkScreenLock && !s.NotifyOnAppBlock)   return;

            // Mode 2: ntfy.sh push notification (no credentials required)
            if (s.NotificationMode == 2)
            {
                if (string.IsNullOrWhiteSpace(s.NtfyTopic)) return;

                if (processName != null)
                {
                    var nowUtc = DateTime.UtcNow;
                    if (_lastSent.TryGetValue(processName, out var last) &&
                        (nowUtc - last).TotalMinutes < 10)
                        return;
                    _lastSent[processName] = nowUtc;
                }

                var content = new StringContent(body);
                content.Headers.Add("Title", subject);
                try
                {
                    _http.PostAsync($"https://ntfy.sh/{Uri.EscapeDataString(s.NtfyTopic)}", content)
                         .GetAwaiter().GetResult();
                }
                catch { }
                return;
            }

            if (string.IsNullOrWhiteSpace(s.NotificationAddress) ||
                string.IsNullOrWhiteSpace(s.SmtpHost)            ||
                string.IsNullOrWhiteSpace(s.SmtpUsername))
                return;

            // Rate-limit per process name (app block events only)
            if (processName != null)
            {
                var now = DateTime.UtcNow;
                if (_lastSent.TryGetValue(processName, out var last) &&
                    (now - last).TotalMinutes < 10)
                    return;
                _lastSent[processName] = now;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("ParentGuard", s.SmtpUsername));
            message.To.Add(MailboxAddress.Parse(s.NotificationAddress));
            message.Subject = subject;
            message.Body    = new TextPart("plain") { Text = body };

            using var client = new SmtpClient();
            // Use StartTls when SSL is enabled (port 587), SslOnConnect for port 465
            var secureOption = s.SmtpUseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;
            client.Connect(s.SmtpHost, s.SmtpPort, secureOption);
            client.Authenticate(s.SmtpUsername, s.SmtpPassword);
            client.Send(message);
            client.Disconnect(true);
        }
        catch { }  // Never crash the caller over a notification failure
    }

    // Called from Settings page to verify configuration works
    public (bool success, string message) SendTest()
    {
        try
        {
            using var db = new AppDbContext();
            var s = db.Settings.FirstOrDefault();
            if (s == null) return (false, "Settings not found.");

            // Mode 2: ntfy.sh push notification
            if (s.NotificationMode == 2)
            {
                if (string.IsNullOrWhiteSpace(s.NtfyTopic))
                    return (false, "No ntfy topic configured.");
                try
                {
                    var content = new StringContent("This is a test notification from ParentGuard.");
                    content.Headers.Add("Title", "ParentGuard: Test Notification");
                    var response = _http.PostAsync(
                        $"https://ntfy.sh/{Uri.EscapeDataString(s.NtfyTopic)}", content)
                        .GetAwaiter().GetResult();
                    return response.IsSuccessStatusCode
                        ? (true,  $"Test push sent to topic '{s.NtfyTopic}'.")
                        : (false, $"ntfy.sh returned {(int)response.StatusCode}.");
                }
                catch (Exception ex)
                {
                    return (false, $"Failed: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(s.NotificationAddress))
                return (false, "No notification address configured.");
            if (string.IsNullOrWhiteSpace(s.SmtpHost))
                return (false, "No SMTP server configured.");
            if (string.IsNullOrWhiteSpace(s.SmtpUsername))
                return (false, "No SMTP username configured.");

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("ParentGuard", s.SmtpUsername));
            message.To.Add(MailboxAddress.Parse(s.NotificationAddress));
            message.Subject = "ParentGuard: Test Notification";
            message.Body    = new TextPart("plain")
            {
                Text = $"This is a test notification from ParentGuard.\n\nSent at {DateTime.Now:f}.\n\nIf you received this, notifications are configured correctly."
            };

            var secureOption = s.SmtpUseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            using var client = new SmtpClient();
            client.Connect(s.SmtpHost, s.SmtpPort, secureOption);
            client.Authenticate(s.SmtpUsername, s.SmtpPassword);
            client.Send(message);
            client.Disconnect(true);

            return (true, $"Test notification sent to {s.NotificationAddress}.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed: {ex.Message}");
        }
    }
}
