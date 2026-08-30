using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Current live resolution facts for one deterministic selector-health candidate.</summary>
public sealed class MauiRepairCandidateResolution
{
    public string? CandidateId { get; set; }
    public int? MatchCount { get; set; }
    public bool? SemanticFingerprintMatches { get; set; }
    public MauiElementFingerprint? CurrentFingerprint { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

/// <summary>Risk and shortlist limits for selector repair proposal generation.</summary>
public sealed class MauiFlowRepairProposalGenerationOptions
{
    public int MaxCandidates { get; set; } = 3;
    public double MinimumScore { get; set; } = 0.55;
    public double MinimumScoreGap { get; set; } = 0.05;
    public bool AllowExactText { get; set; }
    public HashSet<string> AllowedCandidateKinds { get; } = new(StringComparer.Ordinal)
    {
        "automation-id",
    };
    public HashSet<string> AllowedRiskFlags { get; } = new(StringComparer.Ordinal);
}

/// <summary>Pure generation input. Candidate generation never queries a live tree itself.</summary>
public sealed class MauiFlowRepairProposalGenerationInput
{
    public MauiFlowRepairEligibilityDecision? Eligibility { get; set; }
    public MauiTestPlan? Plan { get; set; }
    public MauiFlow? Flow { get; set; }
    public MauiFlowReference? BaseFlow { get; set; }
    public string? SourceRunId { get; set; }
    public string? SourceStepId { get; set; }
    public string? SourceFailureId { get; set; }
    public string? SourceFailureCode { get; set; }
    public MauiElementFingerprint? PriorFingerprint { get; set; }
    public MauiRepairPriorSelectorResolution? PriorActiveSelectorResolution { get; set; }
    public List<MauiSelectorCandidate> SelectorHealthCandidates { get; set; } = [];
    public List<MauiRepairCandidateResolution> CurrentResolutions { get; set; } = [];
    public MauiFlowRepairProposalGenerationOptions? Options { get; set; }
    public MauiActorProvenance? Provenance { get; set; }
    public string? Trust { get; set; }
}

/// <summary>An explicit rejected candidate or proposal abstention.</summary>
public sealed class MauiFlowRepairProposalAbstention
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string? CandidateId { get; init; }
}

/// <summary>Bounded proposal-generation result with retained candidate evidence and abstentions.</summary>
public sealed class MauiFlowRepairProposalGenerationResult
{
    public List<MauiFlowRepairProposal> Proposals { get; } = [];
    public List<MauiSelectorCandidate> ConsideredCandidates { get; } = [];
    public List<MauiFlowRepairProposalAbstention> Abstentions { get; } = [];
    public bool Abstained => Proposals.Count == 0;
}

/// <summary>Result from applying a selector-only patch to an in-memory flow clone.</summary>
public sealed class MauiFlowRepairPatchBuildResult
{
    public MauiFlow? PatchedFlow { get; init; }
    public MauiFlowPatch? Patch { get; init; }
    public MauiRepairSelectorDiff? Diff { get; init; }
    public MauiRepairAssertionProof? Proof { get; init; }
    public string? PatchDigest { get; init; }
    public string? Error { get; init; }
    public bool Ok => PatchedFlow is not null && Patch is not null && Proof is not null && Error is null;
}

/// <summary>
/// Generates selector-only repair proposals from the already deterministic selector-health
/// shortlist. It never generates a selector from a tree, mutates a flow, ranks with a model, or
/// silently activates an alternative selector.
/// </summary>
public static class MauiFlowRepairProposalGenerator
{
    private static readonly HashSet<string> AlwaysBlockingRisks = new(StringComparer.Ordinal)
    {
        "ambiguity",
        "stale-source",
        "platform-divergence",
    };

    private static readonly HashSet<string> KnownRisks = new(StringComparer.Ordinal)
    {
        "ambiguity",
        "virtualization",
        "stale-source",
        "platform-divergence",
        "localization",
    };

