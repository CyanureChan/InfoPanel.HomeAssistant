using System.Globalization;
using InfoPanel.HomeAssistant.Core.Models;

namespace InfoPanel.HomeAssistant.Core.Mapping;

public static class EntityStateMapper
{
    public static bool IsUnavailable(string state) =>
        state is "unavailable" or "unknown";

    public static bool TryParseNumeric(string state, out float value) =>
        float.TryParse(state, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
