using System.Runtime.InteropServices;

namespace ParentalControl.Service.Services;

internal static class SessionLock
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool bWait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken, string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
        string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars;
        public uint dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    /// <summary>
    /// Locks the interactive console session. Safe to call from Session 0 (SYSTEM service).
    /// Tries WTSDisconnectSession first; falls back to spawning rundll32 LockWorkStation
    /// in the user's session via CreateProcessAsUser if the disconnect call fails.
    /// </summary>
    internal static void LockActive()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) return; // no active console session

        // Primary: disconnect the console session — shows sign-in screen on Win10/11
        if (WTSDisconnectSession(IntPtr.Zero, (int)sessionId, false))
            return;

        // Fallback: spawn LockWorkStation inside the user's session via their token.
        // SYSTEM (LocalSystem) has SE_TCB_PRIVILEGE which WTSQueryUserToken requires.
        if (!WTSQueryUserToken(sessionId, out var userToken))
            return;

        try
        {
            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = "winsta0\\default"
            };
            string cmd = $"{Environment.GetFolderPath(Environment.SpecialFolder.System)}\\rundll32.exe user32.dll,LockWorkStation";
            CreateProcessAsUser(userToken, null, cmd,
                IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null,
                ref si, out var pi);
            if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
            if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);
        }
        finally
        {
            CloseHandle(userToken);
        }
    }
}