    /// <summary>Produces at most one safe proposal so close candidates cannot be silently chosen.</summary>
    public static MauiFlowRepairProposalGenerationResult Generate(MauiFlowRepairProposalGenerationInput? input)
    {
        input ??= new MauiFlowRepairProposalGenerationInput();
        var result = new MauiFlowRepairProposalGenerationResult();
        var options = ResolveOptions(input.Options, input.Plan?.RepairPolicy);
        var max = Math.Clamp(options.MaxCandidates, 1, 8);

        if (input.Eligibility?.Eligible != true)
        {
            Add(result, "repair-ineligible", "The repair eligibility gate did not allow candidate generation.");
            return result;
        }
        if (input.Flow is null || input.BaseFlow is null ||
            string.IsNullOrWhiteSpace(input.BaseFlow.Path) ||
            string.IsNullOrWhiteSpace(input.BaseFlow.Digest))
        {
            Add(result, "flow-baseline-missing", "A flow and complete base flow path/digest are required.");
            return result;
        }
        if (!IsCandidateListDeterministic(input.SelectorHealthCandidates))
        {
            Add(result, "selector-health-ranking-invalid", "The supplied selector-health candidates are not in deterministic rank order.");
            return result;
        }

        var step = FindStep(input.Flow, input.SourceStepId);
        if (step is null)
        {
            Add(result, "source-step-missing", "The source repair step does not exist in the current flow.");
            return result;
        }
        var resolvedStepId = MauiFlowStepIdentity.Get(step);

        var activeSelector = EffectiveSelector(step);
        if (activeSelector is null || activeSelector.IsEmpty)
        {
            Add(result, "active-selector-missing", "The failed step has no active selector to replace.");
            return result;
        }
        if (input.PriorActiveSelectorResolution?.TrustedRun != true ||
            input.PriorActiveSelectorResolution.WasUniquelyResolved != true ||
            input.PriorActiveSelectorResolution.ActiveSelector is null ||
            !SelectorsMatch(activeSelector, input.PriorActiveSelectorResolution.ActiveSelector))
        {
            Add(
                result,
                "prior-active-selector-mismatch",
                "The failed step selector is not proven to be the selector uniquely resolved in a prior trusted run.");
            return result;
        }
        var trustedPriorFingerprint = input.PriorActiveSelectorResolution.Fingerprint;
        if (trustedPriorFingerprint is null ||
            (input.PriorFingerprint is not null &&
             !MauiRepairFingerprintComparer.SemanticallyMatches(
                 trustedPriorFingerprint,
                 input.PriorFingerprint)))
        {
            Add(
                result,
                "prior-fingerprint-mismatch",
                "The supplied prior fingerprint does not match the trusted prior selector-resolution evidence.");
            return result;
        }

        var resolutionGroups = (input.CurrentResolutions ?? [])
            .Where(static resolution => !string.IsNullOrWhiteSpace(resolution.CandidateId))
            .GroupBy(static resolution => resolution.CandidateId!, StringComparer.Ordinal)
            .ToArray();
        foreach (var duplicate in resolutionGroups.Where(static group => group.Count() > 1))
        {
            Add(
                result,
                "candidate-current-resolution-duplicate",
                "Multiple current resolution records exist for one candidate; no order-dependent resolution can be selected.",
                duplicate.Key);
        }
        if (result.Abstentions.Count > 0)
            return result;

        var resolutions = resolutionGroups.ToDictionary(
            static group => group.Key,
            static group => group.Single(),
            StringComparer.Ordinal);
        var safe = new List<(MauiSelectorCandidate Candidate, MauiRepairCandidateResolution Resolution)>();
        foreach (var candidate in input.SelectorHealthCandidates.Take(max))
        {
            result.ConsideredCandidates.Add(candidate);
            if (!TryAcceptCandidate(
                    candidate,
                    resolutions,
                    trustedPriorFingerprint,
                    input.Eligibility,
                    options,
                    result,
                    out var resolution))
            {
                continue;
            }

            safe.Add((candidate, resolution!));
        }

        if (safe.Count == 0)
        {
            Add(result, "no-safe-candidate", "No deterministic selector-health candidate met the uniqueness, fingerprint, and risk gates.");
            return result;
        }

        var winner = safe[0];
        if (safe.Count > 1 && IsClose(winner.Candidate, safe[1].Candidate, options.MinimumScoreGap))
        {
            Add(
                result,
                "candidate-scores-too-close",
                "The leading selector candidates are too close to select safely; human diagnosis is required.");
            return result;
        }

        var patch = MauiFlowRepairPatchBuilder.Build(
            input.Flow,
            resolvedStepId,
            winner.Candidate.Selector!);
        if (!patch.Ok)
        {
            Add(result, "selector-patch-invalid", patch.Error ?? "The selector-only patch could not be built.");
            return result;
        }

        var winningCandidate = MauiFlowRepairClone.CloneCandidate(winner.Candidate);
        winningCandidate.EvidenceRefs = winningCandidate.EvidenceRefs
            .Concat(winner.Resolution.EvidenceRefs)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
        var proposalId = "repair_" + Hash(string.Join(
            "\u001f",
            input.SourceRunId,
            resolvedStepId,
            input.SourceFailureId,
            winningCandidate.CandidateId,
            patch.PatchDigest))[..24];
        result.Proposals.Add(new MauiFlowRepairProposal
        {
            ProposalId = proposalId,
            Revision = 1,
            State = MauiFlowRepairOutcomeStates.Proposed,
            SourceRunId = input.SourceRunId,
            SourceStepId = resolvedStepId,
            SourceFailureId = input.SourceFailureId,
            SourceFailureCode = input.SourceFailureCode ?? input.Eligibility.FailureCode,
            PreDispatch = true,
            BaseFlow = CloneReference(input.BaseFlow),
            OldSelector = MauiFlowRepairClone.CloneSelector(activeSelector),
            ProposedSelector = MauiFlowRepairClone.CloneSelector(winningCandidate.Selector),
            Candidate = winningCandidate,
            UniquenessProof = new MauiRepairUniquenessProof
            {
                MatchCount = winner.Resolution.MatchCount,
                ValidatedAt = winningCandidate.Fingerprint?.ObservedAt,
                Platform = winner.Resolution.CurrentFingerprint?.Context.Platform ??
                    winningCandidate.Fingerprint?.Context.Platform,
            },
            Patch = patch.Patch,
            PatchDigest = patch.PatchDigest,
            Diff = patch.Diff,
            UnchangedAssertionsProof = patch.Proof,
            RiskFlags = winningCandidate.RiskFlags.ToList(),
            Provenance = input.Provenance,
            Trust = input.Trust ?? input.Eligibility.Trust,
        });
        return result;
    }

