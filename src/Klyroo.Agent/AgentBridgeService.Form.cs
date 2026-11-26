using System.Globalization;

namespace Klyroo.Agent;

public sealed partial class AgentBridgeService
{
    /// <summary>
    /// Registers every stateful field of a form, plus the form itself. Fields become <c>input</c>, <c>select</c>,
    /// <c>checkbox</c> or <c>number</c> components; the form accepts <c>submit</c>, <c>reset</c> and, for stepped
    /// forms, <c>nextStep</c> and <c>previousStep</c>. Password and one-time-code fields are always redacted.
    /// Hidden fields are never exposed. A field that is not in effect, because of its condition, a hidden container,
    /// section or tab, or a skipped step (the rule <see cref="FormController.ActiveFields"/> uses for the output), is left
    /// out of <see cref="Snapshot"/> and <see cref="Query"/> and refuses every step. A field of another step that is in
    /// effect is listed only when <see cref="Snapshot"/> is asked for off-page components.
    /// Registering the same form twice creates duplicate labels, which <see cref="ActAsync"/> rejects as ambiguous.
    /// </summary>
    /// <param name="form">The form to expose.</param>
    /// <param name="confirmSubmit">
    /// Marks the form component as confirmation-required for every action on the form (<c>submit</c>, <c>reset</c> and
    /// step navigation): the action is passed to <see cref="AllowPolicy"/> with the flag set, and refused when no policy is set.
    /// It is <see langword="true"/> by default, so a form is not submitted by an agent until a policy approves it;
    /// pass <see langword="false"/> to let an agent submit freely.
    /// </param>
    /// <returns>A handle that removes the form's components when disposed, for example when the form closes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="form"/> is <see langword="null"/>.</exception>
    public IDisposable RegisterForm(FormController form, bool confirmSubmit = true)
    {
        ArgumentNullException.ThrowIfNull(form);
        var added = new List<BoundTarget>();
        foreach (var field in form.AllFields)
        {
            // Hidden fields hold values the server intends; the agent neither reads nor writes them.
            if (FormController.IsStateless(field.Kind) || field.Kind == FormFieldKind.Hidden) continue;
            var fieldRef = NextRef();
            var isPassword = field.Kind is FormFieldKind.Password or FormFieldKind.OneTimeCode;
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
                Ref = fieldRef,
                Component = componentKind,
                Label = field.Label ?? field.Key,
                Sensitive = isPassword,
                IsDisabled = () => form.IsFieldDisabled(field),
                IsHidden = () => !form.ActiveFields.Contains(field),
                IsOffPage = () => IsOnAnotherStep(form, field),
                GetValue = () => isPassword ? RedactedValue : form.GetValue(field.Key),
                Actions = actions,
                GetChoices = () => form.GetFieldOptions(field)
                    .Select(o => new AgentChoiceItem(
                        o.Value,
                        o.Label,
                        Selected: Equals(form.GetValue(field.Key)?.ToString(), o.Value),
                        Disabled: o.Disabled))
                    .ToList(),
                ExecuteAction = (action, args) =>
                {
                    switch (action)
                    {
                        case "setValue":
                            // The model's argument is coerced to the type the field kind stores, or the step fails with a reason.
                            if (!FormValueCoercion.TryCoerce(field, form.GetFieldOptions(field), args.Count > 0 ? args[0] : null, out var coerced, out var reason))
                            {
                                return Task.FromResult(StepResult.Fail(reason ?? "the value does not fit the field."));
                            }
                            form.SetValue(field.Key, coerced, markTouched: true);
                            return Task.FromResult<StepResult>(true);
                        case "clear":
                            if (FormController.IsContainer(field.Kind) || field.Kind is FormFieldKind.ArrayList or FormFieldKind.ArrayTable)
                            {
                                return Task.FromResult(StepResult.Fail($"\"{field.Key}\" has no value to clear."));
                            }
                            form.SetValue(field.Key, FormValueCoercion.EmptyValue(field), markTouched: true);
                            return Task.FromResult<StepResult>(true);
                        case "toggle":
                            var cur = form.GetValue<bool>(field.Key);
                            form.SetValue(field.Key, !cur, markTouched: true);
                            return Task.FromResult<StepResult>(true);
                        case "reset":
                            form.ResetField(field.Key);
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
            Label = form.Schema.Title ?? form.Schema.FormKey,
            ConfirmRequired = confirmSubmit,
            GetValue = () => form.IsDirty ? "dirty" : "clean",
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

    // A field in effect that belongs to a step other than the current one: it is not displayed now.
    private static bool IsOnAnotherStep(FormController form, FormFieldSchema field)
    {
        if (!form.IsStepped || form.CurrentStep is not { } step) return false;
        return !Leaves(step.Fields).Contains(field);
    }

    private static IEnumerable<FormFieldSchema> Leaves(IEnumerable<FormFieldSchema> fields)
    {
        foreach (var f in fields)
        {
            if (f.Kind == FormFieldKind.Tabs)
            {
                foreach (var tab in f.Tabs ?? []) foreach (var child in Leaves(tab.Fields)) yield return child;
            }
            else if (FormController.IsContainer(f.Kind))
            {
                foreach (var child in Leaves(f.Fields ?? [])) yield return child;
            }
            else
            {
                yield return f;
            }
        }
    }
}
