using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>
/// Maintains a WebSocket subscription to Home Assistant entity updates
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

    private readonly ConcurrentDictionary<string, byte> _dirtyEntityIds =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string> _entityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private string _baseUrl = string.Empty;
    private string _accessToken = string.Empty;
    private int _subscriptionId;

    public bool IsRunning { get; private set; }

    public bool IsConnected { get; private set; }

    public string? LastError { get; private set; }

    public string SubscriptionMode { get; private set; } = "none";

    public int DirtyCount => _dirtyEntityIds.Count;

    public void SeedStates(IEnumerable<HomeAssistantEntityState> states, bool markDirty = false)
    {
        int count = 0;
        foreach (var state in states)
        {
            if (string.IsNullOrWhiteSpace(state.EntityId))
            {
                continue;
            }

            _states[state.EntityId] = state;
            if (markDirty)
            {
                _dirtyEntityIds[state.EntityId] = 0;
            }

            count++;
        }

        HomeAssistantPluginLog.Debug($"State cache seeded with {count} entities.");
    }

    public void SetEntityIds(IReadOnlyList<string> entityIds)
    {
        _entityIds = entityIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HomeAssistantPluginLog.Info($"WebSocket stream tracking {_entityIds.Count} entity ids.");
    }

    public IReadOnlyList<HomeAssistantEntityState> GetDirtyStates()
    {
        var states = new List<HomeAssistantEntityState>(_dirtyEntityIds.Count);
        foreach (string entityId in _dirtyEntityIds.Keys)
        {
            if (_states.TryGetValue(entityId, out var state))
            {
                states.Add(state);
            }
        }

        return states;
    }

    /// <summary>Entity ids marked dirty since the last successful apply.</summary>
    public IReadOnlyList<string> GetDirtyEntityIds() => _dirtyEntityIds.Keys.ToList();

    public void MarkAllDirty()
    {
        foreach (string entityId in _entityIds)
        {
            if (_states.ContainsKey(entityId))
            {
                _dirtyEntityIds[entityId] = 0;
            }
        }
    }

    public void ClearDirty(IEnumerable<string> entityIds)
    {
        foreach (string entityId in entityIds)
        {
            _dirtyEntityIds.TryRemove(entityId, out _);
        }
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
        SubscriptionMode = "none";
        HomeAssistantPluginTelemetry.SubscriptionMode = SubscriptionMode;
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

                bool subscribed = await TrySubscribeEntitiesAsync(session, cancellationToken);
                if (!subscribed)
                {
                    _subscriptionId = await session.SubscribeStateChangesAsync(cancellationToken);
                    SubscriptionMode = "state_changed";
                    HomeAssistantPluginLog.Warn("subscribe_entities unavailable; using state_changed fallback.");
                }

                IsConnected = true;
                LastError = null;
                backoff = TimeSpan.FromSeconds(2);
                HomeAssistantPluginTelemetry.SubscriptionMode = SubscriptionMode;
                HomeAssistantPluginLog.Info($"WebSocket connected ({SubscriptionMode}).");

                while (!cancellationToken.IsCancellationRequested &&
                       session.State == WebSocketState.Open)
                {
                    using JsonDocument doc = await session.ReceiveJsonAsync(cancellationToken);
                    ProcessMessage(doc);
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
        SubscriptionMode = "none";
        HomeAssistantPluginTelemetry.SubscriptionMode = SubscriptionMode;
        HomeAssistantPluginLog.Info("WebSocket state stream stopped.");
    }

    private async Task<bool> TrySubscribeEntitiesAsync(
        HomeAssistantWebSocketSession session,
        CancellationToken cancellationToken)
    {
        if (_entityIds.Count == 0)
        {
            return false;
        }

        try
        {
            _subscriptionId = await session.SubscribeEntitiesAsync(_entityIds.ToList(), cancellationToken);
            SubscriptionMode = "subscribe_entities";
            return true;
        }
        catch (Exception ex)
        {
            HomeAssistantPluginLog.Warn($"subscribe_entities failed: {ex.Message}");
            return false;
        }
    }

    private void ProcessMessage(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        string? type = typeElement.GetString();
        if (string.Equals(type, "event", StringComparison.Ordinal))
        {
            if (SubscriptionMode == "subscribe_entities")
            {
                ProcessSubscribeEntitiesEvent(doc.RootElement);
            }
            else
            {
                ProcessStateChangedEvent(doc.RootElement);
            }

            return;
        }

        if (string.Equals(type, "result", StringComparison.Ordinal) &&
            doc.RootElement.TryGetProperty("success", out var successElement) &&
            successElement.GetBoolean() &&
            doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            if (SubscriptionMode == "subscribe_entities")
            {
                ProcessSubscribeEntitiesInitialResult(resultElement);
            }
            else
            {
                HomeAssistantPluginLog.Debug($"WS result: {resultElement}");
            }
        }
    }

    private void ProcessSubscribeEntitiesInitialResult(JsonElement resultElement)
    {
        // Some HA versions may nest the snapshot under result.a instead of a flat map.
        JsonElement snapshotElement = resultElement;
        if (resultElement.TryGetProperty("a", out var additionsElement) &&
            additionsElement.ValueKind == JsonValueKind.Object)
        {
            snapshotElement = additionsElement;
        }

        var changed = HomeAssistantSubscribeEntitiesParser.ApplyInitialSnapshot(snapshotElement, _states);
        int applied = 0;
        foreach (string entityId in changed)
        {
            if (!_entityIds.Contains(entityId))
            {
                continue;
            }

            _dirtyEntityIds[entityId] = 0;
            applied++;
        }

        HomeAssistantPluginLog.Info(
            $"WS subscribe_entities snapshot: {changed.Count} entities, {applied} tracked.");
    }

    private void ProcessSubscribeEntitiesEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventElement))
        {
            HomeAssistantPluginTelemetry.RecordWsMessage(false);
            return;
        }

        var changed = HomeAssistantSubscribeEntitiesParser.ApplyEvent(eventElement, _states);
        foreach (string entityId in changed)
        {
            if (_entityIds.Contains(entityId))
            {
                _dirtyEntityIds[entityId] = 0;
                HomeAssistantPluginLog.Debug($"WS subscribe_entities: {entityId} -> {_states[entityId].State}");
                HomeAssistantPluginTelemetry.RecordWsMessage(true);
            }
            else
            {
                HomeAssistantPluginTelemetry.RecordWsMessage(false);
            }
        }
    }

    private void ProcessStateChangedEvent(JsonElement root)
    {
        if (TryApplyStateChangedEvent(root, out string? entityId, out string? newState))
        {
            HomeAssistantPluginLog.Debug($"WS state_changed: {entityId} -> {newState}");
            HomeAssistantPluginTelemetry.RecordWsMessage(true);
        }
        else
        {
            HomeAssistantPluginTelemetry.RecordWsMessage(false);
        }
    }

    private bool TryApplyStateChangedEvent(JsonElement root, out string? entityId, out string? newStateValue)
    {
        entityId = null;
        newStateValue = null;

        if (!root.TryGetProperty("event", out var eventElement))
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
            _dirtyEntityIds[entityId] = 0;
            newStateValue = "(removed)";
            return true;
        }

        var state = JsonSerializer.Deserialize<HomeAssistantEntityState>(
            newStateElement.GetRawText(),
            StateJsonOptions);

        if (state != null && !string.IsNullOrWhiteSpace(state.EntityId))
        {
            _states[state.EntityId] = state;
            _dirtyEntityIds[state.EntityId] = 0;
            newStateValue = state.State;
            return true;
        }

        return false;
    }
}
