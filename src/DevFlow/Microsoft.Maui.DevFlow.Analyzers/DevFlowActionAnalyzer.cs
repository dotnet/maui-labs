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

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DevFlowActionAnalyzer : DiagnosticAnalyzer
{
	private const string DevFlowActionAttributeName = "DevFlowActionAttribute";
	private const string DevFlowActionAttributeShortName = "DevFlowAction";
	private const string DescriptionAttributeName = "DescriptionAttribute";

	// MAUI_DFA001: Unsupported parameter type
	private static readonly DiagnosticDescriptor UnsupportedParameterType = new(
		id: "MAUI_DFA001",
		title: "Unsupported parameter type for [DevFlowAction]",
		messageFormat: "Parameter '{0}' has unsupported type '{1}' — use string, bool, int, long, short, byte, float, double, decimal, enum, or arrays/lists of these types",
		category: "DevFlow",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "DevFlow Actions can only accept parameters of types that can be reliably deserialized from JSON: primitive types, enums, and collections of these.");

	// MAUI_DFA002: Must be public static
	private static readonly DiagnosticDescriptor MustBePublicStatic = new(
		id: "MAUI_DFA002",
		title: "[DevFlowAction] method must be public static",
		messageFormat: "Method '{0}' must be 'public static' to be a DevFlow Action",
		category: "DevFlow",
		defaultSeverity: DiagnosticSeverity.Error,
		isEnabledByDefault: true,
		description: "DevFlow Actions are invoked via reflection and must be public static methods.");

	// MAUI_DFA003: Return type warning
	private static readonly DiagnosticDescriptor ReturnTypeMayNotSerialize = new(
		id: "MAUI_DFA003",
		title: "Return type may not serialize cleanly",
		messageFormat: "Return type '{0}' may not serialize cleanly — prefer void, Task, Task<T> with a simple type, or string",
		category: "DevFlow",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "DevFlow Action return values are serialized to JSON. Complex types may lose fidelity.");

	// MAUI_DFA004: Missing [Description] on parameter
	private static readonly DiagnosticDescriptor MissingParameterDescription = new(
		id: "MAUI_DFA004",
		title: "Parameter missing [Description] attribute",
		messageFormat: "Parameter '{0}' has no [Description] attribute — adding a description helps AI agents understand how to use this action",
		category: "DevFlow",
		defaultSeverity: DiagnosticSeverity.Info,
		isEnabledByDefault: true,
		description: "AI agents rely on parameter descriptions to understand what values to pass. Adding [Description] makes your action more usable.");

	// MAUI_DFA005: Duplicate action name
	private static readonly DiagnosticDescriptor DuplicateActionName = new(
		id: "MAUI_DFA005",
		title: "Duplicate [DevFlowAction] name",
		messageFormat: "Action name '{0}' is already used by '{1}' — duplicate names shadow each other at runtime",
		category: "DevFlow",
		defaultSeverity: DiagnosticSeverity.Warning,
		isEnabledByDefault: true,
		description: "Each [DevFlowAction] name must be unique. At runtime, duplicate names cause the second registration to be silently ignored.",
		customTags: [WellKnownDiagnosticTags.CompilationEnd]);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(UnsupportedParameterType, MustBePublicStatic, ReturnTypeMayNotSerialize, MissingParameterDescription, DuplicateActionName);

	public override void Initialize(AnalysisContext context)
	{
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();

		context.RegisterCompilationStartAction(compilationContext =>
		{
			var actionsByName = new ConcurrentDictionary<string, ConcurrentBag<IMethodSymbol>>(StringComparer.OrdinalIgnoreCase);

			compilationContext.RegisterSymbolAction(symbolContext =>
			{
				var method = (IMethodSymbol)symbolContext.Symbol;

				if (!HasDevFlowActionAttribute(method))
					return;

				// Track action name for DFA005 duplicate check
				var actionName = GetDevFlowActionName(method);
				if (actionName != null)
				{
					actionsByName.GetOrAdd(actionName, _ => new ConcurrentBag<IMethodSymbol>()).Add(method);
				}

				AnalyzeMethod(symbolContext, method);
			}, SymbolKind.Method);

			compilationContext.RegisterCompilationEndAction(endContext =>
			{
				foreach (var kvp in actionsByName)
				{
					var methods = kvp.Value.ToArray();
					if (methods.Length < 2)
						continue;

					// Report on every method that shares the duplicate name
					foreach (var method in methods)
					{
						var otherMethod = methods.First(m => !SymbolEqualityComparer.Default.Equals(m, method));
						var otherName = $"{otherMethod.ContainingType.Name}.{otherMethod.Name}";
						endContext.ReportDiagnostic(Diagnostic.Create(
							DuplicateActionName,
							method.Locations.FirstOrDefault(),
							kvp.Key,
							otherName));
					}
				}
			});
		});
	}

	private static void AnalyzeMethod(SymbolAnalysisContext context, IMethodSymbol method)
	{
		// DFA002: Must be public static
		if (method.DeclaredAccessibility != Accessibility.Public || !method.IsStatic)
		{
			context.ReportDiagnostic(Diagnostic.Create(
				MustBePublicStatic,
				method.Locations.FirstOrDefault(),
				method.Name));
		}

		// DFA001 + DFA004: Check parameters
		foreach (var param in method.Parameters)
		{
			if (!IsSupportedType(param.Type))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					UnsupportedParameterType,
					param.Locations.FirstOrDefault(),
					param.Name,
					param.Type.ToDisplayString()));
			}

			if (!HasDescriptionAttribute(param))
			{
				context.ReportDiagnostic(Diagnostic.Create(
					MissingParameterDescription,
					param.Locations.FirstOrDefault(),
					param.Name));
			}
		}

		// DFA003: Check return type
		var returnType = method.ReturnType;
		if (!IsWellKnownReturnType(returnType))
		{
			context.ReportDiagnostic(Diagnostic.Create(
				ReturnTypeMayNotSerialize,
				method.Locations.FirstOrDefault(),
				returnType.ToDisplayString()));
		}
	}

	private static string? GetDevFlowActionName(IMethodSymbol method)
	{
		foreach (var attr in method.GetAttributes())
		{
			var name = attr.AttributeClass?.Name;
			if (name != DevFlowActionAttributeName && name != DevFlowActionAttributeShortName)
				continue;

			if (attr.ConstructorArguments.Length > 0 &&
				attr.ConstructorArguments[0].Value is string actionName)
			{
				return actionName;
			}
		}

		return null;
	}

	private static bool HasDevFlowActionAttribute(IMethodSymbol method)
	{
		return method.GetAttributes().Any(attr =>
		{
			var name = attr.AttributeClass?.Name;
			return name == DevFlowActionAttributeName || name == DevFlowActionAttributeShortName;
		});
	}

	private static bool HasDescriptionAttribute(IParameterSymbol param)
	{
		return param.GetAttributes().Any(attr =>
			attr.AttributeClass?.Name == DescriptionAttributeName ||
			attr.AttributeClass?.Name == "Description");
	}

	private static bool IsSupportedType(ITypeSymbol type)
	{
		// Handle nullable value types: Nullable<T>
		if (type is INamedTypeSymbol namedType &&
			namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
			namedType.TypeArguments.Length == 1)
		{
			return IsSupportedSimpleType(namedType.TypeArguments[0]);
		}

		// Handle arrays: T[]
		if (type is IArrayTypeSymbol arrayType)
		{
			return IsSupportedSimpleType(arrayType.ElementType);
		}

		// Handle generic collections: List<T>, IList<T>, IEnumerable<T>, IReadOnlyList<T>
		if (type is INamedTypeSymbol genericType && genericType.IsGenericType && genericType.TypeArguments.Length == 1)
		{
			var def = genericType.OriginalDefinition.ToDisplayString();
			if (def == "System.Collections.Generic.List<T>" ||
				def == "System.Collections.Generic.IList<T>" ||
				def == "System.Collections.Generic.IEnumerable<T>" ||
				def == "System.Collections.Generic.IReadOnlyList<T>" ||
				def == "System.Collections.Generic.ICollection<T>" ||
				def == "System.Collections.Generic.IReadOnlyCollection<T>")
			{
				return IsSupportedSimpleType(genericType.TypeArguments[0]);
			}
		}

		return IsSupportedSimpleType(type);
	}

	private static bool IsSupportedSimpleType(ITypeSymbol type)
	{
		switch (type.SpecialType)
		{
			case SpecialType.System_String:
			case SpecialType.System_Boolean:
			case SpecialType.System_Int32:
			case SpecialType.System_Int64:
			case SpecialType.System_Int16:
			case SpecialType.System_Byte:
			case SpecialType.System_Single:
			case SpecialType.System_Double:
			case SpecialType.System_Decimal:
				return true;
		}

		if (type.TypeKind == TypeKind.Enum)
			return true;

		return false;
	}

	private static bool IsWellKnownReturnType(ITypeSymbol type)
	{
		// void
		if (type.SpecialType == SpecialType.System_Void)
			return true;

		// Simple supported types
		if (IsSupportedSimpleType(type))
			return true;

		// string (already covered by IsSupportedSimpleType, but explicit for clarity)
		if (type.SpecialType == SpecialType.System_String)
			return true;

		if (type is INamedTypeSymbol namedType)
		{
			var fullName = namedType.ToDisplayString();

			// Task (non-generic)
			if (fullName == "System.Threading.Tasks.Task")
				return true;

			// Task<T> where T is a supported simple type or string
			if (namedType.IsGenericType &&
				namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.Task<TResult>" &&
				namedType.TypeArguments.Length == 1)
			{
				var inner = namedType.TypeArguments[0];
				return IsSupportedSimpleType(inner) || inner.SpecialType == SpecialType.System_Void;
			}

			// ValueTask
			if (fullName == "System.Threading.Tasks.ValueTask")
				return true;

			// ValueTask<T>
			if (namedType.IsGenericType &&
				namedType.OriginalDefinition.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>" &&
				namedType.TypeArguments.Length == 1)
			{
				var inner = namedType.TypeArguments[0];
				return IsSupportedSimpleType(inner);
			}
		}

		return false;
	}
}
