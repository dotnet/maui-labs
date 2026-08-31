using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Maui.DevFlow.Analyzers;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Builds one narrow, Roslyn-proven C# AutomationId proposal. It opens a project only to obtain a
/// semantic model and creates an in-memory patch; it intentionally has no source-writing API.
/// </summary>
internal sealed class CSharpAutomationIdProposalService
{
    private const long MaxSourceFileBytes = 5 * 1024 * 1024;
    private const int MaxProjectDocuments = 2_048;
    private static readonly object WorkspaceRegistrationGate = new();

    private readonly string? _project;

    internal CSharpAutomationIdProposalService(string? project)
        => _project = string.IsNullOrWhiteSpace(project) ? null : project;

    internal async Task<CSharpSourceProposalBuildResult> BuildAsync(
        ElementInfo element,
        string? proposedAutomationId,
        IEnumerable<ElementInfo>? liveElements,
        IReadOnlyList<MauiCSharpSourceFlowFollowUp>? affectedFlows = null,
        IReadOnlyList<MauiCSharpSourcePlatformVerification>? affectedPlatforms = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var source = ResolveSourcePath(element.SourceFile);
        if (!source.Ok)
            return CSharpSourceProposalBuildResult.Failure(source.Code!, source.Error!);

        SourceSnapshot snapshot;
        try
        {
            snapshot = await SourceSnapshot.ReadAsync(source.Path!, cancellationToken).ConfigureAwait(false);
        }
        catch (SourceFileTooLargeException)
        {
            return CSharpSourceProposalBuildResult.Failure(
                "source-file-too-large",
                "C# source files larger than 5 MB are not eligible for source proposals.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return CSharpSourceProposalBuildResult.Failure(
                "source-unavailable",
                "The mapped C# source file could not be read.");
        }

        var projectPath = source.ProjectPath!;
        var projectRoot = Path.GetDirectoryName(projectPath)!;
        var relativePath = Path.GetRelativePath(projectRoot, source.Path!);
        var generated = IsGeneratedCSharp(relativePath, snapshot.Text);
        var linked = IsLinkedCSharp(projectPath, source.Path!);
        var liveIds = liveElements is null
            ? null
            : Flatten(liveElements)
                .Select(static item => item.AutomationId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();

        if (!TryRegisterMSBuild(out var registrationError))
        {
            return CSharpSourceProposalBuildResult.Failure(
                MauiCSharpSourceIneligibilityCodes.RoslynSemanticModelUnavailable,
                registrationError!);
        }

        using var workspace = MSBuildWorkspace.Create();
        Project project;
        try
        {
            project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return CSharpSourceProposalBuildResult.Failure(
                MauiCSharpSourceIneligibilityCodes.RoslynSemanticModelUnavailable,
                "The registered project could not be loaded into a Roslyn semantic workspace.");
        }

        var document = project.Documents.FirstOrDefault(candidate =>
            candidate.FilePath is not null && PathEquals(candidate.FilePath, source.Path!));
        if (document is null)
        {
            return CSharpSourceProposalBuildResult.Failure(
                MauiCSharpSourceIneligibilityCodes.SourceFileUnregistered,
                "The C# source file is not a regular document in the registered Roslyn project.");
        }

        var documentText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(documentText.ToString(), snapshot.Text, StringComparison.Ordinal))
        {
            return CSharpSourceProposalBuildResult.Failure(
                MauiCSharpSourceIneligibilityCodes.SourceHashMismatch,
                "The project document changed while its C# source proposal was being analyzed.");
        }

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var position = TryGetPosition(documentText, element.SourceLine, element.SourceColumn);
        CSharpAutomationIdProposalSyntaxAnalysis? roslyn = null;
        if (semanticModel is not null && position is not null && !string.IsNullOrWhiteSpace(proposedAutomationId))
        {
            roslyn = CSharpAutomationIdProposalBuilder.Analyze(
                semanticModel,
                position.Value,
                proposedAutomationId!,
                cancellationToken);
        }

        var projectIds = await CollectProjectAutomationIdsAsync(project, cancellationToken).ConfigureAwait(false);
        var siteCount = roslyn?.TargetSymbol is null
            ? 0
            : await CountTargetSitesAsync(
                project,
                roslyn.TargetSymbol,
                roslyn.SiteKind,
                cancellationToken).ConfigureAwait(false);
        var sourceConfidence = IsMappedSourceConfidence(element.SourceConfidence) && semanticModel is not null
            ? "roslyn-proven"
            : "unknown";
        var runtimeTypeMatches = roslyn?.ControlType is not null && MatchesRuntimeType(element, roslyn.ControlType);

        var identity = roslyn is null || semanticModel is null
            ? null
            : CreateIdentity(relativePath, element.SourceHash, documentText, roslyn);
        var eligibility = MauiCSharpSourceEligibilityAnalyzer.Analyze(new MauiCSharpSourceEligibilityInput
        {
            SourceText = snapshot.Text,
            FileRelativePath = relativePath,
            ExpectedSourceHash = element.SourceHash,
            SourceLine = element.SourceLine,
            SourceColumn = element.SourceColumn,
            SourceSpanStart = roslyn?.DeclarationSpan.Start,
            SourceSpanLength = roslyn?.DeclarationSpan.Length,
            SourceConfidence = sourceConfidence,
            IsProjectContained = true,
            IsRegisteredProjectFile = document is not null,
            IsGenerated = generated,
            IsLinked = linked,
            HasReparsePoint = PathContainsReparsePoint(projectRoot, source.Path!),
            IsNativeOrWebViewSynthetic = IsNativeOrSynthetic(element) || roslyn?.IsNativeOrWebViewSynthetic == true,
            IsVirtualizedOrTemplated = element.IsVirtualized == true ||
                !string.IsNullOrWhiteSpace(element.TemplateKind) ||
                roslyn?.IsInsideTemplateOrRepeater == true,
            HasRoslynSemanticModel = semanticModel is not null,
            HasResolvedSymbol = roslyn?.TargetSymbol is not null && runtimeTypeMatches,
            IsSupportedActionableControl = roslyn?.IsSupportedActionableControl == true,
            IsDirectObjectInitializer = roslyn?.IsObjectInitializer == true && roslyn.IsDirectStaticDeclaration,
            IsDirectLiteralAssignment = roslyn?.IsDirectLiteralAssignment == true && roslyn.IsDirectStaticDeclaration,
            IsSingleUnambiguousSite = siteCount == 1,
            IsInsideTemplateOrRepeater = roslyn?.IsInsideTemplateOrRepeater == true,
            IsInsideCollectionLambdaOrFactory = roslyn?.IsInsideCollectionLambdaOrFactory == true,
            HasConditionalOrPreprocessorBranch = roslyn?.HasConditionalOrPreprocessorBranch == true,
            HasReflectionOrDynamicConstruction = roslyn?.HasReflectionOrDynamicConstruction == true,
            HasComputedOrBoundAutomationId = roslyn?.HasComputedOrBoundAutomationId == true,
            ExistingAutomationId = roslyn?.OldAutomationId,
            ProposedAutomationId = proposedAutomationId,
            ProjectAutomationIds = projectIds,
            LiveAutomationIds = liveIds,
            LiveUniquenessAvailable = liveElements is not null,
            RequireLiveUniqueness = true,
        });
        var analysis = new MauiCSharpSourceEligibilityAnalysis
        {
            Decision = eligibility.Decision,
            Element = identity,
            OldAutomationId = roslyn?.OldAutomationId,
            Uniqueness = eligibility.Uniqueness,
        };

        if (!analysis.Decision.Eligible ||
            roslyn is null ||
            !roslyn.CanCreateMinimalPatch ||
            identity is null ||
            roslyn.PatchStart is not { } patchStart ||
            roslyn.PatchLength is not { } patchLength ||
            roslyn.Replacement is null)
        {
            return CSharpSourceProposalBuildResult.Ineligible(analysis);
        }

        var beforeText = snapshot.Text;
        if (patchStart > beforeText.Length || patchLength > beforeText.Length - patchStart)
        {
            return CSharpSourceProposalBuildResult.Failure(
                "csharp-patch-invalid",
                "The Roslyn patch span is outside the mapped C# document.");
        }
        var oldPatchText = beforeText.Substring(patchStart, patchLength);
        var updated = beforeText.Remove(patchStart, patchLength).Insert(patchStart, roslyn.Replacement);
        var parseDiagnostics = CSharpSyntaxTree.ParseText(updated, path: source.Path!)
            .GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        if (parseDiagnostics.Count > 0)
        {
            return CSharpSourceProposalBuildResult.Failure(
                "csharp-patch-invalid",
                "The minimal C# patch would not parse successfully.");
        }

        var beforeDigest = snapshot.ContentDigest;
        var afterBytes = snapshot.Encode(updated);
        var afterDigest = MauiAutomationIdProposalPolicy.ComputeContentDigest(afterBytes);
        var sourceAnchor = identity.SourceAnchor!;
        var operationKind = roslyn.SiteKind == CSharpAutomationIdProposalSiteKind.ObjectInitializerInsertion
            ? "add-literal-automation-id"
            : "replace-literal-automation-id";
        var patch = new MauiCSharpSourcePatch
        {
            Format = "text-replace-v1",
            Operation = operationKind,
            BeforeDigest = beforeDigest,
            AfterDigest = afterDigest,
            Start = patchStart,
            Length = patchLength,
            Replacement = roslyn.Replacement,
        };
        var rollbackPatch = new MauiCSharpSourcePatch
        {
            Format = "text-replace-v1",
            Operation = "rollback-" + operationKind,
            BeforeDigest = afterDigest,
            AfterDigest = beforeDigest,
            Start = patchStart,
            Length = roslyn.Replacement.Length,
            Replacement = oldPatchText,
        };
        var patchDigest = Digest(string.Join("\n",
            "csharp-automation-id-v1",
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            element.SourceHash,
            beforeDigest,
            sourceAnchor,
            patchStart,
            patchLength,
            roslyn.Replacement));
        var rollbackPatchDigest = Digest(string.Join("\n",
            "csharp-automation-id-rollback-v1",
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            afterDigest,
            sourceAnchor,
            patchStart,
            roslyn.Replacement.Length,
            oldPatchText));
        var diff = CreateDiff(relativePath, beforeText, updated, roslyn.DeclarationSpan);
        var flows = await DiscoverAffectedFlowsAsync(
            projectRoot,
            roslyn.OldAutomationId,
            proposedAutomationId!,
            affectedFlows,
            cancellationToken).ConfigureAwait(false);

        var proposal = new MauiCSharpSourceProposal
        {
            ProposalId = OpaqueId("csharpproposal"),
            State = MauiCSharpSourceProposalStates.Proposed,
            CreatedAt = DateTimeOffset.UtcNow,
            Operation = new MauiCSharpSourceOperation
            {
                OperationId = OpaqueId("csharpop"),
                Kind = operationKind,
                FileRelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
                SourceHash = element.SourceHash,
                SourceAnchor = sourceAnchor,
                SymbolId = CSharpAutomationIdProposalBuilder.GetSymbolIdentity(roslyn.TargetSymbol),
                SemanticType = CSharpAutomationIdProposalBuilder.GetSymbolIdentity(roslyn.ControlType),
                OldLiteral = roslyn.OldAutomationId,
                NewLiteral = proposedAutomationId,
                Attribute = "AutomationId",
                SpanStart = roslyn.DeclarationSpan.Start,
                SpanLength = roslyn.DeclarationSpan.Length,
            },
            Element = identity,
            BaseContentDigest = beforeDigest,
            Patch = patch,
            RollbackPatch = rollbackPatch,
            PatchDigest = patchDigest,
            RollbackPatchDigest = rollbackPatchDigest,
            DiffDigest = Digest(diff),
            Diff = diff,
            Eligibility = analysis.Decision,
            Uniqueness = analysis.Uniqueness,
            AffectedFlows = flows,
            AffectedPlatforms = EnsureOfficialPlatformCoverage(affectedPlatforms),
            RiskFlags =
            [
                "roslyn-semantic-model-required",
                "ide-mediated-host-apply-only",
                "broker-never-writes-csharp-source",
                "flow-selector-changes-require-separate-flow-repair",
                "build-remap-replay-and-oracle-verification-required",
            ],
        };
        return CSharpSourceProposalBuildResult.Success(proposal, beforeDigest, afterDigest, analysis);
    }

