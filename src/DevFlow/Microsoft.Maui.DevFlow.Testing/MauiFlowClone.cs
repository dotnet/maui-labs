using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

internal static class MauiFlowClone
{
    public static MauiFlow Clone(MauiFlow source) => new()
    {
        Schema = source.Schema,
        Name = source.Name,
        App = source.App,
        Platform = source.Platform,
        RecordedAt = source.RecordedAt,
        Preconditions = source.Preconditions,
        ExpectedEvidence = CloneExpectedEvidence(source.ExpectedEvidence),
        Steps = source.Steps.Select(CloneStep).ToList(),
        ExtensionData = CloneExtensions(source.ExtensionData),
    };

    private static FlowStep CloneStep(FlowStep step) => new()
    {
        Seq = step.Seq,
        StepId = step.StepId,
        Action = step.Action,
        Label = step.Label,
        Intent = step.Intent,
        AcceptanceCriterionIds = step.AcceptanceCriterionIds is null
            ? null
            : new List<string>(step.AcceptanceCriterionIds),
        Target = CloneSelector(step.Target),
        Value = step.Value,
        Args = step.Args is null ? null : new FlowStepArgs
        {
            Selector = CloneSelector(step.Args.Selector),
            Text = step.Args.Text,
            Name = step.Args.Name,
            Value = step.Args.Value,
            Route = step.Args.Route,
            Theme = step.Args.Theme,
            ValueSource = step.Args.ValueSource,
            SecretEnvironmentVariable = step.Args.SecretEnvironmentVariable,
            Element = step.Args.Element,
            Dx = step.Args.Dx,
            Dy = step.Args.Dy,
            ItemIndex = step.Args.ItemIndex,
            Position = step.Args.Position,
            Animated = step.Args.Animated,
            ExtensionData = CloneExtensions(step.Args.ExtensionData),
        },
        Page = step.Page,
        Navigated = step.Navigated,
        Fragile = step.Fragile,
        Screenshot = step.Screenshot,
        ExpectedEvidence = CloneExpectedEvidence(step.ExpectedEvidence),
        Asserts = step.Asserts?.Select(assertion => new FlowAssert
        {
            Kind = assertion.Kind,
            Selector = CloneSelector(assertion.Selector),
            Name = assertion.Name,
            Expected = assertion.Expected,
            Verify = assertion.Verify,
            Note = assertion.Note,
            ExtensionData = CloneExtensions(assertion.ExtensionData),
        }).ToList(),
        SelectorEvidence = CloneSelectorEvidence(step.SelectorEvidence),
        ExtensionData = CloneExtensions(step.ExtensionData),
    };

    internal static MauiSelectorEvidence? CloneSelectorEvidence(MauiSelectorEvidence? evidence) => evidence is null
        ? null
        : new MauiSelectorEvidence
        {
            Schema = evidence.Schema,
            Fingerprint = CloneFingerprint(evidence.Fingerprint),
            Candidates = evidence.Candidates.Select(CloneCandidate).ToList(),
            Omissions = evidence.Omissions.Select(omission => new MauiSelectorEvidenceOmission
            {
                Kind = omission.Kind,
                Reason = omission.Reason,
                Count = omission.Count,
            }).ToList(),
        };

