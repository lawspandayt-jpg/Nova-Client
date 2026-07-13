using System.Net;

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
            MaxConnectionsPerServer = 16
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NovaClient-Launcher/1.0");
        return client;
    }
}
