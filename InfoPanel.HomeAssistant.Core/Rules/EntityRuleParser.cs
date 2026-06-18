using System.Text;

namespace InfoPanel.HomeAssistant.Core.Rules;

/// <summary>Parses comma- or newline-separated entity selection rule strings.</summary>
internal static class EntityRuleParser
{
    /// <summary>
    /// Parses a rules string into individual <see cref="EntityRule"/> entries.
    /// </summary>
    /// <param name="rulesText">Raw include or exclude text from configuration.</param>
    /// <returns>Parsed rules; empty when input is blank.</returns>
    public static IReadOnlyList<EntityRule> ParseRules(string? rulesText)
    {
        if (string.IsNullOrWhiteSpace(rulesText))
        {
            return [];
        }

        return Tokenize(rulesText)
            .Select(ParseRule)
            .ToList();
    }

    /// <summary>
    /// Splits input on commas and newlines while respecting quoted segments.
    /// </summary>
    /// <param name="input">Raw rule text.</param>
    /// <returns>Trimmed tokens; lines starting with <c>#</c> are ignored.</returns>
    public static IReadOnlyList<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inDoubleQuotes = false;
        bool inSingleQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                current.Append(c);
                continue;
            }

            if (c == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                current.Append(c);
                continue;
            }

            if (!inDoubleQuotes && !inSingleQuotes && (c == ',' || c == '\r' || c == '\n'))
            {
                AddToken(tokens, current);
                continue;
            }

            current.Append(c);
        }

        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        string token = current.ToString().Trim();
        current.Clear();

        if (string.IsNullOrWhiteSpace(token) || token.StartsWith('#'))
        {
            return;
        }

        tokens.Add(token);
    }

    private static EntityRule ParseRule(string token)
    {
        if (string.Equals(token, "*", StringComparison.Ordinal))
        {
            return new EntityRule { Kind = EntityRuleKind.All, Tier = EntityRuleTier.General };
        }

        int separator = IndexOfPrefixSeparator(token);
        if (separator <= 0 || separator == token.Length - 1)
        {
            return new EntityRule
            {
                Kind = EntityRuleKind.Entity,
                Tier = EntityRuleTier.Explicit,
                Value = Unquote(token),
            };
        }

        string prefix = token[..separator].ToLowerInvariant();
        string value = Unquote(token[(separator + 1)..]);

        return prefix switch
        {
            "entity" => new EntityRule { Kind = EntityRuleKind.Entity, Tier = EntityRuleTier.Explicit, Value = value },
            "device" => new EntityRule { Kind = EntityRuleKind.Device, Tier = EntityRuleTier.Explicit, Value = value },
            "integration" => new EntityRule { Kind = EntityRuleKind.Integration, Tier = EntityRuleTier.Explicit, Value = value },
            "domain" => new EntityRule { Kind = EntityRuleKind.Domain, Tier = EntityRuleTier.General, Value = value },
            _ => new EntityRule { Kind = EntityRuleKind.Entity, Tier = EntityRuleTier.Explicit, Value = Unquote(token) },
        };
    }

    private static int IndexOfPrefixSeparator(string token)
    {
        bool inDoubleQuotes = false;
        bool inSingleQuotes = false;

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (c == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (c == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (!inDoubleQuotes && !inSingleQuotes && c == '.')
            {
                return i;
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 &&
            ((value.StartsWith('"') && value.EndsWith('"')) ||
             (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }

        return value;
    }
}
