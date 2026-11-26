using System.Globalization;
using System.Text.Json;

namespace Klyroo.Agent;

/// <summary>
/// A JSON number that fits neither <see cref="long"/> nor <see cref="decimal"/>, such as 12345678901234567890123456789012.
/// It keeps the digits as sent, so a text field or a spreadsheet cell gets them exactly; a numeric field reads
/// <see cref="Approximate"/>, which is infinite when the number is out of the range of a <see cref="double"/>.
/// </summary>
internal sealed record RawNumber(string Text, double Approximate);

/// <summary>Reads the arguments of an agent step, which arrive as <see cref="JsonElement"/> from an LLM or MCP host and as plain values from .NET callers.</summary>
internal static class AgentArgs
{
    /// <summary>
    /// Turns a <see cref="JsonElement"/> into plain CLR values: text, <see langword="bool"/>, <see langword="null"/>, a list of
    /// those, and numbers as <see cref="long"/> when the token is an integer in range, else <see cref="decimal"/> when
    /// it reproduces the token exactly (which keeps every digit and the scale, so <c>1.50</c> stays <c>1.50</c>), else <see cref="RawNumber"/>.
    /// Any other value is left alone. An object, which no action takes, sets <paramref name="error"/>.
    /// </summary>
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
                return UnwrapNumber(element);
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

    private static object UnwrapNumber(JsonElement element)
    {
        var text = element.GetRawText();
        var hasExponent = text.AsSpan().IndexOfAny('e', 'E') >= 0;
        var isIntegerToken = !hasExponent && text.AsSpan().IndexOf('.') < 0;
        if (isIntegerToken && element.TryGetInt64(out var whole)) return whole;
        // A decimal rounds beyond 28 digits and cannot hold an exponent token as sent, so it is only used when it reproduces the token exactly.
        if (!hasExponent && element.TryGetDecimal(out var exact) && exact.ToString(CultureInfo.InvariantCulture) == text) return exact;
        return new RawNumber(text, element.TryGetDouble(out var approx) ? approx : double.PositiveInfinity);
    }

    /// <summary>Whether the value is a number of any CLR type, or a <see cref="RawNumber"/>.</summary>
    public static bool IsNumber(object? value) =>
        value is RawNumber or double or float or decimal or int or long or short or byte or sbyte or uint or ulong or ushort;

    /// <summary>The exact text of a number: the digits as sent for a <see cref="RawNumber"/>, otherwise the invariant text.</summary>
    public static string NumberText(object value) => value switch
    {
        RawNumber raw => raw.Text,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>Reads a finite <see cref="double"/> from a number or from numeric text in the invariant format (no thousands separator).</summary>
    public static bool TryDouble(object? value, out double number)
    {
        number = 0;
        switch (value)
        {
            case string s:
                return double.TryParse(s.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
            case RawNumber raw:
                number = raw.Approximate;
                return double.IsFinite(number);
            case null or bool:
                return false;
            default:
                if (!IsNumber(value)) return false;
                number = value is decimal m ? (double)m : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsFinite(number);
        }
    }

    /// <summary>Reads a whole number that may arrive as an int, a long, a decimal or double without fraction (25.0 is 25), or numeric text.</summary>
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
            case decimal m when m == decimal.Truncate(m) && m is >= int.MinValue and <= int.MaxValue:
                result = (int)m;
                return true;
            case double d when d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue:
                result = (int)d;
                return true;
            case string s:
                return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            default:
                return false;
        }
    }

    /// <summary>Reads the argument at <paramref name="index"/> as text, or <see langword="null"/> when it is missing, empty or not text or a number. A number keeps its exact digits.</summary>
    public static string? Text(IReadOnlyList<object?> args, int index) => index < args.Count
        ? args[index] switch
        {
            string s => s,
            { } n when IsNumber(n) => NumberText(n),
            _ => null,
        }
        : null;
}