    private static bool TryAcceptCandidate(
        MauiSelectorCandidate candidate,
        IReadOnlyDictionary<string, MauiRepairCandidateResolution> resolutions,
        MauiElementFingerprint? priorFingerprint,
        MauiFlowRepairEligibilityDecision eligibility,
        MauiFlowRepairProposalGenerationOptions options,
        MauiFlowRepairProposalGenerationResult result,
        out MauiRepairCandidateResolution? resolution)
    {
        resolution = null;
        var id = candidate.CandidateId;
        if (string.IsNullOrWhiteSpace(id) ||
            !resolutions.TryGetValue(id, out resolution))
        {
            Add(result, "candidate-current-resolution-missing", "A current unique resolution proof is required.", id);
            return false;
        }
        if (resolution.MatchCount != 1 || candidate.Validation.Unique != true ||
            candidate.Validation.MatchCount != 1 || candidate.Unique != true)
        {
            Add(result, "candidate-not-unique", "A candidate must resolve exactly one current target.", id);
            return false;
        }
        if (candidate.Validation.Accepted != true ||
            candidate.PlatformValidated == false ||
            string.Equals(candidate.Validation.PlatformState, "divergent", StringComparison.Ordinal))
        {
            Add(result, "candidate-platform-unsafe", "The candidate is not accepted on the current platform.", id);
            return false;
        }
        if (candidate.Selector is null || candidate.Selector.IsEmpty ||
            !HasExactlyOneSelectorForm(candidate.Selector))
        {
            Add(result, "candidate-selector-not-executable", "The candidate cannot be represented by one executable flow selector.", id);
            return false;
        }

        var kind = candidate.SelectorDescriptor.Kind;
        if (string.IsNullOrWhiteSpace(kind))
        {
            Add(result, "candidate-kind-missing", "The candidate has no deterministic selector kind.", id);
            return false;
        }
        if (!IsAllowedKind(kind, candidate, options))
        {
            Add(result, "candidate-kind-disallowed", $"Candidate kind '{kind}' is not allowed by repair policy.", id);
            return false;
        }
        if (candidate.RiskFlags.Any(AlwaysBlockingRisks.Contains))
        {
            Add(result, "candidate-risk-unsafe", "The candidate carries an ambiguity, virtualization, stale-source, or platform-divergence risk.", id);
            return false;
        }
        if (candidate.RiskFlags.Any(risk => !KnownRisks.Contains(risk)))
        {
            Add(result, "candidate-risk-unknown", "The candidate carries an unknown risk code and cannot be proposed.", id);
            return false;
        }
        var collection = candidate.Fingerprint?.Collection;
        if (candidate.RiskFlags.Contains("virtualization", StringComparer.Ordinal) &&
            (string.IsNullOrWhiteSpace(collection?.Scope) ||
             string.IsNullOrWhiteSpace(collection?.ItemKey)))
        {
            Add(result, "candidate-virtualization-unscoped", "A virtualized candidate must be scoped to a stable collection and item key.", id);
            return false;
        }
        if (!string.IsNullOrWhiteSpace(collection?.TemplateKind) &&
            (string.IsNullOrWhiteSpace(collection!.Scope) ||
             string.IsNullOrWhiteSpace(collection.ItemKey)))
        {
            Add(result, "candidate-template-unscoped", "A template candidate must be scoped to a stable collection and item key.", id);
            return false;
        }
        if (candidate.RiskFlags.Contains("localization", StringComparer.Ordinal) &&
            (kind != "exact-text" || !options.AllowExactText ||
             !options.AllowedRiskFlags.Contains("localization") ||
             string.IsNullOrWhiteSpace(candidate.ScopeDescriptor.LocaleAssumption) ||
             !string.Equals(
                 candidate.ScopeDescriptor.LocaleAssumption,
                 eligibility.CurrentCheckpoint?.Locale,
                 StringComparison.Ordinal)))
        {
            Add(result, "candidate-localization-unsafe", "A localized exact-text candidate requires an explicit matching locale gate.", id);
            return false;
        }
        if (Score(candidate) < options.MinimumScore)
        {
            Add(result, "candidate-score-too-low", "The deterministic candidate score is below the repair safety threshold.", id);
            return false;
        }

        var expectedFingerprint = priorFingerprint;
        if (expectedFingerprint is null || candidate.Fingerprint is null || resolution.CurrentFingerprint is null ||
            resolution.SemanticFingerprintMatches != true ||
            !MauiRepairFingerprintComparer.SemanticallyMatches(expectedFingerprint, candidate.Fingerprint) ||
            !MauiRepairFingerprintComparer.SemanticallyMatches(candidate.Fingerprint, resolution.CurrentFingerprint))
        {
            Add(result, "candidate-fingerprint-mismatch", "The candidate does not have a matching semantic fingerprint in the current target.", id);
            return false;
        }
        return true;
    }

