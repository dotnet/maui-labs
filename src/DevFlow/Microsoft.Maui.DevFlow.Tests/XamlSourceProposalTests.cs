using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Analyzers;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class XamlSourceProposalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "xaml-source-proposal-tests",
        Guid.NewGuid().ToString("N"));

    public XamlSourceProposalTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task BuildAsync_StaticLiteralAdd_PreviewsTheChangeWithoutTouchingSource()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Button Text="Save" />
            </ContentPage>
            """;
        var project = await CreateProjectAsync(xaml, useCrLf: true);
        var persistedXaml = xaml.ReplaceLineEndings("\r\n");
        var element = Element(project.SourcePath, persistedXaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);

        var built = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.True(built.Ok, built.Error);
        var proposal = Assert.IsType<MauiXamlSourceProposal>(built.Proposal);
        Assert.Equal("add-literal-automation-id", proposal.Operation.Kind);
        Assert.Equal("MainPage.xaml", proposal.Operation.FileRelativePath);
        Assert.Contains("+<Button Text=\"Save\" AutomationId=\"SaveButton\" />", proposal.Diff, StringComparison.Ordinal);
        Assert.Contains("SaveButton", proposal.Patch.Replacement, StringComparison.Ordinal);

        // The proposal is a preview only: the BOM, the CRLF line endings and the original literal
        // all have to survive it byte for byte, because nothing in this layer may write source.
        var bytes = await File.ReadAllBytesAsync(project.SourcePath);
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(persistedXaml, await File.ReadAllTextAsync(project.SourcePath));
        Assert.DoesNotContain("AutomationId", await File.ReadAllTextAsync(project.SourcePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_LiteralReplacement_ProducesMinimalValuePatch()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Button AutomationId="OldSave" Text="Save" />
            </ContentPage>
            """;
        var project = await CreateProjectAsync(xaml);
        var element = Element(project.SourcePath, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);

        var built = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.True(built.Ok, built.Error);
        var proposal = built.Proposal!;
        Assert.Equal("replace-literal-automation-id", proposal.Operation.Kind);
        Assert.Equal("OldSave", proposal.Operation.OldLiteral);
        Assert.Equal("SaveButton", proposal.Operation.NewLiteral);
        Assert.Equal("SaveButton".Length, proposal.Patch.Replacement!.Length);
        Assert.Contains("-<Button AutomationId=\"OldSave\" Text=\"Save\" />", proposal.Diff, StringComparison.Ordinal);
        Assert.Contains("+<Button AutomationId=\"SaveButton\" Text=\"Save\" />", proposal.Diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_ReplacementDiscoversAffectedFlowButDoesNotEditIt()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Button AutomationId="OldSave" Text="Save" />
            </ContentPage>
            """;
        var project = await CreateProjectAsync(xaml);
        var workflowRoot = Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, "maui-tests");
        Directory.CreateDirectory(workflowRoot);
        var flowPath = Path.Combine(workflowRoot, "save.md");
        var flow = new MauiFlow
        {
            Name = "save",
            Steps =
            [
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "OldSave" },
                },
            ],
        };
        var markdown = FlowMarkdown.Serialize(flow);
        await File.WriteAllTextAsync(flowPath, markdown);
        var element = Element(project.SourcePath, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);

        var built = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.True(built.Ok, built.Error);
        var followUp = Assert.Single(built.Proposal!.AffectedFlows);
        Assert.Equal("maui-tests/save.md", followUp.FlowPath);
        Assert.Equal("1", Assert.Single(followUp.StepIds));
        Assert.Equal("SaveButton", followUp.RecommendedSelector!.AutomationId);
        Assert.True(followUp.RequiresSeparateApproval);
        Assert.Equal(markdown, await File.ReadAllTextAsync(flowPath));
    }

    [Fact]
    public async Task BuildAsync_RejectsAutomationIdAlreadyDeclaredInProjectCSharp()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Button Text="Save" />
            </ContentPage>
            """;
        var project = await CreateProjectAsync(xaml);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(project.ProjectPath)!, "CodePage.cs"),
            """
            public class CodePage
            {
                void Build()
                {
                    button.AutomationId = "SaveButton";
                }
            }
            """);
        var element = Element(project.SourcePath, xaml, "<Button");

        var result = await new XamlAutomationIdProposalService(project.ProjectPath)
            .BuildAsync(element, "SaveButton", [element]);

        Assert.False(result.Ok);
        Assert.Contains(
            result.Analysis!.Decision.Reasons,
            reason => reason.Code == MauiXamlSourceIneligibilityCodes.AutomationIdDuplicateProject);
    }

    /// <summary>
    /// Source apply lives in a later layer, so this layer's only job is to pin the exact bytes the
    /// preview was computed from. An external edit after the preview must not be reconciled here —
    /// it simply invalidates the digests a later applier is required to compare against.
    /// </summary>
    [Fact]
    public async Task BuildAsync_PinsTheBaseDigestAnExternalEditThenInvalidates()
    {
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Button Text="Save" />
            </ContentPage>
            """;
        var project = await CreateProjectAsync(xaml);
        var element = Element(project.SourcePath, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);
        var built = await service.BuildAsync(element, "SaveButton", [element]);
        Assert.True(built.Ok, built.Error);
        var proposal = built.Proposal!;
        Assert.Equal(
            MauiXamlSourceEligibilityAnalyzer.ComputeContentDigest(
                await File.ReadAllBytesAsync(project.SourcePath)),
            proposal.BaseContentDigest);
        Assert.Equal(proposal.BaseContentDigest, proposal.Patch.BeforeDigest);

        await File.AppendAllTextAsync(project.SourcePath, "\n<!-- external write -->");

        Assert.NotEqual(
            proposal.BaseContentDigest,
            MauiXamlSourceEligibilityAnalyzer.ComputeContentDigest(
                await File.ReadAllBytesAsync(project.SourcePath)));
        Assert.Contains("external write", await File.ReadAllTextAsync(project.SourcePath));
        Assert.DoesNotContain("AutomationId", await File.ReadAllTextAsync(project.SourcePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_SourceOutsideRegisteredProject_FailsClosed()
    {
        const string xaml = "<ContentPage><Button Text=\"Save\" /></ContentPage>";
        var project = await CreateProjectAsync(xaml);
        var outside = Path.Combine(_root, "outside.xaml");
        await File.WriteAllTextAsync(outside, xaml);
        var element = Element(outside, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);

        var built = await service.BuildAsync(element, "SaveButton", [element]);

        Assert.False(built.Ok);
        Assert.Equal(MauiXamlSourceIneligibilityCodes.SourceFileOutsideProject, built.Code);
    }

    [Theory]
    [InlineData("<ContentPage><Button Text=\"{Binding Save}\" /></ContentPage>", "<Button", MauiXamlSourceIneligibilityCodes.BindingOrMarkup)]
    [InlineData("<ContentPage><ContentPage.Resources><Style TargetType=\"Button\"><Setter Property=\"AutomationId\" Value=\"SaveButton\" /></Style></ContentPage.Resources></ContentPage>", "<Setter", MauiXamlSourceIneligibilityCodes.TemplateOrStyle)]
    [InlineData("<ContentPage><CollectionView><CollectionView.ItemTemplate><DataTemplate><Button Text=\"Save\" /></DataTemplate></CollectionView.ItemTemplate></CollectionView></ContentPage>", "<Button", MauiXamlSourceIneligibilityCodes.TemplateOrStyle)]
    [InlineData("<ContentPage><WebView /></ContentPage>", "<WebView", MauiXamlSourceIneligibilityCodes.NativeOrWebViewSynthetic)]
    public void EligibilityAnalyzer_UnsafeDeclarations_ReturnExplicitCodes(
        string xaml,
        string marker,
        string expectedCode)
    {
        var (line, column) = LineColumn(xaml, marker);
        var result = MauiXamlSourceEligibilityAnalyzer.Analyze(new MauiXamlSourceEligibilityInput
        {
            SourceText = xaml,
            FileRelativePath = "MainPage.xaml",
            ExpectedSourceHash = MauiXamlSourceEligibilityAnalyzer.ComputeSourceHash(xaml),
            SourceLine = line,
            SourceColumn = column,
            SourceConfidence = "mapped",
            IsProjectContained = true,
            ProposedAutomationId = "SaveButton",
            ProjectAutomationIds = [],
            LiveAutomationIds = [],
            LiveUniquenessAvailable = true,
        });

        Assert.False(result.Decision.Eligible);
        Assert.Contains(result.Decision.Reasons, reason => reason.Code == expectedCode);
    }

    [Fact]
    public void EligibilityAnalyzer_RejectsGeneratedLinkedReparseInvalidAndDuplicateIds()
    {
        const string xaml = "<ContentPage><Button Text=\"Save\" /></ContentPage>";
        var (line, column) = LineColumn(xaml, "<Button");
        var result = MauiXamlSourceEligibilityAnalyzer.Analyze(new MauiXamlSourceEligibilityInput
        {
            SourceText = xaml,
            FileRelativePath = "MainPage.xaml",
            ExpectedSourceHash = "0000000000000000",
            SourceLine = line,
            SourceColumn = column,
            SourceConfidence = "mapped",
            IsProjectContained = false,
            IsGenerated = true,
            IsLinked = true,
            HasReparsePoint = true,
            ProposedAutomationId = "保存",
            ProjectAutomationIds = ["SaveButton"],
            LiveAutomationIds = ["SaveButton"],
            LiveUniquenessAvailable = true,
        });

        Assert.False(result.Decision.Eligible);
        var codes = result.Decision.Reasons.Select(reason => reason.Code).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.SourceHashMismatch, codes);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.SourceFileOutsideProject, codes);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.SourceFileGenerated, codes);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.SourceFileLinked, codes);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.SourcePathReparsePoint, codes);
        Assert.Contains(MauiXamlSourceIneligibilityCodes.AutomationIdLocalizedOrUserDerived, codes);
    }

    [Theory]
    [InlineData("save button")]
    [InlineData("{Binding UserName}")]
    [InlineData("UserNameEntry")]
    [InlineData("保存")]
    [InlineData("9Save")]
    [InlineData("Save..Button")]
    public void AutomationIdGrammar_RejectsLocalizedDynamicAndUnsafeValues(string value)
    {
        Assert.False(MauiXamlAutomationIdGrammar.TryValidate(value, out _));
    }

    [Fact]
    public async Task Store_ProposePreviewRejectHappyPath()
    {
        const string xaml = "<ContentPage><Button Text=\"Save\" /></ContentPage>";
        var project = await CreateProjectAsync(xaml);
        var element = Element(project.SourcePath, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);
        var built = await service.BuildAsync(element, "SaveButton", [element]);
        Assert.True(built.Ok, built.Error);

        var store = new WorkflowXamlSourceProposalStore();
        var proposed = store.Propose(built.Proposal!);
        Assert.True(proposed.Ok, proposed.Error);
        Assert.Equal(MauiXamlSourceProposalStates.Proposed, proposed.Proposal!.State);

        var previewed = store.Preview(built.Proposal!.ProposalId);
        Assert.True(previewed.Ok, previewed.Error);
        Assert.Equal(MauiXamlSourceProposalStates.Previewed, previewed.Proposal!.State);

        // Preview is idempotent from previewed state.
        var previewedAgain = store.Preview(built.Proposal!.ProposalId);
        Assert.True(previewedAgain.Ok);
        Assert.Equal(MauiXamlSourceProposalStates.Previewed, previewedAgain.Proposal!.State);

        var rejected = store.Reject(built.Proposal!.ProposalId, "reviewer", "not-now");
        Assert.True(rejected.Ok, rejected.Error);
        Assert.Equal(MauiXamlSourceProposalStates.Rejected, rejected.Proposal!.State);
        Assert.Equal("not-now", rejected.Proposal.ReasonCode);

        // Rejected proposals cannot be rejected again.
        var rejectedAgain = store.Reject(built.Proposal!.ProposalId, "reviewer", "reason");
        Assert.False(rejectedAgain.Ok);
        Assert.Equal("proposal-terminal", rejectedAgain.Code);
    }

    [Fact]
    public async Task Store_HistoryAppendChainsProposalTransitionsWithoutLeakingText()
    {
        const string xaml = "<ContentPage><Button AutomationId=\"SecretCustomer\" Text=\"Save\" /></ContentPage>";
        var project = await CreateProjectAsync(xaml);
        var element = Element(project.SourcePath, xaml, "<Button");
        var service = new XamlAutomationIdProposalService(project.ProjectPath);
        var built = await service.BuildAsync(element, "SaveButton", [element]);
        Assert.True(built.Ok, built.Error);
        var proposal = built.Proposal!;

        var store = new WorkflowXamlSourceProposalStore();
        Assert.True(store.Propose(proposal).Ok);
        var previewed = store.Preview(proposal.ProposalId);
        Assert.True(previewed.Ok, previewed.Error);

        var history = new WorkflowXamlSourceHistoryStore(Path.GetDirectoryName(project.ProjectPath)!);
        var first = history.Append(previewed.Proposal!);
        Assert.True(first.Ok, first.Error);
        var rejected = store.Reject(proposal.ProposalId, "reviewer", "not-now");
        Assert.True(rejected.Ok, rejected.Error);
        var second = history.Append(rejected.Proposal!);
        Assert.True(second.Ok, second.Error);

        var contents = await File.ReadAllTextAsync(first.HistoryPath!);
        Assert.DoesNotContain("SecretCustomer", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveButton", contents, StringComparison.Ordinal);
        Assert.DoesNotContain(proposal.Diff!, contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdvisoryAnalyzer_ReportsMissingDuplicateAndTemplateDiagnosticsWithoutFixes()
    {
        var additional = ImmutableArray.Create<AdditionalText>(
            new TestAdditionalText(
                Path.Combine(_root, "MainPage.xaml"),
                """
                <ContentPage>
                  <Button Text="Missing" />
                  <Button AutomationId="Duplicate" />
                  <Button AutomationId="Duplicate" />
                  <CollectionView>
                    <CollectionView.ItemTemplate>
                      <DataTemplate><Button AutomationId="TemplateButton" /></DataTemplate>
                    </CollectionView.ItemTemplate>
                  </CollectionView>
                </ContentPage>
                """));
        var compilation = CSharpCompilation.Create(
            "XamlAdvisory",
            [CSharpSyntaxTree.ParseText("public sealed class Placeholder {}")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var options = new AnalyzerOptions(additional);
        var diagnostics = await compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new XamlAutomationIdAdvisoryAnalyzer()),
            new CompilationWithAnalyzersOptions(options, null, true, false, false))
            .GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DFXAML001");
        Assert.Equal(2, diagnostics.Count(diagnostic => diagnostic.Id == "DFXAML002"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DFXAML003");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Descriptor.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable));
    }

    private async Task<ProjectFixture> CreateProjectAsync(string xaml, bool useCrLf = false)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "TestApp.csproj");
        var sourcePath = Path.Combine(directory, "MainPage.xaml");
        await File.WriteAllTextAsync(projectPath, "<Project />");
        var text = useCrLf ? xaml.ReplaceLineEndings("\r\n") : xaml;
        await File.WriteAllTextAsync(
            sourcePath,
            text,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new ProjectFixture(projectPath, sourcePath);
    }

    private static ElementInfo Element(string sourcePath, string source, string marker)
    {
        var (line, column) = LineColumn(source, marker);
        return new ElementInfo
        {
            Id = "element",
            Type = marker.Contains("WebView", StringComparison.Ordinal) ? "WebView" : "Button",
            FullType = marker.Contains("WebView", StringComparison.Ordinal)
                ? "Microsoft.Maui.Controls.WebView"
                : "Microsoft.Maui.Controls.Button",
            Framework = "maui",
            SourceFile = sourcePath,
            SourceLine = line,
            SourceColumn = column,
            SourceHash = MauiXamlSourceEligibilityAnalyzer.ComputeSourceHash(source),
            SourceConfidence = "mapped",
        };
    }

    private static (int Line, int Column) LineColumn(string source, string marker)
    {
        var offset = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(offset >= 0, $"Could not locate '{marker}'.");
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        // XDocument source map coordinates start at the element name, after '<'.
        return (line, offset - lineStart + 2);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    private sealed record ProjectFixture(string ProjectPath, string SourcePath);

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public TestAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
