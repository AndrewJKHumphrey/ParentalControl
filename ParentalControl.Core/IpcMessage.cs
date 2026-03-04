namespace ParentalControl.Core;

public enum IpcCommand
{
    GetStatus,
    ReloadRules,
    LockNow,
    Unlock
}

public class IpcMessage
{
    public IpcCommand Command { get; set; }
    public string? Payload { get; set; }
}

public class IpcResponse
{
    public bool Success { get; set; }
    public string? Data { get; set; }
    public string? Error { get; set; }
}
