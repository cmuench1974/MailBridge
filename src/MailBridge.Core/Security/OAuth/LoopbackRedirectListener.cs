using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace MailBridge.Core.Security.OAuth;

/// <summary>What the browser redirect delivered: either a code (with state) or an error.</summary>
public sealed record AuthorizationResponse(string? Code, string? State, string? Error, string? ErrorDescription);

/// <summary>Texts of the page the browser shows after the redirect (already localized by the caller).</summary>
public sealed record BrowserPageText(string Title, string SuccessMessage, string FailureMessage);

/// <summary>
/// Receives the OAuth2 redirect on http://localhost:&lt;free port&gt; (RFC 8252 loopback
/// redirect for native apps). The redirect URI has no trailing slash so it matches a
/// registered "http://localhost" exactly; Microsoft ignores the port for localhost.
/// </summary>
public sealed class LoopbackRedirectListener : IDisposable
{
    private readonly HttpListener _listener;

    private LoopbackRedirectListener(HttpListener listener, int port)
    {
        _listener = listener;
        RedirectUri = $"http://localhost:{port}";
    }

    public string RedirectUri { get; }

    public static LoopbackRedirectListener Start()
    {
        // HttpListener cannot choose a free port itself: ask the OS for one, then bind it.
        // Another process can grab the port in between, so retry a few times.
        for (var attempt = 1; ; attempt++)
        {
            var port = GetFreeLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return new LoopbackRedirectListener(listener, port);
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                listener.Close();
            }
        }
    }

    /// <summary>
    /// Waits for the redirect that carries a code or an error and answers it with a small page.
    /// Other requests (e.g. /favicon.ico) get a 404 and are ignored.
    /// </summary>
    public async Task<AuthorizationResponse> WaitForResponseAsync(BrowserPageText page, CancellationToken cancellationToken)
    {
        // GetContextAsync has no cancellation support; stopping the listener aborts it.
        using var registration = cancellationToken.Register(() => _listener.Stop());

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var query = HttpUtility.ParseQueryString(context.Request.Url?.Query ?? string.Empty);
            var code = query["code"];
            var error = query["error"];
            if (code is null && error is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            await WritePageAsync(context.Response, page, success: error is null).ConfigureAwait(false);
            return new AuthorizationResponse(code, query["state"], error, query["error_description"]);
        }
    }

    public void Dispose() => _listener.Close();

    private static int GetFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static async Task WritePageAsync(HttpListenerResponse response, BrowserPageText page, bool success)
    {
        var message = success ? page.SuccessMessage : page.FailureMessage;
        var html = $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(page.Title)}}</title>
            <style>body{font-family:"Segoe UI",sans-serif;margin:3rem auto;max-width:32rem;padding:0 1rem;color:#1f2937}
            @media (prefers-color-scheme:dark){ body{background:#111827;color:#e5e7eb} }</style></head>
            <body><h1>{{WebUtility.HtmlEncode(page.Title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        try
        {
            await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            response.Close();
        }
    }
}