    private static bool IsAllowedKind(
        string kind,
        MauiSelectorCandidate candidate,
        MauiFlowRepairProposalGenerationOptions options)
    {
        if (kind == "exact-text")
            return options.AllowExactText &&
                options.AllowedCandidateKinds.Contains(kind) &&
                !string.IsNullOrWhiteSpace(candidate.SelectorDescriptor.ExactText);
        return options.AllowedCandidateKinds.Contains(kind);
    }

    private static MauiFlowRepairProposalGenerationOptions ResolveOptions(
        MauiFlowRepairProposalGenerationOptions? supplied,
        MauiFlowRepairPolicy? policy)
    {
        var result = new MauiFlowRepairProposalGenerationOptions
        {
            MaxCandidates = supplied?.MaxCandidates ?? 3,
            MinimumScore = supplied?.MinimumScore ?? .55d,
            MinimumScoreGap = supplied?.MinimumScoreGap ?? .05d,
            AllowExactText = supplied?.AllowExactText == true,
        };
        result.AllowedCandidateKinds.Clear();
        foreach (var kind in (supplied?.AllowedCandidateKinds ?? new HashSet<string>(StringComparer.Ordinal) { "automation-id" }))
            result.AllowedCandidateKinds.Add(kind);
        foreach (var risk in supplied?.AllowedRiskFlags ?? [])
            result.AllowedRiskFlags.Add(risk);

        if (policy is null)
            return result;

        if (policy.AllowedCandidateKinds.Count > 0)
        {
            result.AllowedCandidateKinds.Clear();
            foreach (var kind in policy.AllowedCandidateKinds)
                result.AllowedCandidateKinds.Add(kind);
        }
        if (policy.AllowedRiskFlags.Count > 0)
        {
            result.AllowedRiskFlags.Clear();
            foreach (var risk in policy.AllowedRiskFlags)
                result.AllowedRiskFlags.Add(risk);
        }
        result.AllowExactText = (result.AllowExactText ||
            policy.AllowedCandidateKinds.Contains("exact-text")) &&
            result.AllowedCandidateKinds.Contains("exact-text") &&
            result.AllowedRiskFlags.Contains("localization");
        if (policy.MaxCandidates is { } max)
            result.MaxCandidates = max;
        if (policy.MinimumScore is { } score)
            result.MinimumScore = score;
        if (policy.MinimumScoreGap is { } gap)
            result.MinimumScoreGap = gap;
        return result;
    }

