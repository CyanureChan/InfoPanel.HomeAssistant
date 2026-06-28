using System.Text.Json;

namespace InfoPanel.HomeAssistant.Plugin;

internal static class PluginConfigCoercion
{
    public static int ToInt(object? value, int fallback)
    {
        switch (value)
        {
            case int intValue:
                return intValue;
            case long longValue when longValue >= int.MinValue && longValue <= int.MaxValue:
                return (int)longValue;
            case double doubleValue when doubleValue >= int.MinValue && doubleValue <= int.MaxValue:
                return (int)Math.Round(doubleValue);
            case float floatValue when floatValue >= int.MinValue && floatValue <= int.MaxValue:
                return (int)Math.Round(floatValue);
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetInt32(out int jsonInt)
                    ? jsonInt
                    : element.TryGetInt64(out long jsonLong) &&
                      jsonLong >= int.MinValue &&
                      jsonLong <= int.MaxValue
                        ? (int)jsonLong
                        : fallback;
            case JsonElement { ValueKind: JsonValueKind.String } stringElement:
                return int.TryParse(stringElement.GetString(), out int parsedFromJsonString)
                    ? parsedFromJsonString
                    : fallback;
            default:
                return int.TryParse(value?.ToString(), out int parsed) ? parsed : fallback;
        }
    }
}
