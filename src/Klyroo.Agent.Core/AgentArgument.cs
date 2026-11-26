using System.Buffers;
using System.Text.Json;

namespace Klyroo.Agent;

/// <summary>
/// One argument of an <see cref="AgentActStep"/>: text, a number, a boolean, <c>null</c> or a list of those, held as the
/// JSON value a language model sent. A .NET caller writes it as a plain value, because the conversions below
/// create the JSON: <c>new AgentActStep("Quantity", "setValue", [12])</c>. A number keeps its exact digits, so a
/// 64-bit integer or a decimal reaches a field as the digits given. An object is not an argument the
/// bridge accepts: <c>AgentBridgeService.ActAsync</c> fails the step.
/// </summary>
public readonly struct AgentArgument : IEquatable<AgentArgument>
{
    private static readonly JsonElement NullElement = JsonDocument.Parse("null").RootElement;

    private readonly JsonElement _json;

    /// <summary>Creates an argument from a JSON value, such as the elements of the <c>args</c> of an <c>act</c> call.</summary>
    /// <param name="json">The JSON value. An undefined element counts as <c>null</c>.</param>
    public AgentArgument(JsonElement json)
    {
        _json = json.ValueKind == JsonValueKind.Undefined ? default : json.Clone();
    }

    /// <summary>The JSON value. <c>null</c> when the argument was created without a value.</summary>
    public JsonElement Json => _json.ValueKind == JsonValueKind.Undefined ? NullElement : _json;

    /// <summary>The argument that stands for no value.</summary>
    public static AgentArgument Null => new(NullElement);

    /// <summary>Creates a list argument.</summary>
    /// <param name="items">The items, in order.</param>
    /// <returns>The list as one argument.</returns>
    public static AgentArgument List(params AgentArgument[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Build(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in items) item.Json.WriteTo(writer);
            writer.WriteEndArray();
        });
    }

    /// <summary>Creates a list of text arguments, such as the selected values of a multiple choice.</summary>
    /// <param name="items">The items, in order.</param>
    /// <returns>The list as one argument.</returns>
    public static AgentArgument List(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Build(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in items) writer.WriteStringValue(item);
            writer.WriteEndArray();
        });
    }

    /// <summary>Creates a list of whole numbers, such as row numbers.</summary>
    /// <param name="items">The numbers, in order.</param>
    /// <returns>The list as one argument.</returns>
    public static AgentArgument List(IEnumerable<int> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return Build(writer =>
        {
            writer.WriteStartArray();
            foreach (var item in items) writer.WriteNumberValue(item);
            writer.WriteEndArray();
        });
    }

    /// <summary>Creates a text argument. <see langword="null"/> gives <see cref="Null"/>.</summary>
    /// <param name="text">The text.</param>
    public static implicit operator AgentArgument(string? text) => text is null ? Null : Build(w => w.WriteStringValue(text));

    /// <summary>Creates a boolean argument.</summary>
    /// <param name="flag">The flag.</param>
    public static implicit operator AgentArgument(bool flag) => Build(w => w.WriteBooleanValue(flag));

    /// <summary>Creates a number argument.</summary>
    /// <param name="number">The number.</param>
    public static implicit operator AgentArgument(int number) => Build(w => w.WriteNumberValue(number));

    /// <summary>Creates a number argument.</summary>
    /// <param name="number">The number.</param>
    public static implicit operator AgentArgument(long number) => Build(w => w.WriteNumberValue(number));

    /// <summary>Creates a number argument that keeps the scale of the decimal (<c>1.50</c> stays <c>1.50</c>).</summary>
    /// <param name="number">The number.</param>
    public static implicit operator AgentArgument(decimal number) => Build(w => w.WriteRawValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Creates a number argument. A value that JSON cannot hold (not a number, infinity) becomes its name as text, which a numeric field refuses.</summary>
    /// <param name="number">The number.</param>
    public static implicit operator AgentArgument(double number) => double.IsFinite(number)
        ? Build(w => w.WriteNumberValue(number))
        : Build(w => w.WriteStringValue(number.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>Creates a list of text arguments.</summary>
    /// <param name="items">The items, in order.</param>
    public static implicit operator AgentArgument(string[] items) => List((IEnumerable<string>)items);

    /// <summary>Wraps a JSON value.</summary>
    /// <param name="json">The JSON value.</param>
    public static implicit operator AgentArgument(JsonElement json) => new(json);

    /// <inheritdoc />
    public bool Equals(AgentArgument other) => string.Equals(Json.GetRawText(), other.Json.GetRawText(), StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is AgentArgument other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Json.GetRawText().GetHashCode(StringComparison.Ordinal);

    /// <summary>Compares two arguments by their JSON text.</summary>
    /// <param name="left">The first argument.</param>
    /// <param name="right">The second argument.</param>
    /// <returns><see langword="true"/> when both have the same JSON text.</returns>
    public static bool operator ==(AgentArgument left, AgentArgument right) => left.Equals(right);

    /// <summary>Compares two arguments by their JSON text.</summary>
    /// <param name="left">The first argument.</param>
    /// <param name="right">The second argument.</param>
    /// <returns><see langword="true"/> when the JSON text differs.</returns>
    public static bool operator !=(AgentArgument left, AgentArgument right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => Json.GetRawText();

    private static AgentArgument Build(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return new AgentArgument(document.RootElement.Clone());
    }
}