    private static bool HasExactlyOneSelectorForm(FlowSelector selector)
    {
        var forms = 0;
        if (!string.IsNullOrWhiteSpace(selector.AutomationId)) forms++;
        if (!string.IsNullOrWhiteSpace(selector.Text)) forms++;
        if (!string.IsNullOrWhiteSpace(selector.Id)) forms++;
        if (selector.TypeIndex is not null) forms++;
        return forms == 1;
    }

    private static bool SelectorsMatch(FlowSelector first, FlowSelector second)
        => string.Equals(first.AutomationId, second.AutomationId, StringComparison.Ordinal) &&
           string.Equals(first.StableItemKey, second.StableItemKey, StringComparison.Ordinal) &&
           string.Equals(first.CollectionScope, second.CollectionScope, StringComparison.Ordinal) &&
           string.Equals(first.Text, second.Text, StringComparison.Ordinal) &&
           string.Equals(first.Id, second.Id, StringComparison.Ordinal) &&
           string.Equals(first.TypeIndex?.Type, second.TypeIndex?.Type, StringComparison.Ordinal) &&
           first.TypeIndex?.Index == second.TypeIndex?.Index &&
           string.Equals(first.Type, second.Type, StringComparison.Ordinal) &&
           first.Index == second.Index &&
           string.Equals(first.SelectorKind, second.SelectorKind, StringComparison.Ordinal);

    private static bool IsCandidateListDeterministic(IReadOnlyList<MauiSelectorCandidate> candidates)
    {
        var expected = candidates
            .OrderBy(static candidate => candidate.Rank ?? int.MaxValue)
            .ThenBy(static candidate => candidate.Priority ?? int.MaxValue)
            .ThenByDescending(Score)
            .ThenBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Select(static candidate => candidate.CandidateId)
            .ToArray();
        return expected.SequenceEqual(candidates.Select(static candidate => candidate.CandidateId), StringComparer.Ordinal);
    }

    private static bool IsClose(MauiSelectorCandidate first, MauiSelectorCandidate second, double gap)
        => Math.Abs(Score(first) - Score(second)) < Math.Max(0d, gap);

    private static double Score(MauiSelectorCandidate candidate)
        => candidate.Score ?? candidate.Scores.DeterministicRankScore;

    private static FlowStep? FindStep(MauiFlow flow, string? sourceStepId)
        => MauiFlowStepIdentity.Find(flow, sourceStepId);

    private static FlowSelector? EffectiveSelector(FlowStep step)
        => step.Args?.Selector is { IsEmpty: false } selector ? selector : step.Target;

    private static MauiFlowReference CloneReference(MauiFlowReference source) => new()
    {
        Path = source.Path,
        FlowId = source.FlowId,
        Revision = source.Revision,
        Digest = source.Digest,
        ExtensionData = source.ExtensionData is null
            ? null
            : source.ExtensionData.ToDictionary(static pair => pair.Key, static pair => pair.Value.Clone(), StringComparer.Ordinal),
    };

    private static void Add(
        MauiFlowRepairProposalGenerationResult result,
        string code,
        string message,
        string? candidateId = null)
    {
        if (result.Abstentions.Any(item =>
                string.Equals(item.Code, code, StringComparison.Ordinal) &&
                string.Equals(item.CandidateId, candidateId, StringComparison.Ordinal)))
        {
            return;
        }
        result.Abstentions.Add(new MauiFlowRepairProposalAbstention
        {
            Code = code,
            Message = message,
            CandidateId = candidateId,
        });
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
}

/// <summary>
/// Compares only semantic, value-free element facts; selector identifiers and prior-build
/// fingerprints may drift. The repair eligibility gate separately requires the failed step's
/// current build checkpoint to match before this comparison is used.
/// </summary>
public static class MauiRepairFingerprintComparer
{
    /// <summary>Returns true only when stable scope, identity, topology, and collection facts agree.</summary>
    public static bool SemanticallyMatches(MauiElementFingerprint? expected, MauiElementFingerprint? current)
    {
        if (expected is null || current is null)
            return false;

        return Equal(expected.Context.AppId, current.Context.AppId) &&
            Equal(expected.Context.Platform, current.Context.Platform) &&
            Equal(expected.Context.Route, current.Context.Route) &&
            Equal(expected.Context.Window, current.Context.Window) &&
            Equal(expected.Context.Modal, current.Context.Modal) &&
            Equal(expected.Context.Locale, current.Context.Locale) &&
            Equal(expected.Context.Theme, current.Context.Theme) &&
            Equal(expected.Context.Orientation, current.Context.Orientation) &&
            Equal(expected.Context.DisplayProfile, current.Context.DisplayProfile) &&
            Equal(expected.Managed.Type, current.Managed.Type) &&
            Equal(expected.Managed.FullType, current.Managed.FullType) &&
            Equal(expected.Managed.Role, current.Managed.Role) &&
            SequenceEqual(expected.Managed.Traits, current.Managed.Traits) &&
            Equal(expected.Topology.AncestorHash, current.Topology.AncestorHash) &&
            Equal(expected.Topology.SiblingHash, current.Topology.SiblingHash) &&
            EqualOptional(expected.Collection?.Scope, current.Collection?.Scope) &&
            EqualOptional(expected.Collection?.ItemKey, current.Collection?.ItemKey) &&
            EqualOptional(expected.Collection?.TemplateKind, current.Collection?.TemplateKind) &&
            expected.Collection?.Virtualized == current.Collection?.Virtualized;
    }

