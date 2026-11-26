using Klyroo.Agent;
using Klyroo.Form;

var schema = FormSchema.For<Project>(s =>
{
    s.FormKey = "project";
    s.Title = "Demo";
    s.Field(p => p.Name, f => f.Label("Project name").Required());
    s.Field(p => p.Seats, f => f.Range(1, 500));
});
var form = new FormController<Project>(schema, new Project());
var nameField = schema.Field(p => p.Name);
var seatsField = schema.Field(p => p.Seats);

var bridge = new AgentBridgeService();
bridge.RegisterForm(form);

var result = await bridge.ActAsync(
[
    new AgentActStep("Project name", "setValue", ["Klyroo"]),
    new AgentActStep("Seats", "setValue", [12]),
]);

Console.WriteLine($"Agent run ok: {result.Ok}");
Console.WriteLine($"Project name: {form.Get(nameField)}, seats: {form.Get(seatsField)}");

// A value the model cannot give is refused with the reason, and nothing changes.
var refused = await bridge.ActAsync([new AgentActStep("Seats", "setValue", [1000])]);
Console.WriteLine($"Seats 1000: ok={refused.Ok}, {refused.Error}");

// Submitting is a step that needs confirmation, so it is refused until a policy approves it.
var submit = await bridge.ActAsync([new AgentActStep("Demo", "submit")]);
Console.WriteLine($"Submit without a policy: ok={submit.Ok}, {submit.Error}");

public sealed class Project
{
    public string Name { get; set; } = "Apollo";

    public int Seats { get; set; } = 1;
}
