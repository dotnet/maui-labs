using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.DevFlow.Analyzers;

/// <summary>
/// Roslyn-only analysis of the deliberately tiny C# syntax subset accepted by DevFlow reviewed
/// source proposals. This type never reads files, opens a workspace, formats a document, or
/// applies an edit; a host must provide a real semantic model and decide whether to show a proposal.
/// </summary>
public static class CSharpAutomationIdProposalBuilder
{
    private static readonly ImmutableHashSet<string> ActionableControlNames =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "Button", "ImageButton", "Entry", "Editor", "SearchBar", "CheckBox",
            "RadioButton", "Switch", "Slider", "Stepper");

    private static readonly string[] TemplateOrRepeaterTokens =
    [
        "DataTemplate", "ControlTemplate", "ItemTemplate", "ItemsSource", "BindableLayout",
        "CollectionView", "ListView", "CarouselView", "ItemsView", "Repeater",
    ];

    /// <summary>
    /// Analyses the syntax at a mapped source position. The returned patch is an in-memory exact
    /// replacement only; callers must still evaluate project/path/hash/live facts before use.
    /// </summary>
    public static CSharpAutomationIdProposalSyntaxAnalysis Analyze(
        SemanticModel semanticModel,
        int position,
        string proposedAutomationId,
        CancellationToken cancellationToken = default)
    {
        if (semanticModel is null)
            throw new ArgumentNullException(nameof(semanticModel));

        var tree = semanticModel.SyntaxTree;
        var text = tree.GetText(cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var analysis = new CSharpAutomationIdProposalSyntaxAnalysis();
        if (position < 0 || position > text.Length)
        {
            analysis.Add("source-span-invalid", "The mapped C# source position is outside the current document.");
            return analysis;
        }

        var node = root.FindToken(position, findInsideTrivia: true).Parent;
        if (node is null)
        {
            analysis.Add("source-span-invalid", "The mapped C# source position did not resolve to a declaration.");
            return analysis;
        }

        var assignment = node.AncestorsAndSelf()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(IsAutomationIdAssignment);
        if (assignment is not null)
        {
            AnalyzeAssignment(semanticModel, assignment, proposedAutomationId, text, root, analysis, cancellationToken);
            return analysis;
        }

        var creation = node.AncestorsAndSelf().OfType<ObjectCreationExpressionSyntax>().FirstOrDefault();
        if (creation is null)
        {
            analysis.Add("unsupported-csharp-syntax",
                "The mapped C# declaration is neither a direct object initializer nor an AutomationId assignment.");
            return analysis;
        }

        AnalyzeObjectInitializer(semanticModel, creation, proposedAutomationId, text, root, analysis, cancellationToken);
        return analysis;
    }

    /// <summary>Returns true only for the named MAUI actionable controls supported by this preview.</summary>
    public static bool IsSupportedActionableMauiControl(ITypeSymbol? type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (!ActionableControlNames.Contains(current.Name))
                continue;
            if (string.Equals(
                    current.ContainingNamespace?.ToDisplayString(),
                    "Microsoft.Maui.Controls",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Returns a stable display identity without requiring Roslyn Workspaces' SymbolKey API.</summary>
    public static string? GetSymbolIdentity(ISymbol? symbol)
        => symbol is null
            ? null
            : symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static void AnalyzeObjectInitializer(
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax creation,
        string proposedAutomationId,
        SourceText text,
        SyntaxNode root,
        CSharpAutomationIdProposalSyntaxAnalysis analysis,
        CancellationToken cancellationToken)
    {
        analysis.IsObjectInitializer = creation.Initializer is not null;
        analysis.DeclarationSpan = creation.Span;
        analysis.ControlType = semanticModel.GetTypeInfo(creation, cancellationToken).Type;
        analysis.IsSupportedActionableControl = IsSupportedActionableMauiControl(analysis.ControlType);
        analysis.IsNativeOrWebViewSynthetic = IsNativeOrWebView(analysis.ControlType);

        if (creation.Initializer is null)
        {
            analysis.Add("unsupported-csharp-syntax",
                "Only an object initializer can receive a missing AutomationId proposal.");
            return;
        }
        if (!TryGetDirectConstructionSymbol(semanticModel, creation, cancellationToken, out var symbol))
        {
            analysis.Add("collection-lambda-or-factory",
                "The object initializer is not assigned directly to a resolvable local or member.");
            return;
        }

        analysis.TargetSymbol = symbol;
        analysis.IsDirectStaticDeclaration = true;
        AnalyzeSharedSafety(semanticModel, creation, root, analysis, cancellationToken);
        if (!analysis.IsSupportedActionableControl)
        {
            analysis.Add("unsupported-control-type",
                "The constructed type is not a supported Microsoft.Maui.Controls actionable control.");
        }
        if (analysis.IsNativeOrWebViewSynthetic)
        {
            analysis.Add("native-or-webview-synthetic",
                "Shell, native, and WebView controls are not eligible for an AutomationId proposal.");
        }
        if (analysis.HasBlockingReasons)
            return;

        var automationAssignments = creation.Initializer.Expressions
            .OfType<AssignmentExpressionSyntax>()
            .Where(IsAutomationIdAssignment)
            .ToList();
        if (automationAssignments.Count > 1)
        {
            analysis.Add("ambiguous-construction-or-assignment",
                "The object initializer contains more than one AutomationId assignment.");
            return;
        }

        analysis.SiteKind = automationAssignments.Count == 1
            ? CSharpAutomationIdProposalSiteKind.ObjectInitializerReplacement
            : CSharpAutomationIdProposalSiteKind.ObjectInitializerInsertion;

        if (automationAssignments.Count == 1)
        {
            var assignment = automationAssignments[0];
            if (!TryGetSafeStringLiteral(assignment.Right, out var oldLiteral, out var literalSpan))
            {
                analysis.HasComputedOrBoundAutomationId = true;
                analysis.Add("computed-or-bound-automation-id",
                    "The object initializer's AutomationId is not a direct standard string literal.");
                return;
            }

            analysis.OldAutomationId = oldLiteral;
            analysis.PatchStart = literalSpan.Start;
            analysis.PatchLength = literalSpan.Length;
            analysis.Replacement = CreateStringLiteral(proposedAutomationId);
            analysis.DeclarationSpan = assignment.Span;
            return;
        }

        if (!TryCreateInitializerInsertionPatch(creation.Initializer, text, proposedAutomationId, out var start, out var length, out var replacement))
        {
            analysis.Add("unsupported-csharp-syntax",
                "The object initializer trivia cannot be extended without broad formatting.");
            return;
        }
        analysis.PatchStart = start;
        analysis.PatchLength = length;
        analysis.Replacement = replacement;
    }

    private static void AnalyzeAssignment(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        string proposedAutomationId,
        SourceText text,
        SyntaxNode root,
        CSharpAutomationIdProposalSyntaxAnalysis analysis,
        CancellationToken cancellationToken)
    {
        analysis.IsDirectLiteralAssignment = true;
        analysis.SiteKind = CSharpAutomationIdProposalSiteKind.DirectAssignmentReplacement;
        analysis.DeclarationSpan = assignment.Span;

        if (assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
            assignment.Parent is not ExpressionStatementSyntax)
        {
            analysis.Add("unsupported-csharp-syntax",
                "Only a direct statement assigning .AutomationId is eligible.");
            return;
        }

        var receiver = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        if (receiver is not ILocalSymbol and not IFieldSymbol and not IPropertySymbol)
        {
            analysis.Add("semantic-symbol-unresolved",
                "The AutomationId receiver must resolve to one local, field, or property symbol.");
            return;
        }

        analysis.TargetSymbol = receiver;
        analysis.IsDirectStaticDeclaration = true;
        analysis.ControlType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        analysis.IsSupportedActionableControl = IsSupportedActionableMauiControl(analysis.ControlType);
        analysis.IsNativeOrWebViewSynthetic = IsNativeOrWebView(analysis.ControlType);
        AnalyzeSharedSafety(semanticModel, assignment, root, analysis, cancellationToken);
        if (!analysis.IsSupportedActionableControl)
        {
            analysis.Add("unsupported-control-type",
                "The AutomationId receiver is not a supported Microsoft.Maui.Controls actionable control.");
        }
        if (analysis.IsNativeOrWebViewSynthetic)
        {
            analysis.Add("native-or-webview-synthetic",
                "Shell, native, and WebView controls are not eligible for an AutomationId proposal.");
        }
        if (!TryGetSafeStringLiteral(assignment.Right, out var oldLiteral, out var literalSpan))
        {
            analysis.HasComputedOrBoundAutomationId = true;
            analysis.Add("computed-or-bound-automation-id",
                "Only a direct standard string-literal AutomationId assignment can be replaced.");
            return;
        }

        analysis.OldAutomationId = oldLiteral;
        analysis.PatchStart = literalSpan.Start;
        analysis.PatchLength = literalSpan.Length;
        analysis.Replacement = CreateStringLiteral(proposedAutomationId);
    }

    private static void AnalyzeSharedSafety(
        SemanticModel semanticModel,
        SyntaxNode declaration,
        SyntaxNode root,
        CSharpAutomationIdProposalSyntaxAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var ancestors = declaration.AncestorsAndSelf().ToArray();
        analysis.IsInsideTemplateOrRepeater = ancestors
            .Where(node => !ReferenceEquals(node, declaration) &&
                node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax or
                    ArgumentSyntax or InitializerExpressionSyntax)
            .Any(ContainsTemplateOrRepeaterToken);
        analysis.IsInsideCollectionLambdaOrFactory = ancestors.Any(node =>
            node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax or
                ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax ||
            (node is ArgumentSyntax && declaration.Ancestors().OfType<InvocationExpressionSyntax>().Any()));
        analysis.HasConditionalOrPreprocessorBranch =
            ancestors.Any(node => node is IfStatementSyntax or SwitchStatementSyntax or ConditionalExpressionSyntax) ||
            root.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.IsKind(SyntaxKind.IfDirectiveTrivia) ||
                trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) ||
                trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) ||
                trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia));
        analysis.HasReflectionOrDynamicConstruction =
            analysis.ControlType?.TypeKind == TypeKind.Dynamic ||
            ContainsReflectionOrDynamicToken(declaration.Ancestors().FirstOrDefault(node =>
                node is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax) ?? root);

        if (analysis.IsInsideTemplateOrRepeater)
        {
            analysis.Add("template-or-repeater",
                "DataTemplate, ControlTemplate, item-template, and repeater declarations are excluded.");
        }
        if (analysis.IsInsideCollectionLambdaOrFactory)
        {
            analysis.Add("collection-lambda-or-factory",
                "Controls created in a lambda, loop, collection, or invocation are excluded.");
        }
        if (analysis.HasConditionalOrPreprocessorBranch)
        {
            analysis.Add("conditional-or-preprocessor",
                "Conditional and preprocessor branches are excluded because the declaration is not statically singular.");
        }
        if (analysis.HasReflectionOrDynamicConstruction)
        {
            analysis.Add("reflection-or-dynamic-construction",
                "Reflection and dynamic construction are excluded.");
        }
    }

    private static bool TryGetDirectConstructionSymbol(
        SemanticModel semanticModel,
        ObjectCreationExpressionSyntax creation,
        CancellationToken cancellationToken,
        out ISymbol? symbol)
    {
        symbol = null;
        switch (creation.Parent)
        {
            case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }:
                symbol = semanticModel.GetDeclaredSymbol(declarator, cancellationToken);
                break;
            case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property }:
                symbol = semanticModel.GetDeclaredSymbol(property, cancellationToken);
                break;
            case AssignmentExpressionSyntax assignment when ReferenceEquals(assignment.Right, creation) &&
                                                        assignment.Parent is ExpressionStatementSyntax:
                symbol = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol;
                break;
        }

        return symbol is ILocalSymbol or IFieldSymbol or IPropertySymbol;
    }

    private static bool TryCreateInitializerInsertionPatch(
        InitializerExpressionSyntax initializer,
        SourceText text,
        string proposedAutomationId,
        out int start,
        out int length,
        out string replacement)
    {
        start = 0;
        length = 0;
        replacement = string.Empty;
        var property = "AutomationId = " + CreateStringLiteral(proposedAutomationId);
        if (initializer.Expressions.Count == 0)
        {
            start = initializer.CloseBraceToken.SpanStart;
            replacement = initializer.OpenBraceToken.Span.End == initializer.CloseBraceToken.SpanStart
                ? " " + property + " "
                : property;
            return true;
        }

        var last = initializer.Expressions[initializer.Expressions.Count - 1];
        var tail = text.ToString(TextSpan.FromBounds(last.Span.End, initializer.CloseBraceToken.SpanStart));
        if (tail.IndexOf("//", StringComparison.Ordinal) >= 0 ||
            tail.IndexOf("/*", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var hasTrailingComma = tail.TrimStart().StartsWith(",", StringComparison.Ordinal);
        start = last.Span.End;
        length = tail.Length;
        if (tail.IndexOfAny(['\r', '\n']) >= 0)
        {
            replacement = (hasTrailingComma ? string.Empty : ",") + tail + property + tail;
        }
        else
        {
            replacement = (hasTrailingComma ? string.Empty : ",") + " " + property + tail;
        }
        return true;
    }

    private static bool TryGetSafeStringLiteral(
        ExpressionSyntax expression,
        out string value,
        out TextSpan span)
    {
        value = string.Empty;
        span = default;
        if (expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return false;
        }

        var tokenText = literal.Token.Text;
        if (tokenText.Length < 2 ||
            tokenText[0] != '"' ||
            tokenText.StartsWith("\"\"", StringComparison.Ordinal))
        {
            return false;
        }
        value = literal.Token.ValueText;
        span = literal.Span;
        return true;
    }

    private static string CreateStringLiteral(string value)
        => "\"" + value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"") + "\"";

    private static bool IsAutomationIdAssignment(AssignmentExpressionSyntax assignment)
        => assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
           assignment.Left switch
           {
               MemberAccessExpressionSyntax memberAccess =>
                   string.Equals(memberAccess.Name.Identifier.ValueText, "AutomationId", StringComparison.Ordinal),
               IdentifierNameSyntax identifier =>
                   string.Equals(identifier.Identifier.ValueText, "AutomationId", StringComparison.Ordinal),
               _ => false,
           };

    private static bool ContainsTemplateOrRepeaterToken(SyntaxNode node)
        => node.DescendantTokens().Any(token =>
            TemplateOrRepeaterTokens.Any(value =>
                token.ValueText.Contains(value, StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsReflectionOrDynamicToken(SyntaxNode node)
        => node.DescendantTokens().Any(token =>
            token.ValueText.Equals("dynamic", StringComparison.Ordinal) ||
            token.ValueText.Equals("Activator", StringComparison.Ordinal) ||
            token.ValueText.Equals("CreateInstance", StringComparison.Ordinal) ||
            token.ValueText.Equals("GetType", StringComparison.Ordinal));

    private static bool IsNativeOrWebView(ITypeSymbol? type)
        => type is not null && EnumerateTypeNames(type).Any(name =>
            name.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Shell", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Shell", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> EnumerateTypeNames(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            yield return current.Name;
    }
}

/// <summary>Accepted C# source site forms.</summary>
public enum CSharpAutomationIdProposalSiteKind
{
    None,
    ObjectInitializerInsertion,
    ObjectInitializerReplacement,
    DirectAssignmentReplacement,
}

/// <summary>Roslyn syntax and semantic facts returned to a host before public contract projection.</summary>
public sealed class CSharpAutomationIdProposalSyntaxAnalysis
{
    private readonly List<CSharpAutomationIdProposalSyntaxReason> _reasons = [];

    public CSharpAutomationIdProposalSiteKind SiteKind { get; internal set; }
    public bool IsObjectInitializer { get; internal set; }
    public bool IsDirectLiteralAssignment { get; internal set; }
    public bool IsDirectStaticDeclaration { get; internal set; }
    public bool IsSupportedActionableControl { get; internal set; }
    public bool IsNativeOrWebViewSynthetic { get; internal set; }
    public bool IsInsideTemplateOrRepeater { get; internal set; }
    public bool IsInsideCollectionLambdaOrFactory { get; internal set; }
    public bool HasConditionalOrPreprocessorBranch { get; internal set; }
    public bool HasReflectionOrDynamicConstruction { get; internal set; }
    public bool HasComputedOrBoundAutomationId { get; internal set; }
    public string? OldAutomationId { get; internal set; }
    public int? PatchStart { get; internal set; }
    public int? PatchLength { get; internal set; }
    public string? Replacement { get; internal set; }
    public TextSpan DeclarationSpan { get; internal set; }
    public ISymbol? TargetSymbol { get; internal set; }
    public ITypeSymbol? ControlType { get; internal set; }
    public IReadOnlyList<CSharpAutomationIdProposalSyntaxReason> Reasons => _reasons;
    public bool HasBlockingReasons => _reasons.Count > 0;
    public bool CanCreateMinimalPatch =>
        !HasBlockingReasons &&
        PatchStart is >= 0 &&
        PatchLength is >= 0 &&
        Replacement is not null &&
        SiteKind != CSharpAutomationIdProposalSiteKind.None;

    internal void Add(string code, string message)
    {
        if (_reasons.All(reason => !string.Equals(reason.Code, code, StringComparison.Ordinal)))
            _reasons.Add(new CSharpAutomationIdProposalSyntaxReason(code, message));
    }
}

/// <summary>One non-source-text Roslyn rejection fact.</summary>
public sealed record CSharpAutomationIdProposalSyntaxReason(string Code, string Message);