    private static bool Equal(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) &&
           !string.IsNullOrWhiteSpace(second) &&
           string.Equals(first, second, StringComparison.Ordinal);

    private static bool EqualOptional(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second) ||
           Equal(first, second);

    private static bool SequenceEqual(IReadOnlyList<string> first, IReadOnlyList<string> second)
        => first.Count == second.Count &&
           first.OrderBy(static value => value, StringComparer.Ordinal)
               .SequenceEqual(second.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal);
}

/// <summary>Creates and verifies minimal selector-only JSON patches without writing a flow.</summary>
public static class MauiFlowRepairPatchBuilder
{
    /// <summary>Builds an in-memory selector replacement and invariant proof for one flow step.</summary>
    public static MauiFlowRepairPatchBuildResult Build(
        MauiFlow? source,
        int stepSequence,
        FlowSelector? proposedSelector)
        => BuildForStep(
            source,
            MauiFlowStepIdentity.FindBySequence(source, stepSequence),
            proposedSelector);

    /// <summary>
    /// Builds an in-memory selector replacement for a stable step identity, with legacy sequence
    /// strings retained as a compatibility fallback.
    /// </summary>
    public static MauiFlowRepairPatchBuildResult Build(
        MauiFlow? source,
        string? stepId,
        FlowSelector? proposedSelector)
        => BuildForStep(
            source,
            MauiFlowStepIdentity.Find(source, stepId),
            proposedSelector);

    private static MauiFlowRepairPatchBuildResult BuildForStep(
        MauiFlow? source,
        FlowStep? sourceStep,
        FlowSelector? proposedSelector)
    {
        if (source is null)
            return Failure("A source flow is required.");
        if (proposedSelector is null || proposedSelector.IsEmpty)
            return Failure("A proposed executable selector is required.");
        if (!HasExactlyOneSelectorForm(proposedSelector))
            return Failure("The proposed selector must use exactly one selector form.");

        if (sourceStep is null)
            return Failure("The requested source step does not exist.");
        var stepIndex = source.Steps.IndexOf(sourceStep);
        var resolvedStepId = MauiFlowStepIdentity.Get(sourceStep);
        var original = MauiFlowClone.Clone(source);
        var patched = MauiFlowClone.Clone(source);
        var originalStep = original.Steps[stepIndex];
        var patchedStep = patched.Steps[stepIndex];

        var usesArgsSelector = patchedStep.Args?.Selector is { IsEmpty: false };
        var oldSelector = usesArgsSelector ? originalStep.Args!.Selector : originalStep.Target;
        if (oldSelector is null || oldSelector.IsEmpty)
            return Failure("The requested source step has no active selector.");

        var selectorClone = MauiFlowRepairClone.CloneSelector(proposedSelector)!;
        if (usesArgsSelector)
            patchedStep.Args!.Selector = selectorClone;
        else
            patchedStep.Target = selectorClone;

        var before = Invariants(original);
        var after = Invariants(patched);
        if (!string.Equals(before.Assertions, after.Assertions, StringComparison.Ordinal) ||
            !string.Equals(before.Actions, after.Actions, StringComparison.Ordinal) ||
            !string.Equals(before.Values, after.Values, StringComparison.Ordinal) ||
            !string.Equals(before.Order, after.Order, StringComparison.Ordinal))
        {
            return Failure("A selector repair may not change assertions, actions, values, or order.");
        }

        var selectorPath = usesArgsSelector
            ? $"/steps/{stepIndex}/args/selector"
            : $"/steps/{stepIndex}/target";
        var beforeDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(original);
        var afterDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(patched);
        var patch = new MauiFlowPatch
        {
            Format = "json-patch-selector-v1",
            SelectorOnly = true,
            BeforeDigest = beforeDigest,
            AfterDigest = afterDigest,
            Operations =
            [
                new MauiFlowPatchOperation
                {
                    Op = "replace",
                    Path = selectorPath,
                    Value = JsonSerializer.SerializeToElement(selectorClone, MauiFlowJsonContext.Default.FlowSelector),
                },
            ],
        };
        var patchDigest = DigestPatch(patch);
        var proof = new MauiRepairAssertionProof
        {
            Unchanged = true,
            AssertionDigest = before.Assertions,
            ActionsUnchanged = true,
            ActionDigest = before.Actions,
            ValuesUnchanged = true,
            ValueDigest = before.Values,
            OrderUnchanged = true,
            OrderDigest = before.Order,
            Method = "canonical-selector-only-v1",
        };
        var diff = CreateDiff(
            resolvedStepId,
            selectorPath,
            oldSelector,
            selectorClone,
            proof);
        return new MauiFlowRepairPatchBuildResult
        {
            PatchedFlow = patched,
            Patch = patch,
            Diff = diff,
            Proof = proof,
            PatchDigest = patchDigest,
        };
    }

