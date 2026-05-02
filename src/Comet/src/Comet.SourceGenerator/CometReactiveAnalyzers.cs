using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Comet.SourceGenerator
{
	[DiagnosticAnalyzer(LanguageNames.CSharp)]
	public class CometReactiveAnalyzers : DiagnosticAnalyzer
	{
		const string Category = "Comet.Reactive";

		static readonly DiagnosticDescriptor SignalReadonlyRule = new(
		"COMET001",
		"Signal field must be readonly",
		"Signal<{0}> field '{1}' must be declared readonly. Signal identity must be stable across the view's lifetime.",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

		static readonly DiagnosticDescriptor ComputedReadonlyRule = new(
		"COMET002",
		"Computed field must be readonly",
		"Computed<{0}> field '{1}' must be declared readonly.",
		Category,
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

		static readonly DiagnosticDescriptor SignalValueReadRule = new(
		"COMET003",
		"Untracked Signal.Value read",
		"Reading Signal<{0}>.Value outside a reactive context. This read is not tracked. Use Peek() for intentional untracked reads.",
		Category,
		DiagnosticSeverity.Info,
		isEnabledByDefault: true);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
		=> ImmutableArray.Create(SignalReadonlyRule, ComputedReadonlyRule, SignalValueReadRule);

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterCompilationStartAction(RegisterCompilationStart);
		}

		void RegisterCompilationStart(CompilationStartAnalysisContext context)
		{
			var viewSymbol = context.Compilation.GetTypeByMetadataName("Comet.View");
			var bodyAttributeSymbol = context.Compilation.GetTypeByMetadataName("Comet.BodyAttribute");
			if (viewSymbol == null)
			return;

			context.RegisterSymbolAction(ctx => AnalyzeField(ctx, viewSymbol), SymbolKind.Field);
			context.RegisterSyntaxNodeAction(ctx => AnalyzeSignalValueRead(ctx, bodyAttributeSymbol), SyntaxKind.SimpleMemberAccessExpression);
		}

		static void AnalyzeField(SymbolAnalysisContext context, INamedTypeSymbol viewSymbol)
		{
			var fieldSymbol = (IFieldSymbol)context.Symbol;
			if (fieldSymbol.ContainingType == null)
			return;

			if (!InheritsFromView(fieldSymbol.ContainingType, viewSymbol))
			return;

			if (fieldSymbol.IsReadOnly || fieldSymbol.IsConst)
			return;

			if (fieldSymbol.Type is not INamedTypeSymbol namedType || !namedType.IsGenericType)
			return;

			var typeName = namedType.Name;
			if (typeName != "Signal" && typeName != "Computed")
			return;

			var typeArg = namedType.TypeArguments.Length > 0
			? namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
			: "T";

			var descriptor = typeName == "Signal" ? SignalReadonlyRule : ComputedReadonlyRule;
			context.ReportDiagnostic(Diagnostic.Create(descriptor, fieldSymbol.Locations.FirstOrDefault(), typeArg, fieldSymbol.Name));
		}

		static void AnalyzeSignalValueRead(SyntaxNodeAnalysisContext context, INamedTypeSymbol? bodyAttributeSymbol)
		{
			var memberAccess = (MemberAccessExpressionSyntax)context.Node;
			if (memberAccess.Name.Identifier.Text != "Value")
			return;

			if (IsValueWrite(memberAccess))
			return;

			var propertySymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol as IPropertySymbol;
			if (propertySymbol?.ContainingType is not INamedTypeSymbol containingType || !containingType.IsGenericType)
			return;

			if (containingType.Name != "Signal")
			return;

			if (IsInsideBodyMethod(memberAccess, context.SemanticModel, bodyAttributeSymbol, context.CancellationToken))
			return;

			if (IsInsideReactiveLambda(memberAccess, context.SemanticModel, context.CancellationToken))
			return;

			var typeArg = containingType.TypeArguments.Length > 0
			? containingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
			: "T";

			context.ReportDiagnostic(Diagnostic.Create(SignalValueReadRule, memberAccess.Name.GetLocation(), typeArg));
		}

		static bool InheritsFromView(INamedTypeSymbol typeSymbol, INamedTypeSymbol viewSymbol)
		{
			for (var current = typeSymbol; current != null; current = current.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(current, viewSymbol))
				return true;
			}
			return false;
		}

		static bool IsValueWrite(MemberAccessExpressionSyntax memberAccess)
		{
			if (memberAccess.Parent is AssignmentExpressionSyntax assignment && assignment.Left == memberAccess)
			return true;

			if (memberAccess.Parent is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.PreIncrementExpression))
			return true;

			if (memberAccess.Parent is PrefixUnaryExpressionSyntax prefixDec && prefixDec.IsKind(SyntaxKind.PreDecrementExpression))
			return true;

			if (memberAccess.Parent is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.PostIncrementExpression))
			return true;

			if (memberAccess.Parent is PostfixUnaryExpressionSyntax postfixDec && postfixDec.IsKind(SyntaxKind.PostDecrementExpression))
			return true;

			return false;
		}

		static bool IsInsideBodyMethod(SyntaxNode node, SemanticModel semanticModel, INamedTypeSymbol? bodyAttributeSymbol, CancellationToken cancellationToken)
		{
			var methodSyntax = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
			if (methodSyntax == null)
			return false;

			var methodSymbol = semanticModel.GetDeclaredSymbol(methodSyntax, cancellationToken);
			if (methodSymbol == null)
			return false;

			foreach (var attribute in methodSymbol.GetAttributes())
			{
				if (bodyAttributeSymbol != null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, bodyAttributeSymbol))
				return true;

				var name = attribute.AttributeClass?.Name;
				if (name == "Body" || name == "BodyAttribute")
				return true;
			}

			return false;
		}

		static bool IsInsideReactiveLambda(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
		{
			var lambda = node.Ancestors().OfType<LambdaExpressionSyntax>().FirstOrDefault();
			if (lambda == null)
			return false;

			var argument = lambda.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
			if (argument?.Parent?.Parent is not ObjectCreationExpressionSyntax objectCreation)
			return false;

			var typeSymbol = semanticModel.GetTypeInfo(objectCreation, cancellationToken).Type as INamedTypeSymbol;
			if (typeSymbol == null)
			return false;

			return typeSymbol.Name == "Computed" || typeSymbol.Name == "Effect";
		}
	}
}
