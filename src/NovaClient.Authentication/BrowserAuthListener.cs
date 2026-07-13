using System.Diagnostics;
using System.Net;
using System.Text;
using NovaClient.Core.Logging;

namespace NovaClient.Authentication;

/// <summary>
/// Fallback when WebView2 is unavailable: opens the system browser at Microsoft's sign-in page
/// and receives the redirect on a loopback HTTP listener (RFC 8252 native-app flow).
/// The Azure app registration must include "http://localhost" style redirect URIs.
/// </summary>
public sealed class BrowserAuthListener : IDisposable
{
    private readonly HttpListener _listener = new();

    public string RedirectUri { get; }

    public BrowserAuthListener()
    {
        var port = FindFreePort();
        RedirectUri = $"http://127.0.0.1:{port}/callback/";
        _listener.Prefixes.Add(RedirectUri);
    }

    public static void OpenInSystemBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new AuthException(AuthErrorKind.BrowserUnavailable, "Could not open the system browser.", ex);
        }
    }

    /// <summary>Waits for Microsoft to redirect back; returns the full redirect URI.</summary>
    public async Task<Uri> WaitForCallbackAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        _listener.Start();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            var contextTask = _listener.GetContextAsync();
            var context = await contextTask.WaitAsync(timeoutCts.Token);
            var uri = context.Request.Url!;

            var html = "<html><body style=\"font-family:Segoe UI;background:#111;color:#eee;text-align:center;padding-top:120px\">"
                       + "<h2>Sign-in received</h2><p>You can close this tab and return to the launcher.</p></body></html>";
            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, ct);
            context.Response.Close();
            return uri;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AuthException(AuthErrorKind.UserCancelled, "Timed out waiting for the browser sign-in.");
        }
        finally
        {
            try { _listener.Stop(); } catch { }
        }
    }

    private static int FindFreePort()
    {
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        NovaLog.Debug("BrowserAuth", $"Loopback redirect on port {port}.");
        return port;
    }

    public void Dispose()
    {
        try { ((IDisposable)_listener).Dispose(); } catch { }
    }
}