    /// <summary>
    /// Rebuilds a patch from its declared replacement and verifies that it is selector-only before
    /// returning a cloned flow. It is used by host stores before compare-and-swap persistence.
    /// </summary>
    public static MauiFlowRepairPatchBuildResult ApplyVerified(
        MauiFlow? source,
        MauiFlowRepairProposal? proposal)
    {
        if (proposal?.Patch is null || proposal.Patch.SelectorOnly != true ||
            proposal.Patch.Operations.Count != 1 ||
            proposal.ProposedSelector is null ||
            string.IsNullOrWhiteSpace(proposal.SourceStepId))
        {
            return Failure("The proposal is not a single selector-only patch.");
        }

        var rebuilt = Build(source, proposal.SourceStepId, proposal.ProposedSelector);
        if (!rebuilt.Ok)
            return rebuilt;
        if (!string.Equals(rebuilt.PatchDigest, proposal.PatchDigest, StringComparison.Ordinal))
            return Failure("The proposal patch digest does not match the canonical selector-only patch.");
        if (!string.Equals(
                rebuilt.Proof?.AssertionDigest,
                proposal.UnchangedAssertionsProof?.AssertionDigest,
                StringComparison.Ordinal) ||
            proposal.UnchangedAssertionsProof?.Unchanged != true ||
            proposal.UnchangedAssertionsProof?.ActionsUnchanged != true ||
            proposal.UnchangedAssertionsProof?.ValuesUnchanged != true ||
            proposal.UnchangedAssertionsProof?.OrderUnchanged != true)
        {
            return Failure("The proposal invariant proof does not match the current flow.");
        }
        return rebuilt;
    }

    private static MauiRepairSelectorDiff CreateDiff(
        string stepId,
        string selectorPath,
        FlowSelector oldSelector,
        FlowSelector proposedSelector,
        MauiRepairAssertionProof proof)
    {
        var oldJson = JsonSerializer.Serialize(oldSelector, MauiFlowJsonContext.Default.FlowSelector);
        var newJson = JsonSerializer.Serialize(proposedSelector, MauiFlowJsonContext.Default.FlowSelector);
        var node = new JsonObject
        {
            ["schema"] = 1,
            ["op"] = "replace",
            ["path"] = selectorPath,
            ["oldSelector"] = JsonNode.Parse(oldJson),
            ["newSelector"] = JsonNode.Parse(newJson),
            ["assertionsUnchanged"] = proof.Unchanged,
            ["actionsUnchanged"] = proof.ActionsUnchanged,
            ["valuesUnchanged"] = proof.ValuesUnchanged,
            ["orderUnchanged"] = proof.OrderUnchanged,
        };
        var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var markdown = string.Join(
            "\n",
            $"## Selector repair — step {stepId}",
            string.Empty,
            $"```diff",
            $"- {oldJson}",
            $"+ {newJson}",
            "```",
            string.Empty,
            "- Assertions unchanged: yes",
            "- Actions unchanged: yes",
            "- Values unchanged: yes",
            "- Step order unchanged: yes");
        return new MauiRepairSelectorDiff
        {
            Json = json,
            Markdown = markdown,
            StepId = stepId,
            SelectorPath = selectorPath,
            AssertionsUnchanged = true,
            ActionsUnchanged = true,
            ValuesUnchanged = true,
            OrderUnchanged = true,
        };
    }

