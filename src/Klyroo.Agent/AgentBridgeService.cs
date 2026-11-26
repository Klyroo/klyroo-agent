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
public sealed partial class AgentBridgeService
{
    /// <summary>The text that replaces the value of password and one-time-code fields in snapshots, results and observed changes.</summary>
    public const string RedactedValue = "[redacted]";

    /// <summary>The outcome of one action: whether it worked and, when it did not, why, so the model can correct itself.</summary>
    private readonly record struct StepResult(bool Ok, string? Reason = null)
    {
        public static implicit operator StepResult(bool ok) => new(ok);
        public static StepResult Fail(string reason) => new(false, reason);
    }

    /// <summary>One page of a collection and the size of the whole filtered collection.</summary>
    private sealed record RecordPage(IReadOnlyList<IReadOnlyDictionary<string, object?>> Records, int Total);

    private sealed class BoundTarget
    {
        public required string Ref { get; init; }
        public required string Component { get; init; }
        public required string Label { get; init; }
        public required Func<object?> GetValue { get; init; }
        public required IReadOnlyList<string> Actions { get; init; }
        public required Func<IReadOnlyList<AgentChoiceItem>> GetChoices { get; init; }
        public required Func<string, IReadOnlyList<object?>, Task<StepResult>> ExecuteAction { get; init; }
        public Func<bool>? IsDisabled { get; init; }
        public Func<bool>? IsOffPage { get; init; }
        public bool Sensitive { get; init; }
        public bool ConfirmRequired { get; init; }
        /// <summary>The actions that need confirmation even when <see cref="ConfirmRequired"/> is false.</summary>
        public IReadOnlyList<string> ConfirmActions { get; init; } = [];
        /// <summary>Pages through a named collection of records, or <see langword="null"/> when the component has none.</summary>
        public Func<AgentQueryRequest, RecordPage?>? QueryRecords { get; init; }

        public bool NeedsConfirmation(string action) => ConfirmRequired || ConfirmActions.Contains(action, StringComparer.Ordinal);
        /// <summary>The row an action step addresses, for tabular components; reported as <see cref="ObservedChange.Row"/>.</summary>
        public Func<string, IReadOnlyList<object?>, string?>? RowOf { get; init; }
    }

    /// <summary>Removes the components added by one Register call.</summary>
    private sealed class Registration(List<BoundTarget> owner, List<BoundTarget> added) : IDisposable
    {
        public void Dispose()
        {
            foreach (var target in added) owner.Remove(target);
            added.Clear();
        }
    }

    // Adds the components of one Register call and returns the handle that removes them.
    private IDisposable AddTargets(List<BoundTarget> targets)
    {
        _targets.AddRange(targets);
        return new Registration(_targets, targets);
    }

    /// <summary>Creates an empty bridge with no registered components and no <see cref="AllowPolicy"/>.</summary>
    public AgentBridgeService()
    {
    }

    private readonly List<BoundTarget> _targets = [];
    private readonly List<Action<ObservedChange>> _observers = [];
    private int _refCounter;

    /// <summary>
    /// The gate called before every <see cref="ActAsync"/> step with the step and whether it needs confirmation, because
    /// its component was registered as confirmation-required or the action is one that always is (<c>submit</c> and
    /// <c>clearFile</c> on a spreadsheet). Return <see langword="false"/> to refuse the step.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="null"/> and <see cref="ActAsync"/> gets no <c>allow</c> argument either, steps that
    /// need confirmation are refused, and every other step runs. Set a policy to allow them, for example
    /// <c>agent.AllowPolicy = async (step, confirm) =&gt; !confirm || await AskUserAsync(step);</c>, or
    /// <c>(_, _) =&gt; Task.FromResult(true)</c> to approve everything, which is only reasonable when a person drives the agent.
    /// </remarks>
    public Func<AgentActStep, bool, Task<bool>>? AllowPolicy { get; set; }

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
    /// Registers a dashboard. Each non-headless filter becomes a <c>select</c> component, and the dashboard itself
    /// accepts <c>setView</c>, <c>resetFilters</c> and <c>refresh</c>. On a filter, <c>setValue</c> replaces the
    /// selection with the given value, or with every value of a list for a multiple filter; <c>toggle</c> adds or
    /// removes one value of a multiple filter; <c>clear</c> (multiple filters only) empties the selection; and
    /// <c>reset</c> restores the default values. A value that is not one of the filter's options fails the step.
    /// </summary>
    /// <param name="dashboard">The dashboard to expose.</param>
    /// <returns>A handle that removes the dashboard's components when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dashboard"/> is <see langword="null"/>.</exception>
    public IDisposable RegisterDashboard(DashboardController dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        var added = new List<BoundTarget>();
        foreach (var filter in dashboard.Controls)
        {
            if (filter.Headless) continue;
            added.Add(new BoundTarget
            {
                Ref = NextRef(),
                Component = "select",
                Label = filter.Label,
                IsDisabled = () => !filter.Enabled,
                GetValue = () => filter.Multiple ? filter.Values : filter.Value,
                Actions = filter.Multiple ? ["setValue", "toggle", "clear", "reset"] : ["setValue", "reset"],
                GetChoices = () => filter.Items
                    .Select(i => new AgentChoiceItem(i.Value, i.Label, filter.IsSelected(i.Value), i.Disabled))
                    .ToList(),
                ExecuteAction = (action, args) => Task.FromResult<StepResult>(action switch
                {
                    "setValue" => SetFilterSelection(filter, args.Count > 0 ? args[0] : null),
                    "toggle" => ToggleFilterValue(filter, args.Count > 0 ? args[0]?.ToString() : null),
                    "clear" => SetFilterValues(filter, []),
                    "reset" => ResetFilter(filter),
                    _ => false,
                }),
            });
        }

        added.Add(new BoundTarget
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
        return AddTargets(added);
    }

    private static bool ResetFilter(DashboardFilterControl filter)
    {
        filter.Reset();
        return true;
    }

    private static bool IsOption(DashboardFilterControl filter, string value) =>
        filter.Items.Any(i => string.Equals(i.Value, value, StringComparison.Ordinal) && !i.Disabled);

    // setValue: a string replaces the selection with that value; a list (multiple filters) replaces it with the list.
    private static bool SetFilterSelection(DashboardFilterControl filter, object? arg)
    {
        IReadOnlyList<string> wanted = arg switch
        {
            null => [],
            string text => [text],
            System.Collections.IEnumerable list => list.Cast<object?>().Select(v => v?.ToString() ?? "").ToList(),
            _ => [arg.ToString() ?? ""],
        };
        if (wanted.Count == 0) return false;
        if (!filter.Multiple)
        {
            if (wanted.Count != 1 || !IsOption(filter, wanted[0])) return false;
            filter.Value = wanted[0];
            return string.Equals(filter.Value, wanted[0], StringComparison.Ordinal);
        }
        return SetFilterValues(filter, wanted);
    }

    // Brings a multiple filter to exactly `wanted`, through Toggle so the filter's own rules apply. Fails without
    // changing anything when a value is unknown or the list exceeds Max.
    private static bool SetFilterValues(DashboardFilterControl filter, IReadOnlyList<string> wanted)
    {
        if (!filter.Enabled || !filter.Multiple) return false;
        var distinct = wanted.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Any(v => !IsOption(filter, v))) return false;
        if (filter.Max is { } max && distinct.Count > max) return false;
        foreach (var value in filter.Values.Where(v => !distinct.Contains(v, StringComparer.Ordinal)).ToList())
        {
            filter.Toggle(value);
        }
        foreach (var value in distinct.Where(v => !filter.IsSelected(v)))
        {
            filter.Toggle(value);
        }
        return filter.Values.Count == distinct.Count && distinct.All(filter.IsSelected);
    }

