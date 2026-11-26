using System.Globalization;
using System.Text;

namespace Klyroo.Agent;

/// <summary>
/// Exposes Klyroo.Form, Klyroo.Dashboard and Klyroo.Spreadsheet controllers to an AI agent through three
/// operations: <see cref="Snapshot"/> lists what is registered, <see cref="Query"/> lists the choices of a
/// select or filter, and <see cref="ActAsync"/> runs named actions. Register the controllers you want to expose,
/// then call these methods from your tool handlers. The service works on the controllers, not on rendered markup,
/// so it needs no JavaScript interop and behaves the same in every render mode. It is not thread safe: use one
/// instance per user session, which is what <c>AddKlyrooAgent</c> provides.
/// </summary>
public sealed class AgentBridgeService
{
    /// <summary>The text that replaces the value of password and one-time-code fields in snapshots, results and observed changes.</summary>
    public const string RedactedValue = "[redacted]";

    private sealed class BoundTarget
    {
        public required string Ref { get; init; }
        public required string Component { get; init; }
        public required string Label { get; init; }
        public required Func<object?> GetValue { get; init; }
        public required IReadOnlyList<string> Actions { get; init; }
        public required Func<IReadOnlyList<AgentChoiceItem>> GetChoices { get; init; }
        public required Func<string, IReadOnlyList<object?>, Task<bool>> ExecuteAction { get; init; }
        public Func<bool>? IsDisabled { get; init; }
        public Func<bool>? IsOffPage { get; init; }
        public bool Sensitive { get; init; }
        public bool ConfirmRequired { get; init; }
    }

    /// <summary>Creates an empty bridge with no registered components and no <see cref="AllowPolicy"/>.</summary>
    public AgentBridgeService()
    {
    }

    private readonly List<BoundTarget> _targets = [];
    private readonly List<Action<ObservedChange>> _observers = [];
    private int _refCounter;

    /// <summary>
    /// An optional gate called before every <see cref="ActAsync"/> step with the step and whether the target was
    /// registered as confirmation-required. Return <see langword="false"/> to refuse the step. When this is
    /// <see langword="null"/>, steps are never refused, including steps on confirmation-required targets.
    /// </summary>
    public Func<AgentActStep, bool, Task<bool>>? AllowPolicy { get; set; }

    /// <summary>Returns the definitions of the three agent tools, <c>snapshot</c>, <c>query</c> and <c>act</c>, for registration with an LLM or MCP host.</summary>
    /// <returns>Three <see cref="AgentToolDefinition"/> values.</returns>
    public static IReadOnlyList<AgentToolDefinition> GetToolDefinitions() =>
    [
        new("snapshot", "Lists the components on the page, their current state, and available actions.", ReadOnlyHint: true, ConsequentialHint: false, UntrustedContentHint: true),
        new("query", "Finds components by label or kind, and lists choices inside a select or filter without opening popups.", ReadOnlyHint: true, ConsequentialHint: false, UntrustedContentHint: true),
        new("act", "Calls semantic actions on components by ref or label (e.g. setValue, clear, toggle, reset, submit).", ReadOnlyHint: false, ConsequentialHint: true, UntrustedContentHint: true),
    ];

    /// <summary>
    /// Subscribes to the changes made through <see cref="ActAsync"/>. The callback runs synchronously after each
    /// successful step. Changes a user makes directly in the UI are not reported. An exception thrown by the callback
    /// is ignored: the step is already applied, so the run goes on and the other subscribers are still called.
    /// </summary>
    /// <param name="callback">Called with one <see cref="ObservedChange"/> per successful step.</param>
    /// <returns>A handle that removes the subscription when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
    public IDisposable Observe(Action<ObservedChange> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _observers.Add(callback);
        return new ObserverSubscription(_observers, callback);
    }

