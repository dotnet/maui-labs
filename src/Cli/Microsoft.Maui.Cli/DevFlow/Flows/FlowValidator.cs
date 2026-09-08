namespace Microsoft.Maui.Cli.DevFlow.Flows;

public sealed class FlowValidation
{
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Validates a parsed flow before replay: known actions, a usable selector per action, required
/// args present, known assert kinds. Reports fragile selectors as warnings (not errors).
/// </summary>
public static class FlowValidator
{
    private static readonly IReadOnlySet<string> AssertKinds =
        new HashSet<string>(StringComparer.Ordinal) { "propEquals", "exists", "routeIs", "pageChanged" };

    // These have executable replay checks; routeIs/pageChanged remain descriptive recording notes.
    private static readonly IReadOnlySet<string> VerifiableAssertKinds =
        new HashSet<string>(StringComparer.Ordinal) { "propEquals", "exists" };

    private static readonly IReadOnlySet<string> Themes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "light", "dark", "system" };

    public static FlowValidation Validate(MauiFlow flow)
    {
        var v = new FlowValidation();
        if (flow.Schema > MauiFlow.CurrentSchema)
            v.Warnings.Add($"Flow schema {flow.Schema} is newer than supported ({MauiFlow.CurrentSchema}); replay may be incomplete.");
        if (flow.Steps.Count == 0)
            v.Warnings.Add("Flow has no steps.");

        var ordinal = 0;
        foreach (var s in flow.Steps)
        {
            ordinal++;
            var where = $"step {(s.Seq > 0 ? s.Seq : ordinal)} ({s.Action})";
            if (string.IsNullOrWhiteSpace(s.Action) || !FlowActions.All.Contains(s.Action))
            {
                v.Errors.Add($"{where}: unknown action '{s.Action}'.");
                continue;
            }

            switch (s.Action)
            {
                case FlowActions.Tap:
                case FlowActions.Fill:
                case FlowActions.SetProperty:
                    ValidateSelector(v, where, EffectiveSelector(s), required: true);
                    if (s.Action == FlowActions.SetProperty && string.IsNullOrEmpty(s.Args?.Name))
                        v.Errors.Add($"{where}: setProperty requires a property name.");
                    break;
                case FlowActions.Scroll:
                    ValidateSelector(v, where, EffectiveSelector(s), required: false);
                    if (!HasScrollInput(s))
                        v.Warnings.Add($"{where}: scroll has no element, delta, itemIndex, or position — it will be a no-op.");
                    break;
                case FlowActions.Navigate:
                    if (string.IsNullOrEmpty(s.Args?.Route) && string.IsNullOrEmpty(s.Value))
                        v.Errors.Add($"{where}: navigate requires a route.");
                    break;
                case FlowActions.SetTheme:
                    var theme = s.Args?.Theme ?? s.Value;
                    if (string.IsNullOrEmpty(theme) || !Themes.Contains(theme))
                        v.Errors.Add($"{where}: setTheme requires one of light|dark|system (got '{theme ?? "(none)"}').");
                    break;
                case FlowActions.Back:
                    break;
                case FlowActions.Assert:
                    // Validation-only step; the per-assert checks below enforce structure. Warn if
                    // it carries nothing to check.
                    if ((s.Asserts?.Count ?? 0) == 0)
                        v.Warnings.Add($"{where}: assert step has no assertions.");
                    break;
            }

            foreach (var a in s.Asserts ?? Enumerable.Empty<FlowAssert>())
            {
                if (!AssertKinds.Contains(a.Kind))
                {
                    if (a.Verify)
                        v.Errors.Add($"{where}: unknown assert kind '{a.Kind}' cannot be verified on replay.");
                    else
                        v.Warnings.Add($"{where}: unknown assert kind '{a.Kind}' (ignored on replay).");
                    continue;
                }
                if (a.Verify && !VerifiableAssertKinds.Contains(a.Kind))
                {
                    v.Errors.Add($"{where}: {a.Kind} is report-only and cannot be verified on replay.");
                    continue;
                }
                // Hard (verify:true) assertions must be structurally resolvable, or replay would
                // just poll until a misleading failure.
                if (a.Verify)
                {
                    if (a.Selector is null || a.Selector.IsEmpty)
                        v.Errors.Add($"{where}: {a.Kind} assertion requires a selector.");
                    if (a.Kind == "propEquals" && string.IsNullOrEmpty(a.Name))
                        v.Errors.Add($"{where}: propEquals assertion requires a property name.");
                }
            }
            if (s.Fragile)
                v.Warnings.Add($"{where}: uses a fragile selector (no AutomationId) — replay may be brittle.");
        }
        return v;
    }

    /// <summary>args.selector is authoritative; fall back to the step's target for compatibility.</summary>
    internal static FlowSelector? EffectiveSelector(FlowStep s)
    {
        var sel = s.Args?.Selector;
        if (sel is not null && !sel.IsEmpty) return sel;
        return s.Target;
    }

    private static void ValidateSelector(FlowValidation v, string where, FlowSelector? sel, bool required)
    {
        if (sel is null || sel.IsEmpty)
        {
            if (required) v.Errors.Add($"{where}: missing a target selector.");
            return;
        }
        var kinds = 0;
        if (!string.IsNullOrEmpty(sel.AutomationId)) kinds++;
        if (!string.IsNullOrEmpty(sel.Text)) kinds++;
        if (!string.IsNullOrEmpty(sel.Id)) kinds++;
        var hasTypeIndex = sel.TypeIndex is not null
            || (sel.SelectorKind == "typeIndex" && !string.IsNullOrEmpty(sel.Type) && sel.Index is not null);
        if (hasTypeIndex) kinds++;
        if (kinds == 0)
            v.Errors.Add($"{where}: selector has no usable kind (automationId|text|typeIndex|id).");
        var idx = sel.TypeIndex?.Index ?? sel.Index;
        if (idx is < 0)
            v.Errors.Add($"{where}: typeIndex index must be >= 0.");
    }

    private static bool HasScrollInput(FlowStep s)
    {
        var a = s.Args;
        var sel = EffectiveSelector(s);
        var hasSelector = sel is not null && !sel.IsEmpty;
        // Note: args.element is a STALE runtime id — replay ignores it and re-resolves the
        // target selector instead — so it is NOT a meaningful replay input here.
        return hasSelector
            || (a?.Dx is not null && a.Dx != 0)
            || (a?.Dy is not null && a.Dy != 0)
            || a?.ItemIndex is not null
            || !string.IsNullOrEmpty(a?.Position);
    }
}
