using System.Text.Json;
using System.Text.Json.Serialization;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Applies Home Assistant subscribe_entities compressed state payloads.</summary>
public static class HomeAssistantSubscribeEntitiesParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Applies the initial subscribe_entities result snapshot to the entity cache.
    /// </summary>
    /// <returns>Entity ids seeded from the snapshot.</returns>
    public static IReadOnlyList<string> ApplyInitialSnapshot(
        JsonElement resultElement,
        IDictionary<string, HomeAssistantEntityState> cache)
    {
        if (resultElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var changed = new List<string>();
        foreach (var property in resultElement.EnumerateObject())
        {
            var state = DeserializeCompressed(property.Name, property.Value);
            if (state == null)
            {
                continue;
            }

            cache[property.Name] = state;
            changed.Add(property.Name);
        }

        return changed;
    }

    /// <summary>
    /// Applies a subscribe_entities event payload to the entity cache.
    /// </summary>
    /// <returns>Entity ids that changed.</returns>
    public static IReadOnlyList<string> ApplyEvent(
        JsonElement eventElement,
        IDictionary<string, HomeAssistantEntityState> cache)
    {
        var changed = new List<string>();

        if (eventElement.TryGetProperty("a", out var additionsElement))
        {
            changed.AddRange(ApplyAdditions(additionsElement, cache));
        }

        if (eventElement.TryGetProperty("r", out var removalsElement) &&
            removalsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in removalsElement.EnumerateArray())
            {
                string? entityId = item.GetString();
                if (!string.IsNullOrWhiteSpace(entityId) && cache.Remove(entityId))
                {
                    changed.Add(entityId);
                }
            }
        }

        if (eventElement.TryGetProperty("c", out var changesElement))
        {
            changed.AddRange(ApplyChanges(changesElement, cache));
        }

        return changed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ApplyAdditions(
        JsonElement additionsElement,
        IDictionary<string, HomeAssistantEntityState> cache)
    {
        if (additionsElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in additionsElement.EnumerateObject())
        {
            var state = DeserializeCompressed(property.Name, property.Value);
            if (state == null)
            {
                continue;
            }

            cache[property.Name] = state;
            yield return property.Name;
        }
    }

    private static IEnumerable<string> ApplyChanges(
        JsonElement changesElement,
        IDictionary<string, HomeAssistantEntityState> cache)
    {
        if (changesElement.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in changesElement.EnumerateObject())
        {
            string entityId = property.Name;
            if (!cache.TryGetValue(entityId, out var existing))
            {
                var created = DeserializeCompressed(entityId, property.Value);
                if (created != null)
                {
                    cache[entityId] = created;
                    yield return entityId;
                }

                continue;
            }

            if (ApplyDiff(property.Value, existing))
            {
                yield return entityId;
            }
        }
    }

    private static bool ApplyDiff(JsonElement diffElement, HomeAssistantEntityState existing)
    {
        bool changed = false;

        if (diffElement.TryGetProperty("+", out var addElement))
        {
            var partial = JsonSerializer.Deserialize<HomeAssistantCompressedEntityState>(
                addElement.GetRawText(),
                JsonOptions);

            if (partial?.State != null)
            {
                existing.State = partial.State;
                changed = true;
            }

            if (partial?.Attributes != null)
            {
                existing.Attributes ??= new HomeAssistantAttributes();
                foreach (var (key, value) in partial.Attributes)
                {
                    ApplyAttribute(existing.Attributes, key, value);
                }

                changed = true;
            }
        }
        else if (diffElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in diffElement.EnumerateObject())
            {
                if (property.NameEquals("s"))
                {
                    existing.State = property.Value.GetString() ?? existing.State;
                    changed = true;
                }
                else if (property.NameEquals("a") && property.Value.ValueKind == JsonValueKind.Object)
                {
                    existing.Attributes ??= new HomeAssistantAttributes();
                    foreach (var attr in property.Value.EnumerateObject())
                    {
                        ApplyAttribute(existing.Attributes, attr.Name, attr.Value);
                    }

                    changed = true;
                }
            }
        }

        if (diffElement.TryGetProperty("-", out var removeElement))
        {
            var removal = JsonSerializer.Deserialize<HomeAssistantEntityStateDiffRemove>(
                removeElement.GetRawText(),
                JsonOptions);

            if (removal?.Attributes != null && existing.Attributes != null)
            {
                foreach (string key in removal.Attributes)
                {
                    RemoveAttribute(existing.Attributes, key);
                }

                changed = true;
            }
        }

        return changed;
    }

    private static HomeAssistantEntityState? DeserializeCompressed(string entityId, JsonElement element)
    {
        if (element.TryGetProperty("s", out _))
        {
            var compressed = JsonSerializer.Deserialize<HomeAssistantCompressedEntityState>(
                element.GetRawText(),
                JsonOptions);

            if (compressed?.State == null)
            {
                return null;
            }

            return new HomeAssistantEntityState
            {
                EntityId = entityId,
                State = compressed.State,
                Attributes = ConvertAttributes(compressed.Attributes),
            };
        }

        if (element.TryGetProperty("+", out var addElement))
        {
            var partial = JsonSerializer.Deserialize<HomeAssistantCompressedEntityState>(
                addElement.GetRawText(),
                JsonOptions);

            if (partial?.State == null)
            {
                return null;
            }

            return new HomeAssistantEntityState
            {
                EntityId = entityId,
                State = partial.State,
                Attributes = ConvertAttributes(partial.Attributes),
            };
        }

        return null;
    }

    private static HomeAssistantAttributes? ConvertAttributes(Dictionary<string, JsonElement>? attributes)
    {
        if (attributes == null || attributes.Count == 0)
        {
            return null;
        }

        var result = new HomeAssistantAttributes();
        foreach (var (key, value) in attributes)
        {
            ApplyAttribute(result, key, value);
        }

        return result;
    }

    private static void ApplyAttribute(HomeAssistantAttributes attributes, string key, JsonElement value)
    {
        switch (key)
        {
            case "friendly_name":
                attributes.FriendlyName = value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();
                break;
            case "unit_of_measurement":
                attributes.UnitOfMeasurement = value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();
                break;
        }
    }

    private static void RemoveAttribute(HomeAssistantAttributes attributes, string key)
    {
        switch (key)
        {
            case "friendly_name":
                attributes.FriendlyName = null;
                break;
            case "unit_of_measurement":
                attributes.UnitOfMeasurement = null;
                break;
        }
    }
}
