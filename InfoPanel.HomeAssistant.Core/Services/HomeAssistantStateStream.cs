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
        int count = 0;
        foreach (var state in states)
        {
            if (!string.IsNullOrWhiteSpace(state.EntityId))
            {
                _states[state.EntityId] = state;
                count++;
            }
        }

        HomeAssistantPluginLog.Debug($"State cache seeded with {count} entities.");
    }

    public void SetEntityIds(IReadOnlyList<string> entityIds)
    {
        _entityIds = entityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HomeAssistantPluginLog.Info($"WebSocket stream tracking {_entityIds.Count} entity ids.");
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

        HomeAssistantPluginLog.Info($"Starting WebSocket state stream to {_baseUrl}.");
        return Task.CompletedTask;
    }

    public void Stop()
    {
        lock (_lifecycleLock)
        {
            if (IsRunning || IsConnected)
            {
                HomeAssistantPluginLog.Info("Stopping WebSocket state stream.");
            }

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
                HomeAssistantPluginLog.Debug("Connecting WebSocket and authenticating...");
                await session.ConnectAndAuthenticateAsync(_baseUrl, _accessToken, cancellationToken);
                await session.SubscribeStateChangesAsync(cancellationToken);
                IsConnected = true;
                LastError = null;
                backoff = TimeSpan.FromSeconds(2);
                HomeAssistantPluginLog.Info("WebSocket connected and subscribed to state_changed.");

                while (!cancellationToken.IsCancellationRequested &&
                       session.State == WebSocketState.Open)
                {
                    using JsonDocument doc = await session.ReceiveJsonAsync(cancellationToken);
                    if (TryApplyStateChangedEvent(doc, out string? entityId, out string? newState))
                    {
                        HomeAssistantPluginLog.Debug($"WS state_changed: {entityId} -> {newState}");
                    }
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
                HomeAssistantPluginLog.Error(ex, "WebSocket stream error");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            HomeAssistantPluginLog.Warn($"WebSocket reconnecting in {backoff.TotalSeconds:0}s...");
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
        HomeAssistantPluginLog.Info("WebSocket state stream stopped.");
    }

    private bool TryApplyStateChangedEvent(JsonDocument doc, out string? entityId, out string? newStateValue)
    {
        entityId = null;
        newStateValue = null;
        if (!doc.RootElement.TryGetProperty("type", out var typeElement) ||
            !string.Equals(typeElement.GetString(), "event", StringComparison.Ordinal))
        {
            return false;
        }

        if (!doc.RootElement.TryGetProperty("event", out var eventElement))
        {
            return false;
        }

        if (!eventElement.TryGetProperty("event_type", out var eventTypeElement) ||
            !string.Equals(eventTypeElement.GetString(), "state_changed", StringComparison.Ordinal))
        {
            return false;
        }

        if (!eventElement.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("entity_id", out var entityIdElement))
        {
            return false;
        }

        entityId = entityIdElement.GetString();
        if (string.IsNullOrWhiteSpace(entityId) || !_entityIds.Contains(entityId))
        {
            return false;
        }

        if (!dataElement.TryGetProperty("new_state", out var newStateElement) ||
            newStateElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            _states.TryRemove(entityId, out _);
            newStateValue = "(removed)";
            return true;
        }

        var state = JsonSerializer.Deserialize<HomeAssistantEntityState>(
            newStateElement.GetRawText(),
            StateJsonOptions);

        if (state != null && !string.IsNullOrWhiteSpace(state.EntityId))
        {
            _states[state.EntityId] = state;
            newStateValue = state.State;
            return true;
        }

        return false;
    }
}
