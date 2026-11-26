using System.Globalization;

namespace Klyroo.Agent;

public sealed partial class AgentBridgeService
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const int MaxRowNumbersPerQuestion = 20;

    /// <summary>
    /// Registers an importer as one <c>spreadsheet</c> component. Its snapshot value is an
    /// <see cref="AgentSpreadsheetSummary"/> (step, row counts, open questions, unmapped required columns), and
    /// <see cref="Query"/> with <see cref="AgentQueryRequest.Within"/> and <see cref="AgentQueryRequest.Collection"/> pages
    /// through <c>rows</c>, <c>issues</c>, <c>columns</c>, <c>headers</c> and <c>questions</c>.
    /// </summary>
    /// <remarks>
    /// Actions, with the arguments in order. Rows are addressed by the row number the user sees in the file (the
    /// <c>rowNumber</c> of a row record), never by an internal index.
    /// <list type="bullet">
    /// <item><description><c>assignColumn(field, header)</c>: use the file column whose header text matches, or whose 0-based <c>index</c> from the <c>headers</c> collection is given as a number. <c>field</c> is a column key or label.</description></item>
    /// <item><description><c>ignoreColumn(field)</c> and <c>resetColumn(field)</c>: leave a column unmapped, or go back to automatic matching.</description></item>
    /// <item><description><c>answerValue(field, rawValue, answer, mappedValue?)</c>: answer the question about an unknown value with <c>map</c> (and the choice to map to, by value or label), <c>leaveEmpty</c>, <c>skipRows</c> or <c>create</c>.</description></item>
    /// <item><description><c>editCell(rowNumber, field, text)</c> on an editable column.</description></item>
    /// <item><description><c>discardRows(rowNumber...)</c> and <c>restoreRows(rowNumber...)</c>, as separate numbers or one list. Unknown row numbers fail the step without changing anything.</description></item>
    /// <item><description><c>nextStep</c>, <c>previousStep</c>, <c>submit</c> and <c>clearFile</c>. <c>submit</c> and <c>clearFile</c> are confirmation-required.</description></item>
    /// </list>
    /// Loading the file itself is not an agent action.
    /// </remarks>
    /// <param name="importer">The importer to expose.</param>
    /// <returns>A handle that removes the importer's component when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="importer"/> is <see langword="null"/>.</exception>
    public IDisposable RegisterSpreadsheet(SpreadsheetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        return AddTargets([new BoundTarget
        {
            Ref = NextRef(),
            Component = "spreadsheet",
            Label = importer.Schema.Key,
            GetValue = () => SummarizeSpreadsheet(importer),
            Actions = ["assignColumn", "ignoreColumn", "resetColumn", "answerValue", "editCell", "discardRows", "restoreRows", "nextStep", "previousStep", "submit", "clearFile"],
            ConfirmActions = ["submit", "clearFile"],
            GetChoices = () => [],
            QueryRecords = request => QuerySpreadsheet(importer, request),
            RowOf = (action, args) => action switch
            {
                "editCell" => args.Count > 0 ? AgentArgs.Text(args, 0) : null,
                "discardRows" or "restoreRows" => ParseRowNumbers(args, out var numbers, out _) ? string.Join(",", numbers) : null,
                _ => null,
            },
            ExecuteAction = (action, args) => ExecuteSpreadsheetAction(importer, action, args),
        }]);
    }

    private static AgentSpreadsheetSummary SummarizeSpreadsheet(SpreadsheetImporter importer)
    {
        var rows = importer.AllRows;
        return new AgentSpreadsheetSummary(
            Step: importer.CurrentStepKey,
            FileLoaded: importer.FileLoaded,
            FileName: importer.FileName,
            Rows: rows.Count,
            ImportableRows: rows.Count(r => r.Importable),
            InvalidRows: importer.InvalidRows.Count,
            DiscardedRows: importer.DiscardedRows.Count,
            RowsWithWarnings: rows.Count(r => r.Importable && r.Status == SpreadsheetRowStatus.Warning),
            OpenQuestions: importer.OpenQuestions.Count,
            UnmappedRequiredColumns: importer.MissingFields.Select(f => f.Column.EffectiveLabel).ToList(),
            CanSubmit: importer.CanSubmit,
            SubmitStatus: importer.SubmitStatus.ToString(),
            ImportedCount: importer.ImportedCount,
            SubmitError: importer.SubmitError);
    }

    private static string Level(SpreadsheetIssueLevel level) => level.ToString().ToLowerInvariant();

    private static RecordPage? QuerySpreadsheet(SpreadsheetImporter importer, AgentQueryRequest request)
    {
        var text = Normalize(request.Text);
        var filter = Normalize(request.Filter);
        var records = new List<IReadOnlyDictionary<string, object?>>();

        switch (Normalize(request.Collection))
        {
            case "rows":
                foreach (var row in importer.AllRows)
                {
                    var keep = filter switch
                    {
                        "" or "all" => true,
                        "importable" => row.Importable,
                        "invalid" => row.DiscardReason is null && row.Status == SpreadsheetRowStatus.Blocking,
                        "discarded" => row.DiscardReason is not null,
                        _ => false,
                    };
                    if (!keep) continue;
                    var cells = row.Cells.ToDictionary(c => c.Key, c => (object?)c.Value.Text, StringComparer.Ordinal);
                    if (text.Length > 0 && !cells.Values.Any(v => Normalize(v?.ToString()).Contains(text, StringComparison.Ordinal))) continue;
                    records.Add(new Dictionary<string, object?>
                    {
                        ["rowNumber"] = row.RowNumber,
                        ["status"] = row.Status.ToString().ToLowerInvariant(),
                        ["importable"] = row.Importable,
                        ["discardReason"] = row.DiscardReason?.ToString().ToLowerInvariant(),
                        ["edited"] = row.Edited,
                        ["cells"] = cells,
                        ["issues"] = row.Issues.Select(IssueRecord).ToList(),
                    });
                }
                break;

            case "issues":
                foreach (var row in importer.AllRows)
                {
                    foreach (var issue in row.Issues)
                    {
                        var keep = filter switch
                        {
                            "" or "all" => true,
                            "blocking" => issue.Level == SpreadsheetIssueLevel.Error,
                            "warnings" => issue.Level == SpreadsheetIssueLevel.Warning,
                            _ => false,
                        };
                        if (!keep) continue;
                        if (text.Length > 0 && !Normalize(issue.Message).Contains(text, StringComparison.Ordinal)
                            && !Normalize(issue.Field).Contains(text, StringComparison.Ordinal)) continue;
                        var record = IssueRecord(issue);
                        record["rowNumber"] = row.RowNumber;
                        records.Add(record);
                    }
                }
                break;

            case "columns":
                foreach (var field in importer.Fields)
                {
                    var column = field.Column;
                    if (text.Length > 0 && !Normalize(column.EffectiveLabel).Contains(text, StringComparison.Ordinal)
                        && !Normalize(column.Key).Contains(text, StringComparison.Ordinal)) continue;
                    records.Add(new Dictionary<string, object?>
                    {
                        ["key"] = column.Key,
                        ["label"] = column.EffectiveLabel,
                        ["required"] = column.Required,
                        ["editable"] = column.Editable,
                        ["status"] = field.Status.ToString().ToLowerInvariant(),
                        ["headerIndex"] = field.HeaderIndex,
                        ["header"] = field.HeaderText,
                        ["suggestedHeader"] = field.Suggestion?.HeaderText,
                        ["suggestedHeaderIndex"] = field.Suggestion?.HeaderIndex,
                        ["samples"] = field.Samples,
                    });
                }
                break;

            case "headers":
                var used = importer.Fields
                    .Where(f => f.HeaderIndex is not null)
                    .ToDictionary(f => f.HeaderIndex!.Value, f => f.Column.Key);
                for (var i = 0; i < importer.Headers.Count; i++)
                {
                    if (text.Length > 0 && !Normalize(importer.Headers[i]).Contains(text, StringComparison.Ordinal)) continue;
                    records.Add(new Dictionary<string, object?>
                    {
                        ["index"] = i,
                        ["header"] = importer.Headers[i],
                        ["usedBy"] = used.GetValueOrDefault(i),
                    });
                }
                break;

            case "questions":
                foreach (var question in importer.Questions)
                {
                    if (filter == "open" && !question.IsOpen) continue;
                    if (text.Length > 0 && !Normalize(question.RawValue).Contains(text, StringComparison.Ordinal)
                        && !Normalize(question.FieldLabel).Contains(text, StringComparison.Ordinal)) continue;
                    var rowNumbers = question.RowIndexes
                        .Where(i => i >= 0 && i < importer.AllRows.Count)
                        .Select(i => importer.AllRows[i].RowNumber)
                        .ToList();
                    records.Add(new Dictionary<string, object?>
                    {
                        ["field"] = question.FieldKey,
                        ["fieldLabel"] = question.FieldLabel,
                        ["rawValue"] = question.RawValue,
                        ["open"] = question.IsOpen,
                        ["answer"] = question.Answer?.Action.ToString(),
                        ["mappedValue"] = question.Answer?.MappedValue,
                        ["canCreate"] = question.CanCreate,
                        ["rowCount"] = rowNumbers.Count,
                        ["rowNumbers"] = rowNumbers.Take(MaxRowNumbersPerQuestion).ToList(),
                        ["choices"] = question.Choices.Select(c => new Dictionary<string, object?> { ["value"] = c.Value, ["label"] = c.Label }).ToList(),
                    });
                }
                break;

            default:
                return null;
        }

        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit <= 0 ? DefaultPageSize : Math.Min(request.Limit, MaxPageSize);
        return new RecordPage(records.Skip(offset).Take(limit).ToList(), records.Count);
    }

    private static Dictionary<string, object?> IssueRecord(SpreadsheetRowIssue issue) => new()
    {
        ["field"] = issue.Field,
        ["code"] = issue.Code,
        ["level"] = Level(issue.Level),
        ["message"] = issue.Message,
    };

    private static SpreadsheetColumnSchema? FindColumn(SpreadsheetImporter importer, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var byKey = importer.Schema.Columns.FirstOrDefault(c => string.Equals(c.Key, name, StringComparison.Ordinal));
        if (byKey is not null) return byKey;
        var normalized = Normalize(name);
        var byLabel = importer.Schema.Columns.Where(c => Normalize(c.EffectiveLabel) == normalized).ToList();
        return byLabel.Count == 1 ? byLabel[0] : null;
    }

    // Reads row numbers from separate arguments or from one list, and maps them to the importer's row indexes.
    private static bool ParseRowNumbers(IReadOnlyList<object?> args, out List<int> rowNumbers, out string? error)
    {
        rowNumbers = [];
        error = null;
        var raw = args.Count == 1 && args[0] is List<object?> list ? list : args;
        if (raw.Count == 0)
        {
            error = "give at least one row number.";
            return false;
        }
        foreach (var item in raw)
        {
            if (!AgentArgs.TryInt(item, out var number))
            {
                error = $"\"{item}\" is not a row number.";
                return false;
            }
            if (!rowNumbers.Contains(number)) rowNumbers.Add(number);
        }
        return true;
    }

    private static bool TryRowIndexes(SpreadsheetImporter importer, IReadOnlyList<object?> args, out List<int> indexes, out List<int> numbers, out string? error)
    {
        indexes = [];
        if (!ParseRowNumbers(args, out numbers, out error)) return false;
        var unknown = new List<int>();
        foreach (var number in numbers)
        {
            var row = importer.AllRows.FirstOrDefault(r => r.RowNumber == number);
            if (row is null) unknown.Add(number);
            else indexes.Add(row.Index);
        }
        if (unknown.Count > 0)
        {
            error = $"no data row has the row number {string.Join(", ", unknown)}. Row numbers are the ones shown in the file; use query with collection \"rows\" to list them.";
            return false;
        }
        return true;
    }

    private static async Task<StepResult> ExecuteSpreadsheetAction(SpreadsheetImporter importer, string action, IReadOnlyList<object?> args)
    {
        switch (action)
        {
            case "assignColumn":
            {
                var column = FindColumn(importer, AgentArgs.Text(args, 0));
                if (column is null) return StepResult.Fail($"\"{AgentArgs.Text(args, 0)}\" is not a column of this import. Use query with collection \"columns\".");
                if (!importer.FileLoaded) return StepResult.Fail("no file is loaded.");
                int headerIndex;
                if (args.Count > 1 && args[1] is double or int && AgentArgs.TryInt(args[1], out headerIndex))
                {
                    if (headerIndex < 0 || headerIndex >= importer.Headers.Count)
                        return StepResult.Fail($"the file has no column with index {headerIndex}. Use query with collection \"headers\".");
                }
                else
                {
                    var header = AgentArgs.Text(args, 1);
                    if (string.IsNullOrWhiteSpace(header)) return StepResult.Fail("give the header text, or the index of a file column.");
                    var exact = Enumerable.Range(0, importer.Headers.Count).Where(i => string.Equals(importer.Headers[i], header, StringComparison.Ordinal)).ToList();
                    var matches = exact.Count > 0
                        ? exact
                        : Enumerable.Range(0, importer.Headers.Count).Where(i => Normalize(importer.Headers[i]) == Normalize(header)).ToList();
                    if (matches.Count == 0) return StepResult.Fail($"no file column is headed \"{header}\". Use query with collection \"headers\".");
                    if (matches.Count > 1) return StepResult.Fail($"{matches.Count} file columns are headed \"{header}\" (indexes {string.Join(", ", matches)}). Pass the index as a number.");
                    headerIndex = matches[0];
                }
                importer.AssignColumn(column.Key, headerIndex);
                return true;
            }
            case "ignoreColumn":
            case "resetColumn":
            {
                var column = FindColumn(importer, AgentArgs.Text(args, 0));
                if (column is null) return StepResult.Fail($"\"{AgentArgs.Text(args, 0)}\" is not a column of this import. Use query with collection \"columns\".");
                if (action == "ignoreColumn") importer.IgnoreColumn(column.Key);
                else importer.ResetColumn(column.Key);
                return true;
            }
            case "answerValue":
                return AnswerValue(importer, args);
            case "editCell":
            {
                if (args.Count < 3) return StepResult.Fail("expected rowNumber, field and text.");
                if (!TryRowIndexes(importer, [args[0]], out var indexes, out _, out var error)) return StepResult.Fail(error!);
                var column = FindColumn(importer, AgentArgs.Text(args, 1));
                if (column is null) return StepResult.Fail($"\"{AgentArgs.Text(args, 1)}\" is not a column of this import.");
                if (!column.Editable) return StepResult.Fail($"the column \"{column.EffectiveLabel}\" is not editable.");
                var text = args[2] switch
                {
                    null => "",
                    string s => s,
                    double d => d.ToString(CultureInfo.InvariantCulture),
                    bool b => b ? "true" : "false",
                    _ => null,
                };
                if (text is null) return StepResult.Fail("the cell text must be text, a number or a boolean.");
                importer.EditCell(indexes[0], column.Key, text);
                return true;
            }
            case "discardRows":
            case "restoreRows":
            {
                if (!TryRowIndexes(importer, args, out var indexes, out _, out var error)) return StepResult.Fail(error!);
                if (action == "discardRows") importer.DiscardRows(indexes);
                else importer.RestoreRows(indexes);
                return true;
            }
            case "nextStep":
                return importer.NextStep() ? true : StepResult.Fail(StepBlockedReason(importer, "next"));
            case "previousStep":
                return importer.PreviousStep() ? true : StepResult.Fail(StepBlockedReason(importer, "previous"));
            case "submit":
            {
                if (!importer.CanSubmit) return StepResult.Fail(CannotSubmitReason(importer));
                await importer.SubmitAsync();
                if (importer.SubmitStatus == SpreadsheetSubmitStatus.Done && importer.RejectedCount == 0) return true;
                return importer.SubmitStatus == SpreadsheetSubmitStatus.Done
                    ? StepResult.Fail($"{importer.RejectedCount} rows were rejected by the server; use query with collection \"issues\".")
                    : StepResult.Fail(importer.SubmitError ?? $"the submit ended with status {importer.SubmitStatus}.");
            }
            case "clearFile":
                importer.ClearFile();
                return true;
            default:
                return false;
        }
    }

    private static StepResult AnswerValue(SpreadsheetImporter importer, IReadOnlyList<object?> args)
    {
        var column = FindColumn(importer, AgentArgs.Text(args, 0));
        if (column is null) return StepResult.Fail($"\"{AgentArgs.Text(args, 0)}\" is not a column of this import.");
        var rawValue = AgentArgs.Text(args, 1);
        var question = rawValue is null
            ? null
            : importer.Questions.FirstOrDefault(q => q.FieldKey == column.Key
                && SpreadsheetImporter.NormalizeText(q.RawValue) == SpreadsheetImporter.NormalizeText(rawValue));
        if (question is null) return StepResult.Fail($"no question is open about the value \"{rawValue}\" in \"{column.EffectiveLabel}\". Use query with collection \"questions\".");

        SpreadsheetValueAnswer answer;
        switch (Normalize(AgentArgs.Text(args, 2)))
        {
            case "map":
            {
                var wanted = AgentArgs.Text(args, 3);
                if (string.IsNullOrWhiteSpace(wanted)) return StepResult.Fail("map needs the value or label of the choice to map to.");
                var choice = question.Choices.FirstOrDefault(c => string.Equals(c.Value, wanted, StringComparison.Ordinal));
                if (choice is null)
                {
                    var byLabel = question.Choices.Where(c => Normalize(c.Label) == Normalize(wanted)).ToList();
                    if (byLabel.Count > 1) return StepResult.Fail($"\"{wanted}\" matches several choices by label. Use the choice value.");
                    choice = byLabel.FirstOrDefault();
                }
                if (choice is null) return StepResult.Fail($"\"{wanted}\" is not one of the choices ({string.Join(", ", question.Choices.Select(c => c.Value))}).");
                answer = new SpreadsheetValueAnswer(SpreadsheetValueAnswerAction.Map, choice.Value, choice.Label);
                break;
            }
            case "leaveempty":
                answer = new SpreadsheetValueAnswer(SpreadsheetValueAnswerAction.LeaveEmpty);
                break;
            case "skiprows":
                answer = new SpreadsheetValueAnswer(SpreadsheetValueAnswerAction.SkipRows);
                break;
            case "create":
                if (!question.CanCreate) return StepResult.Fail($"values cannot be created for \"{column.EffectiveLabel}\".");
                answer = new SpreadsheetValueAnswer(SpreadsheetValueAnswerAction.Create);
                break;
            default:
                return StepResult.Fail("the answer must be map, leaveEmpty, skipRows or create.");
        }
        importer.AnswerValue(column.Key, question.RawValue, answer);
        return true;
    }

    private static string StepBlockedReason(SpreadsheetImporter importer, string direction)
    {
        var current = importer.GetSteps().FirstOrDefault(s => s.Key == importer.CurrentStepKey);
        if (direction == "next" && current is { Ready: false })
        {
            var blockers = current.Blockers;
            return $"the {current.Key} step is not ready ({blockers.ToString(CultureInfo.InvariantCulture)} blocker{(blockers == 1 ? "" : "s")}); read the snapshot summary.";
        }
        return direction == "next" ? "there is no next step." : "there is no previous step.";
    }

    private static string CannotSubmitReason(SpreadsheetImporter importer)
    {
        if (!importer.FileLoaded) return "no file is loaded.";
        var reasons = new List<string>();
        if (importer.MissingFields.Count > 0) reasons.Add($"required columns are not mapped ({string.Join(", ", importer.MissingFields.Select(f => f.Column.EffectiveLabel))})");
        if (importer.OpenQuestions.Count > 0) reasons.Add($"{importer.OpenQuestions.Count.ToString(CultureInfo.InvariantCulture)} unknown values are unanswered");
        if (importer.ImportableRows.Count == 0) reasons.Add("no row is importable");
        return reasons.Count > 0 ? $"cannot submit: {string.Join("; ", reasons)}." : "cannot submit now.";
    }
}
