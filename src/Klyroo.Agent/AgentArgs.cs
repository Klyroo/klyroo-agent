using System.Text.Json;

namespace Klyroo.Agent;

/// <summary>Reads the arguments of an agent step, which arrive as <see cref="JsonElement"/> from an LLM or MCP host and as plain values from .NET callers.</summary>
internal static class AgentArgs
{
    /// <summary>Turns a <see cref="JsonElement"/> into plain CLR values (string, double, bool, null, List of those) and leaves any other value alone. An object, which no action takes, sets <paramref name="error"/>.</summary>
    public static object? Unwrap(object? raw, out string? error)
    {
        error = null;
        if (raw is not JsonElement element) return raw;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Number:
                return element.TryGetDouble(out var number) ? number : null;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    var inner = Unwrap(item, out error);
                    if (error is not null) return null;
                    list.Add(inner);
                }
                return list;
            default:
                error = "An object is not a valid value.";
                return null;
        }
    }

    /// <summary>Reads a whole number that may arrive as an int, a double without fraction, or numeric text.</summary>
    public static bool TryInt(object? arg, out int result)
    {
        result = 0;
        switch (arg)
        {
            case int i:
                result = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                result = (int)l;
                return true;
            case double d when d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                result = (int)d;
                return true;
            case string s:
                return int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    /// <summary>Reads the argument at <paramref name="index"/> as text, or <see langword="null"/> when it is missing, empty or not text or a number.</summary>
    public static string? Text(IReadOnlyList<object?> args, int index) => index < args.Count
        ? args[index] switch
        {
            string s => s,
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        }
        : null;
}