    private SourcePathResult ResolveSourcePath(string? sourceFile)
    {
        var projectPath = ResolveProjectPath();
        if (projectPath is null)
        {
            return SourcePathResult.Failure(
                "project-unregistered",
                "A broker-registered project file is required for C# source proposals.");
        }
        if (string.IsNullOrWhiteSpace(sourceFile) || !Path.IsPathFullyQualified(sourceFile))
        {
            return SourcePathResult.Failure(
                MauiCSharpSourceIneligibilityCodes.SourceMapUnavailable,
                "The element has no absolute mapped C# source path.");
        }

        try
        {
            var sourcePath = Path.GetFullPath(sourceFile);
            var root = Path.GetDirectoryName(projectPath)!;
            if (!IsUnderRoot(sourcePath, root))
            {
                return SourcePathResult.Failure(
                    MauiCSharpSourceIneligibilityCodes.SourceFileOutsideProject,
                    "Only C# files under the registered project are eligible.");
            }
            if (!File.Exists(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".cs", StringComparison.OrdinalIgnoreCase))
            {
                return SourcePathResult.Failure("source-unavailable", "The mapped C# source file does not exist.");
            }
            if (PathContainsReparsePoint(root, sourcePath))
            {
                return SourcePathResult.Failure(
                    MauiCSharpSourceIneligibilityCodes.SourcePathReparsePoint,
                    "Symbolic-link and reparse-point C# paths are not eligible.");
            }
            return SourcePathResult.Success(sourcePath, projectPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return SourcePathResult.Failure("source-path-invalid", "The mapped C# source path is invalid.");
        }
    }

    private string? ResolveProjectPath()
    {
        if (string.IsNullOrWhiteSpace(_project) || !Path.IsPathFullyQualified(_project))
            return null;
        try
        {
            var full = Path.GetFullPath(_project);
            if (File.Exists(full) && full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return full;
            if (!Directory.Exists(full))
                return null;
            var projects = Directory.EnumerateFiles(full, "*.csproj", SearchOption.TopDirectoryOnly)
                .Take(2)
                .ToArray();
            return projects.Length == 1 ? projects[0] : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool TryRegisterMSBuild(out string? error)
    {
        error = null;
        lock (WorkspaceRegistrationGate)
        {
            try
            {
                if (!MSBuildLocator.IsRegistered)
                    MSBuildLocator.RegisterDefaults();
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException)
            {
                error = "The local .NET SDK/MSBuild installation is unavailable for Roslyn C# analysis.";
                return false;
            }
        }
    }

    private static bool IsGeneratedCSharp(string relativePath, string sourceText)
        => relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)) ||
           relativePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
           sourceText.AsSpan().TrimStart().StartsWith("// <auto-generated", StringComparison.OrdinalIgnoreCase) ||
           sourceText.AsSpan().TrimStart().StartsWith("/* <auto-generated", StringComparison.OrdinalIgnoreCase);

    private static bool IsLinkedCSharp(string projectPath, string sourcePath)
    {
        var root = Path.GetDirectoryName(projectPath)!;
        try
        {
            var relative = Path.GetRelativePath(root, sourcePath).Replace(Path.DirectorySeparatorChar, '/');
            var document = XDocument.Load(projectPath, LoadOptions.None);
            foreach (var item in document.Descendants().Where(element => element.Name.LocalName == "Compile"))
            {
                var include = item.Attribute("Include")?.Value ?? item.Attribute("Update")?.Value;
                var link = item.Attribute("Link")?.Value ?? item.Element(item.Name.Namespace + "Link")?.Value;
                if (!string.IsNullOrWhiteSpace(link) && MatchesProjectItem(include, relative))
                    return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return true;
        }
        return false;
    }

    private static bool MatchesProjectItem(string? item, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(item))
            return false;
        var normalized = item.Replace('\\', '/').TrimStart('.', '/');
        return string.Equals(normalized, relativePath, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("**/*.cs", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("*.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> CollectProjectAutomationIdsAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        foreach (var document in project.Documents.Take(MaxProjectDocuments))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root is null)
                continue;
            foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.Left is not MemberAccessExpressionSyntax member ||
                    !string.Equals(member.Name.Identifier.ValueText, "AutomationId", StringComparison.Ordinal) ||
                    assignment.Right is not LiteralExpressionSyntax literal ||
                    !literal.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    continue;
                }
                var value = literal.Token.ValueText;
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
        }

        var projectRoot = project.FilePath is null ? null : Path.GetDirectoryName(project.FilePath);
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            return result;
        try
        {
            foreach (var path in EnumerateProjectFiles(projectRoot, "*.xaml", MaxProjectDocuments))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsUnderRoot(path, projectRoot) ||
                    PathContainsReparsePoint(projectRoot, path) ||
                    IsGeneratedXamlPath(Path.GetRelativePath(projectRoot, path)))
                {
                    continue;
                }
                var info = new FileInfo(path);
                if (info.Length > MaxSourceFileBytes)
                    continue;
                var text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var xaml = XDocument.Parse(text, LoadOptions.None);
                result.AddRange((xaml.Root?.DescendantsAndSelf() ?? [])
                    .Attributes()
                    .Where(attribute => string.Equals(attribute.Name.LocalName, "AutomationId", StringComparison.Ordinal))
                    .Select(attribute => attribute.Value)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            // An unrelated XAML read/parse failure does not create an unproven duplicate. The
            // selected C# declaration and live scope still fail closed on their own evidence.
        }
        return result;
    }

    private static bool IsGeneratedXamlPath(string relativePath)
        => relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)) ||
           relativePath.EndsWith(".g.xaml", StringComparison.OrdinalIgnoreCase) ||
           relativePath.EndsWith(".generated.xaml", StringComparison.OrdinalIgnoreCase);

    private static async Task<int> CountTargetSitesAsync(
        Project project,
        ISymbol target,
        CSharpAutomationIdProposalSiteKind expectedKind,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var document in project.Documents.Take(MaxProjectDocuments))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || model is null)
                continue;

