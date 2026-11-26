using System.Text.Json;

namespace Klyroo.Agent;

public sealed partial class AgentBridgeService
{
    private const string SnapshotSchema = """
        {
          "type": "object",
          "properties": {
            "includeOffPage": { "type": "boolean", "description": "Also list components that are not displayed right now, such as conditional fields whose condition is false. Default true." }
          }
        }
        """;

    private const string QuerySchema = """
        {
          "type": "object",
          "properties": {
            "within": { "type": "string", "description": "Optional component ref or label to inspect choices or records within." },
            "text": { "type": "string", "description": "Optional search text to filter components, choices or records." },
            "kind": { "type": "string", "description": "Optional component kind filter (for example select, input, spreadsheet)." },
            "collection": { "type": "string", "description": "With within: page through a collection of records instead of choices. A spreadsheet has rows, issues, columns, headers and questions." },
            "filter": { "type": "string", "description": "With collection: a subset, such as invalid, importable or discarded for rows, blocking or warnings for issues, open for questions." },
            "offset": { "type": "integer", "minimum": 0, "description": "With collection: the index of the first record to return." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 100, "description": "With collection: how many records to return. Default 25." }
          }
        }
        """;

    private const string ActSchema = """
        {
          "type": "object",
          "required": ["steps"],
          "properties": {
            "steps": {
              "type": "array",
              "maxItems": 50,
              "items": {
                "type": "object",
                "required": ["ref", "action"],
                "properties": {
                  "ref": { "type": "string", "minLength": 1, "description": "Component ref (for example c1) or label." },
                  "action": { "type": "string", "minLength": 1, "description": "Action name from the snapshot (for example setValue, clear, toggle, submit)." },
                  "args": {
                    "type": "array",
                    "items": {
                      "type": ["string", "number", "boolean", "null", "array"],
                      "items": { "type": ["string", "number", "boolean", "null"] }
                    },
                    "description": "Arguments for the action, such as the new value of setValue. Pass numbers, booleans and lists as JSON values, not as text. An argument is text, a number, a boolean, null or a list of those; objects are not accepted."
                  }
                }
              }
            }
          }
        }
        """;

    private static readonly Lazy<IReadOnlyList<AgentToolDefinition>> ToolDefinitions = new(() =>
    [
        new("snapshot", "Lists the components on the page, their current state, and available actions.",
            ReadOnlyHint: true, ConsequentialHint: false, UntrustedContentHint: true, Schema(SnapshotSchema)),
        new("query", "Finds components by label or kind, lists choices inside a select or filter without opening popups, and pages through the records of a component such as spreadsheet rows and issues.",
            ReadOnlyHint: true, ConsequentialHint: false, UntrustedContentHint: true, Schema(QuerySchema)),
        new("act", "Calls semantic actions on components by ref or label (e.g. setValue, clear, toggle, reset, submit). Steps run in order and the first failure stops the run. Actions that need confirmation, flagged in the snapshot, are refused unless the host approves them.",
            ReadOnlyHint: false, ConsequentialHint: true, UntrustedContentHint: true, Schema(ActSchema)),
    ]);

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Returns the definitions of the three agent tools, <c>snapshot</c>, <c>query</c> and <c>act</c>, for registration
    /// with an LLM or MCP host, each with the JSON Schema of its arguments.
    /// </summary>
    /// <returns>Three <see cref="AgentToolDefinition"/> values.</returns>
    public static IReadOnlyList<AgentToolDefinition> GetToolDefinitions() => ToolDefinitions.Value;

