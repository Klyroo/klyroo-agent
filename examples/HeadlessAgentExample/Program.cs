using Klyroo.Agent;
using Klyroo.Form;
using Klyroo.Dashboard;
using Klyroo.Spreadsheet;

var bridge = new AgentBridgeService();
var form = new FormController(new FormSchema
{
    Title = "Demo",
    Fields = [new FormFieldSchema { Key = "name", Label = "Project name", DefaultValue = "Apollo" }]
});
bridge.RegisterForm(form);
await bridge.ActAsync([new AgentActStep("Project name", "setValue", ["Klyroo"])]);
Console.WriteLine($"Agent updated projectName to: {form.GetValue<string>("name")}");
