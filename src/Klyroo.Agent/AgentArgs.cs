using System.Globalization;
using System.Text.Json;

namespace Klyroo.Agent;

/// <summary>Reads the arguments of an agent step. They arrive as JSON values, from a language model or from <see cref="AgentArgument"/>.</summary>
internal static class AgentArgs
{
    /// <summary>Whether the value is an object, or a list that holds one. No action takes an object.</summary>
    public static bool ContainsObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => true,
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsObject),
        _ => false,
    };

    /// <summary>Whether the value is a JSON number.</summary>
    public static bool IsNumber(JsonElement element) => element.ValueKind == JsonValueKind.Number;

    /// <summary>
    /// The text of a text or number argument: a number keeps the digits as sent, so <c>12345678901234567890</c> stays that
    /// text. <see langword="null"/> for <c>null</c>, a boolean, a list or an object.
    /// </summary>
    public static string? TextOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        _ => null,
    };

    /// <summary>Reads the argument at <paramref name="index"/> as text, or <see langword="null"/> when it is missing, empty or not text or a number.</summary>
    public static string? Text(IReadOnlyList<JsonElement> args, int index) => index < args.Count ? TextOf(args[index]) : null;

    /// <summary>The text of any scalar argument as .NET would print it, and an empty text for <c>null</c>. A list or an object gives its JSON text.</summary>
    public static string ScalarText(JsonElement element) => element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : element.ToString();

    /// <summary>Reads a whole number that may be written <c>25.0</c> or as numeric text.</summary>
    public static bool TryInt(JsonElement element, out int result)
    {
        result = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var number) && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
                {
                    result = (int)number;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                return int.TryParse(element.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }
}
