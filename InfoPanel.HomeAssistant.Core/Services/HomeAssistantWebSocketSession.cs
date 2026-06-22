using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Authenticated Home Assistant WebSocket session for send/receive.</summary>
internal sealed class HomeAssistantWebSocketSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ClientWebSocket _webSocket = new();
    private int _nextMessageId = 1;

    public WebSocketState State => _webSocket.State;

    public async Task ConnectAndAuthenticateAsync(
        string baseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        await _webSocket.ConnectAsync(BuildWebSocketUri(baseUrl), cancellationToken);

        await WaitForMessageTypeAsync("auth_required", cancellationToken);
        await SendJsonAsync(new { type = "auth", access_token = accessToken }, cancellationToken);
        await WaitForAuthResultAsync(cancellationToken);
    }

    public async Task<int> SubscribeStateChangesAsync(CancellationToken cancellationToken)
    {
        int id = _nextMessageId++;
        await SendJsonAsync(new { id, type = "subscribe_events", event_type = "state_changed" }, cancellationToken);
        await WaitForResultSuccessAsync(id, cancellationToken);
        return id;
    }

    public async Task<JsonDocument> ReceiveJsonAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        using var ms = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed by Home Assistant.");
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        ms.Position = 0;
        return await JsonDocument.ParseAsync(ms, cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_webSocket.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "closing",
                    CancellationToken.None);
            }
            catch
            {
            }
        }

        _webSocket.Dispose();
    }

    private async Task WaitForMessageTypeAsync(string expectedType, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await ReceiveJsonAsync(cancellationToken);
        string? type = doc.RootElement.GetProperty("type").GetString();
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected {expectedType}, got {type ?? "null"}.");
        }
    }

    private async Task WaitForAuthResultAsync(CancellationToken cancellationToken)
    {
        using JsonDocument doc = await ReceiveJsonAsync(cancellationToken);
        string? type = doc.RootElement.GetProperty("type").GetString();
        if (string.Equals(type, "auth_invalid", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Home Assistant rejected the access token.");
        }

        if (!string.Equals(type, "auth_ok", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected auth_ok, got {type ?? "null"}.");
        }
    }

    private async Task WaitForResultSuccessAsync(int messageId, CancellationToken cancellationToken)
    {
        while (true)
        {
            using JsonDocument doc = await ReceiveJsonAsync(cancellationToken);
            if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                continue;
            }

            string? type = typeElement.GetString();
            if (!string.Equals(type, "result", StringComparison.Ordinal))
            {
                continue;
            }

            if (!doc.RootElement.TryGetProperty("id", out var idElement) ||
                idElement.GetInt32() != messageId)
            {
                continue;
            }

            if (!doc.RootElement.TryGetProperty("success", out var successElement) ||
                !successElement.GetBoolean())
            {
                string error = doc.RootElement.TryGetProperty("error", out var errorElement)
                    ? errorElement.ToString()
                    : "unknown error";
                throw new InvalidOperationException($"Home Assistant command failed: {error}");
            }

            return;
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var builder = new UriBuilder(baseUrl.Trim().TrimEnd('/'));
        builder.Scheme = builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        builder.Path = $"{builder.Path.TrimEnd('/')}/api/websocket";
        return builder.Uri;
    }
}
