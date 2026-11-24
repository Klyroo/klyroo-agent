using Lux.Agent;
using Lux.Form;
using Lux.Dashboard;
using Lux.Spreadsheet;

var bridge = new AgentBridgeService();
var form = new FormController(new FormSchema
{
    Title = "Demo",
    Fields = [new FormFieldSchema { Key = "name", Label = "Project name", DefaultValue = "Apollo" }]
});
bridge.RegisterForm(form);
await bridge.ActAsync([new AgentActStep("Project name", "setValue", ["LuxStack"])]);
Console.WriteLine($"Agent updated projectName to: {form.GetValue<string>("name")}");
