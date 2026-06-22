using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>
/// Maintains a WebSocket subscription to Home Assistant state_changed events
/// and caches states for selected entity ids.
/// </summary>
public sealed class HomeAssistantStateStream : IAsyncDisposable
{
    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConcurrentDictionary<string, HomeAssistantEntityState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> _entityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private string _baseUrl = string.Empty;
    private string _accessToken = string.Empty;

    public bool IsRunning { get; private set; }

    public bool IsConnected { get; private set; }

    public string? LastError { get; private set; }

    public void SeedStates(IEnumerable<HomeAssistantEntityState> states)
    {
        foreach (var state in states)
        {
            if (!string.IsNullOrWhiteSpace(state.EntityId))
            {
                _states[state.EntityId] = state;
            }
        }
    }

    public void SetEntityIds(IReadOnlyList<string> entityIds)
    {
        _entityIds = entityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<HomeAssistantEntityState> GetSelectedStates()
    {
        return _entityIds
            .Where(_states.ContainsKey)
            .Select(id => _states[id])
            .OrderBy(s => s.EntityId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task StartAsync(string baseUrl, string accessToken, CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            StopInternal();
            _baseUrl = baseUrl.Trim().TrimEnd('/');
            _accessToken = accessToken.Trim();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
            IsRunning = true;
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            StopInternal();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        Task? task;
        lock (_lifecycleLock)
        {
            task = _runTask;
        }

        if (task != null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private void StopInternal()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        IsRunning = false;
        IsConnected = false;
        _runTask = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(2);

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var session = new HomeAssistantWebSocketSession();
            try
            {
                await session.ConnectAndAuthenticateAsync(_baseUrl, _accessToken, cancellationToken);
                await session.SubscribeStateChangesAsync(cancellationToken);
                IsConnected = true;
                LastError = null;
                backoff = TimeSpan.FromSeconds(2);

                while (!cancellationToken.IsCancellationRequested &&
                       session.State == WebSocketState.Open)
                {
                    using JsonDocument doc = await session.ReceiveJsonAsync(cancellationToken);
                    TryApplyStateChangedEvent(doc);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                IsConnected = false;
                LastError = ex.Message;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(backoff, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
        }

        IsConnected = false;
        IsRunning = false;
    }

    private void TryApplyStateChangedEvent(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("type", out var typeElement) ||
            !string.Equals(typeElement.GetString(), "event", StringComparison.Ordinal))
        {
            return;
        }

        if (!doc.RootElement.TryGetProperty("event", out var eventElement))
        {
            return;
        }

        if (!eventElement.TryGetProperty("event_type", out var eventTypeElement) ||
            !string.Equals(eventTypeElement.GetString(), "state_changed", StringComparison.Ordinal))
        {
            return;
        }

        if (!eventElement.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("entity_id", out var entityIdElement))
        {
            return;
        }

        string? entityId = entityIdElement.GetString();
        if (string.IsNullOrWhiteSpace(entityId) || !_entityIds.Contains(entityId))
        {
            return;
        }

        if (!dataElement.TryGetProperty("new_state", out var newStateElement) ||
            newStateElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            _states.TryRemove(entityId, out _);
            return;
        }

        var state = JsonSerializer.Deserialize<HomeAssistantEntityState>(
            newStateElement.GetRawText(),
            StateJsonOptions);

        if (state != null && !string.IsNullOrWhiteSpace(state.EntityId))
        {
            _states[state.EntityId] = state;
        }
    }
}
