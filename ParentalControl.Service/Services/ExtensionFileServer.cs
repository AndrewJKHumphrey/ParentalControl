using System.Net;

namespace ParentalControl.Service.Services;

/// <summary>
/// Serves the browser extension files (update.xml + parentguard.crx) over HTTP on localhost
/// so that Edge/Chrome enterprise policies can fetch the CRX via an http:// URL.
/// file:// URLs in ExtensionInstallForcelist policies are not reliably supported by Edge.
/// </summary>
internal sealed class ExtensionFileServer : IDisposable
{
    public const int Port = 47252;
    public static readonly string Prefix = $"http://localhost:{Port}/extension/";

    private static readonly string ExtDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "ParentalControl", "extension");

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    public void Start()
    {
        try
        {
            _listener.Prefixes.Add(Prefix);
            _listener.Start();
            _ = ServeAsync(_cts.Token);
        }
        catch
        {
            // Best-effort; never crash the service if the port is unavailable.
        }
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleAsync(ctx);
            }
            catch (OperationCanceledException) { break; }
            catch { /* keep serving */ }
        }
    }

    private static async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            // Strip the "/extension/" prefix to get the relative file name.
            var rel = ctx.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
            if (rel.StartsWith("extension/", StringComparison.OrdinalIgnoreCase))
                rel = rel["extension/".Length..];

            var filePath = Path.GetFullPath(Path.Combine(ExtDir, rel));

            // Security: ensure the resolved path is still inside ExtDir.
            if (!filePath.StartsWith(ExtDir, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            ctx.Response.ContentType = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".xml" => "application/xml",
                ".crx" => "application/x-chrome-extension",
                _      => "application/octet-stream"
            };

            var data = await File.ReadAllBytesAsync(filePath);
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data);
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
        _cts.Dispose();
    }
}