    private static (string Assertions, string Actions, string Values, string Order) Invariants(MauiFlow flow)
    {
        var assertionMaterial = string.Join(
            "\u001e",
            flow.Steps.OrderBy(static step => step.Seq).SelectMany(step =>
                (step.Asserts ?? []).Select(assertion => string.Join(
                    "\u001f",
                    step.Seq,
                    assertion.Kind,
                    assertion.Name,
                    assertion.Expected,
                    assertion.Verify,
                    assertion.Note,
                    SelectorMaterial(assertion.Selector)))));
        var actionMaterial = string.Join(
            "\u001e",
            flow.Steps.OrderBy(static step => step.Seq).Select(static step =>
                string.Join("\u001f", step.Seq, step.Action)));
        var valueMaterial = string.Join(
            "\u001e",
            flow.Steps.OrderBy(static step => step.Seq).Select(static step =>
                string.Join(
                    "\u001f",
                    step.Seq,
                    step.Value,
                    step.Args?.Text,
                    step.Args?.Value,
                    step.Args?.Name,
                    step.Args?.Route,
                    step.Args?.Theme,
                    step.Args?.SecretEnvironmentVariable,
                    step.Args?.Element,
                    step.Args?.Dx,
                    step.Args?.Dy,
                    step.Args?.ItemIndex,
                    step.Args?.Position,
                    step.Args?.Animated)));
        var orderMaterial = string.Join(
            "\u001e",
            flow.Steps.Select(static step => step.Seq));
        return (Hash(assertionMaterial), Hash(actionMaterial), Hash(valueMaterial), Hash(orderMaterial));
    }

    private static string DigestPatch(MauiFlowPatch patch)
    {
        var operation = patch.Operations.Single();
        var material = string.Join(
            "\u001f",
            patch.Format,
            patch.SelectorOnly,
            patch.BeforeDigest,
            patch.AfterDigest,
            operation.Op,
            operation.Path,
            operation.Value?.GetRawText());
        return "sha256:" + Hash(material);
    }

    private static bool HasExactlyOneSelectorForm(FlowSelector selector)
    {
        var forms = 0;
        if (!string.IsNullOrWhiteSpace(selector.AutomationId)) forms++;
        if (!string.IsNullOrWhiteSpace(selector.Text)) forms++;
        if (!string.IsNullOrWhiteSpace(selector.Id)) forms++;
        if (selector.TypeIndex is not null) forms++;
        return forms == 1;
    }

    internal static string SelectorMaterial(FlowSelector? selector)
    {
        if (selector is null)
            return string.Empty;
        var legacy = string.Join(
            "\u001f",
            selector.AutomationId,
            selector.Text,
            selector.Id,
            selector.TypeIndex?.Type,
            selector.TypeIndex?.Index);
        return selector.HasScopedStableItem
            ? string.Join("\u001f", legacy, selector.StableItemKey, selector.CollectionScope)
            : legacy;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static MauiFlowRepairPatchBuildResult Failure(string error) => new() { Error = error };
}

internal static class MauiFlowRepairClone
{
    public static FlowSelector? CloneSelector(FlowSelector? selector)
    {
        if (selector is null)
            return null;
        return new FlowSelector
        {
            AutomationId = selector.AutomationId,
            StableItemKey = selector.StableItemKey,
            CollectionScope = selector.CollectionScope,
            Text = selector.Text,
            Id = selector.Id,
            Type = selector.Type,
            Index = selector.Index,
            SelectorKind = selector.SelectorKind,
            MatchCount = selector.MatchCount,
            Quality = selector.Quality,
            FragilityReasons = selector.FragilityReasons?.ToList(),
            TypeIndex = selector.TypeIndex is null
                ? null
                : new FlowTypeIndex
                {
                    Type = selector.TypeIndex.Type,
                    Index = selector.TypeIndex.Index,
                    ExtensionData = CloneExtensions(selector.TypeIndex.ExtensionData),
                },
            ExtensionData = CloneExtensions(selector.ExtensionData),
        };
    }

    public static MauiSelectorCandidate CloneCandidate(MauiSelectorCandidate candidate)
        => MauiFlowClone.CloneSelectorEvidence(new MauiSelectorEvidence { Candidates = [candidate] })!.Candidates.Single();

    private static Dictionary<string, JsonElement>? CloneExtensions(Dictionary<string, JsonElement>? source)
        => source?.ToDictionary(static pair => pair.Key, static pair => pair.Value.Clone(), StringComparer.Ordinal);
}
