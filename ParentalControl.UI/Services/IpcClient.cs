using System.IO.Pipes;
using System.Text.Json;
using ParentalControl.Core;

namespace ParentalControl.UI.Services;

public class IpcClient
{
    private const string PipeName = "ParentalControlPipe";
    private const int TimeoutMs = 3000;

    public async Task<IpcResponse> SendAsync(IpcCommand command, string? payload = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(TimeoutMs);

            var msg = new IpcMessage { Command = command, Payload = payload };
            await WriteMessageAsync(client, msg);
            return await ReadResponseAsync(client) ?? new IpcResponse { Success = false, Error = "No response" };
        }
        catch (TimeoutException)
        {
            return new IpcResponse { Success = false, Error = "Service not responding. Is it running?" };
        }
        catch (Exception ex)
        {
            return new IpcResponse { Success = false, Error = ex.Message };
        }
    }

    private static async Task WriteMessageAsync(PipeStream pipe, IpcMessage msg)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(msg);
        await pipe.WriteAsync(BitConverter.GetBytes(json.Length));
        await pipe.WriteAsync(json);
        await pipe.FlushAsync();
    }

    private static async Task<IpcResponse?> ReadResponseAsync(PipeStream pipe)
    {
        var lenBuf = new byte[4];
        await pipe.ReadExactlyAsync(lenBuf);
        int len = BitConverter.ToInt32(lenBuf);
        var buf = new byte[len];
        await pipe.ReadExactlyAsync(buf);
        return JsonSerializer.Deserialize<IpcResponse>(buf);
    }
}
