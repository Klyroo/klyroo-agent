using System.Text.Json;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class SpreadsheetAdapterTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // Header is row 1; Ada, Grace and Linus are rows 2, 3 and 4. "Grace" has an invalid age, "Linus" an unknown team.
    private static (AgentBridgeService Agent, SpreadsheetImporter Importer, string Ref, List<IReadOnlyList<SpreadsheetRow>> Submitted) Setup(
        string csv = "Full name;Years;Team\nAda;36;Engine\nGrace;abc;Engine\nLinus;54;Kernel",
        bool requireYears = false,
        Func<string, SpreadsheetOption>? create = null)
    {
        var submitted = new List<IReadOnlyList<SpreadsheetRow>>();
        var importer = new SpreadsheetImporter(new SpreadsheetSchema
        {
            Key = "people",
            Columns =
            [
                new SpreadsheetColumnSchema { Key = "name", Label = "Name", Required = true, Headers = ["Full name"] },
                new SpreadsheetColumnSchema { Key = "age", Label = "Age", Kind = SpreadsheetColumnKind.Number, Required = requireYears, Headers = ["Years"] },
                new SpreadsheetColumnSchema
                {
                    Key = "team", Label = "Team", Kind = SpreadsheetColumnKind.Select,
                    Options = [new("engine", "Engine"), new("ops", "Operations")],
                    OnCreateOption = create,
                },
                new SpreadsheetColumnSchema { Key = "id", Label = "Id", Editable = false, Headers = ["Never"] },
            ],
            OnSubmit = rows =>
            {
                submitted.Add(rows);
                return Task.FromResult<IReadOnlyList<SpreadsheetRejectedRow>?>([]);
            },
        });
        importer.LoadText(csv, "people.csv");
        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);
        return (agent, importer, agent.Snapshot().Single().Ref, submitted);
    }

    private static Task<AgentActResult> Act(AgentBridgeService agent, string action, params object?[] args) =>
        agent.ActAsync([new AgentActStep("people", action, args)]);

    private static IReadOnlyDictionary<string, object?> Record(AgentQueryMatch match, int index) => match.Records![index];

    [Fact]
    public void The_snapshot_value_is_a_summary_with_counts_questions_and_unmapped_required_columns()
    {
        var (agent, _, _, _) = Setup();

        var snapshot = agent.Snapshot().Single();
        var summary = Assert.IsType<AgentSpreadsheetSummary>(snapshot.Value);

        Assert.Equal("file", summary.Step);
        Assert.True(summary.FileLoaded);
        Assert.Equal("people.csv", summary.FileName);
        Assert.Equal(3, summary.Rows);
        Assert.Equal(1, summary.OpenQuestions);
        Assert.Equal(2, summary.InvalidRows); // Grace (not a number) and Linus (unanswered team)
        Assert.Empty(summary.UnmappedRequiredColumns);
        Assert.False(summary.CanSubmit);
        Assert.Equal("Idle", summary.SubmitStatus);
        Assert.Equal(["submit", "clearFile"], snapshot.ConfirmActions);
    }

    [Fact]
    public void The_summary_lists_required_columns_that_no_file_column_matches()
    {
        var (agent, _, _, _) = Setup("Nom;Years\nAda;36", requireYears: true);

        var summary = Assert.IsType<AgentSpreadsheetSummary>(agent.Snapshot().Single().Value);

        Assert.Equal(["Name"], summary.UnmappedRequiredColumns);
    }

    [Fact]
    public void An_empty_importer_has_a_summary_of_zeros()
    {
        var importer = new SpreadsheetImporter(new SpreadsheetSchema { Key = "empty", Columns = [new SpreadsheetColumnSchema { Key = "a" }] });
        var agent = new AgentBridgeService();
        agent.RegisterSpreadsheet(importer);

        var summary = Assert.IsType<AgentSpreadsheetSummary>(agent.Snapshot().Single().Value);

        Assert.False(summary.FileLoaded);
        Assert.Equal(0, summary.Rows);
        Assert.Equal("file", summary.Step);
    }

    [Fact]
    public void Query_pages_through_rows_by_user_visible_row_number()
    {
        var csv = "Full name;Years;Team\n" + string.Join("\n", Enumerable.Range(1, 30).Select(i => $"Person {i};{i};Engine"));
        var (agent, _, _, _) = Setup(csv);

        var first = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows", Limit = 10 }));
        Assert.Equal(30, first.Total);
        Assert.Equal(10, first.Records!.Count);
        Assert.Equal(2, Record(first, 0)["rowNumber"]);

        var third = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows", Limit = 10, Offset = 20 }));
        Assert.Equal(22, Record(third, 0)["rowNumber"]);
        Assert.Equal(20, third.Offset);

        var search = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows", Text = "person 7" }));
        Assert.Equal(1, search.Total);
        Assert.Equal(8, Record(search, 0)["rowNumber"]);

        var defaultSize = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows" }));
        Assert.Equal(25, defaultSize.Records!.Count);
        var capped = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows", Limit = 5000 }));
        Assert.Equal(30, capped.Records!.Count);
    }

    [Fact]
    public void Query_filters_rows_and_lists_issues_with_their_row_number()
    {
        var (agent, _, _, _) = Setup();

        var invalid = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "rows", Filter = "invalid" }));
        Assert.Equal(3, Record(invalid, 0)["rowNumber"]);

        var issues = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "issues", Filter = "blocking" }));
        Assert.True(issues.Total >= 1);
        Assert.All(issues.Records!, r => Assert.Equal("error", r["level"]));
        Assert.Contains(issues.Records!, r => Equals(r["rowNumber"], 3));
    }

    [Fact]
    public void Query_lists_columns_headers_and_questions()
    {
        var (agent, _, _, _) = Setup();

        var columns = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "columns" }));
        Assert.Equal(4, columns.Total);
        var team = columns.Records!.Single(r => (string?)r["key"] == "team");
        Assert.Equal("Team", team["header"]);

        var headers = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "headers" }));
        Assert.Equal(["Full name", "Years", "Team"], headers.Records!.Select(r => (string?)r["header"]));
        Assert.Equal("team", headers.Records![2]["usedBy"]);

        var questions = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "questions", Filter = "open" }));
        var question = Assert.Single(questions.Records!);
        Assert.Equal("Kernel", question["rawValue"]);
        Assert.Equal([4], Assert.IsAssignableFrom<IEnumerable<int>>(question["rowNumbers"]));
        Assert.Equal(["engine", "ops"], Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(question["choices"]).Select(c => (string?)c["value"]));
    }

    [Fact]
    public void An_unknown_collection_returns_no_records()
    {
        var (agent, _, _, _) = Setup();
        var match = Assert.Single(agent.Query(new AgentQueryRequest { Within = "people", Collection = "nothing" }));
        Assert.Empty(match.Records!);
        Assert.Equal(0, match.Total);
    }

    [Fact]
    public async Task EditCell_uses_the_row_number_and_refuses_the_header_unknown_rows_and_read_only_columns()
    {
        var (agent, importer, _, _) = Setup();

        var ok = await Act(agent, "editCell", Json("3"), "age", Json("\"41\""));
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal("41", importer.AllRows[1].Cells["age"].Text);

        var header = await Act(agent, "editCell", 1, "age", "5");
        Assert.False(header.Ok);
        Assert.Contains("row number 1", header.Error);

        var missing = await Act(agent, "editCell", 99, "age", "5");
        Assert.False(missing.Ok);

        var readOnly = await Act(agent, "editCell", 2, "id", "x");
        Assert.False(readOnly.Ok);
        Assert.Contains("not editable", readOnly.Error);

        var column = await Act(agent, "editCell", 2, "nope", "x");
        Assert.False(column.Ok);
    }

    [Fact]
    public async Task AssignColumn_maps_by_header_text_or_index_and_explains_failures()
    {
        var (agent, importer, _, _) = Setup("Nom;Years;Team\nAda;36;Engine", requireYears: false);

        var byText = await Act(agent, "assignColumn", "Name", "nom");
        Assert.True(byText.Ok, byText.Error);
        Assert.Equal(0, importer.Fields.Single(f => f.Column.Key == "name").HeaderIndex);

        var byIndex = await Act(agent, "assignColumn", "Age", Json("2"));
        Assert.True(byIndex.Ok, byIndex.Error);
        Assert.Equal(2, importer.Fields.Single(f => f.Column.Key == "age").HeaderIndex);

        var unknownHeader = await Act(agent, "assignColumn", "Name", "Missing");
        Assert.False(unknownHeader.Ok);
        Assert.Contains("headed \"Missing\"", unknownHeader.Error);

        var outOfRange = await Act(agent, "assignColumn", "Name", 9);
        Assert.False(outOfRange.Ok);
        Assert.Contains("index 9", outOfRange.Error);

        var unknownField = await Act(agent, "assignColumn", "Salary", "Nom");
        Assert.False(unknownField.Ok);
        Assert.Contains("not a column", unknownField.Error);
    }

    [Fact]
    public async Task AssignColumn_with_a_duplicated_header_asks_for_the_index()
    {
        var (agent, importer, _, _) = Setup("Nom;Nom;Team\nAda;Grace;Engine");

        var ambiguous = await Act(agent, "assignColumn", "Name", "Nom");
        Assert.False(ambiguous.Ok);
        Assert.Contains("indexes 0, 1", ambiguous.Error);

        Assert.True((await Act(agent, "assignColumn", "Name", 1)).Ok);
        Assert.Equal(1, importer.Fields.Single(f => f.Column.Key == "name").HeaderIndex);
    }

    [Fact]
    public async Task IgnoreColumn_and_ResetColumn_change_the_mapping_and_undo_it()
    {
        var (agent, importer, _, _) = Setup(requireYears: true);

        Assert.True((await Act(agent, "ignoreColumn", "Age")).Ok);
        Assert.Equal(SpreadsheetColumnStatus.Missing, importer.Fields.Single(f => f.Column.Key == "age").Status);
        Assert.Equal(["Age"], Assert.IsType<AgentSpreadsheetSummary>(agent.Snapshot().Single().Value).UnmappedRequiredColumns);

        Assert.True((await Act(agent, "resetColumn", "Age")).Ok);
        Assert.Equal(SpreadsheetColumnStatus.Matched, importer.Fields.Single(f => f.Column.Key == "age").Status);

        Assert.False((await Act(agent, "ignoreColumn", "Salary")).Ok);
    }

    [Fact]
    public async Task AnswerValue_maps_skips_leaves_empty_and_refuses_unknown_choices()
    {
        var (agent, importer, _, _) = Setup();

        var badChoice = await Act(agent, "answerValue", "Team", "Kernel", "map", "payroll");
        Assert.False(badChoice.Ok);
        Assert.Contains("not one of the choices", badChoice.Error);

        var noQuestion = await Act(agent, "answerValue", "Team", "Hardware", "map", "ops");
        Assert.False(noQuestion.Ok);
        Assert.Contains("no question", noQuestion.Error);

        var badAnswer = await Act(agent, "answerValue", "Team", "Kernel", "delete");
        Assert.False(badAnswer.Ok);

        var cannotCreate = await Act(agent, "answerValue", "Team", "Kernel", "create");
        Assert.False(cannotCreate.Ok);
        Assert.Contains("cannot be created", cannotCreate.Error);

        var mapped = await Act(agent, "answerValue", "Team", " KERNEL ", "map", "Operations");
        Assert.True(mapped.Ok, mapped.Error);
        Assert.Empty(importer.OpenQuestions);
        Assert.Equal("ops", importer.Questions.Single().Answer!.MappedValue);

        var skipped = await Act(agent, "answerValue", "Team", "Kernel", "skipRows");
        Assert.True(skipped.Ok, skipped.Error);
        Assert.Equal(SpreadsheetValueAnswerAction.SkipRows, importer.Questions.Single().Answer!.Action);
        Assert.Contains(importer.AllRows, r => r.RowNumber == 4 && r.DiscardReason is not null);

        var empty = await Act(agent, "answerValue", "Team", "Kernel", "leaveEmpty");
        Assert.True(empty.Ok, empty.Error);
    }

    [Fact]
    public async Task AnswerValue_create_works_when_the_column_can_create_options()
    {
        var (agent, importer, _, _) = Setup(create: text => new SpreadsheetOption(text.ToLowerInvariant(), text));

        var created = await Act(agent, "answerValue", "Team", "Kernel", "create");

        Assert.True(created.Ok, created.Error);
        Assert.Equal(SpreadsheetValueAnswerAction.Create, importer.Questions.Single().Answer!.Action);
    }

    [Fact]
    public async Task DiscardRows_and_RestoreRows_use_row_numbers_and_change_nothing_on_an_unknown_number()
    {
        var (agent, importer, _, _) = Setup();

        var bad = await Act(agent, "discardRows", 2, 77);
        Assert.False(bad.Ok);
        Assert.Contains("77", bad.Error);
        Assert.Empty(importer.DiscardedRows);

        var observed = new List<ObservedChange>();
        using var sub = agent.Observe(observed.Add);
        var discarded = await Act(agent, "discardRows", Json("[2,3]"));
        Assert.True(discarded.Ok, discarded.Error);
        Assert.Equal([2, 3], importer.DiscardedRows.Select(r => r.RowNumber).Order());
        Assert.Equal("2,3", Assert.Single(observed).Row);

        var restored = await Act(agent, "restoreRows", 3);
        Assert.True(restored.Ok, restored.Error);
        Assert.Equal([2], importer.DiscardedRows.Select(r => r.RowNumber));

        Assert.False((await Act(agent, "discardRows")).Ok);
        Assert.False((await Act(agent, "discardRows", "first")).Ok);
    }

    [Fact]
    public async Task NextStep_that_is_blocked_says_why()
    {
        var (agent, importer, _, _) = Setup("Nom;Years\nAda;36", requireYears: false);
        Assert.Equal("file", importer.CurrentStepKey);
        Assert.True((await Act(agent, "nextStep")).Ok);
        Assert.Equal("columns", importer.CurrentStepKey);

        var blocked = await Act(agent, "nextStep");
        Assert.False(blocked.Ok);
        Assert.Contains("not ready", blocked.Error);

        var back = await Act(agent, "previousStep");
        Assert.True(back.Ok);
        Assert.False((await Act(agent, "previousStep")).Ok);
    }

    [Fact]
    public async Task Submit_that_cannot_run_names_the_blockers_and_a_ready_import_is_sent_once()
    {
        var (agent, importer, _, submitted) = Setup();
        agent.AllowPolicy = (_, _) => Task.FromResult(true);

        var blocked = await Act(agent, "submit");
        Assert.False(blocked.Ok);
        Assert.Contains("unknown values are unanswered", blocked.Error);
        Assert.Empty(submitted);

        Assert.True((await Act(agent, "answerValue", "Team", "Kernel", "map", "ops")).Ok);
        Assert.True((await Act(agent, "discardRows", 3)).Ok);
        var done = await Act(agent, "submit");
        Assert.True(done.Ok, done.Error);
        Assert.Equal(2, Assert.Single(submitted).Count);
        var summary = Assert.IsType<AgentSpreadsheetSummary>(agent.Snapshot().Single().Value);
        Assert.Equal("Done", summary.SubmitStatus);
        Assert.Equal(2, summary.ImportedCount);
    }

    [Fact]
    public async Task Submit_reports_the_error_of_a_callback_that_throws()
    {
        var importer = new SpreadsheetImporter(new SpreadsheetSchema
        {
            Key = "people",
            Columns = [new SpreadsheetColumnSchema { Key = "name" }],
            OnSubmit = _ => throw new InvalidOperationException("quota exceeded"),
        });
        importer.LoadText("Name\nAda", "p.csv");
        var agent = new AgentBridgeService { AllowPolicy = (_, _) => Task.FromResult(true) };
        agent.RegisterSpreadsheet(importer);

        var result = await Act(agent, "submit");

        Assert.False(result.Ok);
        Assert.Contains("quota exceeded", result.Error);
    }

    [Fact]
    public async Task Submit_and_clearFile_reach_the_policy_as_confirmation_required_while_edits_do_not()
    {
        var (agent, importer, _, _) = Setup();
        var seen = new List<(string Action, bool Confirm)>();
        agent.AllowPolicy = (step, confirm) =>
        {
            seen.Add((step.Action, confirm));
            return Task.FromResult(true);
        };

        await Act(agent, "editCell", 2, "name", "Ada L.");
        await Act(agent, "submit");
        await Act(agent, "clearFile");

        Assert.Equal([("editCell", false), ("submit", true), ("clearFile", true)], seen);
        Assert.False(importer.FileLoaded);
    }

    [Fact]
    public async Task A_policy_can_refuse_clearFile_and_the_file_stays_loaded()
    {
        var (agent, importer, _, _) = Setup();
        agent.AllowPolicy = (_, confirm) => Task.FromResult(!confirm);

        var result = await Act(agent, "clearFile");

        Assert.False(result.Ok);
        Assert.Contains("refused by policy", result.Error);
        Assert.True(importer.FileLoaded);
    }
}
