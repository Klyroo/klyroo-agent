using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class FormVisibilityTests
{
    private static async Task<AgentActResult> Set(AgentBridgeService agent, string label, AgentArgument value) =>
        await agent.ActAsync([new AgentActStep(label, "setValue", [value])]);

    [Fact]
    public async Task A_Hidden_field_is_not_listed_queried_addressed_or_written()
    {
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "f", Title = "F",
            Fields =
            [
                new DynamicFieldSchema { Key = "name", Label = "Name" },
                new DynamicFieldSchema { Key = "tenant", Label = "Tenant", Kind = FormFieldKind.Hidden, DefaultValue = "acme-42" },
            ],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);

        Assert.DoesNotContain(agent.Snapshot(includeOffPage: true), c => c.Label == "Tenant");
        Assert.DoesNotContain(agent.Query(), m => m.Label == "Tenant");
        Assert.Empty(agent.Query(new AgentQueryRequest { Within = "Tenant" }));
        Assert.DoesNotContain("acme-42", string.Join("|", agent.Snapshot(true).Select(c => c.Value)));

        var write = await Set(agent, "Tenant", "other");
        Assert.False(write.Ok);
        Assert.Contains("not found", write.Error);
        Assert.Equal("acme-42", form.GetValue("tenant"));
    }

    [Fact]
    public async Task Fields_in_a_hidden_container_or_tab_are_neither_readable_nor_writable()
    {
        var showGroup = false;
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "f", Title = "F",
            Fields =
            [
                new DynamicFieldSchema
                {
                    Key = "g", Kind = FormFieldKind.Group, Condition = _ => showGroup,
                    Fields = [new DynamicFieldSchema { Key = "inner", Label = "Inner", DefaultValue = "secret" }],
                },
                new DynamicFieldSchema
                {
                    Key = "t", Kind = FormFieldKind.Tabs,
                    Tabs =
                    [
                        new DynamicTabSchema { Key = "a", Label = "A", Fields = [new DynamicFieldSchema { Key = "tabbed", Label = "Tabbed", DefaultValue = "shown" }] },
                        new DynamicTabSchema { Key = "b", Label = "B", Condition = _ => false, Fields = [new DynamicFieldSchema { Key = "tabHidden", Label = "Tab hidden", DefaultValue = "nope" }] },
                    ],
                },
            ],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);

        var labels = agent.Snapshot(includeOffPage: true).Select(c => c.Label).ToList();
        Assert.DoesNotContain("Inner", labels);
        Assert.DoesNotContain("Tab hidden", labels);
        Assert.Contains("Tabbed", labels);
        Assert.Empty(agent.Query(new AgentQueryRequest { Within = "Inner" }));

        var write = await Set(agent, "Inner", "x");
        Assert.False(write.Ok);
        Assert.Contains("not displayed", write.Error);
        Assert.Equal("secret", form.GetValue("inner"));
        Assert.False((await Set(agent, "Tab hidden", "x")).Ok);

        showGroup = true;
        Assert.Contains(agent.Snapshot().Select(c => c.Label), l => l == "Inner");
        Assert.True((await Set(agent, "Inner", "now")).Ok);
    }

    [Fact]
    public async Task A_skipped_step_hides_its_fields_while_another_step_in_effect_is_off_page()
    {
        var skipSecond = true;
        var form = new DynamicFormController(new DynamicFormSchema
        {
            FormKey = "w", Title = "W",
            Steps =
            [
                new DynamicStepSchema { Key = "one", Title = "One", Fields = [new DynamicFieldSchema { Key = "a", Label = "A" }] },
                new DynamicStepSchema { Key = "two", Title = "Two", SkipWhen = _ => skipSecond, Fields = [new DynamicFieldSchema { Key = "b", Label = "B", DefaultValue = "private" }] },
                new DynamicStepSchema { Key = "three", Title = "Three", Fields = [new DynamicFieldSchema { Key = "c", Label = "C" }] },
            ],
        });
        var agent = new AgentBridgeService();
        agent.RegisterForm(form, confirmSubmit: false);

        var all = agent.Snapshot(includeOffPage: true);
        Assert.DoesNotContain(all, c => c.Label == "B");
        Assert.False(Assert.Single(all, c => c.Label == "A").OffPage);
        Assert.True(Assert.Single(all, c => c.Label == "C").OffPage);
        Assert.DoesNotContain(agent.Snapshot(includeOffPage: false), c => c.Label == "C");
        Assert.DoesNotContain(agent.Query(), m => m.Label is "B" or "C");

        var write = await Set(agent, "B", "x");
        Assert.False(write.Ok);
        Assert.Equal("private", form.GetValue("b"));

        skipSecond = false;
        Assert.Contains(agent.Snapshot(includeOffPage: true), c => c.Label == "B");
    }
}
