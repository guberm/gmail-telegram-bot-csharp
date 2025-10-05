using System.Net;
using System.Text;
using TelegramGmailBot.Models;

namespace TelegramGmailBot.Services;

public class OAuthCallbackServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly DatabaseService _databaseService;
    private readonly int _port;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    public event EventHandler<OAuthCallbackEventArgs>? CallbackReceived;

    public OAuthCallbackServer(DatabaseService databaseService, int port)
    {
        _databaseService = databaseService;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    public void Start()
    {
        if (_listenerTask != null) return;
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
        Console.WriteLine($"OAuth callback server started on port {_port}");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listenerTask?.Wait(TimeSpan.FromSeconds(5));
        _listenerTask = null;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.WriteLine($"OAuth callback error: {ex.Message}"); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            if (request.Url?.AbsolutePath == "/oauth/callback" && request.HttpMethod == "GET")
            {
                var query = request.Url.Query;
                var code = GetQueryParam(query, "code");
                var state = GetQueryParam(query, "state");
                var error = GetQueryParam(query, "error");
                
                if (!string.IsNullOrEmpty(error))
                {
                    await SendResponseAsync(response, 400, $"<html><body><h1>? Error</h1><p>{error}</p></body></html>");
                    return;
                }
                
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    await SendResponseAsync(response, 400, "<html><body><h1>? Invalid Request</h1></body></html>");
                    return;
                }
                
                var oauthState = _databaseService.GetOAuthState(state);
                if (oauthState == null)
                {
                    await SendResponseAsync(response, 400, "<html><body><h1>? Invalid or Expired</h1></body></html>");
                    return;
                }
                
                _databaseService.DeleteOAuthState(state);
                CallbackReceived?.Invoke(this, new OAuthCallbackEventArgs { Code = code, State = state, ChatId = oauthState.ChatId });
                
                await SendResponseAsync(response, 200, @"
<html>
<head><title>Success</title><style>body{font-family:Arial;text-align:center;padding:50px;}</style></head>
<body>
    <h1 style='color:#4CAF50;'>? Authentication Successful!</h1>
    <p>You can close this window and return to Telegram.</p>
</body>
</html>");
            }
            else { await SendResponseAsync(response, 404, "Not Found"); }
        }
        catch (Exception ex) { Console.WriteLine($"Callback handling error: {ex.Message}"); }
    }

    private string? GetQueryParam(string query, string paramName)
    {
        if (string.IsNullOrEmpty(query)) return null;
        var pairs = query.TrimStart('?').Split('&');
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length == 2 && parts[0] == paramName)
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }

    private async Task SendResponseAsync(HttpListenerResponse response, int statusCode, string content)
    {
        response.StatusCode = statusCode;
        response.ContentType = content.Contains("<html>") ? "text/html" : "text/plain";
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _cts?.Dispose();
    }
}

public class OAuthCallbackEventArgs : EventArgs
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public long ChatId { get; set; }
}
