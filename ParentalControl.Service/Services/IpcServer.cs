using System.IO.Pipes;
using System.Text.Json;
using ParentalControl.Core;

namespace ParentalControl.Service.Services;

public class IpcServer : IDisposable
{
    private const string PipeName = "ParentalControlPipe";
    private readonly ProcessMonitor _processMonitor;
    private readonly ScreenTimeEnforcer _screenTimeEnforcer;
    private readonly WebsiteFilter _websiteFilter;
    private readonly ActivityLogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;

    public IpcServer(ProcessMonitor pm, ScreenTimeEnforcer st, WebsiteFilter wf, ActivityLogger logger)
    {
        _processMonitor = pm;
        _screenTimeEnforcer = st;
        _websiteFilter = wf;
        _logger = logger;
    }

    public void Start()
    {
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                pipeSecurity.AddAccessRule(new PipeAccessRule(
                    "Everyone",
                    PipeAccessRights.ReadWrite,
                    System.Security.AccessControl.AccessControlType.Allow));

                using var server = NamedPipeServerStreamAcl.Create(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                    4096, 4096, pipeSecurity);

                await server.WaitForConnectionAsync(ct);

                var request = await ReadMessageAsync(server, ct);
                if (request != null)
                {
                    var response = HandleCommand(request);
                    await WriteMessageAsync(server, response, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(1000, ct); }
        }
    }

    private IpcResponse HandleCommand(IpcMessage msg)
    {
        try
        {
            switch (msg.Command)
            {
                case IpcCommand.ReloadRules:
                    _processMonitor.LoadRules();
                    _screenTimeEnforcer.LoadRules();
                    _websiteFilter.LoadRules();
                    return new IpcResponse { Success = true };

                case IpcCommand.LockNow:
                    SessionLock.LockActive();
                    _logger.Log(Core.Models.ActivityType.ScreenLocked, "Manual lock by parent");
                    return new IpcResponse { Success = true };

                case IpcCommand.GetStatus:
                    return new IpcResponse { Success = true, Data = "Service running" };

                default:
                    return new IpcResponse { Success = false, Error = "Unknown command" };
            }
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    private static async Task<IpcMessage?> ReadMessageAsync(PipeStream pipe, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        await pipe.ReadExactlyAsync(lenBuf, ct);
        int len = BitConverter.ToInt32(lenBuf);
        var buf = new byte[len];
        await pipe.ReadExactlyAsync(buf, ct);
        return JsonSerializer.Deserialize<IpcMessage>(buf);
    }

    private static async Task WriteMessageAsync(PipeStream pipe, IpcResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response);
        var lenBuf = BitConverter.GetBytes(json.Length);
        await pipe.WriteAsync(lenBuf, ct);
        await pipe.WriteAsync(json, ct);
        await pipe.FlushAsync(ct);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listenTask?.Wait(2000);
        _cts.Dispose();
    }
}

