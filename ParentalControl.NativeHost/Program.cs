using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

// Native Messaging protocol: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
// The browser launches this process, sends one message, reads the response, and may keep the
// pipe open for further messages. We loop until stdin closes.

static byte[] ReadMessage(Stream stdin)
{
    var lenBytes = new byte[4];
    int totalRead = 0;
    while (totalRead < 4)
    {
        int n = stdin.Read(lenBytes, totalRead, 4 - totalRead);
        if (n == 0) return [];
        totalRead += n;
    }
    int length = BitConverter.ToInt32(lenBytes, 0);
    if (length <= 0 || length > 1_048_576) return []; // 1 MB safety cap

    var buf = new byte[length];
    totalRead = 0;
    while (totalRead < length)
    {
        int n = stdin.Read(buf, totalRead, length - totalRead);
        if (n == 0) return [];
        totalRead += n;
    }
    return buf;
}

static void WriteMessage(Stream stdout, string json)
{
    var payload = Encoding.UTF8.GetBytes(json);
    var lenBytes = BitConverter.GetBytes(payload.Length);
    stdout.Write(lenBytes, 0, 4);
    stdout.Write(payload, 0, payload.Length);
    stdout.Flush();
}

var stdin  = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

while (true)
{
    var msgBytes = ReadMessage(stdin);
    if (msgBytes.Length == 0) break;

    string response;
    try
    {
        var doc     = JsonDocument.Parse(msgBytes);
        var msgType = doc.RootElement.GetProperty("type").GetString();

        using var db = new AppDbContext();

        if (msgType == "get_rules")
        {
            // Determine which profile is active: match current Windows username
            var username = Environment.UserName;
            var profile  = db.UserProfiles
                             .AsNoTracking()
                             .FirstOrDefault(p => p.WindowsUsername.ToLower() == username.ToLower())
                          ?? db.UserProfiles.AsNoTracking().FirstOrDefault(p => p.WindowsUsername == "")
                          ?? new UserProfile { Id = 1 };

            var rules = db.WebsiteRules
                          .AsNoTracking()
                          .Where(r => r.UserProfileId == profile.Id)
                          .ToList();

            // Determine effective mode: if any rule is IsBlocked=false it signals allow-list mode
            bool allowMode = rules.Any(r => !r.IsBlocked);
            var  domains   = rules.Select(r => r.Pattern).ToArray();

            response = JsonSerializer.Serialize(new
            {
                mode      = allowMode ? "allow" : "block",
                domains,
                profileId = profile.Id
            });
        }
        else if (msgType == "blocked")
        {
            var url = doc.RootElement.TryGetProperty("url", out var urlProp)
                      ? urlProp.GetString() ?? ""
                      : "";

            db.ActivityEntries.Add(new ActivityEntry
            {
                Type      = ActivityType.WebsiteBlocked,
                Detail    = $"Blocked: {url}",
                Timestamp = DateTime.Now,
            });
            db.SaveChanges();

            response = "{\"ok\":true}";
        }
        else
        {
            response = "{\"error\":\"unknown type\"}";
        }
    }
    catch (Exception ex)
    {
        response = JsonSerializer.Serialize(new { error = ex.Message });
    }

    WriteMessage(stdout, response);
}
