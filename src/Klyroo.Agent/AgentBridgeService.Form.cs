using System.Buffers;
using System.Text.Json;

namespace Klyroo.Agent;

public sealed partial class AgentBridgeService
{
    /// <summary>
    /// Registers every stateful field of a form, plus the form itself. <paramref name="form"/> is the form seen through
    /// <see cref="IFormRuntime"/>, so a typed <c>FormController&lt;TModel&gt;</c> and a <c>DynamicFormController</c> are exposed the same
    /// way. Fields become <c>input</c>, <c>select</c>, <c>checkbox</c> or <c>number</c> components; the form accepts
    /// <c>submit</c>, <c>reset</c> and, for stepped forms, <c>nextStep</c> and <c>previousStep</c>. Password and
    /// one-time-code fields are always redacted. Hidden inputs are never exposed. A field that is not in effect, because
    /// of its condition, a hidden container, section or tab, or a skipped step (<see cref="IFormFieldAccess.IsHidden"/>),
    /// is left out of <see cref="Snapshot"/> and <see cref="Query"/> and refuses every step. A field that is disabled
    /// refuses every step too, because the form itself would write to it. A field of another step that is in effect
    /// is listed only when <see cref="Snapshot"/> is asked for off-page components.
    /// Registering the same form twice creates duplicate labels, which <see cref="ActAsync"/> rejects as ambiguous.
    /// </summary>
    /// <param name="form">The form to expose: a <c>FormController&lt;TModel&gt;</c> or a <c>DynamicFormController</c>.</param>
    /// <param name="confirmSubmit">
    /// Marks <c>submit</c> and <c>reset</c> (which sends the form, or throws away what was typed) as confirmation-required:
    /// they are passed to <see cref="AllowPolicy"/> with the flag set, and refused when no policy is set. Moving between
    /// steps never needs confirmation. It is <see langword="true"/> by default, so a form is not submitted by an agent
    /// until a policy approves it; pass <see langword="false"/> to let an agent submit freely.
    /// </param>
    /// <returns>A handle that removes the form's components when disposed, for example when the form closes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="form"/> is <see langword="null"/>.</exception>
    public IDisposable RegisterForm(IFormRuntime form, bool confirmSubmit = true)
    {
        ArgumentNullException.ThrowIfNull(form);
        var added = new List<BoundTarget>();
        foreach (var field in form.Fields)
        {
            var isPassword = field.Sensitive;
            var componentKind = field.Kind switch
            {
                FormFieldKind.Select or FormFieldKind.AutoComplete or FormFieldKind.Radio => "select",
                FormFieldKind.Checkbox or FormFieldKind.Switch => "checkbox",
                FormFieldKind.Number or FormFieldKind.Slider => "number",
                _ => "input",
            };
            var actions = componentKind switch
            {
                "select" => new[] { "setValue", "clear", "reset" },
                "checkbox" => new[] { "toggle", "setValue", "clear", "reset" },
                _ => new[] { "setValue", "clear", "reset" },
            };

            added.Add(new BoundTarget
            {
                Ref = NextRef(),
                Component = componentKind,
                Label = field.Label,
                Sensitive = isPassword,
                // The form writes to a disabled field when asked, so the refusal is the bridge's.
                IsDisabled = () => field.IsDisabled,
                IsHidden = () => field.IsHidden,
                IsOffPage = () => field.IsOffPage,
                GetValue = () => isPassword ? AgentValue.From(RedactedValue) : ReadValue(field),
                Actions = actions,
                GetChoices = () =>
                {
                    var current = ReadValue(field);
                    return field.Options
                        .Select(o => new AgentChoiceItem(o.Value, o.Label, Selected: IsChosen(current, o.Value), Disabled: o.Disabled))
                        .ToList();
                },
                ExecuteAction = (action, args) =>
                {
                    switch (action)
                    {
                        case "setValue":
                            // The codec of the field reads the JSON and checks it against the type of the value, or says why not.
                            return Task.FromResult(ToStepResult(field.TrySet(args.Count > 0 ? args[0] : default)));
                        case "clear":
                            return Task.FromResult(ToStepResult(field.Clear()));
                        case "toggle":
                            return Task.FromResult(ToStepResult(field.Toggle()));
                        case "reset":
                            field.Reset();
                            return Task.FromResult<StepResult>(true);
                        default:
                            return Task.FromResult<StepResult>(false);
                    }
                },
            });
        }

        // Also register the form root for submit / reset / nextStep / previousStep
        added.Add(new BoundTarget
        {
            Ref = NextRef(),
            Component = "form",
            Label = form.Title ?? form.FormKey,
            ConfirmActions = confirmSubmit ? ["submit", "reset"] : [],
            GetValue = () => AgentValue.From(form.IsDirty ? "dirty" : "clean"),
            Actions = form.IsStepped ? ["submit", "reset", "nextStep", "previousStep"] : ["submit", "reset"],
            GetChoices = () => [],
            ExecuteAction = async (action, _) =>
            {
                switch (action)
                {
                    case "submit":
                        return await form.SubmitAsync();
                    case "reset":
                        form.Reset();
                        return true;
                    case "nextStep":
                        return await form.NextStepAsync();
                    case "previousStep":
                        return await form.PreviousStepAsync();
                    default:
                        return false;
                }
            },
        });
        return AddTargets(added);
    }

    private static StepResult ToStepResult(FormWriteResult result) =>
        result.Ok ? true : StepResult.Fail(result.Error ?? "the value does not fit the field.");

    // The value of a field as the JSON the form writes for it (a sensitive field writes null).
    private static AgentValue? ReadValue(IFormFieldAccess field)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            field.WriteValue(writer);
        }
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return AgentValue.FromJson(document.RootElement);
    }

    // An option is chosen when the value is that text, or a list that holds it.
    private static bool IsChosen(AgentValue? current, string option) => current switch
    {
        AgentTextValue text => string.Equals(text.Text, option, StringComparison.Ordinal),
        AgentNumberValue number => string.Equals(number.Text, option, StringComparison.Ordinal),
        AgentBooleanValue flag => string.Equals(flag.Flag ? "true" : "false", option, StringComparison.Ordinal),
        AgentListValue list => list.Items.Any(i => IsChosen(i, option)),
        _ => false,
    };
}
