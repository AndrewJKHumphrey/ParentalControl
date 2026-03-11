using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParentalControl.Core.Data;
using ParentalControl.Core.Models;

// Native Messaging protocol: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
// The browser connects via chrome.runtime.connectNative (persistent port).
// A background thread polls the web_rules_version.txt file and pushes {type:"reload"}
// to the extension whenever the UI saves new rules.
//
// Message types (extension → host):
//   get_rules        → returns manual block/allow rules + tagBlockedCount (NO tag domains inline)
//   get_tag_domains  → returns a paginated chunk of tag domains (offset, limit params)
//   blocked          → logs a blocked navigation to the activity log
//
// Tag domains are served via get_tag_domains in chunks of ≤40,000 to stay under the
// Chrome/Edge 1 MB native messaging message size limit (174k adult-content domains
// would produce a ~4 MB JSON blob which Chrome silently rejects).

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

var stdin      = Console.OpenStandardInput();
var stdout     = Console.OpenStandardOutput();
var stdoutLock = new object();

// Version file: UI writes a new timestamp here whenever web rules change.
// This process polls it every second and pushes a reload notification to the extension.
var versionFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "ParentalControl", "web_rules_version.txt");

var cts = new CancellationTokenSource();

// Background thread: watch version file, push reload message when it changes
var pollThread = new Thread(() =>
{
    string lastVersion = File.Exists(versionFile) ? File.ReadAllText(versionFile) : "";
    while (!cts.Token.IsCancellationRequested)
    {
        Thread.Sleep(1000);
        try
        {
            string current = File.Exists(versionFile) ? File.ReadAllText(versionFile) : "";
            if (current != lastVersion)
            {
                lastVersion = current;
                lock (stdoutLock)
                    WriteMessage(stdout, "{\"type\":\"reload\"}");
            }
        }
        catch { }
    }
}) { IsBackground = true };
pollThread.Start();

// Resolve the active profile for the current Windows user.
// If a specific profile exists but has no web filter configuration
// (auto-seeded blank profile), fall back to the Default catch-all profile
// so that rules set on the Default profile still apply.
static UserProfile ResolveProfile(AppDbContext db)
{
    var username = Environment.UserName;

    var specific = db.UserProfiles.AsNoTracking()
                      .FirstOrDefault(p => p.WindowsUsername.ToLower() == username.ToLower());

    if (specific != null)
    {
        bool hasRules = db.WebsiteRules.Any(r => r.UserProfileId == specific.Id);
        bool hasTags  = db.ProfileWebFilterTags.Any(pt => pt.UserProfileId == specific.Id);
        if (hasRules || hasTags || specific.WebFilterAllowMode)
            return specific;
    }

    // Fall through to the Default (catch-all) profile
    return db.UserProfiles.AsNoTracking().FirstOrDefault(p => p.WindowsUsername == "")
        ?? specific                // return the blank specific profile if no Default exists
        ?? new UserProfile { Id = 1 };
}

// Main thread: handle request messages from the extension
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
            var profile  = ResolveProfile(db);
            var settings = db.Settings.AsNoTracking().FirstOrDefault();

            // Read theme colors written by the UI whenever the theme is changed.
            JsonElement? theme = null;
            try
            {
                var themeFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ParentalControl", "theme.json");
                if (File.Exists(themeFile))
                {
                    using var themeDoc = JsonDocument.Parse(File.ReadAllText(themeFile));
                    theme = themeDoc.RootElement.Clone();
                }
            }
            catch { }

            if (settings?.WebFilterEnabled == false)
            {
                // Web filter disabled — tell extension to clear all rules.
                response = JsonSerializer.Serialize(new
                {
                    type            = "rules",
                    allowMode       = false,
                    blocked         = Array.Empty<string>(),
                    allowed         = Array.Empty<string>(),
                    tagBlockedCount = 0,
                    profileId       = profile.Id,
                    theme
                });
            }
            else
            {
                var rules = db.WebsiteRules
                              .AsNoTracking()
                              .Where(r => r.UserProfileId == profile.Id)
                              .ToList();

                // Count how many tag domains are enabled — they are NOT inlined here
                // to avoid exceeding the 1 MB native messaging message size limit.
                // The extension fetches them separately via get_tag_domains.
                var enabledTagIds = db.ProfileWebFilterTags
                    .AsNoTracking()
                    .Where(pt => pt.UserProfileId == profile.Id)
                    .Select(pt => pt.TagId)
                    .ToHashSet();

                int tagBlockedCount = enabledTagIds.Count > 0
                    ? db.WebFilterTagDomains.Count(d => enabledTagIds.Contains(d.TagId))
                    : 0;

                var blocked   = rules.Where(r =>  r.IsBlocked).Select(r => r.Pattern).ToArray();
                var allowed   = rules.Where(r => !r.IsBlocked).Select(r => r.Pattern).ToArray();
                bool allowMode = profile.WebFilterAllowMode;

                response = JsonSerializer.Serialize(new
                {
                    type = "rules",
                    allowMode,
                    blocked,         // manual block entries only (small)
                    allowed,
                    tagBlockedCount, // extension fetches tag domains separately
                    profileId = profile.Id,
                    theme
                });
            }
        }
        else if (msgType == "get_tag_domains")
        {
            // Paginated tag domain fetch — keeps each response well under 1 MB.
            int offset = doc.RootElement.TryGetProperty("offset", out var offsetProp)
                ? offsetProp.GetInt32() : 0;
            int limit = doc.RootElement.TryGetProperty("limit", out var limitProp)
                ? limitProp.GetInt32() : 40_000;

            var profile = ResolveProfile(db);

            var enabledTagIds = db.ProfileWebFilterTags
                .AsNoTracking()
                .Where(pt => pt.UserProfileId == profile.Id)
                .Select(pt => pt.TagId)
                .ToHashSet();

            if (enabledTagIds.Count == 0)
            {
                response = JsonSerializer.Serialize(new
                {
                    type    = "tag_domains",
                    offset,
                    total   = 0,
                    domains = Array.Empty<string>()
                });
            }
            else
            {
                int total = db.WebFilterTagDomains
                              .Count(d => enabledTagIds.Contains(d.TagId));

                var domains = db.WebFilterTagDomains
                                .AsNoTracking()
                                .Where(d => enabledTagIds.Contains(d.TagId))
                                .OrderBy(d => d.Id)  // stable order for consistent pagination
                                .Skip(offset)
                                .Take(limit)
                                .Select(d => d.Domain)
                                .ToArray();

                response = JsonSerializer.Serialize(new { type = "tag_domains", offset, total, domains });
            }
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

            response = "{\"type\":\"ok\"}";
        }
        else
        {
            response = "{\"type\":\"error\",\"error\":\"unknown type\"}";
        }
    }
    catch (Exception ex)
    {
        response = JsonSerializer.Serialize(new { type = "error", error = ex.Message });
    }

    lock (stdoutLock)
        WriteMessage(stdout, response);
}

cts.Cancel();
