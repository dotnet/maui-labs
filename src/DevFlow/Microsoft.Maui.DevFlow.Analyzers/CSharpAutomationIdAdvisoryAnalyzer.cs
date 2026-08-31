using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.Maui.DevFlow.Analyzers;

/// <summary>
/// Advisory diagnostics for the intentionally narrow C# AutomationId proposal subset. The
/// analyzer never supplies an automatic code fix: a reviewed proposal is opened by a capable IDE
/// host only after the broker has validated source identity and runtime uniqueness.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CSharpAutomationIdAdvisoryAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MissingAutomationId = new(
        "DFCS001",
        "Static MAUI control initializer has no AutomationId",
        "Static {0} initializer has no AutomationId",
        "DevFlow.Testability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A durable AutomationId may improve testability. This advisory never applies a source change.");

    public static readonly DiagnosticDescriptor DuplicateAutomationId = new(
        "DFCS002",
        "Duplicate C# AutomationId",
        "AutomationId '{0}' is duplicated by C# declarations",
        "DevFlow.Testability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Duplicate AutomationIds are ambiguous for automation. This advisory never changes source.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor UnsafeDeclaration = new(
        "DFCS003",
        "C# AutomationId declaration is outside the reviewed proposal subset",
        "AutomationId declaration is in an unsupported {0} context",
        "DevFlow.Testability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Templates, repeaters, factories, dynamic construction, and conditional declarations require a separate stable identity design.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingAutomationId, DuplicateAutomationId, UnsafeDeclaration);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationStartAnalysisContext context)
    {
        var declarations = new ConcurrentBag<AutomationIdDeclaration>();
        var unsafeLocations = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        context.RegisterSyntaxNodeAction(syntaxContext =>
        {
            var creation = (ObjectCreationExpressionSyntax)syntaxContext.Node;
            var result = CSharpAutomationIdProposalBuilder.Analyze(
                syntaxContext.SemanticModel,
                creation.SpanStart,
                "DevFlowAutomationId",
                syntaxContext.CancellationToken);

            ReportUnsafeContext(syntaxContext, result, creation.GetLocation(), unsafeLocations);
            if (!result.CanCreateMinimalPatch ||
                !result.IsObjectInitializer ||
                !result.IsSupportedActionableControl)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.OldAutomationId))
            {
                syntaxContext.ReportDiagnostic(Diagnostic.Create(
                    MissingAutomationId,
                    creation.Type.GetLocation(),
                    result.ControlType?.Name ?? creation.Type.ToString()));
            }
            else
            {
                declarations.Add(new AutomationIdDeclaration(result.OldAutomationId!, creation.GetLocation()));
            }
        }, SyntaxKind.ObjectCreationExpression);

        context.RegisterSyntaxNodeAction(syntaxContext =>
        {
            var assignment = (AssignmentExpressionSyntax)syntaxContext.Node;
            if (assignment.Left is not MemberAccessExpressionSyntax member ||
                !string.Equals(member.Name.Identifier.ValueText, "AutomationId", StringComparison.Ordinal))
            {
                return;
            }

            var result = CSharpAutomationIdProposalBuilder.Analyze(
                syntaxContext.SemanticModel,
                assignment.SpanStart,
                "DevFlowAutomationId",
                syntaxContext.CancellationToken);
            ReportUnsafeContext(syntaxContext, result, assignment.GetLocation(), unsafeLocations);
            if (result.CanCreateMinimalPatch &&
                result.SiteKind == CSharpAutomationIdProposalSiteKind.DirectAssignmentReplacement &&
                !string.IsNullOrWhiteSpace(result.OldAutomationId))
            {
                declarations.Add(new AutomationIdDeclaration(result.OldAutomationId!, member.Name.GetLocation()));
            }
        }, SyntaxKind.SimpleAssignmentExpression);

        context.RegisterCompilationEndAction(endContext =>
        {
            foreach (var group in declarations
                         .GroupBy(static declaration => declaration.Value, StringComparer.Ordinal)
                         .Where(static group => group.Count() > 1))
            {
                foreach (var declaration in group)
                {
                    endContext.ReportDiagnostic(Diagnostic.Create(
                        DuplicateAutomationId,
                        declaration.Location,
                        group.Key));
                }
            }
        });
    }

    private static void ReportUnsafeContext(
        SyntaxNodeAnalysisContext context,
        CSharpAutomationIdProposalSyntaxAnalysis analysis,
        Location location,
        ConcurrentDictionary<string, byte> reported)
    {
        var reason = analysis.Reasons.FirstOrDefault(static reason => reason.Code is
            "template-or-repeater" or
            "collection-lambda-or-factory" or
            "conditional-or-preprocessor" or
            "reflection-or-dynamic-construction");
        var key = location.SourceTree?.FilePath + ":" + location.SourceSpan.Start + ":" + reason?.Code;
        if (reason is null || !reported.TryAdd(key, 0))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            UnsafeDeclaration,
            location,
            reason.Code.Replace('-', ' ')));
    }

    private sealed class AutomationIdDeclaration
    {
        public AutomationIdDeclaration(string value, Location location)
        {
            Value = value;
            Location = location;
        }

        public string Value { get; }
        public Location Location { get; }
    }
}
