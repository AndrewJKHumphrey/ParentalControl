using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using ParentalControl.Core.Data;

namespace ParentalControl.Service.Services;

public class WebsiteFilter : IDisposable
{
    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string MarkerBegin = "# BEGIN ParentalControl";
    private const string MarkerEnd = "# END ParentalControl";
    private const string NrptNamespace = ".";
    private const string DnsServerAddress = "127.0.0.53";

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern bool DnsFlushResolverCache();

    private LocalDnsServer? _dnsServer;
    private bool _allowModeActive;

    public void LoadRules() => SyncHostsFile();

    public void SyncAndRestartBrowsers()
    {
        SyncHostsFile();

        // Apply browser-specific policies only when web filter rules are explicitly saved.
        // These registry writes (especially BackgroundModeEnabled) cause Edge/Chrome to
        // close background/boost processes, so we only do them here — not on every ReloadRules.
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            bool webFilterEnabled = settings?.WebFilterEnabled ?? true;
            bool isAllowMode = settings?.IsAllowMode ?? false;

            if (webFilterEnabled)
            {
                DisableBrowserDoh();

                var blockedDomains = isAllowMode
                    ? new List<string>()
                    : db.WebsiteRules
                        .Where(r => r.IsBlocked)
                        .Select(r => r.Domain.ToLower().Trim())
                        .ToList();

                SetBrowserHostResolverRules(blockedDomains);
            }
        }
        catch { }