            IEnumerable<SyntaxNode> candidates = expectedKind == CSharpAutomationIdProposalSiteKind.DirectAssignmentReplacement
                ? root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                    .Where(static node => node.Left is MemberAccessExpressionSyntax member &&
                                          string.Equals(member.Name.Identifier.ValueText, "AutomationId", StringComparison.Ordinal))
                : root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
            foreach (var candidate in candidates)
            {
                var analyzed = CSharpAutomationIdProposalBuilder.Analyze(
                    model,
                    candidate.SpanStart,
                    "DevFlowAutomationId",
                    cancellationToken);
                var sameKind = expectedKind == CSharpAutomationIdProposalSiteKind.DirectAssignmentReplacement
                    ? analyzed.SiteKind == CSharpAutomationIdProposalSiteKind.DirectAssignmentReplacement
                    : analyzed.SiteKind is CSharpAutomationIdProposalSiteKind.ObjectInitializerInsertion or
                        CSharpAutomationIdProposalSiteKind.ObjectInitializerReplacement;
                if (sameKind &&
                    SymbolEqualityComparer.Default.Equals(analyzed.TargetSymbol, target))
                {
                    count++;
                    if (count > 1)
                        return count;
                }
            }
        }
        return count;
    }

    private static MauiCSharpSourceElementIdentity CreateIdentity(
        string relativePath,
        string? sourceHash,
        SourceText text,
        CSharpAutomationIdProposalSyntaxAnalysis analysis)
    {
        var line = text.Lines.GetLineFromPosition(analysis.DeclarationSpan.Start);
        var lineNumber = line.LineNumber + 1;
        var column = analysis.DeclarationSpan.Start - line.Start + 1;
        var anchorInput = string.Join("|",
            relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            sourceHash ?? string.Empty,
            analysis.DeclarationSpan.Start,
            analysis.DeclarationSpan.Length,
            CSharpAutomationIdProposalBuilder.GetSymbolIdentity(analysis.TargetSymbol) ?? string.Empty,
            CSharpAutomationIdProposalBuilder.GetSymbolIdentity(analysis.ControlType) ?? string.Empty);
        var anchor = "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(anchorInput))).ToLowerInvariant();
        return new MauiCSharpSourceElementIdentity
        {
            ElementType = analysis.ControlType?.Name,
            Line = lineNumber,
            Column = column,
            Path = relativePath.Replace(Path.DirectorySeparatorChar, '/'),
            SourceAnchor = anchor,
            SpanStart = analysis.DeclarationSpan.Start,
            SpanLength = analysis.DeclarationSpan.Length,
            SymbolId = CSharpAutomationIdProposalBuilder.GetSymbolIdentity(analysis.TargetSymbol),
            SemanticType = CSharpAutomationIdProposalBuilder.GetSymbolIdentity(analysis.ControlType),
        };
    }

    private static int? TryGetPosition(SourceText text, int? line, int? column)
    {
        if (line is not > 0 || column is not > 0 || line.Value > text.Lines.Count)
            return null;
        var sourceLine = text.Lines[line.Value - 1];
        var offset = column.Value - 1;
        return offset <= sourceLine.Span.Length ? sourceLine.Start + offset : null;
    }

    private static bool IsMappedSourceConfidence(string? value)
        => string.Equals(value, "mapped", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "roslyn-proven", StringComparison.OrdinalIgnoreCase);

    private static bool IsNativeOrSynthetic(ElementInfo element)
        => !string.Equals(element.Framework, "maui", StringComparison.OrdinalIgnoreCase) ||
           element.Type.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
           element.FullType.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
           element.Type.Contains("Shell", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(element.TemplateKind, "synthetic", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRuntimeType(ElementInfo element, ITypeSymbol controlType)
    {
        var semantic = CSharpAutomationIdProposalBuilder.GetSymbolIdentity(controlType)?
            .Replace("global::", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(semantic))
            return false;
        if (!string.IsNullOrWhiteSpace(element.FullType) &&
            string.Equals(semantic, element.FullType, StringComparison.Ordinal))
        {
            return true;
        }
        return string.Equals(controlType.Name, element.Type, StringComparison.Ordinal);
    }

    private static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> roots)
    {
        var pending = new Stack<ElementInfo>(roots.Reverse());
        var count = 0;
        while (pending.Count > 0 && count++ < 10_000)
        {
            var current = pending.Pop();
            yield return current;
            if (current.Children is null)
                continue;
            for (var index = current.Children.Count - 1; index >= 0; index--)
                pending.Push(current.Children[index]);
        }
    }

    private static async Task<List<MauiCSharpSourceFlowFollowUp>> DiscoverAffectedFlowsAsync(
        string projectRoot,
        string? oldAutomationId,
        string proposedAutomationId,
        IReadOnlyList<MauiCSharpSourceFlowFollowUp>? supplied,
        CancellationToken cancellationToken)
    {
        var results = NormalizeAffectedFlows(supplied, proposedAutomationId);
        if (string.IsNullOrWhiteSpace(oldAutomationId))
            return results;

        var workflowRoot = Path.Combine(projectRoot, "maui-tests");
        if (!Directory.Exists(workflowRoot) || PathContainsReparsePoint(projectRoot, workflowRoot))
            return results;

        try
        {
            foreach (var path in Directory.EnumerateFiles(workflowRoot, "*.md", SearchOption.AllDirectories).Take(256))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsUnderRoot(path, workflowRoot) || PathContainsReparsePoint(workflowRoot, path))
                    continue;
                var info = new FileInfo(path);
                if (info.Length > 1_048_576)
                    continue;
                var markdown = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var parsed = FlowMarkdown.Parse(markdown);
                if (!parsed.Ok || parsed.Flow is null)
                    continue;
                var matching = parsed.Flow.Steps
                    .Where(step => UsesAutomationId(step, oldAutomationId))
                    .Select(static step => step.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .ToList();
                if (matching.Count == 0)
                    continue;

                var relative = Path.GetRelativePath(projectRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                if (results.Any(existing => string.Equals(existing.FlowPath, relative, StringComparison.Ordinal)))
                    continue;
                results.Add(new MauiCSharpSourceFlowFollowUp
                {
                    FlowPath = relative,
                    FlowId = ReadFlowId(parsed.Flow),
                    FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(parsed.Flow),
                    StepIds = matching,
                    RecommendedSelector = new FlowSelector { AutomationId = proposedAutomationId },
                    RequiresSeparateApproval = true,
                });
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Flow discovery is advisory; it never changes selectors or blocks a source proof.
        }
        return results.Take(64).ToList();
    }

    private static List<MauiCSharpSourceFlowFollowUp> NormalizeAffectedFlows(
        IReadOnlyList<MauiCSharpSourceFlowFollowUp>? values,
        string proposedAutomationId)
        => (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value.FlowPath) ||
                                   !string.IsNullOrWhiteSpace(value.FlowId))
            .Take(64)
            .Select(value => new MauiCSharpSourceFlowFollowUp
            {
                FlowPath = Bounded(value.FlowPath, 512),
                FlowId = Bounded(value.FlowId, 256),
                FlowDigest = Bounded(value.FlowDigest, 256),
                StepIds = value.StepIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Take(128).ToList(),
                RecommendedSelector = value.RecommendedSelector ?? new FlowSelector { AutomationId = proposedAutomationId },
                RequiresSeparateApproval = true,
            })
            .ToList();

    private static bool UsesAutomationId(FlowStep step, string automationId)
        => string.Equals(step.Target?.AutomationId, automationId, StringComparison.Ordinal) ||
           string.Equals(step.Args?.Selector?.AutomationId, automationId, StringComparison.Ordinal) ||
           (step.Asserts ?? []).Any(assertion =>
               string.Equals(assertion.Selector?.AutomationId, automationId, StringComparison.Ordinal));

    private static string? ReadFlowId(MauiFlow flow)
        => flow.ExtensionData is not null &&
           flow.ExtensionData.TryGetValue("flowId", out var value) &&
           value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<MauiCSharpSourcePlatformVerification> EnsureOfficialPlatformCoverage(
        IReadOnlyList<MauiCSharpSourcePlatformVerification>? values)
    {
        var platforms = (values ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value.Platform) ||
                                   !string.IsNullOrWhiteSpace(value.TargetFramework))
            .Take(16)
            .Select(value => new MauiCSharpSourcePlatformVerification
            {
                Platform = Bounded(value.Platform, 64),
                TargetFramework = Bounded(value.TargetFramework, 128),
                BuildState = Bounded(value.BuildState, 128),
                RuntimeRemapState = Bounded(value.RuntimeRemapState, 128),
                UniquenessState = Bounded(value.UniquenessState, 128),
                ReplayState = Bounded(value.ReplayState, 128),
                OracleState = Bounded(value.OracleState, 128),
                ReasonCode = Bounded(value.ReasonCode, 128),
            })
            .ToList();
        var appleExternal = OperatingSystem.IsWindows();
        foreach (var (platform, targetFramework) in new[]
                 {
                     ("android", "net10.0-android"),
                     ("windows", "net10.0-windows10.0.19041.0"),
                     ("ios", "net10.0-ios"),
                     ("maccatalyst", "net10.0-maccatalyst"),
                 })
        {
            if (platforms.Any(existing =>
                    string.Equals(existing.Platform, platform, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(existing.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var external = appleExternal && (platform is "ios" or "maccatalyst");
            platforms.Add(new MauiCSharpSourcePlatformVerification
            {
                Platform = platform,
                TargetFramework = targetFramework,
                BuildState = external ? "pending-external-qa" : "pending-host-build",
                RuntimeRemapState = external ? "pending-external-qa" : "pending-runtime-remap",
                UniquenessState = external ? "pending-external-qa" : "pending-runtime-uniqueness",
                ReplayState = external ? "pending-external-qa" : "pending-flow-replay",
                OracleState = external ? "pending-external-qa" : "pending-independent-oracle",
                ReasonCode = external ? "apple-target-unavailable-on-windows" : null,
            });
        }
        return platforms;
    }

    private static string CreateDiff(string path, string before, string after, TextSpan declaration)
    {
        var beforeText = SourceText.From(before);
        var afterText = SourceText.From(after);
        var startLine = Math.Max(0, beforeText.Lines.GetLineFromPosition(declaration.Start).LineNumber - 1);
        var endPosition = Math.Min(before.Length, declaration.End);
        var endLine = Math.Min(beforeText.Lines.Count - 1, beforeText.Lines.GetLineFromPosition(endPosition).LineNumber + 1);
        var beforeLines = beforeText.Lines.Skip(startLine).Take(endLine - startLine + 1)
            .Select(line => line.ToString()).ToList();
        var afterStart = Math.Min(startLine, afterText.Lines.Count - 1);
        var afterEnd = Math.Min(afterText.Lines.Count - 1, endLine + Math.Max(0, afterText.Lines.Count - beforeText.Lines.Count));
        var afterLines = afterText.Lines.Skip(afterStart).Take(afterEnd - afterStart + 1)
            .Select(line => line.ToString()).ToList();
        return $"--- a/{path.Replace('\\', '/')}\n+++ b/{path.Replace('\\', '/')}\n@@ -{startLine + 1},{beforeLines.Count} +{afterStart + 1},{afterLines.Count} @@\n" +
               string.Concat(beforeLines.Select(static line => "-" + line + "\n")) +
               string.Concat(afterLines.Select(static line => "+" + line + "\n"));
    }

    private static bool PathContainsReparsePoint(string root, string path)
    {
        var rootInfo = Directory.Exists(root)
            ? (FileSystemInfo)new DirectoryInfo(root)
            : new FileInfo(root);
        rootInfo.Refresh();
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0 || rootInfo.LinkTarget is not null)
            return true;

        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root, string searchPattern, int maximum)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var count = 0;
        while (pending.Count > 0 && count < maximum)
        {
            var directory = pending.Pop();
            if (IsReparsePoint(directory))
                continue;

            IEnumerable<string> files;
            IEnumerable<string> children;
            try
            {
                files = Directory.EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly).ToArray();
                children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (count++ == maximum)
                    yield break;
                yield return file;
            }
            foreach (var child in children)
            {
                if (!IsReparsePoint(child))
                    pending.Push(child);
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var info = Directory.Exists(path)
                ? (FileSystemInfo)new DirectoryInfo(path)
                : new FileInfo(path);
            info.Refresh();
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathEquals(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Digest(string value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string OpaqueId(string prefix)
        => prefix + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string? Bounded(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maximum ? trimmed : null;
    }

    private readonly record struct SourcePathResult(
        bool Ok,
        string? Path,
        string? ProjectPath,
        string? Code,
        string? Error)
    {
        public static SourcePathResult Success(string path, string projectPath)
            => new(true, path, projectPath, null, null);

        public static SourcePathResult Failure(string code, string error)
            => new(false, null, null, code, error);
    }

    private sealed class SourceFileTooLargeException : Exception;

    private sealed class SourceSnapshot
    {
        private readonly Encoding _encoding;
        private readonly byte[] _preamble;

        private SourceSnapshot(byte[] bytes, string text, Encoding encoding, byte[] preamble)
        {
            Bytes = bytes;
            Text = text;
            _encoding = encoding;
            _preamble = preamble;
            ContentDigest = MauiAutomationIdProposalPolicy.ComputeContentDigest(bytes);
        }

        public byte[] Bytes { get; }
        public string Text { get; }
        public string ContentDigest { get; }

        public static async Task<SourceSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxSourceFileBytes)
                throw new SourceFileTooLargeException();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var (encoding, preambleLength) = DetectEncoding(bytes);
            return new SourceSnapshot(
                bytes,
                encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength),
                encoding,
                bytes[..preambleLength]);
        }

        public byte[] Encode(string text)
        {
            var body = _encoding.GetBytes(text);
            var result = new byte[_preamble.Length + body.Length];
            _preamble.CopyTo(result, 0);
            body.CopyTo(result, _preamble.Length);
            return result;
        }

        private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), 3);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
                return (new UnicodeEncoding(false, true, true), 2);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
                return (new UnicodeEncoding(true, true, true), 2);
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0);
        }
    }
}

