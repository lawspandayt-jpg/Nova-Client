using System.Net;
using System.Net.Sockets;

namespace NovaClient.Core.Http;

/// <summary>Shared HttpClient (connection pooling) with the launcher's User-Agent.</summary>
public static class HttpProvider
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 16,
            // IPv4-first connect with per-address timeout. Many home networks advertise IPv6
            // routes that silently black-hole traffic; browsers fall back automatically
            // (Happy Eyeballs) but .NET would otherwise hang until HttpClient.Timeout.
            ConnectCallback = ConnectPreferringIPv4Async
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NovaClient-Launcher/1.0");
        return client;
    }

    private static async ValueTask<System.IO.Stream> ConnectPreferringIPv4Async(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var ordered = addresses
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6) // IPv4 first
            .ToList();
        if (ordered.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        Exception? lastError = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5)); // fail fast, move to the next address
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, timeout.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (ct.IsCancellationRequested) throw;
                lastError = ex;
            }
        }
        throw lastError!;
    }
}
