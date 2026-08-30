using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A human-reviewed, IDE-mediated proposal to add or replace one literal C# <c>AutomationId</c>.
/// It is advisory only: neither an agent nor the broker may write the source document.
/// </summary>
public sealed class MauiCSharpSourceProposal
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("language")] public string Language { get; init; } = "CSharp";
    [JsonPropertyName("proposalId")] public string? ProposalId { get; init; }
    [JsonPropertyName("revision")] public int? Revision { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("operation")] public MauiCSharpSourceOperation Operation { get; init; } = new();
    [JsonPropertyName("element")] public MauiCSharpSourceElementIdentity Element { get; init; } = new();
    [JsonPropertyName("baseContentDigest")] public string? BaseContentDigest { get; init; }
    [JsonPropertyName("patch")] public MauiCSharpSourcePatch Patch { get; init; } = new();
    [JsonPropertyName("rollbackPatch")] public MauiCSharpSourcePatch RollbackPatch { get; init; } = new();
    [JsonPropertyName("patchDigest")] public string? PatchDigest { get; init; }
    [JsonPropertyName("rollbackPatchDigest")] public string? RollbackPatchDigest { get; init; }
    [JsonPropertyName("diffDigest")] public string? DiffDigest { get; init; }
    [JsonPropertyName("diff")] public string? Diff { get; init; }
    [JsonPropertyName("eligibility")] public MauiCSharpSourceEligibilityDecision Eligibility { get; init; } = new();
    [JsonPropertyName("uniqueness")] public MauiCSharpSourceUniquenessEvidence Uniqueness { get; init; } = new();
    [JsonPropertyName("affectedFlows")] public List<MauiCSharpSourceFlowFollowUp> AffectedFlows { get; init; } = [];
    [JsonPropertyName("affectedPlatforms")] public List<MauiCSharpSourcePlatformVerification> AffectedPlatforms { get; init; } = [];
    [JsonPropertyName("riskFlags")] public List<string> RiskFlags { get; init; } = [];
    [JsonPropertyName("provenance")] public MauiActorProvenance? Provenance { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One Roslyn-proven, tightly bounded operation over a single registered C# document.</summary>
public sealed class MauiCSharpSourceOperation
{
    [JsonPropertyName("operationId")] public string? OperationId { get; init; }
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    [JsonPropertyName("fileRelativePath")] public string? FileRelativePath { get; init; }
    [JsonPropertyName("sourceHash")] public string? SourceHash { get; init; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; init; }
    [JsonPropertyName("symbolId")] public string? SymbolId { get; init; }
    [JsonPropertyName("semanticType")] public string? SemanticType { get; init; }
    [JsonPropertyName("oldLiteral")] public string? OldLiteral { get; init; }
    [JsonPropertyName("newLiteral")] public string? NewLiteral { get; init; }
    [JsonPropertyName("attribute")] public string? Attribute { get; init; }
    [JsonPropertyName("spanStart")] public int? SpanStart { get; init; }
    [JsonPropertyName("spanLength")] public int? SpanLength { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Exact semantic declaration identity, not a runtime selector.</summary>
public sealed class MauiCSharpSourceElementIdentity
{
    [JsonPropertyName("elementType")] public string? ElementType { get; init; }
    [JsonPropertyName("line")] public int? Line { get; init; }
    [JsonPropertyName("column")] public int? Column { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; init; }
    [JsonPropertyName("spanStart")] public int? SpanStart { get; init; }
    [JsonPropertyName("spanLength")] public int? SpanLength { get; init; }
    [JsonPropertyName("symbolId")] public string? SymbolId { get; init; }
    [JsonPropertyName("semanticType")] public string? SemanticType { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One exact text replacement. Hosts must compare both digests before applying it.</summary>
public sealed class MauiCSharpSourcePatch
{
    [JsonPropertyName("format")] public string? Format { get; init; }
    [JsonPropertyName("operation")] public string? Operation { get; init; }
    [JsonPropertyName("beforeDigest")] public string? BeforeDigest { get; init; }
    [JsonPropertyName("afterDigest")] public string? AfterDigest { get; init; }
    [JsonPropertyName("start")] public int? Start { get; init; }
    [JsonPropertyName("length")] public int? Length { get; init; }
    [JsonPropertyName("replacement")] public string? Replacement { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Fail-closed C# proposal eligibility result.</summary>
public sealed class MauiCSharpSourceEligibilityDecision
{
    [JsonPropertyName("eligible")] public bool Eligible { get; init; }
    [JsonPropertyName("reasons")] public List<MauiCSharpSourceEligibilityReason> Reasons { get; init; } = [];
    [JsonPropertyName("analyzedAt")] public DateTimeOffset? AnalyzedAt { get; init; }
    [JsonPropertyName("policy")] public string? Policy { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One precise reason that a C# declaration is eligible or rejected.</summary>
public sealed class MauiCSharpSourceEligibilityReason
{
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("blocking")] public bool Blocking { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Project and live-tree uniqueness evidence collected before a proposal is issued.</summary>
public sealed class MauiCSharpSourceUniquenessEvidence
{
    [JsonPropertyName("projectScope")] public string? ProjectScope { get; init; }
    [JsonPropertyName("projectMatchCount")] public int? ProjectMatchCount { get; init; }
    [JsonPropertyName("liveScope")] public string? LiveScope { get; init; }
    [JsonPropertyName("liveMatchCount")] public int? LiveMatchCount { get; init; }
    [JsonPropertyName("liveScopeAvailable")] public bool? LiveScopeAvailable { get; init; }
    [JsonPropertyName("validatedAt")] public DateTimeOffset? ValidatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A potentially affected flow; its selector remains a separate reviewed repair proposal.</summary>
public sealed class MauiCSharpSourceFlowFollowUp
{
    [JsonPropertyName("flowPath")] public string? FlowPath { get; init; }
    [JsonPropertyName("flowId")] public string? FlowId { get; init; }
    [JsonPropertyName("flowDigest")] public string? FlowDigest { get; init; }
    [JsonPropertyName("stepIds")] public List<string> StepIds { get; init; } = [];
    [JsonPropertyName("recommendedSelector")] public FlowSelector? RecommendedSelector { get; init; }
    [JsonPropertyName("requiresSeparateApproval")] public bool RequiresSeparateApproval { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Build, remap, uniqueness, replay, and oracle status for a required platform target.</summary>
public sealed class MauiCSharpSourcePlatformVerification
{
    [JsonPropertyName("platform")] public string? Platform { get; init; }
    [JsonPropertyName("targetFramework")] public string? TargetFramework { get; init; }
    [JsonPropertyName("buildState")] public string? BuildState { get; init; }
    [JsonPropertyName("runtimeRemapState")] public string? RuntimeRemapState { get; init; }
    [JsonPropertyName("uniquenessState")] public string? UniquenessState { get; init; }
    [JsonPropertyName("replayState")] public string? ReplayState { get; init; }
    [JsonPropertyName("oracleState")] public string? OracleState { get; init; }
    [JsonPropertyName("reasonCode")] public string? ReasonCode { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Lifecycle states shared with reviewed XAML proposals, with no automatic apply state.</summary>
public static class MauiCSharpSourceProposalStates
{
    public const string Proposed = "proposed";
    public const string Previewed = "previewed";
    public const string Stale = "stale";
    public const string Rejected = "rejected";
}

/// <summary>Stable fail-closed reason codes for C# source proposal eligibility.</summary>
public static class MauiCSharpSourceIneligibilityCodes
{
    public const string SourceMapUnavailable = "source-map-unavailable";
    public const string SourceHashMismatch = "source-hash-mismatch";
    public const string SourceFileOutsideProject = "source-file-outside-project";
    public const string SourceFileUnregistered = "source-file-unregistered";
    public const string SourceFileGenerated = "source-file-generated";
    public const string SourceFileLinked = "source-file-linked";
    public const string SourcePathReparsePoint = "source-path-reparse-point";
    public const string SourceNotCSharp = "source-not-csharp";
    public const string NativeOrWebViewSynthetic = "native-or-webview-synthetic";
    public const string RepeaterOrVirtualized = "repeater-or-virtualized";
    public const string AutomationIdInvalid = "automation-id-invalid";
    public const string AutomationIdLocalizedOrUserDerived = "automation-id-localized-or-user-derived";
    public const string AutomationIdUnchanged = "automation-id-unchanged";
    public const string AutomationIdDuplicateProject = "automation-id-duplicate-project";
    public const string AutomationIdDuplicateLive = "automation-id-duplicate-live";
    public const string LiveUniquenessUnavailable = "live-uniqueness-unavailable";
    public const string RoslynSemanticModelUnavailable = "roslyn-semantic-model-unavailable";
    public const string SemanticSymbolUnresolved = "semantic-symbol-unresolved";
    public const string UnsupportedControlType = "unsupported-control-type";
    public const string UnsupportedSyntax = "unsupported-csharp-syntax";
    public const string AmbiguousConstructionOrAssignment = "ambiguous-construction-or-assignment";
    public const string TemplateOrRepeater = "template-or-repeater";
    public const string CollectionOrFactory = "collection-lambda-or-factory";
    public const string ConditionalOrPreprocessor = "conditional-or-preprocessor";
    public const string ReflectionOrDynamic = "reflection-or-dynamic-construction";
    public const string ComputedOrBoundValue = "computed-or-bound-automation-id";
}

/// <summary>Provider-neutral input from a Roslyn host to the C# proposal eligibility evaluator.</summary>
public sealed class MauiCSharpSourceEligibilityInput
{
    public string? SourceText { get; init; }
    public string? FileRelativePath { get; init; }
    public string? ExpectedSourceHash { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public int? SourceSpanStart { get; init; }
    public int? SourceSpanLength { get; init; }
    public string? SourceConfidence { get; init; }
    public bool IsProjectContained { get; init; }
    public bool IsRegisteredProjectFile { get; init; }
    public bool IsGenerated { get; init; }
    public bool IsLinked { get; init; }
    public bool HasReparsePoint { get; init; }
    public bool IsNativeOrWebViewSynthetic { get; init; }
    public bool IsVirtualizedOrTemplated { get; init; }
    public bool HasRoslynSemanticModel { get; init; }
    public bool HasResolvedSymbol { get; init; }
    public bool IsSupportedActionableControl { get; init; }
    public bool IsDirectObjectInitializer { get; init; }
    public bool IsDirectLiteralAssignment { get; init; }
    public bool IsSingleUnambiguousSite { get; init; }
    public bool IsInsideTemplateOrRepeater { get; init; }
    public bool IsInsideCollectionLambdaOrFactory { get; init; }
    public bool HasConditionalOrPreprocessorBranch { get; init; }
    public bool HasReflectionOrDynamicConstruction { get; init; }
    public bool HasComputedOrBoundAutomationId { get; init; }
    public string? ExistingAutomationId { get; init; }
    public string? ProposedAutomationId { get; init; }
    public IReadOnlyList<string>? ProjectAutomationIds { get; init; }
    public IReadOnlyList<string>? LiveAutomationIds { get; init; }
    public bool LiveUniquenessAvailable { get; init; }
    public bool RequireLiveUniqueness { get; init; } = true;
}

/// <summary>Parsed C# declaration facts retained with an eligibility result.</summary>
public sealed class MauiCSharpSourceEligibilityAnalysis
{
    public MauiCSharpSourceEligibilityDecision Decision { get; init; } = new();
    public MauiCSharpSourceElementIdentity? Element { get; init; }
    public string? OldAutomationId { get; init; }
    public MauiCSharpSourceUniquenessEvidence Uniqueness { get; init; } = new();
}

/// <summary>
/// Pure C# eligibility policy. Roslyn hosts supply semantic facts; this method performs no file
/// access, project loading, compilation, source mutation, or provider invocation.
/// </summary>
public static class MauiCSharpSourceEligibilityAnalyzer
{
    public static MauiCSharpSourceEligibilityAnalysis Analyze(MauiCSharpSourceEligibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reasons = new List<MauiCSharpSourceEligibilityReason>();
        void Reject(string code, string message)
        {
            if (!reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
            {
                reasons.Add(new MauiCSharpSourceEligibilityReason { Code = code, Message = message });
            }
        }

        foreach (var reason in MauiSourceProposalCommonEligibility.Analyze(
                     new MauiSourceProposalCommonEligibilityInput
                     {
                         SourceText = input.SourceText,
                         FileRelativePath = input.FileRelativePath,
                         ExpectedSourceHash = input.ExpectedSourceHash,
                         RequiredFileExtension = ".cs",
                         WrongLanguageCode = MauiCSharpSourceIneligibilityCodes.SourceNotCSharp,
                         HasMappedSource = input.SourceLine is > 0 &&
                             input.SourceColumn is > 0 &&
                             input.SourceSpanStart is >= 0 &&
                             input.SourceSpanLength is >= 0 &&
                             string.Equals(input.SourceConfidence, "roslyn-proven", StringComparison.OrdinalIgnoreCase),
                         IsProjectContained = input.IsProjectContained,
                         IsRegisteredProjectFile = input.IsRegisteredProjectFile,
                         IsGenerated = input.IsGenerated,
                         IsLinked = input.IsLinked,
                         HasReparsePoint = input.HasReparsePoint,
                         IsNativeOrWebViewSynthetic = input.IsNativeOrWebViewSynthetic,
                         IsVirtualizedOrTemplated = input.IsVirtualizedOrTemplated,
                         ProposedAutomationId = input.ProposedAutomationId,
                         ProjectAutomationIds = input.ProjectAutomationIds,
                         LiveAutomationIds = input.LiveAutomationIds,
                         LiveUniquenessAvailable = input.LiveUniquenessAvailable,
                         RequireLiveUniqueness = input.RequireLiveUniqueness,
                     }))
        {
            Reject(reason.Code, reason.Message);
        }

        if (!input.HasRoslynSemanticModel)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.RoslynSemanticModelUnavailable,
                "A Roslyn semantic model for the registered project document is required.");
        }
        if (!input.HasResolvedSymbol)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.SemanticSymbolUnresolved,
                "The target declaration and its control symbol must resolve unambiguously.");
        }
        if (!input.IsSupportedActionableControl)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.UnsupportedControlType,
                "Only a statically resolved supported MAUI actionable control can receive a C# proposal.");
        }
        if (!input.IsDirectObjectInitializer && !input.IsDirectLiteralAssignment)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.UnsupportedSyntax,
                "Only a direct object initializer or a direct AutomationId string-literal assignment is supported.");
        }
        if (!input.IsSingleUnambiguousSite)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.AmbiguousConstructionOrAssignment,
                "Exactly one construction or AutomationId assignment site must be proven for the target symbol.");
        }
        if (input.IsInsideTemplateOrRepeater)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.TemplateOrRepeater,
                "DataTemplate, ControlTemplate, item-factory, BindableLayout, and repeater declarations are not eligible.");
        }
        if (input.IsInsideCollectionLambdaOrFactory)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.CollectionOrFactory,
                "Collection, lambda, loop, and factory-created controls may represent repeated instances and are not eligible.");
        }
        if (input.HasConditionalOrPreprocessorBranch)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.ConditionalOrPreprocessor,
                "Conditional or unresolved preprocessor declarations are not eligible.");
        }
        if (input.HasReflectionOrDynamicConstruction)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.ReflectionOrDynamic,
                "Reflection or dynamic control construction is not eligible.");
        }
        if (input.HasComputedOrBoundAutomationId)
        {
            Reject(MauiCSharpSourceIneligibilityCodes.ComputedOrBoundValue,
                "Bound, computed, localized, or user-derived AutomationId expressions are not eligible.");
        }
        if (!string.IsNullOrWhiteSpace(input.ExistingAutomationId) &&
            (MauiAutomationIdProposalPolicy.IsPotentiallyLocalizedOrUserDerived(input.ExistingAutomationId) ||
             !MauiAutomationIdProposalPolicy.TryValidate(input.ExistingAutomationId, out _)))
        {
            Reject(MauiCSharpSourceIneligibilityCodes.ComputedOrBoundValue,
                "Replacing an AutomationId is allowed only when the existing value is a safe static test literal.");
        }
        if (string.Equals(input.ExistingAutomationId, input.ProposedAutomationId, StringComparison.Ordinal))
        {
            Reject(MauiCSharpSourceIneligibilityCodes.AutomationIdUnchanged,
                "The proposed AutomationId already matches the safe literal declaration.");
        }

        var proposed = input.ProposedAutomationId ?? string.Empty;
        return new MauiCSharpSourceEligibilityAnalysis
        {
            Decision = new MauiCSharpSourceEligibilityDecision
            {
                Eligible = reasons.Count == 0,
                Reasons = reasons,
                AnalyzedAt = DateTimeOffset.UtcNow,
                Policy = "csharp-automation-id-proposal-v1",
            },
            OldAutomationId = input.ExistingAutomationId,
            Uniqueness = new MauiCSharpSourceUniquenessEvidence
            {
                ProjectScope = "registered-project-csharp",
                ProjectMatchCount = CountMatches(input.ProjectAutomationIds, proposed),
                LiveScope = "current-live-tree",
                LiveMatchCount = input.LiveUniquenessAvailable
                    ? CountMatches(input.LiveAutomationIds, proposed)
                    : null,
                LiveScopeAvailable = input.LiveUniquenessAvailable,
                ValidatedAt = DateTimeOffset.UtcNow,
            },
        };
    }

    private static int CountMatches(IReadOnlyList<string>? values, string proposed)
        => values?.Count(value => string.Equals(value, proposed, StringComparison.Ordinal)) ?? 0;
}
