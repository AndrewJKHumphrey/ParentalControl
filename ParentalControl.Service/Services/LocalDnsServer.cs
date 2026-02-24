using System.Net;
using System.Net.Sockets;
using ParentalControl.Core.Data;

namespace ParentalControl.Service.Services;

/// <summary>
/// A minimal UDP DNS sinkhole used in Allow Mode.
/// Listens on 127.0.0.53:53. Allowed domains are forwarded to the upstream resolver;
/// all other domains receive NXDOMAIN so the browser cannot reach them.
/// </summary>
public class LocalDnsServer : IDisposable
{
    private const string ListenAddress = "127.0.0.53";
    private const int ListenPort = 53;
    private const string UpstreamDns = "8.8.8.8";
    private const int UpstreamPort = 53;
    private const int TimeoutMs = 3000;

    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Domains the sinkhole always passes through (OS internals, loopback, etc.)
    private static readonly string[] AlwaysAllowed =
    [
        "localhost", "local", "internal", "lan",
        "microsoft.com", "windows.com", "windowsupdate.com",
        "msftconnecttest.com", "msftncsi.com",
        "office.com", "live.com", "microsoftonline.com",
    ];

    private HashSet<string> _allowedDomains = [];

    public void Start()
    {
        ReloadAllowList();

        _cts = new CancellationTokenSource();
        _listener = new UdpClient(new IPEndPoint(IPAddress.Parse(ListenAddress), ListenPort));
        _loop = Task.Run(() => RunLoop(_cts.Token));
    }

    public void ReloadAllowList()
    {
        try
        {
            using var db = new AppDbContext();
            var domains = db.WebsiteRules
                .Where(r => r.IsAllowed)
                .Select(r => r.Domain.ToLower().Trim())
                .ToHashSet();
            _allowedDomains = domains;
        }
        catch { }
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(ct);
                // Handle each query on a thread-pool thread so the receive loop stays fast
                _ = Task.Run(() => HandleQuery(result.Buffer, result.RemoteEndPoint), ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* keep running */ }
        }
    }

    private void HandleQuery(byte[] query, IPEndPoint client)
    {
        try
        {
            string domain = ExtractQueryDomain(query);

            if (IsPermitted(domain))
            {
                // Forward to upstream and relay response
                byte[]? response = ForwardToUpstream(query);
                if (response != null)
                    _listener!.Send(response, response.Length, client);
            }
            else
            {
                // Return NXDOMAIN
                byte[] nxdomain = BuildNxDomain(query);
                _listener!.Send(nxdomain, nxdomain.Length, client);
            }
        }
        catch { }
    }

    private bool IsPermitted(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return true;

        // Always allow system/OS domains
        foreach (var a in AlwaysAllowed)
            if (domain == a || domain.EndsWith("." + a))
                return true;

        // Check user-configured allow list (exact match or subdomain)
        foreach (var a in _allowedDomains)
            if (domain == a || domain.EndsWith("." + a))
                return true;

        return false;
    }

    private static byte[]? ForwardToUpstream(byte[] query)
    {
        try
        {
            using var upstream = new UdpClient();
            upstream.Client.ReceiveTimeout = TimeoutMs;
            upstream.Send(query, query.Length, UpstreamDns, UpstreamPort);
            var ep = new IPEndPoint(IPAddress.Any, 0);
            return upstream.Receive(ref ep);
        }
        catch { return null; }
    }

    /// <summary>
    /// Parses the QNAME from a raw DNS query packet (RFC 1035 §4.1.2).
    /// </summary>
    private static string ExtractQueryDomain(byte[] data)
    {
        try
        {
            // DNS header is 12 bytes; QNAME starts at offset 12
            int i = 12;
            var labels = new List<string>();
            while (i < data.Length)
            {
                int len = data[i++];
                if (len == 0) break;
                if (i + len > data.Length) break;
                labels.Add(System.Text.Encoding.ASCII.GetString(data, i, len));
                i += len;
            }
            return string.Join(".", labels).ToLower();
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Builds a minimal NXDOMAIN response for the given query packet.
    /// Copies the transaction ID and question section, sets RCODE=3.
    /// </summary>
    private static byte[] BuildNxDomain(byte[] query)
    {
        byte[] response = new byte[query.Length];
        Array.Copy(query, response, query.Length);

        // Byte 2-3: Flags — QR=1 (response), AA=0, TC=0, RD=1, RA=1, RCODE=3 (NXDOMAIN)
        // Original flags from query are in bytes 2-3
        response[2] = 0x81; // QR=1, Opcode=0, AA=0, TC=0, RD=1
        response[3] = 0x83; // RA=1, Z=0, RCODE=3 (NXDOMAIN)

        // Answer/Authority/Additional counts all zero
        response[6] = 0; response[7] = 0;
        response[8] = 0; response[9] = 0;
        response[10] = 0; response[11] = 0;

        return response;
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Close(); } catch { }
        try { _loop?.Wait(2000); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _listener?.Dispose();
        _cts?.Dispose();
    }
}
