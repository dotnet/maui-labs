using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Comet.SourceGenerator
{
	/// <summary>
	/// Generates per-control style infrastructure from [CometControlState] attributes:
	/// - {Control}Configuration readonly struct (in Comet.Styles)
	/// - {Control}StyleExtensions static class with scoped .{Control}Style() method
	/// - Partial class with private state fields and ResolveCurrentStyle() method
	/// </summary>
	[Generator]
	public class StyleInfrastructureGenerator : ISourceGenerator
	{
		public void Initialize(GeneratorInitializationContext context)
		{
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		public void Execute(GeneratorExecutionContext context)
		{
			if (!(context.SyntaxContextReceiver is SyntaxReceiver rx) || !rx.ControlStates.Any())
				return;

			foreach (var info in rx.ControlStates)
			{
				EmitConfigurationStruct(context, info);
				EmitStyleExtensions(context, info);
				EmitResolveCurrentStyle(context, info);
			}
		}

		static void EmitConfigurationStruct(GeneratorExecutionContext context, ControlStateInfo info)
		{
			// Skip if hand-written version already exists in the compilation
			var existing = context.Compilation.GetTypeByMetadataName(
				$"Comet.Styles.{info.ControlName}Configuration");
			if (existing != null)
				return;

			var sb = new StringBuilder();
			sb.AppendLine("using System;");
			sb.AppendLine("using Comet;");
			sb.AppendLine();
			sb.AppendLine("namespace Comet.Styles");
			sb.AppendLine("{");
			sb.AppendLine($"\t/// <summary>");
			sb.AppendLine($"\t/// Configuration provided to {info.ControlName} style implementations.");
			sb.AppendLine($"\t/// TargetView carries the view reference so styles can resolve tokens");
			sb.AppendLine($"\t/// against the nearest scoped theme.");
			sb.AppendLine($"\t/// </summary>");
			sb.AppendLine($"\tpublic readonly struct {info.ControlName}Configuration");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\tpublic View TargetView { get; init; }");
			sb.AppendLine("\t\tpublic bool IsEnabled { get; init; }");

			foreach (var state in info.States)
			{
				sb.AppendLine($"\t\tpublic bool {state} {{ get; init; }}");
			}

			foreach (var prop in info.ConfigProperties)
			{
				sb.AppendLine($"\t\tpublic {prop.Type} {prop.Name} {{ get; init; }}");
			}

			sb.AppendLine("\t}");
			sb.AppendLine("}");

			context.AddSource($"{info.ControlName}Configuration.g.cs", sb.ToString());
		}

		static void EmitStyleExtensions(GeneratorExecutionContext context, ControlStateInfo info)
		{
			// Skip if a style extension method already exists (e.g., hand-written ControlStyleExtensions)
			if (HasExistingStyleExtension(context.Compilation, info.ControlName))
				return;

			var sb = new StringBuilder();
			sb.AppendLine("using System;");
			sb.AppendLine("using Comet;");
			sb.AppendLine("using Comet.Styles;");
			sb.AppendLine();
			sb.AppendLine("namespace Comet");
			sb.AppendLine("{");
			sb.AppendLine($"\t/// <summary>");
			sb.AppendLine($"\t/// Scoped style extension for {info.ControlName}.");
			sb.AppendLine($"\t/// Sets the style in the cascading environment so all {info.ControlName}");
			sb.AppendLine($"\t/// descendants resolve it via ResolveCurrentStyle().");
			sb.AppendLine($"\t/// </summary>");
			sb.AppendLine($"\tpublic static class {info.ControlName}StyleExtensions");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\t/// <summary>Sets the {info.ControlName} style for this view and its subtree.</summary>");
			sb.AppendLine($"\t\tpublic static T {info.ControlName}Style<T>(");
			sb.AppendLine($"\t\t\tthis T view,");
			sb.AppendLine($"\t\t\tIControlStyle<{info.ControlName}, {info.ControlName}Configuration> style) where T : View");
			sb.AppendLine("\t\t{");
			sb.AppendLine($"\t\t\tview.SetEnvironment(StyleToken<{info.ControlName}>.Key, style, cascades: true);");
			sb.AppendLine("\t\t\treturn view;");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			context.AddSource($"{info.ControlName}StyleExtensions.g.cs", sb.ToString());
		}

		static void EmitResolveCurrentStyle(GeneratorExecutionContext context, ControlStateInfo info)
		{
			// Look up the actual config struct from the compilation to match its fields
			var configTypeName = $"Comet.Styles.{info.ControlName}Configuration";
			var configType = context.Compilation.GetTypeByMetadataName(configTypeName);

			// Determine which state fields actually exist on the config struct
			var validStates = new List<string>();
			foreach (var state in info.States)
			{
				if (configType == null || configType.GetMembers(state).Any())
					validStates.Add(state);
			}

			// Determine which config properties actually exist on the config struct
			var validProps = new List<(string Name, string Type, string SourceField)>();
			foreach (var prop in info.ConfigProperties)
			{
				if (configType == null || configType.GetMembers(prop.Name).Any())
					validProps.Add(prop);
			}

			// Check if Theme has the 2-type-param GetControlStyle<T, TConfig>() method
			var themeType = context.Compilation.GetTypeByMetadataName("Comet.Styles.Theme");
			var hasNewStyleApi = themeType != null && themeType.GetMembers("GetControlStyle")
				.OfType<IMethodSymbol>()
				.Any(m => m.TypeParameters.Length == 2);

			var sb = new StringBuilder();
			sb.AppendLine("using System;");
			sb.AppendLine("using Comet.Styles;");
			sb.AppendLine();
			sb.AppendLine("namespace Comet");
			sb.AppendLine("{");
			sb.AppendLine($"\tpublic partial class {info.ControlName}");
			sb.AppendLine("\t{");

			// Private state-tracking fields (handlers will update these)
			foreach (var state in info.States)
			{
				var fieldName = "_" + char.ToLower(state[0]) + state.Substring(1);
				sb.AppendLine($"\t\tprivate bool {fieldName};");
			}

			sb.AppendLine();
			sb.AppendLine("\t\t/// <summary>");
			sb.AppendLine($"\t\t/// Resolves the current {info.ControlName} style from environment or theme defaults.");
			sb.AppendLine("\t\t/// Called during the view's render cycle or on control state changes.");
			sb.AppendLine("\t\t/// </summary>");
			sb.AppendLine("\t\tinternal ViewModifier ResolveCurrentStyle()");
			sb.AppendLine("\t\t{");

			// 1. Check scoped or local environment
			sb.AppendLine($"\t\t\tvar style = this.GetEnvironment<IControlStyle<{info.ControlName}, {info.ControlName}Configuration>>(");
			sb.AppendLine($"\t\t\t\tStyleToken<{info.ControlName}>.Key);");
			sb.AppendLine();

			// 2. Fall back to active theme's control style defaults
			if (hasNewStyleApi)
			{
				sb.AppendLine("\t\t\tif (style == null)");
				sb.AppendLine("\t\t\t{");
				sb.AppendLine("\t\t\t\tvar theme = ThemeManager.Current(this);");
				sb.AppendLine($"\t\t\t\tstyle = theme?.GetControlStyle<{info.ControlName}, {info.ControlName}Configuration>();");
				sb.AppendLine("\t\t\t}");
			}
			else
			{
				sb.AppendLine("\t\t\t// TODO: Theme fallback enabled once Theme.GetControlStyle<T, TConfig>() is added.");
				sb.AppendLine("\t\t\t// Spec §4.8: style ??= ThemeManager.Current(this)?.GetControlStyle<T, TConfig>();");
			}
			sb.AppendLine();

			sb.AppendLine("\t\t\tif (style == null)");
			sb.AppendLine("\t\t\t\treturn ViewModifier.Empty;");
			sb.AppendLine();

			// 3. Build config struct with current state
			sb.AppendLine($"\t\t\tvar config = new {info.ControlName}Configuration");
			sb.AppendLine("\t\t\t{");
			sb.AppendLine("\t\t\t\tTargetView = this,");
			sb.AppendLine("\t\t\t\tIsEnabled = this.GetEnvironment<bool?>(nameof(Microsoft.Maui.IView.IsEnabled)) ?? true,");

			foreach (var state in validStates)
			{
				var fieldName = "_" + char.ToLower(state[0]) + state.Substring(1);
				sb.AppendLine($"\t\t\t\t{state} = {fieldName},");
			}

			foreach (var prop in validProps)
			{
				var bindingField = char.ToLower(prop.SourceField[0]) + prop.SourceField.Substring(1);
				// Use ?? default to handle nullable Binding<T>.CurrentValue
				if (prop.Type == "string")
					sb.AppendLine($"\t\t\t\t{prop.Name} = {bindingField}?.CurrentValue,");
				else
					sb.AppendLine($"\t\t\t\t{prop.Name} = {bindingField}?.CurrentValue ?? default,");
			}

			sb.AppendLine("\t\t\t};");
			sb.AppendLine();
			sb.AppendLine("\t\t\treturn style.Resolve(config);");
			sb.AppendLine("\t\t}");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			context.AddSource($"{info.ControlName}ResolveStyle.g.cs", sb.ToString());
		}

		/// <summary>
		/// Checks if a {controlName}Style extension method already exists in the compilation.
		/// </summary>
		static bool HasExistingStyleExtension(Compilation compilation, string controlName)
		{
			var methodName = $"{controlName}Style";

			// Check the hand-written ControlStyleExtensions class
			var extensionsType = compilation.GetTypeByMetadataName("Comet.ControlStyleExtensions");
			if (extensionsType != null)
			{
				if (extensionsType.GetMembers().OfType<IMethodSymbol>().Any(m => m.Name == methodName))
					return true;
			}

			// Check if a generated {Control}StyleExtensions class already exists
			var generatedType = compilation.GetTypeByMetadataName($"Comet.{controlName}StyleExtensions");
			if (generatedType != null)
				return true;

			return false;
		}

		class ControlStateInfo
		{
			public string ControlName { get; set; }
			public INamedTypeSymbol InterfaceType { get; set; }
			public List<string> States { get; set; } = new List<string>();
			public List<(string Name, string Type, string SourceField)> ConfigProperties { get; set; }
				= new List<(string, string, string)>();
		}

		class SyntaxReceiver : ISyntaxContextReceiver
		{
			public List<ControlStateInfo> ControlStates { get; } = new List<ControlStateInfo>();

			public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
			{
				if (!(context.Node is AttributeSyntax attrib))
					return;

				var typeInfo = context.SemanticModel.GetTypeInfo(attrib);
				var typeName = typeInfo.Type?.ToDisplayString();

				if (typeName != "Comet.CometControlStateAttribute")
					return;

				var args = attrib.ArgumentList?.Arguments;
				if (args == null || args.Value.Count == 0)
					return;

				// First argument: typeof(IButton)
				if (!(args.Value[0].Expression is TypeOfExpressionSyntax typeOfExpr))
					return;

				var interfaceSymbolInfo = context.SemanticModel.GetSymbolInfo(typeOfExpr.Type);
				if (!(interfaceSymbolInfo.Symbol is INamedTypeSymbol interfaceSymbol))
					return;

				var info = new ControlStateInfo
				{
					InterfaceType = interfaceSymbol,
					ControlName = interfaceSymbol.Name.TrimStart('I'),
				};

				// Parse named arguments
				foreach (var arg in args.Value.Skip(1))
				{
					var argName = arg.NameEquals?.Name.Identifier.ValueText;

					if (argName == "ControlName")
					{
						var constVal = context.SemanticModel.GetConstantValue(arg.Expression);
						if (constVal.HasValue)
							info.ControlName = constVal.Value.ToString();
					}
					else if (argName == "States")
					{
						foreach (var expr in GetArrayExpressions(arg.Expression))
						{
							var val = context.SemanticModel.GetConstantValue(expr);
							if (val.HasValue)
								info.States.Add(val.Value.ToString());
						}
					}
					else if (argName == "ConfigProperties")
					{
						foreach (var expr in GetArrayExpressions(arg.Expression))
						{
							var val = context.SemanticModel.GetConstantValue(expr);
							if (val.HasValue)
							{
								var parts = val.Value.ToString().Split(':');
								if (parts.Length >= 2)
								{
									var name = parts[0].Trim();
									var type = parts[1].Trim();
									var source = parts.Length >= 3 ? parts[2].Trim() : name;
									info.ConfigProperties.Add((name, type, source));
								}
							}
						}
					}
				}

				ControlStates.Add(info);
			}

			static IEnumerable<ExpressionSyntax> GetArrayExpressions(ExpressionSyntax expr)
			{
				if (expr is ImplicitArrayCreationExpressionSyntax iac)
					return iac.Initializer.Expressions;
				if (expr is ArrayCreationExpressionSyntax ac && ac.Initializer != null)
					return ac.Initializer.Expressions;
				return Enumerable.Empty<ExpressionSyntax>();
			}
		}
	}
}
