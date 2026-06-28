using InfoPanel.HomeAssistant.Core.Configuration;
using InfoPanel.HomeAssistant.Core.Mapping;
using InfoPanel.HomeAssistant.Core.Models;
using InfoPanel.Plugins;

namespace InfoPanel.HomeAssistant.Plugin.Layout;

/// <summary>Maps a Home Assistant entity to InfoPanel sensor or text entries.</summary>
internal sealed class EntityEntry
{
    /// <summary>Full Home Assistant entity id.</summary>
    public required string EntityId { get; init; }

    /// <summary>Plugin data entry registered with the host.</summary>
    public required IPluginData Data { get; init; }

    /// <summary>Numeric sensor entry, when the entity domain is numeric.</summary>
    public PluginSensor? Sensor { get; init; }

    /// <summary>Text entry, when the entity domain is state-based.</summary>
    public PluginText? Text { get; init; }

    /// <summary>
    /// Creates a plugin entry for the given entity state.
    /// </summary>
    /// <param name="state">Home Assistant entity state.</param>
    /// <returns>Sensor or text entry wired to the entity id.</returns>
    public static EntityEntry Create(HomeAssistantEntityState state)
    {
        string entryId = EntityIdHelper.ToEntryId(state.EntityId);
        string displayName = state.DisplayName;

        if (ExposableEntityDomains.IsTextDomain(state.EntityId))
        {
            var text = new PluginText(entryId, displayName, "-");
            return new EntityEntry
            {
                EntityId = state.EntityId,
                Data = text,
                Text = text,
            };
        }

        string? unit = state.Attributes?.UnitOfMeasurement;
        var sensor = new PluginSensor(entryId, displayName, 0, unit);
        return new EntityEntry
        {
            EntityId = state.EntityId,
            Data = sensor,
            Sensor = sensor,
        };
    }

    /// <summary>
    /// Sets the entry value from discovery without skipping unchanged defaults.
    /// </summary>
    public void ApplyInitialState(HomeAssistantEntityState state)
    {
        if (Text != null)
        {
            Text.Value = state.State;
            return;
        }

        if (Sensor != null && EntityStateMapper.TryParseNumeric(state.State, out float value))
        {
            Sensor.Value = value;
        }
    }

    /// <summary>
    /// Updates the plugin entry from a fresh Home Assistant state.
    /// </summary>
    /// <param name="state">Current entity state.</param>
    /// <returns>True when a displayed value changed.</returns>
    public bool ApplyState(HomeAssistantEntityState state)
    {
        if (EntityStateMapper.IsUnavailable(state.State))
        {
            if (Text != null && !string.Equals(Text.Value, state.State, StringComparison.Ordinal))
            {
                Text.Value = state.State;
                return true;
            }

            return false;
        }

        if (Sensor != null)
        {
            if (EntityStateMapper.IsUnavailable(state.State))
            {
                return false;
            }

            if (EntityStateMapper.TryParseNumeric(state.State, out float value) &&
                Math.Abs(Sensor.Value - value) > 0.0001f)
            {
                Sensor.Value = value;
                return true;
            }

            return false;
        }

        if (Text != null && !string.Equals(Text.Value, state.State, StringComparison.Ordinal))
        {
            Text.Value = state.State;
            return true;
        }

        return false;
    }
}