    /// <summary>
    /// Registers every stateful field of a form, plus the form itself. Fields become <c>input</c>, <c>select</c>,
    /// <c>checkbox</c> or <c>number</c> components; the form accepts <c>submit</c>, <c>reset</c> and, for stepped
    /// forms, <c>nextStep</c> and <c>previousStep</c>. Password and one-time-code fields are always redacted.
    /// Registering the same form twice creates duplicate labels, which <see cref="ActAsync"/> rejects as ambiguous.
    /// </summary>
    /// <param name="form">The form to expose.</param>
    /// <param name="confirmSubmit">
    /// Marks the form component as confirmation-required, which is passed to <see cref="AllowPolicy"/> for every
    /// action on the form (<c>submit</c>, <c>reset</c> and step navigation).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="form"/> is <see langword="null"/>.</exception>
    public void RegisterForm(FormController form, bool confirmSubmit = false)
    {
        ArgumentNullException.ThrowIfNull(form);
        foreach (var field in form.AllFields)
        {
            if (FormController.IsStateless(field.Kind)) continue;
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
                "checkbox" => new[] { "toggle", "setValue", "reset" },
                _ => new[] { "setValue", "clear", "reset" },
            };

            _targets.Add(new BoundTarget
            {
                Ref = fieldRef,
                Component = componentKind,
                Label = field.Label ?? field.Key,
                Sensitive = isPassword,
                IsDisabled = () => form.IsFieldDisabled(field),
                IsOffPage = () => !form.ShouldRenderField(field),
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
                            form.SetValue(field.Key, args.Count > 0 ? args[0] : null, markTouched: true);
                            return Task.FromResult(true);
                        case "clear":
                            form.SetValue(field.Key, field.Kind == FormFieldKind.Checkbox ? false : null, markTouched: true);
                            return Task.FromResult(true);
                        case "toggle":
                            var cur = form.GetValue<bool>(field.Key);
                            form.SetValue(field.Key, !cur, markTouched: true);
                            return Task.FromResult(true);
                        case "reset":
                            form.ResetField(field.Key);
                            return Task.FromResult(true);
                        default:
                            return Task.FromResult(false);
                    }
                },
            });
        }

        // Also register the form root for submit / reset / nextStep / previousStep
        _targets.Add(new BoundTarget
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
    }

    /// <summary>
    /// Registers a dashboard. Each non-headless filter becomes a <c>select</c> component, and the dashboard itself
    /// accepts <c>setView</c>, <c>resetFilters</c> and <c>refresh</c>.
    /// </summary>
    /// <param name="dashboard">The dashboard to expose.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dashboard"/> is <see langword="null"/>.</exception>
    public void RegisterDashboard(DashboardController dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        foreach (var filter in dashboard.Controls)
        {
            if (filter.Headless) continue;
            _targets.Add(new BoundTarget
            {
                Ref = NextRef(),
                Component = "select",
                Label = filter.Label,
                IsDisabled = () => !filter.Enabled,
                GetValue = () => filter.Multiple ? filter.Values : filter.Value,
                Actions = ["setValue", "toggle", "clear", "reset"],
                GetChoices = () => filter.Items
                    .Select(i => new AgentChoiceItem(i.Value, i.Label, filter.IsSelected(i.Value), i.Disabled))
                    .ToList(),
                ExecuteAction = (action, args) =>
                {
                    switch (action)
                    {
                        case "setValue":
                        case "toggle":
                            var val = args.Count > 0 ? args[0]?.ToString() : null;
                            if (val is null) return Task.FromResult(false);
                            filter.Toggle(val);
                            return Task.FromResult(true);
                        case "clear":
                        case "reset":
                            filter.Reset();
                            return Task.FromResult(true);
                        default:
                            return Task.FromResult(false);
                    }
                },
            });
        }

        _targets.Add(new BoundTarget
        {
            Ref = NextRef(),
            Component = "dashboard",
            Label = dashboard.Key,
            GetValue = () => dashboard.CurrentView ?? "default",
            Actions = ["setView", "resetFilters", "refresh"],
            GetChoices = () => dashboard.Views
                .Select(v => new AgentChoiceItem(v.Key, v.Label, Selected: dashboard.CurrentView == v.Key))
                .ToList(),
            ExecuteAction = async (action, args) =>
            {
                switch (action)
                {
                    case "setView":
                        if (args.Count == 0 || args[0] is null) return false;
                        dashboard.CurrentView = args[0]!.ToString();
                        return true;
                    case "resetFilters":
                        dashboard.ResetFilters();
                        return true;
                    case "refresh":
                        await dashboard.RefreshAsync();
                        return true;
                    default:
                        return false;
                }
            },
        });
    }

    /// <summary>
    /// Registers an importer as one <c>spreadsheet</c> component that accepts <c>editCell</c>, <c>nextStep</c>,
    /// <c>previousStep</c>, <c>submit</c> and <c>clearFile</c>. Loading the file itself is not an agent action.
    /// </summary>
    /// <param name="importer">The importer to expose.</param>
    /// <exception cref="ArgumentNullException"><paramref name="importer"/> is <see langword="null"/>.</exception>
    public void RegisterSpreadsheet(SpreadsheetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        _targets.Add(new BoundTarget
        {
            Ref = NextRef(),
            Component = "spreadsheet",
            Label = importer.Schema.Key,
            GetValue = () => importer.CurrentStepKey,
            Actions = ["editCell", "nextStep", "previousStep", "submit", "clearFile"],
            GetChoices = () => [],
            ExecuteAction = async (action, args) =>
            {
                switch (action)
                {
                    case "editCell":
                        if (args.Count < 3) return false;
                        var rIdx = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                        var fKey = args[1]?.ToString() ?? "";
                        var text = args[2]?.ToString() ?? "";
                        importer.EditCell(rIdx, fKey, text);
                        return true;
                    case "nextStep":
                        return importer.NextStep();
                    case "previousStep":
                        return importer.PreviousStep();
                    case "submit":
                        await importer.SubmitAsync();
                        return importer.SubmitStatus == SpreadsheetSubmitStatus.Done;
                    case "clearFile":
                        importer.ClearFile();
                        return true;
                    default:
                        return false;
                }
            },
        });
    }

    /// <summary>Lists the registered components in registration order, with their current value and available actions. At most 100 are returned.</summary>
    /// <param name="includeOffPage">When <see langword="false"/>, components that are not rendered, such as conditional form fields whose condition is false, are left out.</param>
    /// <returns>One <see cref="AgentComponentSnapshot"/> per component.</returns>
    public IReadOnlyList<AgentComponentSnapshot> Snapshot(bool includeOffPage = true)
    {
        var results = new List<AgentComponentSnapshot>();
        foreach (var t in _targets)
        {
            var offPage = t.IsOffPage?.Invoke() ?? false;
            if (!includeOffPage && offPage) continue;
            var val = t.Sensitive ? RedactedValue : t.GetValue();
            results.Add(new AgentComponentSnapshot(
                t.Ref,
                t.Component,
                t.Label,
                val,
                t.Actions,
                Disabled: t.IsDisabled?.Invoke() ?? false,
                OffPage: offPage,
                ConfirmRequired: t.ConfirmRequired));
            if (results.Count >= 100) break;
        }
        return results;
    }

    /// <summary>
    /// Finds components by label, kind or choice text, or lists the choices of one component, without opening any
    /// popup. Matching ignores case and accents.
    /// </summary>
    /// <param name="request">The filters, or <see langword="null"/> to match every component.</param>
    /// <returns>The matching components, each with at most 50 matching choices.</returns>
    public IReadOnlyList<AgentQueryMatch> Query(AgentQueryRequest? request = null)
    {
        var req = request ?? new AgentQueryRequest();
        var normWithin = Normalize(req.Within);
        var normText = Normalize(req.Text);
        var normKind = Normalize(req.Kind);

        if (!string.IsNullOrEmpty(normWithin))
        {
            var target = _targets.FirstOrDefault(t =>
                string.Equals(t.Ref, req.Within, StringComparison.OrdinalIgnoreCase)
                || Normalize(t.Label) == normWithin);
            if (target is null) return [];

            var allChoices = target.GetChoices();
            var filtered = string.IsNullOrEmpty(normText)
                ? allChoices
                : allChoices.Where(c => Normalize(c.Label).Contains(normText, StringComparison.Ordinal)
                                        || Normalize(c.Value).Contains(normText, StringComparison.Ordinal)).ToList();
            return
            [
                new AgentQueryMatch(
                    target.Ref,
                    target.Component,
                    target.Label,
                    filtered.Take(50).ToList(),
                    Math.Max(0, filtered.Count - 50))
            ];
        }

        var matches = new List<AgentQueryMatch>();
        foreach (var t in _targets)
        {
            if (!string.IsNullOrEmpty(normKind) && Normalize(t.Component) != normKind) continue;
            var choices = t.GetChoices();
            var matchedChoices = string.IsNullOrEmpty(normText)
                ? choices
                : choices.Where(c => Normalize(c.Label).Contains(normText, StringComparison.Ordinal)
                                     || Normalize(c.Value).Contains(normText, StringComparison.Ordinal)).ToList();
            var labelMatch = string.IsNullOrEmpty(normText) || Normalize(t.Label).Contains(normText, StringComparison.Ordinal);
            if (labelMatch || matchedChoices.Count > 0)
            {
                matches.Add(new AgentQueryMatch(
                    t.Ref,
                    t.Component,
                    t.Label,
                    matchedChoices.Take(50).ToList(),
                    Math.Max(0, matchedChoices.Count - 50)));
            }
        }
        return matches;
    }

    /// <summary>
    /// Runs the steps in order. A step is resolved by reference or by label (case and accent insensitive), then
    /// checked for existence, ambiguity, disabled state and supported action, then passed to <see cref="AllowPolicy"/>
    /// and <paramref name="allow"/>. The first failing step stops the run; steps that already ran are not rolled
    /// back. Each successful step raises one <see cref="ObservedChange"/>. Failures are reported in
    /// <see cref="AgentActResult.Error"/> rather than thrown.
    /// </summary>
    /// <param name="steps">The steps to run.</param>
    /// <param name="allow">An extra gate for this call only, called after <see cref="AllowPolicy"/> with the same arguments.</param>
    /// <returns>The outcome of the run.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="steps"/> is <see langword="null"/>.</exception>
    public async Task<AgentActResult> ActAsync(
        IReadOnlyList<AgentActStep> steps,
        Func<AgentActStep, bool, Task<bool>>? allow = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var outcomes = new List<AgentActStepOutcome>();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var byRef = _targets.Where(t => string.Equals(t.Ref, step.Ref, StringComparison.Ordinal)).ToList();
            var matches = byRef.Count == 1
                ? byRef
                : _targets.Where(t => Normalize(t.Label) == Normalize(step.Ref)).ToList();

            if (matches.Count == 0)
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: component \"{step.Ref}\" not found.");
            }
            if (matches.Count > 1)
            {
                var refs = string.Join(", ", matches.Select(m => m.Ref));
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: ambiguous label \"{step.Ref}\" matched {matches.Count} components ({refs}). Use a specific ref.");
            }

            var target = matches[0];
            if (target.IsDisabled?.Invoke() == true)
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: component \"{target.Label}\" ({target.Ref}) is disabled.");
            }
            if (!target.Actions.Contains(step.Action, StringComparer.Ordinal))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" is not supported by \"{target.Label}\" ({target.Ref}).");
            }

            if (AllowPolicy is not null && !await AllowPolicy(step, target.ConfirmRequired))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" was refused by policy.");
            }
            if (allow is not null && !await allow(step, target.ConfirmRequired))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" was refused by caller policy.");
            }

            var previousValue = target.Sensitive ? RedactedValue : target.GetValue();
            var ok = await target.ExecuteAction(step.Action, step.Args ?? []);
            var currentValue = target.Sensitive ? RedactedValue : target.GetValue();
            values[target.Ref] = currentValue;

            if (!ok)
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" returned false.");
            }

            outcomes.Add(new AgentActStepOutcome(target.Ref, step.Action, true, currentValue));
            EmitObservedChange(new ObservedChange(
                target.Component,
                target.Ref,
                target.Label,
                "value",
                previousValue,
                currentValue,
                step.Action,
                AgentChangeSource.Agent));
        }

        return new AgentActResult(true, outcomes, values);
    }

    private void EmitObservedChange(ObservedChange change)
    {
        foreach (var obs in _observers.ToList())
        {
            try
            {
                obs(change);
            }
            catch (Exception)
            {
                // The step is already applied: a failing subscriber must not abort the run or starve the others.
            }
        }
    }

    private string NextRef() => $"c{++_refCounter}";

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var formD = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        var prevSpace = false;
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
        }
        return sb.ToString().Trim();
    }

    private sealed class ObserverSubscription(List<Action<ObservedChange>> list, Action<ObservedChange> callback) : IDisposable
    {
        public void Dispose() => list.Remove(callback);
    }
}
