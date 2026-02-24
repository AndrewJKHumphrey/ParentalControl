using System.Runtime.InteropServices;

namespace ParentalControl.Service.Services;

internal static class SessionHelper
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    /// <summary>
    /// Returns the session ID of the active interactive (console) session,
    /// or -1 if no session is active. Does not require elevated privileges.
    /// </summary>
    public static int GetActiveConsoleSessionId()
    {
        uint id = WTSGetActiveConsoleSessionId();
        return id == 0xFFFFFFFF ? -1 : (int)id;
    }
}
