using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Analyzers;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class CSharpSourceProposalTests : IDisposable
{
    private const string MauiStubs = """

        namespace Microsoft.Maui.Controls
        {
            public class Button
            {
                public string AutomationId { get; set; } = "";
                public string Text { get; set; } = "";
            }

            public class WebView
            {
                public string AutomationId { get; set; } = "";
            }
        }
        """;

    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "csharp-source-proposal-tests",
        Guid.NewGuid().ToString("N"));

    public CSharpSourceProposalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AdvisoryAnalyzer_ReportsMissingAndDuplicateSafeDeclarations()
    {
        const string missing = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var save = new {|#0:Button|} { Text = "Save" };
                }
            }
            """;
        var missingExpected = new DiagnosticResult("DFCS001", DiagnosticSeverity.Info)
            .WithLocation(0)
            .WithArguments("Button");
        await CreateAnalyzerTest(missing, missingExpected).RunAsync();

        const string duplicate = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var first = new Button();
                    var second = new Button();
                    first.{|#0:AutomationId|} = "SaveButton";
                    second.{|#1:AutomationId|} = "SaveButton";
                }
            }
            """;
        var first = new DiagnosticResult("DFCS002", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("SaveButton");
        var second = new DiagnosticResult("DFCS002", DiagnosticSeverity.Warning)
            .WithLocation(1)
            .WithArguments("SaveButton");
        await CreateAnalyzerTest(duplicate, first, second).RunAsync();
    }

    [Fact]
    public async Task AdvisoryAnalyzer_DoesNotOfferDiagnosisForSafeExistingLiteral()
    {
        const string source = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var save = new Button { AutomationId = "SaveButton", Text = "Save" };
                }
            }
            """;

        await CreateAnalyzerTest(source).RunAsync();
    }

    [Theory]
    [InlineData("""
        using Microsoft.Maui.Controls;
        public class Page { void Build() { var save = new Button { Text = "Save" }; } }
        """, "new Button", null)]
    [InlineData("""
        using Microsoft.Maui.Controls;
        public class Page { void Build() { var save = new Button { AutomationId = "OldSave", Text = "Save" }; } }
        """, "new Button", "OldSave")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        public class Page { void Build() { var save = new Button(); save.AutomationId = "OldSave"; } }
        """, "save.AutomationId", "OldSave")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        public class Page
        {
            private static Button Save = new Button { Text = "Save" };
            void Build() { Save.AutomationId = "OldSave"; }
        }
        """, "Save.AutomationId", "OldSave")]
    public void RoslynBuilder_ValidInitializerAndAssignment_ProducesMinimalPatch(
        string source,
        string marker,
        string? expectedOld)
    {
        var (model, position) = SemanticModelAt(source, marker);
        var result = CSharpAutomationIdProposalBuilder.Analyze(model, position, "SaveButton");

        Assert.True(result.CanCreateMinimalPatch, string.Join("; ", result.Reasons.Select(reason => reason.Message)));
        Assert.Equal(expectedOld, result.OldAutomationId);
        Assert.NotNull(result.PatchStart);
        Assert.NotNull(result.PatchLength);
        Assert.Contains("SaveButton", result.Replacement, StringComparison.Ordinal);
        Assert.True(result.IsSupportedActionableControl);
    }

    [Theory]
    [InlineData("""
        using Microsoft.Maui.Controls;
        class DataTemplate { public DataTemplate(System.Action action) {} }
        class Page { void Build() { var t = new DataTemplate(() => { var save = new Button { Text = "Save" }; }); } }
        """, "new Button", "template-or-repeater")]
    [InlineData("""
        using System.Collections.Generic;
        using Microsoft.Maui.Controls;
        class Page { void Build() { var buttons = new List<Button> { new Button { Text = "Save" } }; } }
        """, "new Button", "collection-lambda-or-factory")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        class Page
        {
            void Build()
            {
        #if true
                var save = new Button { Text = "Save" };
        #endif
            }
        }
        """, "new Button", "conditional-or-preprocessor")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        class Page { void Build() { dynamic save = new Button { Text = "Save" }; } }
        """, "new Button", "reflection-or-dynamic-construction")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        class Page { string Id() => "SaveButton"; void Build() { var save = new Button { AutomationId = Id() }; } }
        """, "new Button", "computed-or-bound-automation-id")]
    [InlineData("""
        using Microsoft.Maui.Controls;
        class Page { void Build() { var view = new WebView { AutomationId = "Web" }; } }
        """, "new WebView", "unsupported-control-type")]
    public void RoslynBuilder_UnsafeConstructs_ReturnExplicitRejection(
        string source,
        string marker,
        string expectedCode)
    {
        var (model, position) = SemanticModelAt(source, marker);
        var result = CSharpAutomationIdProposalBuilder.Analyze(model, position, "SaveButton");

        Assert.Contains(result.Reasons, reason => reason.Code == expectedCode);
        Assert.False(result.CanCreateMinimalPatch);
    }

    [Fact]
    public void EligibilityAnalyzer_RejectsEveryNonRoslynSafetyGate()
    {
        const string source = "var save = new Button { Text = \"Save\" };";
        var result = MauiCSharpSourceEligibilityAnalyzer.Analyze(new MauiCSharpSourceEligibilityInput
        {
            SourceText = source,
            FileRelativePath = "obj/Generated.g.cs",
            ExpectedSourceHash = MauiAutomationIdProposalPolicy.ComputeSourceHash(source),
            SourceLine = 1,
            SourceColumn = 12,
            SourceSpanStart = 11,
            SourceSpanLength = 30,
            SourceConfidence = "roslyn-proven",
            IsProjectContained = false,
            IsRegisteredProjectFile = false,
            IsGenerated = true,
            IsLinked = true,
            HasReparsePoint = true,
            IsNativeOrWebViewSynthetic = true,
            IsVirtualizedOrTemplated = true,
            HasRoslynSemanticModel = false,
            HasResolvedSymbol = false,
            IsSupportedActionableControl = false,
            IsDirectObjectInitializer = false,
            IsDirectLiteralAssignment = false,
            IsSingleUnambiguousSite = false,
            IsInsideTemplateOrRepeater = true,
            IsInsideCollectionLambdaOrFactory = true,
            HasConditionalOrPreprocessorBranch = true,
            HasReflectionOrDynamicConstruction = true,
            HasComputedOrBoundAutomationId = true,
            ExistingAutomationId = "OldSave",
            ProposedAutomationId = "UserName",
            ProjectAutomationIds = ["UserName"],
            LiveAutomationIds = ["UserName"],
            LiveUniquenessAvailable = true,
        });

        Assert.False(result.Decision.Eligible);
        var codes = result.Decision.Reasons.Select(reason => reason.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var code in new[]
                 {
                     MauiCSharpSourceIneligibilityCodes.SourceFileOutsideProject,
                     MauiCSharpSourceIneligibilityCodes.SourceFileUnregistered,
                     MauiCSharpSourceIneligibilityCodes.SourceFileGenerated,
                     MauiCSharpSourceIneligibilityCodes.SourceFileLinked,
                     MauiCSharpSourceIneligibilityCodes.SourcePathReparsePoint,
                     MauiCSharpSourceIneligibilityCodes.NativeOrWebViewSynthetic,
                     MauiCSharpSourceIneligibilityCodes.RepeaterOrVirtualized,
                     MauiCSharpSourceIneligibilityCodes.RoslynSemanticModelUnavailable,
                     MauiCSharpSourceIneligibilityCodes.SemanticSymbolUnresolved,
                     MauiCSharpSourceIneligibilityCodes.UnsupportedControlType,
                     MauiCSharpSourceIneligibilityCodes.UnsupportedSyntax,
                     MauiCSharpSourceIneligibilityCodes.AmbiguousConstructionOrAssignment,
                     MauiCSharpSourceIneligibilityCodes.TemplateOrRepeater,
                     MauiCSharpSourceIneligibilityCodes.CollectionOrFactory,
                     MauiCSharpSourceIneligibilityCodes.ConditionalOrPreprocessor,
                     MauiCSharpSourceIneligibilityCodes.ReflectionOrDynamic,
                     MauiCSharpSourceIneligibilityCodes.ComputedOrBoundValue,
                     MauiCSharpSourceIneligibilityCodes.AutomationIdLocalizedOrUserDerived,
                     MauiCSharpSourceIneligibilityCodes.AutomationIdDuplicateProject,
                     MauiCSharpSourceIneligibilityCodes.AutomationIdDuplicateLive,
                 })
        {
            Assert.Contains(code, codes);
        }
    }

    [Fact]
    public void EligibilityAnalyzer_RejectsUnsafeExistingLiteralReplacement()
    {
        const string source = "var save = new Button { AutomationId = \"UserName\" };";
        var result = MauiCSharpSourceEligibilityAnalyzer.Analyze(new MauiCSharpSourceEligibilityInput
        {
            SourceText = source,
            FileRelativePath = "MainPage.cs",
            ExpectedSourceHash = MauiAutomationIdProposalPolicy.ComputeSourceHash(source),
            SourceLine = 1,
            SourceColumn = 12,
            SourceSpanStart = 11,
            SourceSpanLength = 42,
            SourceConfidence = "roslyn-proven",
            IsProjectContained = true,
            IsRegisteredProjectFile = true,
            HasRoslynSemanticModel = true,
            HasResolvedSymbol = true,
            IsSupportedActionableControl = true,
            IsDirectObjectInitializer = true,
            IsSingleUnambiguousSite = true,
            ExistingAutomationId = "UserName",
            ProposedAutomationId = "SaveButton",
            ProjectAutomationIds = [],
            LiveAutomationIds = [],
            LiveUniquenessAvailable = true,
        });

        Assert.False(result.Decision.Eligible);
        Assert.Contains(
            result.Decision.Reasons,
            reason => reason.Code == MauiCSharpSourceIneligibilityCodes.ComputedOrBoundValue);
    }

    [Fact]
    public async Task BuildAsync_ObjectInitializerPreservesTriviaAndCreatesRollbackPatch()
    {
        const string source = """
            using Microsoft.Maui.Controls;

            public class Page
            {
                void Build()
                {
                    var save = new Button
                    {
                        // Keep this hand-authored trivia.
                        Text = "Save",
                    };
                }
            }

            namespace Microsoft.Maui.Controls
            {
                public class Button
                {
                    public string AutomationId { get; set; } = "";
                    public string Text { get; set; } = "";
                }
            }
            """;
        var project = await CreateProjectAsync(source);
        var (line, column) = LineColumn(source, "new Button");
        var element = Element(project.SourcePath, source, line, column);
        var service = new CSharpAutomationIdProposalService(project.ProjectPath);

        var result = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.True(result.Ok, result.Error);
        var proposal = Assert.IsType<MauiCSharpSourceProposal>(result.Proposal);
        Assert.Equal("CSharp", proposal.Language);
        Assert.Equal("add-literal-automation-id", proposal.Operation.Kind);
        Assert.Contains("AutomationId = \"SaveButton\"", proposal.Patch.Replacement, StringComparison.Ordinal);
        var preview = source.Remove(proposal.Patch.Start!.Value, proposal.Patch.Length!.Value)
            .Insert(proposal.Patch.Start.Value, proposal.Patch.Replacement!);
        Assert.Contains("// Keep this hand-authored trivia.", preview, StringComparison.Ordinal);
        Assert.Contains("AutomationId = \"SaveButton\"", preview, StringComparison.Ordinal);
        Assert.Equal(proposal.Patch.AfterDigest, proposal.RollbackPatch.BeforeDigest);
        Assert.Equal(proposal.BaseContentDigest, proposal.RollbackPatch.AfterDigest);
        Assert.Contains("broker-never-writes-csharp-source", proposal.RiskFlags);
        Assert.Equal(source, await File.ReadAllTextAsync(project.SourcePath));
    }

    [Fact]
    public async Task BuildAsync_RejectsGeneratedLinkedAndVirtualizedDeclarations()
    {
        const string source = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var save = new Button { Text = "Save" };
                }
            }
            namespace Microsoft.Maui.Controls
            {
                public class Button { public string AutomationId { get; set; } = ""; public string Text { get; set; } = ""; }
            }
            """;
        var project = await CreateProjectAsync(source, generatedFileName: "MainPage.g.cs");
        var (line, column) = LineColumn(source, "new Button");
        var element = Element(project.SourcePath, source, line, column);
        element.IsVirtualized = true;
        var service = new CSharpAutomationIdProposalService(project.ProjectPath);

        var result = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.False(result.Ok);
        var reasons = result.Analysis!.Decision.Reasons.Select(reason => reason.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(MauiCSharpSourceIneligibilityCodes.SourceFileGenerated, reasons);
        Assert.Contains(MauiCSharpSourceIneligibilityCodes.RepeaterOrVirtualized, reasons);
    }

    [Fact]
    public async Task BuildAsync_RejectsAutomationIdAlreadyDeclaredInProjectXaml()
    {
        const string source = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var save = new Button { Text = "Save" };
                }
            }
            namespace Microsoft.Maui.Controls
            {
                public class Button { public string AutomationId { get; set; } = ""; public string Text { get; set; } = ""; }
            }
            """;
        var project = await CreateProjectAsync(
            source,
            xaml: "<ContentPage><Button AutomationId=\"SaveButton\" /></ContentPage>");
        var (line, column) = LineColumn(source, "new Button");
        var element = Element(project.SourcePath, source, line, column);

        var result = await new CSharpAutomationIdProposalService(project.ProjectPath)
            .BuildAsync(element, "SaveButton", [element]);

        Assert.False(result.Ok);
        Assert.Contains(
            result.Analysis!.Decision.Reasons,
            reason => reason.Code == MauiCSharpSourceIneligibilityCodes.AutomationIdDuplicateProject);
    }

    [Fact]
    public async Task Store_ProposePreviewRejectHappyPath()
    {
        var proposal = await BuildProposalAsync();
        var store = new WorkflowCSharpSourceProposalStore();

        var proposed = store.Propose(proposal);
        Assert.True(proposed.Ok, proposed.Error);
        Assert.Equal(MauiCSharpSourceProposalStates.Proposed, proposed.Proposal!.State);

        var previewed = store.Preview(proposal.ProposalId);
        Assert.True(previewed.Ok, previewed.Error);
        Assert.Equal(MauiCSharpSourceProposalStates.Previewed, previewed.Proposal!.State);

        // Preview is idempotent from previewed state.
        var previewedAgain = store.Preview(proposal.ProposalId);
        Assert.True(previewedAgain.Ok);
        Assert.Equal(MauiCSharpSourceProposalStates.Previewed, previewedAgain.Proposal!.State);

        var rejected = store.Reject(proposal.ProposalId, "reviewer", "not-now");
        Assert.True(rejected.Ok, rejected.Error);
        Assert.Equal(MauiCSharpSourceProposalStates.Rejected, rejected.Proposal!.State);

        var rejectedAgain = store.Reject(proposal.ProposalId, "reviewer", "reason");
        Assert.False(rejectedAgain.Ok);
        Assert.Equal("proposal-terminal", rejectedAgain.Code);
    }

    [Fact]
    public async Task Store_RejectsAgentOriginatedCSharpProposal()
    {
        var proposal = await BuildProposalAsync();
        var store = new WorkflowCSharpSourceProposalStore();

        var result = store.Propose(proposal, agentOriginated: true);

        Assert.False(result.Ok);
        Assert.Equal("agent-source-proposal-forbidden", result.Code);
    }

    [Fact]
    public async Task Store_HistoryAppendChainsProposalTransitionsRedactedly()
    {
        var proposal = await BuildProposalAsync();
        var project = await CreateProjectAsync("public class Placeholder {}", sourceFileName: "Placeholder.cs");
        var store = new WorkflowCSharpSourceProposalStore();
        Assert.True(store.Propose(proposal).Ok);
        var previewed = store.Preview(proposal.ProposalId);
        Assert.True(previewed.Ok, previewed.Error);
        var history = new WorkflowCSharpSourceHistoryStore(Path.GetDirectoryName(project.ProjectPath)!);
        var first = history.Append(previewed.Proposal!);
        Assert.True(first.Ok, first.Error);
        var rejected = store.Reject(proposal.ProposalId, "reviewer", "not-now");
        var second = history.Append(rejected.Proposal!);
        Assert.True(second.Ok, second.Error);
        var contents = await File.ReadAllTextAsync(first.HistoryPath!);
        Assert.DoesNotContain("SaveButton", contents, StringComparison.Ordinal);
        Assert.DoesNotContain(proposal.Diff!, contents, StringComparison.Ordinal);
    }

    private async Task<MauiCSharpSourceProposal> BuildProposalAsync()
    {
        const string source = """
            using Microsoft.Maui.Controls;
            public class Page
            {
                void Build()
                {
                    var save = new Button { Text = "Save" };
                }
            }
            namespace Microsoft.Maui.Controls
            {
                public class Button
                {
                    public string AutomationId { get; set; } = "";
                    public string Text { get; set; } = "";
                }
            }
            """;
        var project = await CreateProjectAsync(source);
        var (line, column) = LineColumn(source, "new Button");
        var built = await new CSharpAutomationIdProposalService(project.ProjectPath)
            .BuildAsync(Element(project.SourcePath, source, line, column), "SaveButton", []);
        Assert.True(built.Ok, built.Error);
        return built.Proposal!;
    }

    private static CSharpAnalyzerTest<CSharpAutomationIdAdvisoryAnalyzer, DefaultVerifier> CreateAnalyzerTest(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CSharpAutomationIdAdvisoryAnalyzer, DefaultVerifier>
        {
            TestCode = source + MauiStubs,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    private static (SemanticModel Model, int Position) SemanticModelAt(string source, string marker)
    {
        var tree = CSharpSyntaxTree.ParseText(source + MauiStubs);
        var compilation = CSharpCompilation.Create(
            "CSharpProposalTests",
            [tree],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(position >= 0);
        return (compilation.GetSemanticModel(tree), position);
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static path => MetadataReference.CreateFromFile(path));

    private async Task<(string ProjectPath, string SourcePath)> CreateProjectAsync(
        string source,
        string sourceFileName = "MainPage.cs",
        string? generatedFileName = null,
        string? xaml = null)
    {
        var root = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var projectPath = Path.Combine(root, "TestApp.csproj");
        var sourcePath = Path.Combine(root, generatedFileName ?? sourceFileName);
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(sourcePath, source);
        if (xaml is not null)
            await File.WriteAllTextAsync(Path.Combine(root, "MainPage.xaml"), xaml);
        return (projectPath, sourcePath);
    }

    private static ElementInfo Element(string path, string source, int line, int column)
        => new()
        {
            Id = "save",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Framework = "maui",
            IsVisible = true,
            IsEnabled = true,
            SourceFile = path,
            SourceLine = line,
            SourceColumn = column,
            SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)), 0, 8).ToLowerInvariant(),
            SourceConfidence = "mapped",
        };

    private static (int Line, int Column) LineColumn(string source, string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var line = 1;
        var lineStart = 0;
        for (var offset = 0; offset < index; offset++)
        {
            if (source[offset] == '\n')
            {
                line++;
                lineStart = offset + 1;
            }
        }
        return (line, index - lineStart + 1);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
