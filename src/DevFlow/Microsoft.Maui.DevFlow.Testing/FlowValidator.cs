namespace Microsoft.Maui.DevFlow.Testing;

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
        new HashSet<string>(StringComparer.Ordinal) { "propEquals", "exists", "notExists", "routeIs", "pageChanged" };

    // routeIs has an authoritative AgentStatus.Route value. pageChanged is report-only because a
    // generic flow does not retain authoritative before/after page identity.
    internal static readonly IReadOnlySet<string> VerifiableAssertKinds =
        new HashSet<string>(StringComparer.Ordinal) { "propEquals", "exists", "notExists", "routeIs" };

    private static readonly IReadOnlySet<string> Themes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "light", "dark", "system" };

    /// <summary>Evidence declarations allowed on one flow or one step.</summary>
    private const int MaxExpectedEvidenceDeclarations = 16;

    public static FlowValidation Validate(MauiFlow flow)
    {
        var v = new FlowValidation();
        if (flow.Schema < 1)
            v.Errors.Add($"Flow schema {flow.Schema} is invalid; supported schemas are 1 through {MauiFlow.CurrentSchema}.");
        else if (flow.Schema > MauiFlow.CurrentSchema)
            v.Errors.Add($"Flow schema {flow.Schema} is newer than supported ({MauiFlow.CurrentSchema}); upgrade DevFlow before replaying it.");
        if (flow.Steps is null || flow.Steps.Count == 0)
        {
            v.Errors.Add("Flow must contain at least one step.");
            return v;
        }

        ValidateStepIdentities(v, flow.Steps, requirePositiveSequence: flow.Schema >= 2);
        ValidateExpectedEvidence(v, "flow", flow.ExpectedEvidence);

        var ordinal = 0;
        foreach (var s in flow.Steps)
        {
            ordinal++;
            var where = $"step {(s.Seq > 0 ? s.Seq : ordinal)} ({s.Action})";
            ValidateExpectedEvidence(v, where, s.ExpectedEvidence);
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
                    ValidateSecretReference(v, where, s);
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
                    if ((a.Kind is "propEquals" or "exists" or "notExists") &&
                        (a.Selector is null || a.Selector.IsEmpty))
                        v.Errors.Add($"{where}: {a.Kind} assertion requires a selector.");
                    if (a.Kind == "propEquals" && string.IsNullOrEmpty(a.Name))
                        v.Errors.Add($"{where}: propEquals assertion requires a property name.");
                    if (a.Kind == "routeIs" && string.IsNullOrWhiteSpace(a.Expected))
                        v.Errors.Add($"{where}: routeIs assertion requires an expected route.");
                }
            }
            if (s.Fragile)
                v.Warnings.Add($"{where}: uses a fragile selector (no AutomationId) — replay may be brittle.");
        }
        return v;
    }

    /// <summary>
    /// Rejects an evidence declaration that cannot be checked. A declaration nobody can evaluate
    /// is worse than no declaration, because the report would silently show it as satisfied.
    /// </summary>
    private static void ValidateExpectedEvidence(
        FlowValidation validation,
        string where,
        List<FlowExpectedEvidence>? declarations)
    {
        if (declarations is null)
            return;
        if (declarations.Count > MaxExpectedEvidenceDeclarations)
        {
            validation.Errors.Add(
                $"{where}: expectedEvidence declares {declarations.Count} entries; at most " +
                $"{MaxExpectedEvidenceDeclarations} are allowed.");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (declaration is null)
            {
                validation.Errors.Add($"{where}: expectedEvidence contains an empty entry.");
                continue;
            }
            // Identity is checked before shape so a duplicate id is reported even when the entry it
            // collides with is also malformed.
            if (declaration.Id?.Trim() is { Length: > 0 } id && !seen.Add(id))
                validation.Errors.Add($"{where}: expectedEvidence id '{id}' is declared more than once.");
            if (!MauiFlowEvidenceKinds.IsKnown(declaration.Kind))
            {
                validation.Errors.Add(
                    $"{where}: expectedEvidence kind '{declaration.Kind}' is unknown; expected one of " +
                    $"{string.Join(", ", MauiFlowEvidenceKinds.All)}.");
                continue;
            }
            var kind = declaration.Kind.Trim().ToLowerInvariant();
            if (kind == MauiFlowEvidenceKinds.BusinessOracle &&
                string.IsNullOrWhiteSpace(declaration.Reference))
            {
                validation.Errors.Add(
                    $"{where}: expectedEvidence kind 'business-oracle' requires the oracle id in 'reference'.");
            }
        }
    }

    private static void ValidateStepIdentities(
        FlowValidation validation,
        IReadOnlyList<FlowStep> steps,
        bool requirePositiveSequence)
    {
        var sequences = new Dictionary<int, int>();
        var stableIds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var ordinal = index + 1;
            if (requirePositiveSequence && step.Seq < 1)
            {
                validation.Errors.Add(
                    $"step {ordinal}: seq must be a positive integer.");
            }
            if (!sequences.TryAdd(step.Seq, ordinal))
            {
                validation.Errors.Add(
                    $"step {step.Seq}: duplicate seq value; integer stepSequence APIs require a unique sequence.");
            }

            var where = $"step {(step.Seq > 0 ? step.Seq : ordinal)}";
            var acceptanceCriterionIds = step.AcceptanceCriterionIds ?? [];
            var criterionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var criterionId in acceptanceCriterionIds)
            {
                if (string.IsNullOrWhiteSpace(criterionId) ||
                    criterionId.Length > 128 ||
                    !char.IsAsciiLetterOrDigit(criterionId[0]) ||
                    criterionId.Any(static character =>
                        !char.IsAsciiLetterOrDigit(character) &&
                        character is not '-' and not '_' and not '.' and not ':'))
                {
                    validation.Errors.Add(
                        $"{where}: acceptanceCriterionIds must contain non-empty identifier-shaped values.");
                    continue;
                }
                if (!criterionIds.Add(criterionId))
                {
                    validation.Errors.Add(
                        $"{where}: acceptanceCriterionIds contains duplicate value '{criterionId}'.");
                }
            }

            if (step.StepId is null)
                continue;

            var value = step.StepId.Trim();
            if (value.Length == 0)
            {
                validation.Errors.Add($"{where}: stepId cannot be empty or whitespace.");
                continue;
            }
            if (!string.Equals(value, step.StepId, StringComparison.Ordinal))
                validation.Errors.Add($"{where}: stepId cannot contain leading or trailing whitespace.");
            if (value.Length > 128)
                validation.Errors.Add($"{where}: stepId cannot exceed 128 characters.");
            if (!char.IsAsciiLetterOrDigit(value[0]) ||
                value.Any(static character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character is not '-' and not '_' and not '.' and not ':'))
            {
                validation.Errors.Add(
                    $"{where}: stepId must start with a letter or digit and contain only letters, digits, '-', '_', '.', or ':'.");
            }
            if (value.All(char.IsAsciiDigit))
            {
                validation.Errors.Add(
                    $"{where}: numeric stepId values are reserved for legacy sequence lookup.");
            }
            if (value.StartsWith("step-", StringComparison.Ordinal) &&
                value.Length > 5 &&
                value.AsSpan(5).ToArray().All(char.IsAsciiDigit) &&
                !string.Equals(value, MauiFlowStepIdentity.Create(step.Seq), StringComparison.Ordinal))
            {
                validation.Errors.Add(
                    $"{where}: sequence-shaped stepId values must equal '{MauiFlowStepIdentity.Create(step.Seq)}' for that step.");
            }
            if (!stableIds.TryAdd(value, ordinal))
                validation.Errors.Add($"{where}: duplicate stepId '{value}'.");
        }
    }

    private static void ValidateSecretReference(FlowValidation validation, string where, FlowStep step)
    {
        var variable = step.Args?.SecretEnvironmentVariable;
        if (variable is null)
            return;
        if (!FlowSecretReference.IsValidEnvironmentVariable(variable))
            validation.Errors.Add($"{where}: secretEnvironmentVariable must use the {FlowSecretReference.EnvironmentPrefix} prefix and contain only letters, digits, and underscores.");
        if (!string.IsNullOrEmpty(step.Value) ||
            !string.IsNullOrEmpty(step.Args?.Text) ||
            !string.IsNullOrEmpty(step.Args?.Value))
        {
            validation.Errors.Add($"{where}: a secret-backed step cannot also persist a literal value.");
        }
    }

    /// <summary>
    /// args.selector is authoritative; fall back to the step's target for compatibility.
    /// This is the single source of truth for which element a step will actually drive, and it is
    /// public so that authorization can bind to the same answer the runner will act on. Anything
    /// that decides what a step is allowed to touch must call this rather than reimplement it.
    /// </summary>
    public static FlowSelector? EffectiveSelector(FlowStep step)
    {
        var sel = step.Args?.Selector;
        if (sel is not null && !sel.IsEmpty) return sel;
        return step.Target;
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
        var hasStableItemKey = !string.IsNullOrWhiteSpace(sel.StableItemKey);
        var hasCollectionScope = !string.IsNullOrWhiteSpace(sel.CollectionScope);
        if (hasStableItemKey != hasCollectionScope)
            v.Errors.Add($"{where}: stableItemKey and collectionScope must be supplied together.");
        if ((hasStableItemKey || hasCollectionScope) && string.IsNullOrWhiteSpace(sel.AutomationId))
            v.Errors.Add($"{where}: a scoped item selector also requires an AutomationId.");
        if (hasStableItemKey && !FlowSelector.IsOpaqueStableItemKey(sel.StableItemKey))
            v.Errors.Add($"{where}: stableItemKey must be an opaque SHA-256 identity.");
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

/// <summary>
/// Publicly named facade for the canonical flow validator. <see cref="FlowValidator"/> remains
/// available for existing callers.
/// </summary>
public static class MauiFlowValidator
{
    public static FlowValidation Validate(MauiFlow flow) => FlowValidator.Validate(flow);
}
