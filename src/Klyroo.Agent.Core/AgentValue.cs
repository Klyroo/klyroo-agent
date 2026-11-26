using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Klyroo.Agent;

/// <summary>
/// A value the agent bridge reports: the current value of a component, the value before and after a step, or the
/// value a step left behind. It is a closed set, so a consumer matches on the subtype instead of testing an
/// <see cref="object"/>: <see cref="AgentTextValue"/>, <see cref="AgentNumberValue"/>, <see cref="AgentBooleanValue"/>,
/// <see cref="AgentListValue"/> and <see cref="AgentSpreadsheetSummary"/>. No value at all is <see langword="null"/>.
/// It serializes to the JSON an agent reads: a string, a number, a boolean, an array, or the summary object. Serialize it
/// through the <see cref="AgentValue"/> type, as a property or with <c>Serialize&lt;AgentValue&gt;</c>, so the converter of this type is the one used.
/// </summary>
[JsonConverter(typeof(AgentValueJsonConverter))]
public abstract record AgentValue
{
    /// <summary>Creates a text value.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    public static AgentValue From(string text) => new AgentTextValue(text);

    /// <summary>Creates a boolean value.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>The value.</returns>
    public static AgentValue From(bool flag) => new AgentBooleanValue(flag);

    /// <summary>Creates a list of text values.</summary>
    /// <param name="items">The items, in order.</param>
    /// <returns>The value.</returns>
    public static AgentValue From(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new AgentListValue([.. items.Select(i => (AgentValue)new AgentTextValue(i))]);
    }

    /// <summary>Reads a JSON value into an <see cref="AgentValue"/>. A number keeps its digits, a list keeps its order, and a JSON object, which no component holds, is kept as its JSON text.</summary>
    /// <param name="element">The JSON value.</param>
    /// <returns>The value, or <see langword="null"/> for JSON <c>null</c> or an undefined element.</returns>
    public static AgentValue? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => new AgentTextValue(element.GetString() ?? ""),
        JsonValueKind.Number => new AgentNumberValue(element.GetRawText()),
        JsonValueKind.True => new AgentBooleanValue(true),
        JsonValueKind.False => new AgentBooleanValue(false),
        JsonValueKind.Array => new AgentListValue([.. element.EnumerateArray().Select(i => FromJson(i) ?? new AgentTextValue(""))]),
        _ => new AgentTextValue(element.GetRawText()),
    };

    /// <summary>A short text for a log or a label: the text, the digits, <c>true</c> or <c>false</c>, or the items joined by a comma.</summary>
    /// <returns>The text.</returns>
    public virtual string ToDisplayString() => ToString();
}

/// <summary>A text value.</summary>
/// <param name="Text">The text.</param>
public sealed record AgentTextValue(string Text) : AgentValue
{
    /// <inheritdoc />
    public override string ToDisplayString() => Text;
}

/// <summary>A number, with the digits exactly as the component reports them, so a 64-bit integer or a decimal of 28 digits is not rounded.</summary>
/// <param name="Text">The number in the invariant format of JSON, such as <c>12.5</c> or <c>9007199254740993</c>.</param>
public sealed record AgentNumberValue(string Text) : AgentValue
{
    /// <summary>The number as a <see cref="double"/>, or <see langword="null"/> when it is out of range. It can lose digits; read <see cref="Text"/> for the exact number.</summary>
    public double? Approximate => double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;

    /// <summary>The number as a <see cref="decimal"/>, or <see langword="null"/> when it does not fit one.</summary>
    public decimal? Exact => decimal.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : null;

    /// <inheritdoc />
    public override string ToDisplayString() => Text;
}

/// <summary>A boolean value.</summary>
/// <param name="Flag">The flag.</param>
public sealed record AgentBooleanValue(bool Flag) : AgentValue
{
    /// <inheritdoc />
    public override string ToDisplayString() => Flag ? "true" : "false";
}

/// <summary>A list of values, such as the selected options of a multiple choice.</summary>
/// <param name="Items">The items, in order.</param>
public sealed record AgentListValue(IReadOnlyList<AgentValue> Items) : AgentValue
{
    /// <inheritdoc />
    public override string ToDisplayString() => string.Join(", ", Items.Select(i => i.ToDisplayString()));
}

internal sealed class AgentValueJsonConverter : JsonConverter<AgentValue>
{
    public override AgentValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        AgentValue.FromJson(JsonElement.ParseValue(ref reader));

    public override void Write(Utf8JsonWriter writer, AgentValue value, JsonSerializerOptions options) => Write(writer, value);

    private static void Write(Utf8JsonWriter writer, AgentValue value)
    {
        switch (value)
        {
            case AgentTextValue text:
                writer.WriteStringValue(text.Text);
                break;
            case AgentNumberValue number:
                writer.WriteRawValue(number.Text);
                break;
            case AgentBooleanValue flag:
                writer.WriteBooleanValue(flag.Flag);
                break;
            case AgentListValue list:
                writer.WriteStartArray();
                foreach (var item in list.Items) Write(writer, item);
                writer.WriteEndArray();
                break;
            case AgentSpreadsheetSummary summary:
                writer.WriteStartObject();
                writer.WriteString("step", summary.Step);
                writer.WriteBoolean("fileLoaded", summary.FileLoaded);
                if (summary.FileName is not null) writer.WriteString("fileName", summary.FileName);
                writer.WriteNumber("rows", summary.Rows);
                writer.WriteNumber("importableRows", summary.ImportableRows);
                writer.WriteNumber("invalidRows", summary.InvalidRows);
                writer.WriteNumber("discardedRows", summary.DiscardedRows);
                writer.WriteNumber("rowsWithWarnings", summary.RowsWithWarnings);
                writer.WriteNumber("openQuestions", summary.OpenQuestions);
                writer.WriteStartArray("unmappedRequiredColumns");
                foreach (var column in summary.UnmappedRequiredColumns) writer.WriteStringValue(column);
                writer.WriteEndArray();
                writer.WriteBoolean("canSubmit", summary.CanSubmit);
                writer.WriteString("submitStatus", summary.SubmitStatus);
                writer.WriteNumber("importedCount", summary.ImportedCount);
                if (summary.SubmitError is not null) writer.WriteString("submitError", summary.SubmitError);
                writer.WriteEndObject();
                break;
            default:
                throw new NotSupportedException($"{value.GetType().Name} is not an agent value this converter knows.");
        }
    }
}
