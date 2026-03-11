using System.Runtime.InteropServices;

namespace ParentalControl.Watchdog;

/// <summary>
/// Hardens the watchdog process's DACL at startup so that standard users cannot
/// terminate, suspend, or inject into it via Task Manager or other tools.
/// SYSTEM and Administrators retain full process access.
/// Identical logic to ParentalControl.Service.Services.ProcessProtection —
/// kept as a separate file to avoid a cross-project dependency.
/// </summary>
internal static class ProcessProtection
{
    // ── Win32 constants ────────────────────────────────────────────────────────
    private const uint DACL_SECURITY_INFORMATION = 0x00000004;
    private const uint SDDL_REVISION_1           = 1;
    private const uint SE_KERNEL_OBJECT          = 6;

    // PROCESS_ALL_ACCESS (0x1FFFFF) for SYSTEM and Administrators;
    // PROCESS_QUERY_LIMITED_INFORMATION (0x1000) for Everyone — enough for Task Manager
    // to display the process name/CPU but does NOT include PROCESS_TERMINATE (0x0001).
    private const string ProcessSddl =
        "D:(A;;0x1FFFFF;;;SY)(A;;0x1FFFFF;;;BA)(A;;0x1000;;;WD)";

    // ── P/Invoke declarations ──────────────────────────────────────────────────

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string  StringSecurityDescriptor,
        uint    StringSDRevision,
        out IntPtr SecurityDescriptor,
        out uint   SecurityDescriptorSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetSecurityDescriptorDacl(
        IntPtr   pSecurityDescriptor,
        out bool bDaclPresent,
        out IntPtr pDacl,
        out bool bDaclDefaulted);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern uint SetSecurityInfo(
        IntPtr handle,
        uint   ObjectType,
        uint   SecurityInfo,
        IntPtr psidOwner,
        IntPtr psidGroup,
        IntPtr pDacl,
        IntPtr pSacl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a restrictive DACL on the current process handle.
    /// Failures are swallowed — hardening is best-effort and should not crash the watchdog.
    /// </summary>
    public static void HardenCurrentProcess()
    {
        IntPtr pSd = IntPtr.Zero;
        try
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                    ProcessSddl, SDDL_REVISION_1, out pSd, out _))
                return;

            try
            {
                if (!GetSecurityDescriptorDacl(pSd,
                        out bool daclPresent, out IntPtr pDacl, out _)
                    || !daclPresent)
                    return;

                SetSecurityInfo(
                    GetCurrentProcess(),
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    pDacl,
                    IntPtr.Zero);
            }
            finally
            {
                LocalFree(pSd);
            }
        }
        catch { /* best-effort; never crash the watchdog */ }
    }
}
