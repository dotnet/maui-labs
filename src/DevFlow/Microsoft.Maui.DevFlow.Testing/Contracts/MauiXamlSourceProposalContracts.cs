using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A human-reviewed, non-executable proposal to add or replace one literal XAML
/// <c>AutomationId</c>. A source proposal is deliberately distinct from a flow repair proposal.
/// </summary>
public sealed class MauiXamlSourceProposal
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("proposalId")] public string? ProposalId { get; init; }
    [JsonPropertyName("revision")] public int? Revision { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("operation")] public MauiXamlSourceOperation Operation { get; init; } = new();
    [JsonPropertyName("element")] public MauiXamlSourceElementIdentity Element { get; init; } = new();
    [JsonPropertyName("baseContentDigest")] public string? BaseContentDigest { get; init; }
    [JsonPropertyName("patch")] public MauiXamlSourcePatch Patch { get; init; } = new();
    [JsonPropertyName("patchDigest")] public string? PatchDigest { get; init; }
    [JsonPropertyName("diffDigest")] public string? DiffDigest { get; init; }
    [JsonPropertyName("diff")] public string? Diff { get; init; }
    [JsonPropertyName("eligibility")] public MauiXamlSourceEligibilityDecision Eligibility { get; init; } = new();
    [JsonPropertyName("uniqueness")] public MauiXamlSourceUniquenessEvidence Uniqueness { get; init; } = new();
    [JsonPropertyName("affectedFlows")] public List<MauiXamlSourceFlowFollowUp> AffectedFlows { get; init; } = [];
    [JsonPropertyName("affectedPlatforms")] public List<MauiXamlSourcePlatformVerification> AffectedPlatforms { get; init; } = [];
    [JsonPropertyName("riskFlags")] public List<string> RiskFlags { get; init; } = [];
    [JsonPropertyName("provenance")] public MauiActorProvenance? Provenance { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One tightly bounded source operation. No arbitrary C# or source edits are represented.</summary>
public sealed class MauiXamlSourceOperation
{
    [JsonPropertyName("operationId")] public string? OperationId { get; init; }
    [JsonPropertyName("kind")] public string? Kind { get; init; }
    [JsonPropertyName("fileRelativePath")] public string? FileRelativePath { get; init; }
    [JsonPropertyName("sourceHash")] public string? SourceHash { get; init; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; init; }
    [JsonPropertyName("oldLiteral")] public string? OldLiteral { get; init; }
    [JsonPropertyName("newLiteral")] public string? NewLiteral { get; init; }
    [JsonPropertyName("attribute")] public string? Attribute { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Exact declaration identity, not a runtime element or a selector.</summary>
public sealed class MauiXamlSourceElementIdentity
{
    [JsonPropertyName("elementType")] public string? ElementType { get; init; }
    [JsonPropertyName("line")] public int? Line { get; init; }
    [JsonPropertyName("column")] public int? Column { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; init; }
    [JsonPropertyName("startTagOffset")] public int? StartTagOffset { get; init; }
    [JsonPropertyName("startTagLength")] public int? StartTagLength { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Minimal deterministic source patch metadata. The text is available only on the proposal.</summary>
public sealed class MauiXamlSourcePatch
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

/// <summary>A deterministic eligibility answer with explicit fail-closed reasons.</summary>
public sealed class MauiXamlSourceEligibilityDecision
{
    [JsonPropertyName("eligible")] public bool Eligible { get; init; }
    [JsonPropertyName("reasons")] public List<MauiXamlSourceEligibilityReason> Reasons { get; init; } = [];
    [JsonPropertyName("analyzedAt")] public DateTimeOffset? AnalyzedAt { get; init; }
    [JsonPropertyName("policy")] public string? Policy { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One concise, non-source-text eligibility finding.</summary>
public sealed class MauiXamlSourceEligibilityReason
{
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("blocking")] public bool Blocking { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Evidence that the proposed literal is unique in the project and current live scope.</summary>
public sealed class MauiXamlSourceUniquenessEvidence
{
    [JsonPropertyName("projectScope")] public string? ProjectScope { get; init; }
    [JsonPropertyName("projectMatchCount")] public int? ProjectMatchCount { get; init; }
    [JsonPropertyName("liveScope")] public string? LiveScope { get; init; }
    [JsonPropertyName("liveMatchCount")] public int? LiveMatchCount { get; init; }
    [JsonPropertyName("liveScopeAvailable")] public bool? LiveScopeAvailable { get; init; }
    [JsonPropertyName("validatedAt")] public DateTimeOffset? ValidatedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A flow that may later need its selector reviewed in a separate flow-repair proposal.</summary>
public sealed class MauiXamlSourceFlowFollowUp
{
    [JsonPropertyName("flowPath")] public string? FlowPath { get; init; }
    [JsonPropertyName("flowId")] public string? FlowId { get; init; }
    [JsonPropertyName("flowDigest")] public string? FlowDigest { get; init; }
    [JsonPropertyName("stepIds")] public List<string> StepIds { get; init; } = [];
    [JsonPropertyName("recommendedSelector")] public FlowSelector? RecommendedSelector { get; init; }
    [JsonPropertyName("requiresSeparateApproval")] public bool RequiresSeparateApproval { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Build, runtime remap, uniqueness, replay, and external-QA status for one target.</summary>
public sealed class MauiXamlSourcePlatformVerification
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

/// <summary>Known source proposal lifecycle states.</summary>
public static class MauiXamlSourceProposalStates
{
    public const string Proposed = "proposed";
    public const string Previewed = "previewed";
    public const string Stale = "stale";
    public const string Rejected = "rejected";
}

/// <summary>Stable eligibility codes used by hosts and documentation.</summary>
public static class MauiXamlSourceIneligibilityCodes
{
    public const string SourceMapUnavailable = "source-map-unavailable";
    public const string SourceHashMismatch = "source-hash-mismatch";
    public const string SourceMapAmbiguous = "source-map-ambiguous";
    public const string SourceFileOutsideProject = "source-file-outside-project";
    public const string SourceFileUnregistered = "source-file-unregistered";
    public const string SourceFileGenerated = "source-file-generated";
    public const string SourceFileLinked = "source-file-linked";
    public const string SourcePathReparsePoint = "source-path-reparse-point";
    public const string SourceNotXaml = "source-not-xaml";
    public const string XamlMalformed = "xaml-malformed";
    public const string ElementNotFound = "element-not-found";
    public const string ElementNotDirect = "element-not-direct";
    public const string TemplateOrStyle = "template-or-style";
    public const string ResourceDictionary = "resource-dictionary";
    public const string RepeaterOrVirtualized = "repeater-or-virtualized";
    public const string BindingOrMarkup = "binding-or-markup-extension";
    public const string ConditionalOrGeneratedElement = "conditional-or-generated-element";
    public const string NativeOrWebViewSynthetic = "native-or-webview-synthetic";
    public const string AutomationIdInvalid = "automation-id-invalid";
    public const string AutomationIdLocalizedOrUserDerived = "automation-id-localized-or-user-derived";
    public const string AutomationIdUnchanged = "automation-id-unchanged";
    public const string AutomationIdDuplicateProject = "automation-id-duplicate-project";
    public const string AutomationIdDuplicateLive = "automation-id-duplicate-live";
    public const string LiveUniquenessUnavailable = "live-uniqueness-unavailable";
}

/// <summary>Input for the pure, provider-neutral XAML source eligibility evaluator.</summary>
public sealed class MauiXamlSourceEligibilityInput
{
    public string? SourceText { get; init; }
    public string? FileRelativePath { get; init; }
    public string? ExpectedSourceHash { get; init; }
    public int? SourceLine { get; init; }
    public int? SourceColumn { get; init; }
    public string? SourceConfidence { get; init; }
    public bool IsProjectContained { get; init; }
    public bool? IsRegisteredProjectFile { get; init; }
    public bool IsGenerated { get; init; }
    public bool IsLinked { get; init; }
    public bool HasReparsePoint { get; init; }
    public bool IsNativeOrWebViewSynthetic { get; init; }
    public bool IsVirtualized { get; init; }
    public string? TemplateKind { get; init; }
    public string? ProposedAutomationId { get; init; }
    public IReadOnlyList<string>? ProjectAutomationIds { get; init; }
    public IReadOnlyList<string>? LiveAutomationIds { get; init; }
    public bool LiveUniquenessAvailable { get; init; }
    public bool RequireLiveUniqueness { get; init; } = true;
}

/// <summary>Parsed static eligibility facts used by a source host to construct a proposal.</summary>
public sealed class MauiXamlSourceEligibilityAnalysis
{
    public MauiXamlSourceEligibilityDecision Decision { get; init; } = new();
    public MauiXamlSourceElementIdentity? Element { get; init; }
    public string? OldAutomationId { get; init; }
    public int? AttributeValueStart { get; init; }
    public int? AttributeValueLength { get; init; }
    public char? AttributeQuote { get; init; }
    public int? StartTagEnd { get; init; }
    public MauiXamlSourceUniquenessEvidence Uniqueness { get; init; } = new();
}

/// <summary>
/// Pure, conservative XAML eligibility evaluator. It reads no files, invokes no provider, and
/// never writes source. Hosts must establish filesystem and runtime facts before invoking it.
/// </summary>
public static class MauiXamlSourceEligibilityAnalyzer
{
    private static readonly HashSet<string> TemplateOrStyleAncestors = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataTemplate", "ControlTemplate", "Style", "Setter",
    };

    private static readonly HashSet<string> RepeaterAncestors = new(StringComparer.OrdinalIgnoreCase)
    {
        "CollectionView", "ListView", "CarouselView", "BindableLayout", "ItemsView", "CollectionViewSource",
    };

    public static MauiXamlSourceEligibilityAnalysis Analyze(MauiXamlSourceEligibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reasons = new List<MauiXamlSourceEligibilityReason>();
        void Reject(string code, string message)
        {
            if (!reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
            {
                reasons.Add(new MauiXamlSourceEligibilityReason
                {
                    Code = code,
                    Message = message,
                });
            }
        }

        foreach (var reason in MauiSourceProposalCommonEligibility.Analyze(
                     new MauiSourceProposalCommonEligibilityInput
                     {
                         SourceText = input.SourceText,
                         FileRelativePath = input.FileRelativePath,
                         ExpectedSourceHash = input.ExpectedSourceHash,
                         RequiredFileExtension = ".xaml",
                         WrongLanguageCode = MauiXamlSourceIneligibilityCodes.SourceNotXaml,
                         HasMappedSource = input.SourceLine is > 0 &&
                             input.SourceColumn is > 0 &&
                             string.Equals(input.SourceConfidence, "mapped", StringComparison.OrdinalIgnoreCase),
                         IsProjectContained = input.IsProjectContained,
                         IsRegisteredProjectFile = input.IsRegisteredProjectFile == true,
                         IsGenerated = input.IsGenerated,
                         IsLinked = input.IsLinked,
                         HasReparsePoint = input.HasReparsePoint,
                         IsNativeOrWebViewSynthetic = input.IsNativeOrWebViewSynthetic,
                         IsVirtualizedOrTemplated = input.IsVirtualized ||
                             !string.IsNullOrWhiteSpace(input.TemplateKind),
                         ProposedAutomationId = input.ProposedAutomationId,
                         ProjectAutomationIds = input.ProjectAutomationIds,
                         LiveAutomationIds = input.LiveAutomationIds,
                         LiveUniquenessAvailable = input.LiveUniquenessAvailable,
                         RequireLiveUniqueness = input.RequireLiveUniqueness,
                     }))
        {
            Reject(reason.Code, reason.Message);
        }

        if (string.IsNullOrWhiteSpace(input.SourceText) ||
            input.SourceLine is not > 0 ||
            input.SourceColumn is not > 0 ||
            !string.Equals(input.SourceConfidence, "mapped", StringComparison.OrdinalIgnoreCase))
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceMapUnavailable,
                "A current unambiguous mapped XAML declaration is required.");
        }
        if (!IsXamlPath(input.FileRelativePath))
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceNotXaml,
                "Only a registered project .xaml file can receive a source proposal.");
        }
        if (!input.IsProjectContained)
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceFileOutsideProject,
                "The mapped source is not contained by the registered project.");
        }
        if (input.IsRegisteredProjectFile != true)
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceFileUnregistered,
                "The mapped XAML file is not registered by the current project.");
        }
        if (input.IsGenerated)
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceFileGenerated,
                "Generated XAML is not eligible for source proposals.");
        }
        if (input.IsLinked)
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceFileLinked,
                "Linked XAML is not eligible for source proposals.");
        }
        if (input.HasReparsePoint)
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourcePathReparsePoint,
                "A source path containing a symbolic link or reparse point is not eligible.");
        }
        if (input.IsNativeOrWebViewSynthetic)
        {
            Reject(MauiXamlSourceIneligibilityCodes.NativeOrWebViewSynthetic,
                "Native and WebView synthetic elements do not map to a writable static declaration.");
        }
        if (input.IsVirtualized || !string.IsNullOrWhiteSpace(input.TemplateKind))
        {
            Reject(MauiXamlSourceIneligibilityCodes.RepeaterOrVirtualized,
                "Virtualized, templated, and repeater elements are not eligible.");
        }

        var source = input.SourceText ?? string.Empty;
        var actualHash = ComputeSourceHash(source);
        if (!IsExpectedHash(input.ExpectedSourceHash) ||
            !string.Equals(actualHash, input.ExpectedSourceHash, StringComparison.OrdinalIgnoreCase))
        {
            Reject(MauiXamlSourceIneligibilityCodes.SourceHashMismatch,
                "The source map hash does not match the current XAML text.");
        }

        if (MauiXamlAutomationIdGrammar.IsPotentiallyLocalizedOrUserDerived(input.ProposedAutomationId))
        {
            Reject(MauiXamlSourceIneligibilityCodes.AutomationIdLocalizedOrUserDerived,
                "AutomationId must be a static nonlocalized test identifier and must not contain user-derived data.");
        }
        else if (!MauiXamlAutomationIdGrammar.TryValidate(input.ProposedAutomationId, out var idReason))
        {
            Reject(MauiXamlSourceIneligibilityCodes.AutomationIdInvalid, idReason!);
        }

        XElement? element = null;
        XDocument? document = null;
        try
        {
            document = XDocument.Parse(source, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var candidates = document.Root?
                .DescendantsAndSelf()
                .Where(candidate => IsAtMappedLocation(candidate, input.SourceLine, input.SourceColumn))
                .ToList() ?? [];
            if (candidates.Count != 1)
            {
                Reject(
                    candidates.Count == 0
                        ? MauiXamlSourceIneligibilityCodes.ElementNotFound
                        : MauiXamlSourceIneligibilityCodes.SourceMapAmbiguous,
                    candidates.Count == 0
                        ? "The mapped XAML declaration was not found."
                        : "The mapped XAML location identifies more than one declaration.");
            }
            else
            {
                element = candidates[0];
            }
        }
        catch (XmlException)
        {
            Reject(MauiXamlSourceIneligibilityCodes.XamlMalformed,
                "The current XAML is not well-formed.");
        }

        var identity = element is null ? null : CreateIdentity(source, input.FileRelativePath, actualHash, element);
        var oldAutomationId = element?.Attribute("AutomationId")?.Value;
        int? attributeValueStart = null;
        int? attributeValueLength = null;
        char? attributeQuote = null;
        int? startTagEnd = null;

        if (element is not null)
        {
            var tag = FindStartTag(source, element);
            if (tag is null || element.Name.LocalName.Contains('.', StringComparison.Ordinal))
            {
                Reject(MauiXamlSourceIneligibilityCodes.ElementNotDirect,
                    "Only a direct static XAML element start tag can be changed.");
            }
            else
            {
                startTagEnd = tag.Value.End;
                var parsed = ParseAttributes(source, tag.Value.Start, tag.Value.End);
                if (!parsed.Success)
                {
                    Reject(MauiXamlSourceIneligibilityCodes.ElementNotDirect,
                        "The mapped XAML element start tag could not be parsed safely.");
                }
                else
                {
                    var automation = parsed.Attributes.FirstOrDefault(
                        attribute => string.Equals(attribute.Name, "AutomationId", StringComparison.Ordinal));
                    if (automation.Name is not null)
                    {
                        attributeValueStart = automation.ValueStart;
                        attributeValueLength = automation.ValueLength;
                        attributeQuote = automation.Quote;
                    }

                    if (parsed.Attributes.Any(attribute => IsMarkupExtension(attribute.Value)))
                    {
                        Reject(MauiXamlSourceIneligibilityCodes.BindingOrMarkup,
                            "A declaration with a binding, resource, or markup extension is not eligible.");
                    }
                }
            }

            if (HasUnsafeAncestor(element))
            {
                Reject(MauiXamlSourceIneligibilityCodes.TemplateOrStyle,
                    "Data templates, control templates, styles, and setters are not eligible.");
            }
            if (HasResourceDictionaryAncestor(element))
            {
                Reject(MauiXamlSourceIneligibilityCodes.ResourceDictionary,
                    "Resource dictionary declarations are not eligible.");
            }
            if (HasRepeaterAncestor(element))
            {
                Reject(MauiXamlSourceIneligibilityCodes.RepeaterOrVirtualized,
                    "Repeater and item-template declarations are not eligible.");
            }
            if (HasConditionalOrGeneratedMarker(element))
            {
                Reject(MauiXamlSourceIneligibilityCodes.ConditionalOrGeneratedElement,
                    "Conditional or generated element declarations are not eligible.");
            }
            if (IsWebView(element))
            {
                Reject(MauiXamlSourceIneligibilityCodes.NativeOrWebViewSynthetic,
                    "WebView declarations are not eligible for AutomationId proposals.");
            }
            if (string.Equals(oldAutomationId, input.ProposedAutomationId, StringComparison.Ordinal))
            {
                Reject(MauiXamlSourceIneligibilityCodes.AutomationIdUnchanged,
                    "The proposed AutomationId is already the declaration's literal value.");
            }
        }

        var proposed = input.ProposedAutomationId ?? string.Empty;
        var projectMatchCount = CountMatches(input.ProjectAutomationIds, proposed);
        var liveMatchCount = CountMatches(input.LiveAutomationIds, proposed);
        if (projectMatchCount > 0)
        {
            Reject(MauiXamlSourceIneligibilityCodes.AutomationIdDuplicateProject,
                "The proposed AutomationId already exists in the required project scope.");
        }
        if (input.RequireLiveUniqueness && !input.LiveUniquenessAvailable)
        {
            Reject(MauiXamlSourceIneligibilityCodes.LiveUniquenessUnavailable,
                "Current live uniqueness evidence is required before a source proposal can be approved.");
        }
        if (input.LiveUniquenessAvailable && liveMatchCount > 0)
        {
            Reject(MauiXamlSourceIneligibilityCodes.AutomationIdDuplicateLive,
                "The proposed AutomationId already exists in the current live scope.");
        }

        return new MauiXamlSourceEligibilityAnalysis
        {
            Decision = new MauiXamlSourceEligibilityDecision
            {
                Eligible = reasons.Count == 0,
                Reasons = reasons,
                AnalyzedAt = DateTimeOffset.UtcNow,
                Policy = "xaml-automation-id-proposal-v1",
            },
            Element = identity,
            OldAutomationId = oldAutomationId,
            AttributeValueStart = attributeValueStart,
            AttributeValueLength = attributeValueLength,
            AttributeQuote = attributeQuote,
            StartTagEnd = startTagEnd,
            Uniqueness = new MauiXamlSourceUniquenessEvidence
            {
                ProjectScope = "registered-project",
                ProjectMatchCount = projectMatchCount,
                LiveScope = "current-live-tree",
                LiveMatchCount = input.LiveUniquenessAvailable ? liveMatchCount : null,
                LiveScopeAvailable = input.LiveUniquenessAvailable,
                ValidatedAt = DateTimeOffset.UtcNow,
            },
        };
    }

    /// <summary>Matches the short UTF-8 text hash emitted by the XAML source map generator.</summary>
    public static string ComputeSourceHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();

    /// <summary>Computes a non-secret full content digest suitable for compare-and-swap binding.</summary>
    public static string ComputeContentDigest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsExpectedHash(string? value)
        => value is { Length: 16 } && value.All(Uri.IsHexDigit);

    private static bool IsXamlPath(string? relativePath)
        => !string.IsNullOrWhiteSpace(relativePath) &&
           !Path.IsPathRooted(relativePath) &&
           relativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) &&
           !relativePath.Contains("..", StringComparison.Ordinal);

    private static bool IsAtMappedLocation(XElement element, int? line, int? column)
    {
        if (line is null || column is null ||
            element is not IXmlLineInfo info ||
            !info.HasLineInfo() ||
            info.LineNumber != line)
        {
            return false;
        }

        // XDocument reports the first name character; hand-built test maps often point at '<'.
        return info.LinePosition == column || info.LinePosition == column + 1;
    }

    private static MauiXamlSourceElementIdentity CreateIdentity(
        string source,
        string? path,
        string hash,
        XElement element)
    {
        var info = (IXmlLineInfo)element;
        var startTag = FindStartTag(source, element);
        var elementPath = GetElementPath(element);
        var anchorInput = string.Join("|",
            path ?? string.Empty,
            hash,
            info.LineNumber,
            info.LinePosition,
            element.Name,
            elementPath);
        var anchor = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(anchorInput))).ToLowerInvariant();
        return new MauiXamlSourceElementIdentity
        {
            ElementType = element.Name.LocalName,
            Line = info.LineNumber,
            Column = info.LinePosition,
            Path = elementPath,
            SourceAnchor = anchor,
            StartTagOffset = startTag?.Start,
            StartTagLength = startTag is { } value ? value.End - value.Start + 1 : null,
        };
    }

    private static string GetElementPath(XElement element)
    {
        var path = new List<int>();
        while (element.Parent is XElement parent)
        {
            var index = 0;
            foreach (var sibling in parent.Elements())
            {
                if (ReferenceEquals(sibling, element))
                    break;
                index++;
            }
            path.Add(index);
            element = parent;
        }
        path.Reverse();
        return string.Join("/", path);
    }

    private static (int Start, int End)? FindStartTag(string source, XElement element)
    {
        if (element is not IXmlLineInfo info || !info.HasLineInfo())
            return null;
        if (!TryGetOffset(source, info.LineNumber, info.LinePosition, out var offset))
            return null;
        if (offset > 0 && source[offset - 1] == '<')
            offset--;
        if (offset >= source.Length || source[offset] != '<')
            return null;

        var quote = '\0';
        for (var cursor = offset + 1; cursor < source.Length; cursor++)
        {
            var ch = source[cursor];
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                continue;
            }
            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }
            if (ch == '>')
                return (offset, cursor);
        }
        return null;
    }

    private static bool TryGetOffset(string source, int line, int column, out int offset)
    {
        var lineStart = 0;
        var currentLine = 1;
        while (currentLine < line)
        {
            var newline = source.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                offset = 0;
                return false;
            }
            lineStart = newline + 1;
            currentLine++;
        }

        var lineEnd = source.IndexOf('\n', lineStart);
        if (lineEnd < 0)
            lineEnd = source.Length;
        if (lineEnd > lineStart && source[lineEnd - 1] == '\r')
            lineEnd--;

        offset = lineStart + column - 1;
        return offset >= lineStart && offset < lineEnd;
    }

    private static ParsedAttributes ParseAttributes(string source, int start, int end)
    {
        var cursor = start + 1;
        while (cursor < end && !char.IsWhiteSpace(source[cursor]) && source[cursor] is not '/' and not '>')
            cursor++;
        if (cursor == start + 1)
            return ParsedAttributes.Invalid;

        var attributes = new List<ParsedAttribute>();
        while (cursor < end)
        {
            SkipWhitespace(source, ref cursor);
            if (cursor >= end || source[cursor] == '/')
                break;

            var nameStart = cursor;
            while (cursor < end && !char.IsWhiteSpace(source[cursor]) && source[cursor] is not '=' and not '/' and not '>')
                cursor++;
            if (cursor == nameStart)
                return ParsedAttributes.Invalid;
            var name = source[nameStart..cursor];
            SkipWhitespace(source, ref cursor);
            if (cursor >= end || source[cursor] != '=')
                return ParsedAttributes.Invalid;
            cursor++;
            SkipWhitespace(source, ref cursor);
            if (cursor >= end || source[cursor] is not ('"' or '\''))
                return ParsedAttributes.Invalid;
            var quote = source[cursor++];
            var valueStart = cursor;
            while (cursor < end && source[cursor] != quote)
                cursor++;
            if (cursor >= end)
                return ParsedAttributes.Invalid;
            var value = source[valueStart..cursor];
            attributes.Add(new ParsedAttribute(name, value, valueStart, cursor - valueStart, quote));
            cursor++;
        }
        return new ParsedAttributes(true, attributes);
    }

    private static void SkipWhitespace(string source, ref int cursor)
    {
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
    }

    private static bool IsMarkupExtension(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith('{') && !trimmed.StartsWith("{}", StringComparison.Ordinal);
    }

    private static bool HasUnsafeAncestor(XElement element)
        => element.Ancestors().Any(ancestor =>
            TemplateOrStyleAncestors.Contains(ancestor.Name.LocalName) ||
            ancestor.Name.LocalName.EndsWith(".ItemTemplate", StringComparison.OrdinalIgnoreCase) ||
            ancestor.Name.LocalName.EndsWith(".ControlTemplate", StringComparison.OrdinalIgnoreCase));

    private static bool HasResourceDictionaryAncestor(XElement element)
        => element.AncestorsAndSelf().Any(ancestor =>
            string.Equals(ancestor.Name.LocalName, "ResourceDictionary", StringComparison.OrdinalIgnoreCase) ||
            ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.OrdinalIgnoreCase));

    private static bool HasRepeaterAncestor(XElement element)
        => element.Ancestors().Any(ancestor =>
            RepeaterAncestors.Contains(ancestor.Name.LocalName) ||
            ancestor.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "ItemsSource", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attribute.Name.LocalName, "ItemTemplate", StringComparison.OrdinalIgnoreCase)));

    private static bool HasConditionalOrGeneratedMarker(XElement element)
        => element.Attributes().Any(attribute =>
            string.Equals(attribute.Name.LocalName, "Load", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.Name.LocalName, "ClassModifier", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attribute.Name.LocalName, "Generated", StringComparison.OrdinalIgnoreCase));

    private static bool IsWebView(XElement element)
        => element.Name.LocalName.Contains("WebView", StringComparison.OrdinalIgnoreCase);

    private static int CountMatches(IReadOnlyList<string>? values, string proposed)
        => values?.Count(value => string.Equals(value, proposed, StringComparison.Ordinal)) ?? 0;

    private readonly record struct ParsedAttribute(
        string? Name,
        string Value,
        int ValueStart,
        int ValueLength,
        char Quote);

    private sealed record ParsedAttributes(bool Success, List<ParsedAttribute> Attributes)
    {
        public static ParsedAttributes Invalid { get; } = new(false, []);
    }
}

/// <summary>Deliberately narrow grammar for stable, non-localized static test identifiers.</summary>
public static class MauiXamlAutomationIdGrammar
{
    public const int MaximumLength = MauiAutomationIdProposalPolicy.MaximumLength;

    public static bool TryValidate(string? value, out string? reason)
        => MauiAutomationIdProposalPolicy.TryValidate(value, out reason);

    public static bool IsPotentiallyLocalizedOrUserDerived(string? value)
        => MauiAutomationIdProposalPolicy.IsPotentiallyLocalizedOrUserDerived(value);
}