        TerminateBrowsers();
    }

    public void SyncHostsFile()
    {
        bool isAllowMode;
        bool webFilterEnabled;
        try
        {
            using var db = new AppDbContext();
            var settings = db.Settings.FirstOrDefault();
            isAllowMode      = settings?.IsAllowMode      ?? false;
            webFilterEnabled = settings?.WebFilterEnabled ?? true;
        }
        catch { return; }

        if (!webFilterEnabled)
        {
            RemoveAllRules();
            return;
        }

        if (isAllowMode)
            ApplyAllowMode();
        else
            ApplyBlockMode();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Block Mode — hosts file sinkhole
    // ──────────────────────────────────────────────────────────────────────────

    private void ApplyBlockMode()
    {
        // Tear down Allow Mode if it was previously active
        if (_allowModeActive)
        {
            StopDnsServer();
            RemoveNrptRule();
            _allowModeActive = false;
        }

        List<string> blockedDomains;
        try
        {
            using var db = new AppDbContext();
            blockedDomains = db.WebsiteRules
                .Where(r => r.IsBlocked)
                .Select(r => r.Domain.ToLower().Trim())
                .ToList();
        }
        catch { return; }

        try
        {
            var lines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath).ToList()
                : new List<string>();

            int startIdx = lines.IndexOf(MarkerBegin);
            int endIdx = lines.IndexOf(MarkerEnd);

            if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
                lines.RemoveRange(startIdx, endIdx - startIdx + 1);

            if (blockedDomains.Count > 0)
            {
                lines.Add(MarkerBegin);
                foreach (var domain in blockedDomains)
                {
                    lines.Add($"127.0.0.1 {domain}");
                    lines.Add($"127.0.0.1 www.{domain}");
                    lines.Add($"::1 {domain}");
                    lines.Add($"::1 www.{domain}");
                }
                lines.Add(MarkerEnd);
            }

            File.WriteAllLines(HostsPath, lines);
            ProtectHostsFile();
            DnsFlushResolverCache();
            RunHidden("ipconfig", "/flushdns");
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "ParentalControl",
                $"WebsiteFilter block-mode update failed: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Warning);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Allow Mode — local DNS sinkhole + NRPT wildcard rule
    // ──────────────────────────────────────────────────────────────────────────

    private void ApplyAllowMode()
    {
        // Clear hosts-file block entries and browser host rules (not used in Allow Mode)
        ClearHostsBlock();
        SetBrowserHostResolverRules(new List<string>());

        // Start or reload the local DNS server
        if (!_allowModeActive)
        {
            StartDnsServer();
            AddNrptRule();
            _allowModeActive = true;
        }
        else
        {
            _dnsServer?.ReloadAllowList();
        }

        DnsFlushResolverCache();
        RunHidden("ipconfig", "/flushdns");
    }

    private void StartDnsServer()
    {
        try
        {
            _dnsServer?.Dispose();
            _dnsServer = new LocalDnsServer();
            _dnsServer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "ParentalControl",
                $"LocalDnsServer failed to start: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Warning);
        }
    }

    private void StopDnsServer()
    {
        try { _dnsServer?.Dispose(); } catch { }
        _dnsServer = null;
    }

    /// <summary>
    /// Adds an NRPT wildcard rule that routes ALL DNS queries through our local sinkhole.
    /// </summary>
    private static void AddNrptRule()
    {
        try
        {
            // Remove any existing rule for "." first to avoid duplicates
            RunHidden("powershell", $"-NonInteractive -Command \"Remove-DnsClientNrptRule -Namespace '{NrptNamespace}' -Force -ErrorAction SilentlyContinue\"");
            RunHidden("powershell", $"-NonInteractive -Command \"Add-DnsClientNrptRule -Namespace '{NrptNamespace}' -ServerAddresses '{DnsServerAddress}'\"");
        }
        catch { }
    }

    /// <summary>
    /// Removes the NRPT wildcard rule installed by Allow Mode.
    /// </summary>
    private static void RemoveNrptRule()
    {
        try
        {
            RunHidden("powershell", $"-NonInteractive -Command \"Remove-DnsClientNrptRule -Namespace '{NrptNamespace}' -Force -ErrorAction SilentlyContinue\"");
        }
        catch { }
    }

    private static void ClearHostsBlock()
    {
        try
        {
            if (!File.Exists(HostsPath)) return;
            var lines = File.ReadAllLines(HostsPath).ToList();
            int startIdx = lines.IndexOf(MarkerBegin);
            int endIdx = lines.IndexOf(MarkerEnd);
            if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
            {
                lines.RemoveRange(startIdx, endIdx - startIdx + 1);
                File.WriteAllLines(HostsPath, lines);
            }
        }
        catch { }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes Group Policy registry keys to disable DNS-over-HTTPS in all major browsers,
    /// forcing them to use the system resolver which reads the hosts file.
    /// </summary>
    private static void DisableBrowserDoh()
    {
        try
        {
            // Edge (Chromium) — HKLM so it applies to all users
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Microsoft\Edge", writable: true))
            {
                // Disable Edge's built-in async DNS client (force OS resolver)
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                // Disable DNS-over-HTTPS entirely
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                // Clear any pre-configured DoH provider template — a non-empty template
                // can override DnsOverHttpsMode in some Edge versions
                key.SetValue("DnsOverHttpsTemplates", "", RegistryValueKind.String);
                // Prevent Edge from running silently in the background between launches
                // so it re-reads policy on next open instead of reusing a stale process
                key.SetValue("BackgroundModeEnabled", 0, RegistryValueKind.DWord);
                // Disable Edge's startup boost (pre-launched background process)
                key.SetValue("StartupBoostEnabled", 0, RegistryValueKind.DWord);
            }

            // Chrome
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Google\Chrome", writable: true))
            {
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                key.SetValue("DnsOverHttpsTemplates", "", RegistryValueKind.String);
            }

            // Firefox
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Mozilla\Firefox", writable: true))
            {
                using var doh = key.CreateSubKey("DNSOverHTTPS");
                doh.SetValue("Enabled", 0, RegistryValueKind.DWord);
                doh.SetValue("Locked", 1, RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    /// <summary>
    /// Terminates Edge and Chrome entirely (all processes, including helpers) so they
    /// restart clean with the updated policy and hosts file.
    /// taskkill /F /T kills the entire process tree, catching GPU/network/utility children.
    /// </summary>
    private static void TerminateBrowsers()
    {
        RunHidden("taskkill", "/F /IM msedge.exe /T");
        RunHidden("taskkill", "/F /IM chrome.exe /T");
    }

    /// <summary>
    /// Writes HostResolverRules into the Edge and Chrome Group Policy registry keys so that
    /// Chromium's built-in DNS client maps each blocked domain to 0.0.0.0, regardless of
    /// whether the OS hosts file is being read. Passing an empty list clears the policy.
    /// </summary>
    private static void SetBrowserHostResolverRules(List<string> blockedDomains)
    {
        var rules = blockedDomains.Count == 0
            ? ""
            : string.Join(", ", blockedDomains.SelectMany(d => new[]
              {
                  $"MAP {d} 0.0.0.0",
                  $"MAP www.{d} 0.0.0.0"
              }));

        foreach (var path in new[] {
            @"SOFTWARE\Policies\Microsoft\Edge",
            @"SOFTWARE\Policies\Google\Chrome" })
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(path, writable: true);
                if (string.IsNullOrEmpty(rules))
                    key.DeleteValue("HostResolverRules", throwOnMissingValue: false);
                else
                    key.SetValue("HostResolverRules", rules, RegistryValueKind.String);
            }
            catch { }
        }
    }

    private static void RunHidden(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(5000);
        }
        catch { }
    }

    private static void ProtectHostsFile()
    {
        try
        {
            var fileSecurity = new FileSecurity(HostsPath, AccessControlSections.Access);

            // Remove inherited rules
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Allow SYSTEM full control
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // Allow Administrators full control
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // Allow TrustedInstaller read
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"),
                FileSystemRights.Read,
                AccessControlType.Allow));

            // Deny Users write/delete so the child can't edit the hosts file to unblock sites
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions,
                AccessControlType.Deny));

            new FileInfo(HostsPath).SetAccessControl(fileSecurity);
        }
        catch { }
    }

    public void RemoveAllRules()
    {
        ClearHostsBlock();
        SetBrowserHostResolverRules(new List<string>());
        if (_allowModeActive)
        {
            StopDnsServer();
            RemoveNrptRule();
            _allowModeActive = false;
        }
        DnsFlushResolverCache();
        RunHidden("ipconfig", "/flushdns");
    }

    public void Dispose()
    {
        StopDnsServer();
    }
}
