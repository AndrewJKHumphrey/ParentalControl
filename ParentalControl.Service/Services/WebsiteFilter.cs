using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using ParentalControl.Core.Data;

namespace ParentalControl.Service.Services;

public class WebsiteFilter
{
    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string MarkerBegin = "# BEGIN ParentalControl";
    private const string MarkerEnd = "# END ParentalControl";

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern bool DnsFlushResolverCache();

    public void LoadRules()
    {
        SyncHostsFile();
    }

    public void SyncHostsFile()
    {
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
            // Read existing hosts file, stripping our previous block
            var lines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath).ToList()
                : new List<string>();

            int startIdx = lines.IndexOf(MarkerBegin);
            int endIdx = lines.IndexOf(MarkerEnd);

            if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
                lines.RemoveRange(startIdx, endIdx - startIdx + 1);

            // Append our block
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
            DisableBrowserDoh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.EventLog.WriteEntry(
                "ParentalControl",
                $"WebsiteFilter hosts update failed: {ex.Message}",
                System.Diagnostics.EventLogEntryType.Warning);
        }
    }

    /// <summary>
    /// Sets browser group policy registry keys to disable DNS-over-HTTPS,
    /// forcing browsers to use the system resolver (and therefore the hosts file).
    /// </summary>
    private static void DisableBrowserDoh()
    {
        try
        {
            // Edge (Chromium)
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Microsoft\Edge", writable: true))
            {
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
            }

            // Chrome
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Google\Chrome", writable: true))
            {
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
            }

            // Firefox
            using (var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", writable: true))
            {
                key.SetValue("Enabled", 0, RegistryValueKind.DWord);
            }
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

            // Deny Users write
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
        try
        {
            var lines = File.Exists(HostsPath)
                ? File.ReadAllLines(HostsPath).ToList()
                : new List<string>();

            int startIdx = lines.IndexOf(MarkerBegin);
            int endIdx = lines.IndexOf(MarkerEnd);

            if (startIdx >= 0 && endIdx >= 0 && endIdx > startIdx)
                lines.RemoveRange(startIdx, endIdx - startIdx + 1);

            File.WriteAllLines(HostsPath, lines);
        }
        catch { }
    }
}