    private static MauiElementFingerprint? CloneFingerprint(MauiElementFingerprint? fingerprint) => fingerprint is null
        ? null
        : new MauiElementFingerprint
        {
            Schema = fingerprint.Schema,
            FingerprintId = fingerprint.FingerprintId,
            AppId = fingerprint.AppId,
            BuildFingerprint = fingerprint.BuildFingerprint,
            Platform = fingerprint.Platform,
            Route = fingerprint.Route,
            Window = fingerprint.Window,
            Modal = fingerprint.Modal,
            ManagedType = fingerprint.ManagedType,
            FullType = fingerprint.FullType,
            Role = fingerprint.Role,
            Traits = fingerprint.Traits.ToList(),
            AutomationId = fingerprint.AutomationId,
            NativeAutomationId = fingerprint.NativeAutomationId,
            SourceAnchor = fingerprint.SourceAnchor,
            SourceHash = fingerprint.SourceHash,
            SourceConfidence = fingerprint.SourceConfidence,
            AncestorTopologyHash = fingerprint.AncestorTopologyHash,
            SiblingTopologyHash = fingerprint.SiblingTopologyHash,
            CollectionKey = fingerprint.CollectionKey,
            ItemKey = fingerprint.ItemKey,
            Locale = fingerprint.Locale,
            Theme = fingerprint.Theme,
            Orientation = fingerprint.Orientation,
            DisplayProfile = fingerprint.DisplayProfile,
            Context = new MauiElementFingerprintContext
            {
                AppId = fingerprint.Context.AppId,
                AppBuild = fingerprint.Context.AppBuild,
                Platform = fingerprint.Context.Platform,
                Route = fingerprint.Context.Route,
                Window = fingerprint.Context.Window,
                Modal = fingerprint.Context.Modal,
                Locale = fingerprint.Context.Locale,
                Theme = fingerprint.Context.Theme,
                Orientation = fingerprint.Context.Orientation,
                DisplayProfile = fingerprint.Context.DisplayProfile,
            },
            Managed = new MauiManagedElementIdentity
            {
                Type = fingerprint.Managed.Type,
                FullType = fingerprint.Managed.FullType,
                Framework = fingerprint.Managed.Framework,
                Role = fingerprint.Managed.Role,
                Traits = fingerprint.Managed.Traits.ToList(),
                AutomationId = fingerprint.Managed.AutomationId,
            },
            Native = fingerprint.Native is null ? null : new MauiNativeAutomationIdentity
            {
                Identity = fingerprint.Native.Identity,
                Kind = fingerprint.Native.Kind,
                Authoritative = fingerprint.Native.Authoritative,
            },
            Source = fingerprint.Source is null ? null : new MauiSourceAnchor
            {
                File = fingerprint.Source.File,
                Line = fingerprint.Source.Line,
                Column = fingerprint.Source.Column,
                BuildHash = fingerprint.Source.BuildHash,
                CurrentHash = fingerprint.Source.CurrentHash,
                State = fingerprint.Source.State,
                Confidence = fingerprint.Source.Confidence,
            },
            Topology = new MauiTopologySignature
            {
                AncestorHash = fingerprint.Topology.AncestorHash,
                SiblingHash = fingerprint.Topology.SiblingHash,
                ChildHash = fingerprint.Topology.ChildHash,
                StableAncestorAutomationId = fingerprint.Topology.StableAncestorAutomationId,
            },
            Collection = fingerprint.Collection is null ? null : new MauiCollectionIdentity
            {
                Scope = fingerprint.Collection.Scope,
                ItemKey = fingerprint.Collection.ItemKey,
                TemplateKind = fingerprint.Collection.TemplateKind,
                Virtualized = fingerprint.Collection.Virtualized,
            },
            NormalizedBounds = fingerprint.NormalizedBounds is null ? null : new MauiNormalizedBounds
            {
                X = fingerprint.NormalizedBounds.X,
                Y = fingerprint.NormalizedBounds.Y,
                Width = fingerprint.NormalizedBounds.Width,
                Height = fingerprint.NormalizedBounds.Height,
            },
            ObservedAt = fingerprint.ObservedAt,
            CapabilityVersion = fingerprint.CapabilityVersion,
            EvidenceRefs = fingerprint.EvidenceRefs.ToList(),
            ExtensionData = CloneExtensions(fingerprint.ExtensionData),
        };