    /// <summary>
    /// Runs one tool call the way an LLM or MCP host delivers it: a tool name and the raw JSON object of its arguments.
    /// Arguments are read as JSON values, so numbers, booleans and lists in <c>args</c> keep their type, and a
    /// malformed call is reported as an <see cref="AgentToolError"/> rather than thrown, since the model wrote it.
    /// </summary>
    /// <param name="tool">The tool name: <c>snapshot</c>, <c>query</c> or <c>act</c>.</param>
    /// <param name="arguments">The arguments object. An undefined or null value counts as an empty object.</param>
    /// <param name="allow">Passed to <see cref="ActAsync"/> for an <c>act</c> call.</param>
    /// <returns>
    /// An <see cref="AgentSnapshotResult"/> for <c>snapshot</c>, an <see cref="AgentQueryResult"/> for <c>query</c>, an
    /// <see cref="AgentActResult"/> for <c>act</c>, or an <see cref="AgentToolError"/>.
    /// </returns>
    public async Task<AgentToolResult> InvokeToolAsync(string tool, JsonElement arguments, Func<AgentActStep, bool, Task<bool>>? allow = null)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            arguments = Schema("{}");
        }
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return new AgentToolError("The arguments must be a JSON object.");
        }

        switch (tool)
        {
            case "snapshot":
                if (!TryReadBool(arguments, "includeOffPage", true, out var includeOffPage, out var snapshotError)) return new AgentToolError(snapshotError!);
                return new AgentSnapshotResult(Snapshot(includeOffPage));

            case "query":
                return TryParseQuery(arguments, out var request, out var queryError) ? new AgentQueryResult(Query(request)) : new AgentToolError(queryError!);

            case "act":
                return TryParseSteps(arguments, out var steps, out var actError) ? await ActAsync(steps, allow) : new AgentToolError(actError!);

            default:
                return new AgentToolError($"Unknown tool \"{tool}\". Use snapshot, query or act.");
        }
    }

    private static bool TryReadBool(JsonElement obj, string name, bool fallback, out bool value, out string? error)
    {
        value = fallback;
        error = null;
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = element.GetBoolean();
            return true;
        }
        error = $"\"{name}\" must be a boolean.";
        return false;
    }

    private static bool TryReadString(JsonElement obj, string name, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }
        error = $"\"{name}\" must be text.";
        return false;
    }

    private static bool TryReadInt(JsonElement obj, string name, out int value, out string? error)
    {
        value = 0;
        error = null;
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null) return true;
        // 25.0 is a whole number too, as the schema says "integer".
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number)
            && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)number;
            return true;
        }
        error = $"\"{name}\" must be a whole number.";
        return false;
    }

    private static bool TryParseQuery(JsonElement obj, out AgentQueryRequest request, out string? error)
    {
        request = new AgentQueryRequest();
        if (!TryReadString(obj, "within", out var within, out error)
            || !TryReadString(obj, "text", out var text, out error)
            || !TryReadString(obj, "kind", out var kind, out error)
            || !TryReadString(obj, "collection", out var collection, out error)
            || !TryReadString(obj, "filter", out var filter, out error)
            || !TryReadInt(obj, "offset", out var offset, out error)
            || !TryReadInt(obj, "limit", out var limit, out error))
        {
            return false;
        }
        request = new AgentQueryRequest { Within = within, Text = text, Kind = kind, Collection = collection, Filter = filter, Offset = offset, Limit = limit };
        return true;
    }

    private static bool TryParseSteps(JsonElement obj, out List<AgentActStep> steps, out string? error)
    {
        steps = [];
        error = null;
        if (!obj.TryGetProperty("steps", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            error = "\"steps\" must be a list of steps.";
            return false;
        }
        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            index++;
            if (item.ValueKind != JsonValueKind.Object
                || !TryReadString(item, "ref", out var reference, out _) || string.IsNullOrEmpty(reference)
                || !TryReadString(item, "action", out var action, out _) || string.IsNullOrEmpty(action))
            {
                error = $"Step {index}: a step needs a \"ref\" and an \"action\" as text.";
                return false;
            }
            List<AgentArgument>? args = null;
            if (item.TryGetProperty("args", out var argList) && argList.ValueKind != JsonValueKind.Null)
            {
                if (argList.ValueKind != JsonValueKind.Array)
                {
                    error = $"Step {index}: \"args\" must be a list.";
                    return false;
                }
                if (argList.EnumerateArray().Any(AgentArgs.ContainsObject))
                {
                    error = $"Step {index}: an argument is an object; arguments are text, numbers, booleans, null or lists of those.";
                    return false;
                }
                // The raw elements are kept, so a number stays a number with its digits and a list stays a list.
                args = argList.EnumerateArray().Select(a => new AgentArgument(a)).ToList();
            }
            steps.Add(new AgentActStep(reference, action, args));
        }
        return true;
    }
}
