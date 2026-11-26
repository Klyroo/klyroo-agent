using Xunit;

namespace Klyroo.Agent.Tests;

/// <summary>What the model may write to a form: never a sensitive field unless the application opts in, and only the current step.</summary>
public sealed class FormWriteGuardTests
{
    private sealed class Login
    {
        public string Name { get; set; } = "ada";

        public string Password { get; set; } = "hunter2";

        public string Code { get; set; } = "";
    }

    private static (AgentBridgeService Agent, FormController<Login> Form, FormSchema<Login> Schema) Setup(IReadOnlyCollection<string>? allowSensitiveWrites = null)
    {
        var schema = FormSchema.For<Login>(s =>
        {
            s.FormKey = "login";
            s.Title = "Login";
            s.Field(m => m.Name);
            s.Field(m => m.Password, f => f.AsPassword());
            s.Field(m => m.Code, f => f.AsOneTimeCode());
        });
        var form = new FormController<Login>(schema, new Login());
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false, allowSensitiveWrites: allowSensitiveWrites);
        return (agent, form, schema);
    }

    [Theory]
    [InlineData("Password", "setValue")]
    [InlineData("Password", "clear")]
    [InlineData("Password", "reset")]
    [InlineData("Code", "setValue")]
    public async Task A_sensitive_field_refuses_every_write_by_default_and_never_shows_a_value(string label, string action)
    {
        var (agent, form, schema) = Setup();
        var observed = new List<ObservedChange>();
        using var subscription = agent.Observe(observed.Add);

        var result = await agent.ActAsync([new AgentActStep(label, action, action == "setValue" ? ["s3cret!"] : null)]);

        Assert.False(result.Ok);
        Assert.Contains("sensitive", result.Error);
        Assert.DoesNotContain("s3cret!", result.Error);
        Assert.DoesNotContain("hunter2", result.Error);
        Assert.Empty(observed);
        Assert.Empty(result.Steps);
        Assert.Equal("hunter2", form.Get(schema.Field(m => m.Password)));
        Assert.Equal("", form.Get(schema.Field(m => m.Code)));
    }

    [Fact]
    public async Task A_refused_sensitive_write_is_refused_before_the_policy_is_asked()
    {
        var (agent, _, _) = Setup();
        var asked = 0;
        agent.AllowPolicy = (_, _) =>
        {
            asked++;
            return Task.FromResult(true);
        };

        var result = await agent.ActAsync([new AgentActStep("Password", "setValue", ["x"])]);

        Assert.False(result.Ok);
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task The_opt_in_allows_writes_to_the_named_field_only_and_reads_stay_redacted()
    {
        var (agent, form, schema) = Setup(["Password"]);
        var observed = new List<ObservedChange>();
        using var subscription = agent.Observe(observed.Add);

        var written = await agent.ActAsync([new AgentActStep("Password", "setValue", ["s3cret!"])]);

        Assert.True(written.Ok, written.Error);
        Assert.Equal("s3cret!", form.Get(schema.Field(m => m.Password)));
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), written.Values.Values.Single());
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), Assert.Single(written.Steps).Value);
        var change = Assert.Single(observed);
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), change.Previous);
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), change.Value);
        Assert.DoesNotContain("s3cret!", string.Join("|", agent.Snapshot().Select(c => c.Value?.ToDisplayString())));

        var other = await agent.ActAsync([new AgentActStep("Code", "setValue", ["123456"])]);
        Assert.False(other.Ok);
        Assert.Equal("", form.Get(schema.Field(m => m.Code)));
    }

    [Fact]
    public async Task The_opt_in_is_for_one_registered_form()
    {
        var (_, _, _) = Setup(["Password"]);
        var (agent, form, schema) = Setup();

        var result = await agent.ActAsync([new AgentActStep("Password", "setValue", ["x"])]);

        Assert.False(result.Ok);
        Assert.Equal("hunter2", form.Get(schema.Field(m => m.Password)));
    }

    [Fact]
    public async Task A_dynamic_form_follows_the_same_rule()
    {
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "f", Title = "F",
            Fields = [new DynamicFieldSchema { Key = "pw", Label = "Pw", Kind = FormFieldKind.Password, DefaultValue = "a" }],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form);

        var result = await agent.ActAsync([new AgentActStep("Pw", "setValue", ["b"])]);

        Assert.False(result.Ok);
        Assert.Equal("a", form.GetValue<string>("pw"));
    }

    private sealed class Wizard
    {
        public string Name { get; set; } = "ada";

        public string Email { get; set; } = "";

        public bool Agree { get; set; }
    }

    private static (AgentBridgeService Agent, FormController<Wizard> Form, FormSchema<Wizard> Schema) SetupWizard()
    {
        var schema = FormSchema.For<Wizard>(s =>
        {
            s.FormKey = "wizard";
            s.Title = "Wizard";
            s.Step("one", "One", st => st.Field(m => m.Name, f => f.Required()));
            s.Step("two", "Two", st =>
            {
                st.Field(m => m.Email);
                st.Field(m => m.Agree);
            });
        });
        var form = new FormController<Wizard>(schema, new Wizard { Name = "" });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);
        return (agent, form, schema);
    }

    [Theory]
    [InlineData("Email", "setValue")]
    [InlineData("Email", "clear")]
    [InlineData("Email", "reset")]
    [InlineData("Agree", "toggle")]
    [InlineData("Agree", "setValue")]
    public async Task A_field_of_another_step_refuses_every_write_and_points_to_next_step(string label, string action)
    {
        var (agent, form, schema) = SetupWizard();

        var result = await agent.ActAsync([new AgentActStep(label, action, action == "setValue" ? [label == "Agree" ? true : "a@b.c"] : null)]);

        Assert.False(result.Ok);
        Assert.Contains("nextStep", result.Error);
        Assert.Contains("another step", result.Error);
        Assert.Equal("", form.Get(schema.Field(m => m.Email)));
        Assert.False(form.Get(schema.Field(m => m.Agree)));
    }

    [Fact]
    public async Task Next_step_validates_the_step_and_the_next_step_fields_become_writable()
    {
        var (agent, form, schema) = SetupWizard();

        // The first step is invalid (Name is required): nextStep stays on it, like a user pressing Next.
        var blocked = await agent.ActAsync([new AgentActStep("Wizard", "nextStep")]);
        Assert.False(blocked.Ok);
        Assert.Equal("one", form.CurrentStepKey);
        Assert.False((await agent.ActAsync([new AgentActStep("Email", "setValue", ["a@b.c"])])).Ok);

        Assert.True((await agent.ActAsync([new AgentActStep("Name", "setValue", ["Grace"])])).Ok);
        Assert.True((await agent.ActAsync([new AgentActStep("Wizard", "nextStep")])).Ok);
        Assert.Equal("two", form.CurrentStepKey);
        Assert.True((await agent.ActAsync([new AgentActStep("Email", "setValue", ["a@b.c"])])).Ok);
        Assert.Equal("a@b.c", form.Get(schema.Field(m => m.Email)));
        Assert.False((await agent.ActAsync([new AgentActStep("Name", "setValue", ["Zed"])])).Ok);
    }

    [Fact]
    public void Snapshot_and_query_still_list_an_off_page_field_as_before()
    {
        var (agent, _, _) = SetupWizard();

        Assert.True(Assert.Single(agent.Snapshot(includeOffPage: true), c => c.Label == "Email").OffPage);
        Assert.DoesNotContain(agent.Snapshot(includeOffPage: false), c => c.Label == "Email");
    }
}