    private static MauiSelectorCandidate CloneCandidate(MauiSelectorCandidate candidate) => new()
    {
        Schema = candidate.Schema,
        CandidateId = candidate.CandidateId,
        Rank = candidate.Rank,
        Priority = candidate.Priority,
        Selector = CloneSelector(candidate.Selector),
        SelectorDescriptor = new MauiSelectorCandidateSelector
        {
            Kind = candidate.SelectorDescriptor.Kind,
            AutomationId = candidate.SelectorDescriptor.AutomationId,
            StableItemKey = candidate.SelectorDescriptor.StableItemKey,
            NativeAutomationIdentity = candidate.SelectorDescriptor.NativeAutomationIdentity,
            Role = candidate.SelectorDescriptor.Role,
            Type = candidate.SelectorDescriptor.Type,
            AncestorAutomationId = candidate.SelectorDescriptor.AncestorAutomationId,
            SourceAnchor = candidate.SelectorDescriptor.SourceAnchor,
            ExactText = candidate.SelectorDescriptor.ExactText,
        },
        Scope = candidate.Scope is null ? null : new MauiFlowCheckpoint
        {
            AppBuildFingerprint = candidate.Scope.AppBuildFingerprint,
            AgentInstanceId = candidate.Scope.AgentInstanceId,
            SeedFingerprint = candidate.Scope.SeedFingerprint,
            BackendStateFingerprint = candidate.Scope.BackendStateFingerprint,
            Route = candidate.Scope.Route,
            Window = candidate.Scope.Window,
            Modal = candidate.Scope.Modal,
            Locale = candidate.Scope.Locale,
            Theme = candidate.Scope.Theme,
            Orientation = candidate.Scope.Orientation,
            DisplayProfile = candidate.Scope.DisplayProfile,
            CollectionItemKey = candidate.Scope.CollectionItemKey,
            ExtensionData = CloneExtensions(candidate.Scope.ExtensionData),
        },
        ScopeDescriptor = new MauiSelectorCandidateScope
        {
            Route = candidate.ScopeDescriptor.Route,
            Window = candidate.ScopeDescriptor.Window,
            Modal = candidate.ScopeDescriptor.Modal,
            CollectionScope = candidate.ScopeDescriptor.CollectionScope,
            LocaleAssumption = candidate.ScopeDescriptor.LocaleAssumption,
        },
        Origin = candidate.Origin,
        OriginCodes = candidate.OriginCodes.ToList(),
        RationaleCodes = candidate.RationaleCodes.ToList(),
        RiskFlags = candidate.RiskFlags.ToList(),
        Score = candidate.Score,
        ScoreComponents = new Dictionary<string, double>(candidate.ScoreComponents, StringComparer.Ordinal),
        Unique = candidate.Unique,
        PlatformValidated = candidate.PlatformValidated,
        CalibrationStatus = candidate.CalibrationStatus,
        Fingerprint = CloneFingerprint(candidate.Fingerprint),
        Scores = new MauiSelectorCandidateScores
        {
            RuleVersion = candidate.Scores.RuleVersion,
            AppOwnedIdentifier = candidate.Scores.AppOwnedIdentifier,
            ScopeMatch = candidate.Scores.ScopeMatch,
            ManagedNativeAgreement = candidate.Scores.ManagedNativeAgreement,
            SourceAnchorMatch = candidate.Scores.SourceAnchorMatch,
            TopologySimilarity = candidate.Scores.TopologySimilarity,
            NormalizedGeometryCorroboration = candidate.Scores.NormalizedGeometryCorroboration,
            LocalizationPenalty = candidate.Scores.LocalizationPenalty,
            VirtualizationPenalty = candidate.Scores.VirtualizationPenalty,
            StaleSourcePenalty = candidate.Scores.StaleSourcePenalty,
            PlatformDivergencePenalty = candidate.Scores.PlatformDivergencePenalty,
            AmbiguityPenalty = candidate.Scores.AmbiguityPenalty,
            DeterministicRankScore = candidate.Scores.DeterministicRankScore,
        },
        Validation = new MauiSelectorCandidateValidation
        {
            Unique = candidate.Validation.Unique,
            MatchCount = candidate.Validation.MatchCount,
            PlatformState = candidate.Validation.PlatformState,
            SourceState = candidate.Validation.SourceState,
            Accepted = candidate.Validation.Accepted,
            RejectionReason = candidate.Validation.RejectionReason,
        },
        Calibration = new MauiSelectorCandidateCalibration
        {
            State = candidate.Calibration.State,
            RuleVersion = candidate.Calibration.RuleVersion,
        },
        EvidenceRefs = candidate.EvidenceRefs.ToList(),
        ExtensionData = CloneExtensions(candidate.ExtensionData),
    };

    private static FlowSelector? CloneSelector(FlowSelector? selector) => selector is null ? null : new FlowSelector
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
        FragilityReasons = selector.FragilityReasons is null ? null : new List<string>(selector.FragilityReasons),
        TypeIndex = selector.TypeIndex is null ? null : new FlowTypeIndex
        {
            Type = selector.TypeIndex.Type,
            Index = selector.TypeIndex.Index,
            ExtensionData = CloneExtensions(selector.TypeIndex.ExtensionData),
        },
        ExtensionData = CloneExtensions(selector.ExtensionData),
    };

    private static List<FlowExpectedEvidence>? CloneExpectedEvidence(List<FlowExpectedEvidence>? declarations)
        => declarations?.Select(static declaration => new FlowExpectedEvidence
        {
            Id = declaration.Id,
            Kind = declaration.Kind,
            Reference = declaration.Reference,
            Note = declaration.Note,
            ExtensionData = CloneExtensions(declaration.ExtensionData),
        }).ToList();

    private static Dictionary<string, JsonElement>? CloneExtensions(Dictionary<string, JsonElement>? extensions)
    {
        if (extensions is null)
            return null;

        var clone = new Dictionary<string, JsonElement>(extensions.Count, StringComparer.Ordinal);
        foreach (var (name, value) in extensions)
            clone[name] = value.Clone();

        return clone;
    }
}
