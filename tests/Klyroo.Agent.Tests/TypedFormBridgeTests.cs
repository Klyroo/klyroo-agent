using System.Text.Json;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class Account
{
    public string Name { get; set; } = "Ada";

    public long Seats { get; set; } = 3;

    public decimal Budget { get; set; }

    public bool Newsletter { get; set; }

    public string Plan { get; set; } = "free";

    public string Password { get; set; } = "hunter2";

    public string Tenant { get; set; } = "acme-42";

    public bool IsCompany { get; set; }

    public string Company { get; set; } = "";

    public string Locked { get; set; } = "fixed";

    public List<string> Tags { get; set; } = ["new"];

    public string Phone { get; set; } = "";
}

/// <summary>The bridge over <c>FormController&lt;TModel&gt;</c>, through <see cref="IFormRuntime"/>, with the rules the dynamic controller has.</summary>
public sealed class TypedFormBridgeTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // The handle of the multiple choice, per schema: a schema is built by the lambda, which is where MultiSelect returns it.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FormSchema<Account>, FormField<Account, IReadOnlyList<string>>> TagHandles = [];

    private static FormSchema<Account> Schema(Func<FormController<Account>, bool>? companyVisible = null)
    {
        FormField<Account, IReadOnlyList<string>>? tags = null;
        var schema = FormSchema.For<Account>(s =>
    {
        s.FormKey = "account";
        s.Title = "Account";
        s.Field(a => a.Name, f => f.Required());
        s.Field(a => a.Seats, f => f.Range(1L, 500L));
        s.Field(a => a.Budget);
        s.Field(a => a.Newsletter);
        s.Field(a => a.Plan, f => f.Options([new FormOption<string>("free", "Gratuit"), new FormOption<string>("pro", "Pro"), new FormOption<string>("old", "Old", Disabled: true)]));
        s.Field(a => a.Password, f => f.AsPassword());
        s.Field(a => a.Tenant, f => f.Kind(FormFieldKind.Hidden));
        var isCompany = s.Field(a => a.IsCompany);
        s.Field(a => a.Company, f => f.VisibleWhen(companyVisible ?? (c => c.Get(isCompany))));
        s.Field(a => a.Locked, f => f.DisabledWhen(_ => true));
        tags = s.MultiSelect(a => a.Tags, [new FormOption<string>("new", "New"), new FormOption<string>("vip", "VIP"), new FormOption<string>("late", "Late")], f => f.Label("Tags"));
    });
        TagHandles.Add(schema, tags!);
        return schema;
    }

    private static FormField<Account, IReadOnlyList<string>> Tags(FormController<Account> form) => TagHandles.TryGetValue(form.Schema, out var handle) ? handle : throw new InvalidOperationException();

    private static (AgentBridgeService Agent, FormController<Account> Form, FormSchema<Account> Schema) Setup(bool confirmSubmit = false)
    {
        var schema = Schema();
        var form = new FormController<Account>(schema, new Account());
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit);
        return (agent, form, schema);
    }

    private static Task<AgentActResult> Set(AgentBridgeService agent, string label, AgentArgument value) =>
        agent.ActAsync([new AgentActStep(label, "setValue", [value])]);

    [Fact]
    public void A_typed_controller_is_listed_with_the_kinds_of_its_members_and_without_its_hidden_input()
    {
        var (agent, _, _) = Setup();

        var snapshot = agent.Snapshot(includeOffPage: true);

        Assert.Equivalent(
            new[] { "Name", "Seats", "Budget", "Newsletter", "Plan", "Password", "Is company", "Locked", "Tags", "Account" },
            snapshot.Select(c => c.Label));
        Assert.DoesNotContain(snapshot, c => c.Label is "Tenant" or "Company");
        Assert.Equal("input", snapshot.Single(c => c.Label == "Name").Component);
        Assert.Equal("number", snapshot.Single(c => c.Label == "Seats").Component);
        Assert.Equal("checkbox", snapshot.Single(c => c.Label == "Newsletter").Component);
        Assert.Equal("select", snapshot.Single(c => c.Label == "Plan").Component);
        Assert.Equal(AgentValue.From("Ada"), snapshot.Single(c => c.Label == "Name").Value);
        Assert.Equal(new AgentNumberValue("3"), snapshot.Single(c => c.Label == "Seats").Value);
        Assert.Equal(AgentValue.From(false), snapshot.Single(c => c.Label == "Newsletter").Value);
        Assert.Equal(AgentValue.From("clean"), snapshot.Single(c => c.Label == "Account").Value);
        Assert.Equal(["submit", "reset"], snapshot.Single(c => c.Label == "Account").Actions);
    }

    [Fact]
    public async Task A_choice_is_matched_by_value_or_by_label_and_a_disabled_option_is_refused()
    {
        var (agent, form, schema) = Setup();

        Assert.True((await Set(agent, "Plan", "Pro")).Ok);
        Assert.Equal("pro", form.Get(schema.Field(a => a.Plan)));

        Assert.True((await Set(agent, "Plan", "free")).Ok);
        Assert.Equal("free", form.Get(schema.Field(a => a.Plan)));

        var disabled = await Set(agent, "Plan", "old");
        Assert.False(disabled.Ok);
        Assert.Equal("free", form.Get(schema.Field(a => a.Plan)));

        var unknown = await Set(agent, "Plan", "enterprise");
        Assert.False(unknown.Ok);
        Assert.Contains("Plan", unknown.Error);
    }

    [Fact]
    public async Task A_long_and_a_decimal_keep_every_digit_through_the_bridge()
    {
        var (agent, form, schema) = Setup();

        var big = await Set(agent, "Seats", Json("500"));
        Assert.True(big.Ok, big.Error);
        Assert.Equal(500L, form.Get(schema.Field(a => a.Seats)));

        var budget = await Set(agent, "Budget", Json("1234567890123456789.1234567890"));
        Assert.True(budget.Ok, budget.Error);
        Assert.Equal(1234567890123456789.1234567890m, form.Get(schema.Field(a => a.Budget)));
        // The value the agent reads back has the same digits, not a rounded double.
        Assert.Equal(new AgentNumberValue("1234567890123456789.1234567890"), budget.Values.Values.Single());
        Assert.Equal(1234567890123456789.1234567890m, form.CreateModel().Budget);
    }

    [Theory]
    [InlineData("\"Seats\"", "2.5")]
    [InlineData("\"Seats\"", "501")]
    [InlineData("\"Seats\"", "0")]
    [InlineData("\"Seats\"", "\"many\"")]
    [InlineData("\"Seats\"", "true")]
    public async Task A_number_that_does_not_fit_the_member_is_refused_with_a_reason_and_changes_nothing(string label, string json)
    {
        var (agent, form, schema) = Setup();

        var result = await Set(agent, JsonSerializer.Deserialize<string>(label)!, Json(json));

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal(3L, form.Get(schema.Field(a => a.Seats)));
    }

    [Fact]
    public async Task A_number_past_the_range_of_a_long_is_refused()
    {
        var (agent, form, schema) = Setup();

        var result = await Set(agent, "Seats", Json("9223372036854775808"));

        Assert.False(result.Ok);
        Assert.Equal(3L, form.Get(schema.Field(a => a.Seats)));
    }

    [Fact]
    public async Task Sensitive_fields_are_never_exposed_in_a_snapshot_a_result_or_an_observed_change()
    {
        var (agent, form, _) = Setup();
        var observed = new List<ObservedChange>();
        using var subscription = agent.Observe(observed.Add);

        var snapshot = agent.Snapshot().Single(c => c.Label == "Password");
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), snapshot.Value);

        var result = await Set(agent, "Password", "s3cret!");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), result.Values.Values.Single());
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), Assert.Single(result.Steps).Value);
        var change = Assert.Single(observed);
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), change.Previous);
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), change.Value);
        Assert.Equal("s3cret!", form.CreateModel().Password);
        Assert.DoesNotContain("hunter2", string.Join("|", agent.Snapshot().Select(c => c.Value?.ToDisplayString())));
        Assert.DoesNotContain("s3cret!", string.Join("|", agent.Snapshot().Select(c => c.Value?.ToDisplayString())));
    }

    [Fact]
    public async Task A_hidden_input_is_not_listed_queried_addressed_or_written()
    {
        var (agent, form, schema) = Setup();

        Assert.DoesNotContain(agent.Snapshot(includeOffPage: true), c => c.Label == "Tenant");
        Assert.DoesNotContain(agent.Query(), m => m.Label == "Tenant");
        Assert.Empty(agent.Query(new AgentQueryRequest { Within = "Tenant" }));

        var write = await Set(agent, "Tenant", "other");
        Assert.False(write.Ok);
        Assert.Contains("not found", write.Error);
        Assert.Equal("acme-42", form.Get(schema.Field(a => a.Tenant)));
    }

    [Fact]
    public async Task A_field_whose_condition_is_false_is_not_in_effect_until_the_condition_holds()
    {
        var (agent, form, schema) = Setup();

        Assert.DoesNotContain(agent.Snapshot(includeOffPage: true), c => c.Label == "Company");
        Assert.Empty(agent.Query(new AgentQueryRequest { Within = "Company" }));
        var refused = await Set(agent, "Company", "Acme");
        Assert.False(refused.Ok);
        Assert.Contains("not displayed", refused.Error);
        Assert.Equal("", form.Get(schema.Field(a => a.Company)));

        Assert.True((await Set(agent, "Is company", true)).Ok);

        Assert.Contains(agent.Snapshot(), c => c.Label == "Company");
        Assert.True((await Set(agent, "Company", "Acme")).Ok);
        Assert.Equal("Acme", form.Get(schema.Field(a => a.Company)));
    }

    [Fact]
    public async Task A_disabled_field_is_refused_by_every_action_and_reported_as_disabled()
    {
        var (agent, form, schema) = Setup();

        Assert.True(agent.Snapshot().Single(c => c.Label == "Locked").Disabled);
        foreach (var action in new[] { "setValue", "clear", "reset" })
        {
            var result = await agent.ActAsync([new AgentActStep("Locked", action, action == "setValue" ? ["changed"] : null)]);
            Assert.False(result.Ok);
            Assert.Contains("disabled", result.Error);
        }
        Assert.Equal("fixed", form.Get(schema.Field(a => a.Locked)));
    }

    [Fact]
    public async Task A_field_of_another_step_is_off_page_and_a_skipped_step_is_hidden()
    {
        var skipSecond = true;
        var schema = FormSchema.For<Account>(s =>
        {
            s.FormKey = "wizard";
            s.Title = "Wizard";
            s.Step("one", "One", st => st.Field(a => a.Name));
            s.Step("two", "Two", st => st.Field(a => a.Phone), skipWhen: _ => skipSecond);
            s.Step("three", "Three", st => st.Field(a => a.Company));
        });
        var form = new FormController<Account>(schema, new Account());
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);

        var all = agent.Snapshot(includeOffPage: true);
        Assert.DoesNotContain(all, c => c.Label == "Phone");
        Assert.False(Assert.Single(all, c => c.Label == "Name").OffPage);
        Assert.True(Assert.Single(all, c => c.Label == "Company").OffPage);
        Assert.DoesNotContain(agent.Snapshot(includeOffPage: false), c => c.Label == "Company");
        Assert.DoesNotContain(agent.Query(), m => m.Label is "Phone" or "Company");
        Assert.Contains("nextStep", all.Single(c => c.Label == "Wizard").Actions);

        var write = await Set(agent, "Phone", "x");
        Assert.False(write.Ok);

        skipSecond = false;
        Assert.Contains(agent.Snapshot(includeOffPage: true), c => c.Label == "Phone");
        Assert.True((await agent.ActAsync([new AgentActStep("Wizard", "nextStep")])).Ok);
        Assert.Equal("two", form.CurrentStepKey);
        Assert.False(Assert.Single(agent.Snapshot(), c => c.Label == "Phone").OffPage);
    }

    [Fact]
    public async Task Submit_is_refused_by_default_and_runs_once_a_policy_approves_it()
    {
        var (agent, form, schema) = Setup(confirmSubmit: true);

        var refused = await agent.ActAsync([new AgentActStep("Account", "submit")]);
        Assert.False(refused.Ok);
        Assert.Contains("needs confirmation", refused.Error);

        Assert.True((await Set(agent, "Name", "")).Ok);
        var invalid = await agent.ActAsync([new AgentActStep("Account", "submit")], (_, _) => Task.FromResult(true));
        Assert.False(invalid.Ok);
        Assert.NotNull(form.GetError(schema.Field(a => a.Name)));

        Assert.True((await Set(agent, "Name", "Grace")).Ok);
        var done = await agent.ActAsync([new AgentActStep("Account", "submit")], (_, _) => Task.FromResult(true));
        Assert.True(done.Ok, done.Error);
        Assert.False(form.IsDirty);
    }

    [Fact]
    public async Task Clear_toggle_and_reset_drive_the_typed_fields()
    {
        var (agent, form, schema) = Setup();

        Assert.True((await agent.ActAsync([new AgentActStep("Newsletter", "toggle")])).Ok);
        Assert.True(form.Get(schema.Field(a => a.Newsletter)));

        Assert.True((await Set(agent, "Tags", new[] { "vip", "late" })).Ok);
        Assert.Equal(["vip", "late"], form.Get(Tags(form)));

        Assert.True((await agent.ActAsync([new AgentActStep("Tags", "clear")])).Ok);
        Assert.Empty(form.Get(Tags(form))!);

        Assert.True((await Set(agent, "Name", "Linus")).Ok);
        Assert.True((await agent.ActAsync([new AgentActStep("Name", "reset")])).Ok);
        Assert.Equal("Ada", form.Get(schema.Field(a => a.Name)));
    }

    [Fact]
    public async Task The_choices_of_a_single_and_a_multiple_choice_report_what_is_selected()
    {
        var (agent, _, _) = Setup();
        Assert.True((await Set(agent, "Tags", new[] { "vip", "new" })).Ok);

        var plan = Assert.Single(agent.Query(new AgentQueryRequest { Within = "Plan" })).Items;
        Assert.Equal(["free"], plan.Where(i => i.Selected).Select(i => i.Value));
        Assert.True(plan.Single(i => i.Value == "old").Disabled);

        var tags = Assert.Single(agent.Query(new AgentQueryRequest { Within = "Tags" })).Items;
        Assert.Equal(["new", "vip"], tags.Where(i => i.Selected).Select(i => i.Value));
    }

    [Fact]
    public async Task An_observer_that_throws_does_not_stop_the_steps_or_the_other_observers_on_a_typed_form()
    {
        var (agent, form, schema) = Setup();
        var seen = new List<ObservedChange>();
        using var failing = agent.Observe(_ => throw new InvalidOperationException("observer failure"));
        using var healthy = agent.Observe(seen.Add);

        var result = await agent.ActAsync([
            new AgentActStep("Name", "setValue", ["Grace"]),
            new AgentActStep("Seats", "setValue", [7]),
        ]);

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, seen.Count);
        Assert.Equal("Grace", form.Get(schema.Field(a => a.Name)));
        Assert.Equal(7L, form.Get(schema.Field(a => a.Seats)));
    }

    [Fact]
    public async Task A_dynamic_and_a_typed_form_answer_the_same_calls_alike()
    {
        var typed = Setup().Agent;
        var dynamicAgent = new AgentBridgeService();
        dynamicAgent.RegisterForm(new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "account",
            Title = "Account",
            Fields =
            [
                new DynamicFieldSchema { Key = "Name", Label = "Name", DefaultValue = "Ada" },
                new DynamicFieldSchema { Key = "Plan", Label = "Plan", Kind = FormFieldKind.Select, DefaultValue = "free", Options = [new("free", "Gratuit"), new("pro", "Pro")] },
            ],
        }), confirmSubmit: false);

        foreach (var agent in new[] { typed, dynamicAgent })
        {
            var result = await agent.ActAsync([new AgentActStep("Name", "setValue", ["Grace"]), new AgentActStep("Plan", "setValue", ["Pro"])]);
            Assert.True(result.Ok, result.Error);
            Assert.Equal(AgentValue.From("Grace"), agent.Snapshot().Single(c => c.Label == "Name").Value);
            Assert.Equal(AgentValue.From("pro"), agent.Snapshot().Single(c => c.Label == "Plan").Value);
        }
    }

    // A form the engine does not know: whatever implements IFormRuntime can be registered, and the reason it gives for a refused write reaches the model.
    private sealed class StubField(string key, FormWriteResult onSet) : IFormFieldAccess
    {
        public string Key => key;
        public FormFieldKind Kind => FormFieldKind.Text;
        public string Label => key;
        public bool Sensitive => false;
        public bool IsHidden => false;
        public bool IsOffPage => false;
        public bool IsDisabled => false;
        public bool IsRequired => false;
        public IReadOnlyList<FormOptionItem> Options => [];
        public void WriteValue(System.Text.Json.Utf8JsonWriter writer) => writer.WriteStringValue("x");
        public FormWriteResult TrySet(JsonElement value) => onSet;
        public FormWriteResult Clear() => FormWriteResult.Success;
        public FormWriteResult Toggle() => FormWriteResult.Fail("not a switch.");
        public void Reset() { }
    }

    private sealed class StubRuntime(params IFormFieldAccess[] fields) : IFormRuntime
    {
        public string FormKey => "stub";
        public string? Title => null;
        public bool IsDirty => false;
        public bool IsStepped => false;
        public string? CurrentStepKey => null;
        public IReadOnlyList<IFormFieldAccess> Fields => fields;
        public event Action? Changed { add { } remove { } }
        public Task<bool> SubmitAsync() => Task.FromResult(true);
        public void Reset() { }
        public Task<bool> NextStepAsync() => Task.FromResult(false);
        public Task<bool> PreviousStepAsync() => Task.FromResult(false);
    }

    [Fact]
    public async Task Any_IFormRuntime_can_be_registered_and_the_reason_of_a_refused_write_reaches_the_model()
    {
        var agent = new AgentBridgeService();
        agent.RegisterForm(new StubRuntime(new StubField("a", FormWriteResult.Fail("must be at most 3 characters.")), new StubField("b", FormWriteResult.Success)));

        var refused = await Set(agent, "a", "toolong");
        Assert.False(refused.Ok);
        Assert.Contains("must be at most 3 characters.", refused.Error);

        var accepted = await Set(agent, "b", "ok");
        Assert.True(accepted.Ok, accepted.Error);
        Assert.Equal(AgentValue.From("x"), Assert.Single(accepted.Steps).Value);
        Assert.Equal("stub", agent.Snapshot().Last().Label);
    }
}
