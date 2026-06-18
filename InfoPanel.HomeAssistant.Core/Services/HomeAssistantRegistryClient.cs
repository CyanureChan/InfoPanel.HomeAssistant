using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

public sealed class HomeAssistantRegistryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<HomeAssistantRegistrySnapshot> FetchAsync(
        string baseUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
        {
            return HomeAssistantRegistrySnapshot.Unavailable("Not configured");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using var webSocket = new ClientWebSocket();
            webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

            Uri uri = BuildWebSocketUri(baseUrl);
            await webSocket.ConnectAsync(uri, timeoutCts.Token);

            await WaitForAuthRequiredAsync(webSocket, timeoutCts.Token);
            await SendAuthAsync(webSocket, accessToken, timeoutCts.Token);
            await WaitForAuthOkAsync(webSocket, timeoutCts.Token);

            var entities = await SendListCommandAsync<EntityRegistryEntry>(
                webSocket,
                "config/entity_registry/list",
                1,
                timeoutCts.Token);

            var devices = await SendListCommandAsync<DeviceRegistryEntry>(
                webSocket,
                "config/device_registry/list",
                2,
                timeoutCts.Token);

            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }

            return new HomeAssistantRegistrySnapshot
            {
                IsAvailable = true,
                Entities = entities,
                Devices = devices,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HomeAssistantRegistrySnapshot.Unavailable(ex.Message);
        }
    }

    private static Uri BuildWebSocketUri(string baseUrl)
    {
        var builder = new UriBuilder(baseUrl.Trim().TrimEnd('/'));
        builder.Scheme = builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
        builder.Path = $"{builder.Path.TrimEnd('/')}/api/websocket";
        return builder.Uri;
    }

    private static async Task WaitForAuthRequiredAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await ReceiveJsonAsync(webSocket, cancellationToken);
        string? type = doc.RootElement.GetProperty("type").GetString();
        if (!string.Equals(type, "auth_required", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected auth_required, got {type ?? "null"}.");
        }
    }

    private static async Task SendAuthAsync(ClientWebSocket webSocket, string accessToken, CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new { type = "auth", access_token = accessToken });
        await SendTextAsync(webSocket, payload, cancellationToken);
    }

    private static async Task WaitForAuthOkAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        using JsonDocument doc = await ReceiveJsonAsync(webSocket, cancellationToken);
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

    private static async Task<IReadOnlyList<T>> SendListCommandAsync<T>(
        ClientWebSocket webSocket,
        string commandType,
        int messageId,
        CancellationToken cancellationToken)
    {
        string payload = JsonSerializer.Serialize(new { id = messageId, type = commandType });
        await SendTextAsync(webSocket, payload, cancellationToken);

        using JsonDocument doc = await ReceiveJsonAsync(webSocket, cancellationToken);
        if (!doc.RootElement.TryGetProperty("success", out var successElement) ||
            !successElement.GetBoolean())
        {
            string error = doc.RootElement.TryGetProperty("error", out var errorElement)
                ? errorElement.ToString()
                : "unknown error";
            throw new InvalidOperationException($"Home Assistant registry command failed: {error}");
        }

        if (!doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<T>>(resultElement.GetRawText(), JsonOptions) ?? [];
    }

    private static async Task SendTextAsync(ClientWebSocket webSocket, string payload, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket webSocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("WebSocket closed before a response was received.");
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
}
