using System.Runtime.InteropServices;

namespace ParentalControl.Service.Services;

/// <summary>
/// Hardens the current process's DACL at service startup so that standard users
/// (Interactive Users, Everyone) cannot terminate, suspend, or inject into it.
/// SYSTEM and Administrators retain full process access.
/// Call HardenCurrentProcess() once from StartAsync before the main loop begins.
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
    /// Failures are swallowed — hardening is best-effort; it should not
    /// crash the service if it fails (e.g. when debugging as a normal user).
    /// </summary>
    public static void HardenCurrentProcess()
    {
        IntPtr pSd = IntPtr.Zero;
        try
        {
            // 1. Parse the SDDL string into a binary security descriptor.
            if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                    ProcessSddl, SDDL_REVISION_1, out pSd, out _))
                return;

            try
            {
                // 2. Extract the DACL pointer from the binary SD.
                if (!GetSecurityDescriptorDacl(pSd,
                        out bool daclPresent, out IntPtr pDacl, out _)
                    || !daclPresent)
                    return;

                // 3. Apply the DACL to the current process kernel object.
                SetSecurityInfo(
                    GetCurrentProcess(),
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    IntPtr.Zero,   // owner — not changing
                    IntPtr.Zero,   // group — not changing
                    pDacl,
                    IntPtr.Zero);  // SACL  — not changing
            }
            finally
            {
                // 4. Free memory allocated by ConvertStringSecurityDescriptorToSecurityDescriptor.
                LocalFree(pSd);
            }
        }
        catch { /* best-effort; never crash the service */ }
    }
}