    // toggle: adds or removes one value of a multiple filter. Fails when the value is unknown or Max blocks the add.
    private static bool ToggleFilterValue(DashboardFilterControl filter, string? value)
    {
        if (value is null || !filter.Multiple || !IsOption(filter, value)) return false;
        var wasSelected = filter.IsSelected(value);
        filter.Toggle(value);
        return filter.IsSelected(value) != wasSelected;
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
                ConfirmRequired: t.ConfirmRequired,
                ConfirmActions: t.ConfirmActions));
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

            if (!string.IsNullOrWhiteSpace(req.Collection))
            {
                var page = target.QueryRecords?.Invoke(req) ?? new RecordPage([], 0);
                return
                [
                    new AgentQueryMatch(target.Ref, target.Component, target.Label, [], 0, page.Records, page.Total, Math.Max(0, req.Offset)),
                ];
            }

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
    /// checked for existence, ambiguity, disabled state, visibility (a component that is not displayed, such as a
    /// conditional field whose condition is false, is refused) and supported action, then passed to
    /// <see cref="AllowPolicy"/> and <paramref name="allow"/>. The first failing step stops the run; steps that
    /// already ran are not rolled back. Each successful step raises one <see cref="ObservedChange"/>. Failures,
    /// including an exception thrown while the action runs, are reported in <see cref="AgentActResult.Error"/>
    /// rather than thrown.
    /// </summary>
    /// <param name="steps">The steps to run.</param>
    /// <param name="allow">An extra gate for this call only, called after <see cref="AllowPolicy"/> with the same arguments. It also counts as a policy for steps that need confirmation.</param>
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
            if (target.IsOffPage?.Invoke() == true)
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: component \"{target.Label}\" ({target.Ref}) is not displayed right now.");
            }
            if (!target.Actions.Contains(step.Action, StringComparer.Ordinal))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" is not supported by \"{target.Label}\" ({target.Ref}).");
            }

            var needsConfirmation = target.NeedsConfirmation(step.Action);
            if (needsConfirmation && AllowPolicy is null && allow is null)
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" needs confirmation and no AllowPolicy is set.");
            }
            if (AllowPolicy is not null && !await AllowPolicy(step, needsConfirmation))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" was refused by policy.");
            }
            if (allow is not null && !await allow(step, needsConfirmation))
            {
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" was refused by caller policy.");
            }

            var stepArgs = new List<object?>();
            foreach (var arg in step.Args ?? [])
            {
                var plain = AgentArgs.Unwrap(arg, out var argError);
                if (argError is not null)
                {
                    return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" was rejected: {argError}");
                }
                stepArgs.Add(plain);
            }

            var previousValue = target.Sensitive ? RedactedValue : target.GetValue();
            StepResult result;
            try
            {
                result = await target.ExecuteAction(step.Action, stepArgs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Failures are values: a step that throws (a controller hook, a bad argument) fails the run.
                values[target.Ref] = target.Sensitive ? RedactedValue : target.GetValue();
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" failed: {ex.Message}");
            }
            var currentValue = target.Sensitive ? RedactedValue : target.GetValue();
            values[target.Ref] = currentValue;

            if (!result.Ok)
            {
                var why = result.Reason is null ? "returned false" : $"was rejected: {result.Reason}";
                return new AgentActResult(false, outcomes, values, $"Step {i + 1}: action \"{step.Action}\" on \"{target.Label}\" {why}.");
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
                AgentChangeSource.Agent,
                target.RowOf?.Invoke(step.Action, stepArgs)));
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

    internal static string Normalize(string? text)
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