internal sealed class CSharpSourceProposalBuildResult
{
    public bool Ok { get; private init; }
    public MauiCSharpSourceProposal? Proposal { get; private init; }
    public MauiCSharpSourceEligibilityAnalysis? Analysis { get; private init; }
    public string? BaseContentDigest { get; private init; }
    public string? AfterContentDigest { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static CSharpSourceProposalBuildResult Success(
        MauiCSharpSourceProposal proposal,
        string baseContentDigest,
        string afterContentDigest,
        MauiCSharpSourceEligibilityAnalysis analysis)
        => new()
        {
            Ok = true,
            Proposal = proposal,
            BaseContentDigest = baseContentDigest,
            AfterContentDigest = afterContentDigest,
            Analysis = analysis,
        };

    public static CSharpSourceProposalBuildResult Ineligible(MauiCSharpSourceEligibilityAnalysis analysis)
        => new()
        {
            Analysis = analysis,
            Code = analysis.Decision.Reasons.FirstOrDefault()?.Code ?? "source-ineligible",
            Error = analysis.Decision.Reasons.FirstOrDefault()?.Message ?? "The C# declaration is not eligible for a source proposal.",
        };

    public static CSharpSourceProposalBuildResult Failure(string code, string error)
        => new() { Code = code, Error = error };
}
