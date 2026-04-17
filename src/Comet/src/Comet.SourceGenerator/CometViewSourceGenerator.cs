using Comet.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stubble.Core.Builders;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//[assembly: Comet(typeof(IButton), nameof(IButton.Text))]

namespace Comet.SourceGenerator
{
	[Generator]
	public class CometViewSourceGenerator : ISourceGenerator
	{
		private const string attributeSource = @"
  	[System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
	internal class CometGenerateAttribute : System.Attribute
	{
		public CometGenerateAttribute(System.Type classType, params string[] keyProperties)
		{
			ClassType = classType;
			KeyProperties = keyProperties;
		}
		public string ClassName { get; set; }
		public System.Type ClassType { get; }
		public string[] KeyProperties { get; }
		public string Namespace { get; set; }
		public System.Type BaseClass { get; set; }
		public string[] DefaultValues { get; set; }
		public string[] Skip { get; set; }
	}
";


		const string classMustacheTemplate = @"
using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using System.Collections.Generic;
namespace {{NameSpace}} {
	public partial class {{ClassName}} : {{BaseClassName}} 
	{
		{{#HasParameters}}
		public {{ClassName}}() {}
		{{/HasParameters}}

		{{#SignalConstructorFunction}}	
		public {{ClassName}} ({{#SignalParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/SignalParametersFunction}})
		{
			{{#Parameters}}
			{{{SignalAssignment}}}
			{{/Parameters}}
		}
		{{/SignalConstructorFunction}}

		{{#FuncConstructorFunction}}
		public {{ClassName}} ({{#FuncParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/FuncParametersFunction}})
		{
			{{#Parameters}}
			{{{FuncAssignment}}}
			{{/Parameters}}
		}
		{{/FuncConstructorFunction}}

		{{#ComputedConstructorFunction}}
		public {{ClassName}} ({{#ComputedParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/ComputedParametersFunction}})
		{
			{{#Parameters}}
			{{{ComputedAssignment}}}
			{{/Parameters}}
		}
		{{/ComputedConstructorFunction}}

		{{#ValueConstructorFunction}}
		public {{ClassName}} ({{#ValueParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/ValueParametersFunction}})
		{
			{{#Parameters}}
			{{{ValueAssignment}}}
			{{/Parameters}}
		}
		{{/ValueConstructorFunction}}

		{{#Parameters}}
		{{{FieldDeclaration}}}
		{{{PropertyDeclaration}}}
		{{/Parameters}}
		
		{{#Properties}}
		{{#PropertiesFunc}}
		{{/PropertiesFunc}}
		{{/Properties}}
		{{#HasRenamedProperties}}
		protected static Dictionary<string, string> {{ClassName}}HandlerPropertyMapper = new(HandlerPropertyMapper)
		{

			{{#RenamedProperties}}
			[""{{NewName}}""] = ""{{OldName}}"",
			{{/RenamedProperties}}
		};
		protected override string GetHandlerPropertyName(string property)
			=> {{ClassName}}HandlerPropertyMapper.TryGetValue(property, out var value) ? value : property;
		{{/HasRenamedProperties}}
	}
}
";
		const string extensionProperty = @"
		public static T {{Name}}<T>(this T view, {{{Type}}} {{LowercaseName}}, bool cascades = true) where T : {{ClassName}} =>
			view.SetEnvironment(nameof({{FullName}}),(object){{LowercaseName}},cascades);
		
		public static T {{Name}}<T>(this T view, Func<{{{Type}}}> {{LowercaseName}}, bool cascades = true) where T : {{ClassName}} =>
			view.SetEnvironment(nameof({{FullName}}),(object){{LowercaseName}},cascades);
";
		const string extensionActionProperty = @"
		public static T {{Name}}<T>(this T view, {{{Type}}} {{LowercaseName}}, bool cascades = true) where T : {{ClassName}} =>
			view.SetEnvironment(nameof({{FullName}}),{{LowercaseName}},cascades);
";
		const string onPrefixedExtensionActionProperty = @"
		public static T On{{Name}}<T>(this T view, {{{Type}}} {{LowercaseName}}, bool cascades = true) where T : {{ClassName}} =>
			view.SetEnvironment(nameof({{FullName}}),{{LowercaseName}},cascades);
";
		const string tokenExtensionProperty = @"
		public static T {{Name}}<T>(this T view, Comet.Styles.Token<{{{Type}}}> token) where T : {{ClassName}} =>
			view.SetEnvironment(nameof({{FullName}}),(object)(Func<{{{Type}}}>)(() => view.GetToken(token)),true);
";
		const string factoryMustacheTemplate = @"
using System;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
namespace {{NameSpace}} {
	public static partial class CometControls
	{
		{{#SignalConstructorFunction}}
		public static {{ClassName}} {{ClassName}}({{#SignalParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/SignalParametersFunction}})
			=> new {{ClassName}}({{#ParameterNamesFunction}} {{LowercaseName}}{{/ParameterNamesFunction}});
		{{/SignalConstructorFunction}}

		{{#FuncConstructorFunction}}
		public static {{ClassName}} {{ClassName}}({{#FuncParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/FuncParametersFunction}})
			=> new {{ClassName}}({{#ParameterNamesFunction}} {{LowercaseName}}{{/ParameterNamesFunction}});
		{{/FuncConstructorFunction}}

		{{#ComputedConstructorFunction}}
		public static {{ClassName}} {{ClassName}}({{#ComputedParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/ComputedParametersFunction}})
			=> new {{ClassName}}({{#ParameterNamesFunction}} {{LowercaseName}}{{/ParameterNamesFunction}});
		{{/ComputedConstructorFunction}}

		{{#ValueConstructorFunction}}
		public static {{ClassName}} {{ClassName}}({{#ValueParametersFunction}} {{{Type}}} {{LowercaseName}}{{DefaultValueString}}{{/ValueParametersFunction}})
			=> new {{ClassName}}({{#ParameterNamesFunction}} {{LowercaseName}}{{/ParameterNamesFunction}});
		{{/ValueConstructorFunction}}

		{{#HasParameters}}
		public static {{ClassName}} {{ClassName}}()
			=> new {{ClassName}}();
		{{/HasParameters}}
	}
}
";
		const string extensionMustacheTemplate = @"
using Comet;
using Comet.Reactive;
using Comet.Styles;
using Microsoft.Maui;
using System;
namespace {{NameSpace}} {
	public static partial class {{ClassName}}Extensions
	{		
		{{#Properties}}
		{{#ExtensionPropertiesFunc}}


		{{/ExtensionPropertiesFunc}}
		{{/Properties}}
	
	}
}
";

		const string styleBuilderMustacheTemplate = @"
// StyleBuilder generator removed in Phase 1 (legacy ControlStyle<T> deleted per
// STYLE_THEME_SPEC.md §1.2 ""one way to do each thing""). See BuiltInStyles.cs for
// the replacement IControlStyle<TControl, TConfig> pattern.
";
		const string styleBuilderPropertyMustache = @"";

		static Dictionary<(bool HasGet, bool HasSet), (string FromEnvironment, string FromProperty)> interfacePropertyDictionary;

		static CometViewSourceGenerator()
		{
			var interfacePropertyEnvironmentMustache = @"
				{{{Type}}} {{FullName}} {
					get => this.GetEnvironment<{{{CleanType}}}>(""{{Name}}"") ?? {{DefaultValue}};
					set => this.SetEnvironment(""{{Name}}"", value);
				}
";
			var interfacePropertySetOnlyEnvironmentMustache = @"
				{{{Type}}} {{FullName}} {
					set => this.SetEnvironment(""{{Name}}"", value);
				}
";

			var interfacePropertyGetOnlyEnvironmentMustache = @"
				{{{Type}}} {{FullName}} => this.GetEnvironment<{{{CleanType}}}>(""{{Name}}"") ?? {{DefaultValue}};
";

			var interfacePropertyMustache = @"
				{{{Type}}} {{FullName}} {
					get => {{Name}}?.CurrentValue ?? {{DefaultValue}};
					set => {{Name}}?.Set(value);
				}
";

			var interfacePropertyGetOnlyMustache = @"
				{{{Type}}} {{FullName}} => {{Name}}?.CurrentValue ?? {{DefaultValue}};
";


			var interfacePropertySetOnlyMustache = @"
				{{{Type}}} {{FullName}} {
					set => {{Name}}?.Set(value);
				}
";
			var interfacePropertyMethodEnvironmentMustache = @"

				void {{FullName}} ({{{ActionsParameters}}}) => this.GetEnvironment<{{{CleanType}}}>(""{{Name}}"")?.Invoke({{{ActionsInvokeParameters}}});
";

			var interfacePropertyMethodMustache = @"
				void {{FullName}} ({{{ActionsParameters}}}) => {{Name}}?.Invoke({{{ActionsInvokeParameters}}});
";

			interfacePropertyDictionary = new Dictionary<(bool HasGet, bool HasSet), (string FromEnvironment, string FromProperty)>
			{
				[(true, true)] = (interfacePropertyEnvironmentMustache, interfacePropertyMustache),
				[(true, false)] = (interfacePropertyGetOnlyEnvironmentMustache, interfacePropertyGetOnlyMustache),
				[(false, true)] = (interfacePropertySetOnlyEnvironmentMustache, interfacePropertySetOnlyMustache),
				[(false, false)] = (interfacePropertyMethodEnvironmentMustache, interfacePropertyMethodMustache),
			};
		}

		Stubble.Core.StubbleVisitorRenderer stubble = new StubbleBuilder().Build();
		public void Execute(GeneratorExecutionContext context)
		{

			var rx = (SyntaxReceiver)context.SyntaxContextReceiver!;

			foreach (var item in rx.TemplateInfo)
			{
				var input = GetModelData(item.name, item.interfaceType, item.keyProperties, item.nameSpace, item.baseClass, item.propertyNameTransforms, item.propertyDefaultValues, item.skippedProperties);
				var classSource = stubble.Render(classMustacheTemplate, input);

				context.AddSource($"{item.name}.g.cs", classSource);

				var extensionSource = stubble.Render(extensionMustacheTemplate, input);
				context.AddSource($"{item.name}Extension.g.cs", extensionSource);

				var factorySource = stubble.Render(factoryMustacheTemplate, input);
				context.AddSource($"{item.name}Factory.g.cs", factorySource);
			}
		}
		public static string GetFullName(ISymbol symbol, string ending = null)
		{
			string symbolName = symbol.Name;
			// Handle generic types (e.g. Nullable<DateTime> → System.Nullable<System.DateTime>)
			if (symbol is INamedTypeSymbol nts && nts.TypeArguments.Length > 0)
			{
				var typeArgs = string.Join(", ", nts.TypeArguments.Select(ta => GetFullName(ta)));
				symbolName = $"{symbol.Name}<{typeArgs}>";
			}
			var name = Combine(symbolName, ending);
			if (symbol.ContainingNamespace != null)
				return GetFullName(symbol.ContainingNamespace, name);

			return name;
		}
		static string Combine(string first, string second)
		{
			if (string.IsNullOrWhiteSpace(second))
				return first;
			if (string.IsNullOrWhiteSpace(first))
				return second;
			return $"{first}.{second}";
		}

		dynamic GetModelData(string name, INamedTypeSymbol interfaceType, List<string> keyProperties, string nameSpace, INamedTypeSymbol baseClass, Dictionary<string, string> propertyNameTransforms, Dictionary<string, string> propertyDefaultValues, List<string> skippedProperties)
		{
			var interfaces = interfaceType.AllInterfaces.ToList();
			interfaces.Insert(0, interfaceType);
			var alreadyImplemented = baseClass.AllInterfaces;
			interfaces.RemoveAll(x => alreadyImplemented.Contains(x));
			List<(string Type, string CleanType, string Name, string FullName, bool ShouldBeExtension, bool Skip, List<(string Type, string Name)> Parameters)> properties = new();
			Dictionary<string, string> constructorTypes = new ();
			Dictionary<string, bool> constructorIsValueType = new ();
			List<string> propertiesWithSetters = new();
			List<string> propertiesWithGetters = new();
			Dictionary<string, bool> quoteDefaultData = new();
			Dictionary<string, bool> processedProperty = new();
			foreach (var i in interfaces)
			{
				if(i.Name == "ITextStyle")
				{
					//If its an ITextStyle, we need to convert it into a Font OBject
					propertyDefaultValues ??= new ();
					skippedProperties ??= new ();
					propertyDefaultValues["Font"] = "this.GetFont(null)";
					skippedProperties.Add("Font");
				}
				var members = i.GetMembers();
				foreach (var m in members)
				{
					var fullName = $"{GetFullName(i)}.{m.Name}";
					if (m.Name.StartsWith("get_"))
					{
						fullName = fullName.Replace("get_", "");
						propertiesWithGetters.Add(fullName);
						continue;
					}
					else if (m.Name.StartsWith("set_"))
					{
						fullName = fullName.Replace("set_", "");
						propertiesWithSetters.Add(fullName);
						continue;
					}


					string type = null;
					bool canBeNull = true;
					List<(string Type, string Name)> parameters = new List<(string Type, string Name)>();
					if (m is IPropertySymbol pi)
					{
						//cleanType = GetFullName(pi.Type.WithNullableAnnotation(pi.Type.NullableAnnotation));
						canBeNull = !pi.Type.IsValueType;
						type = GetFullName(pi.Type);
						if (pi.Type.Name == "String")
							quoteDefaultData[m.Name] = true;
					} else if(m is IMethodSymbol mi)
					{
						parameters = mi.Parameters.Select(x => (GetFullName(x.Type), x.Name)).ToList();
						var parameterTypeString = string.Join(", ", parameters.Select(x => $"{x.Type} {x.Name}"));
						var parameterTypeOnlyString = string.Join(", ", parameters.Select(x =>x.Type));
						var paramString = parameters.Count > 1 ? $"({parameterTypeString})" : parameterTypeOnlyString;

						if (mi.ReturnsVoid)
						{
							if (mi.Parameters.Any())
							{
								type = $"System.Action<{paramString}>";
							}
						}
						else
						{
							if (mi.Parameters.Any()) {
								type = $"System.Func<{paramString},{GetFullName(mi.ReturnType)}>";
							}
							else
							{
								type = $"System.Func<{GetFullName(mi.ReturnType)}>";
							}

						}

					}

					var cleanType = canBeNull ? type : $"{type}?";
					if (keyProperties.Contains(m.Name))
					{
						constructorTypes[m.Name] = type;
						constructorIsValueType[m.Name] = !canBeNull;
						var t = (type, cleanType, m.Name, $"{fullName}", false, skippedProperties.Contains(m.Name), parameters);
						if (!properties.Contains(t))
							properties.Add(t);
					}
					else
					{
						var t = (type, cleanType, m.Name, $"{fullName}", true, skippedProperties.Contains(m.Name), parameters);
						if (!properties.Contains(t))
							properties.Add(t);

					}
				}


			}
			List<(string Type, string Name, string defaultValueString, bool IsValueType)> constructorParameters = new();
			for (var i = 0; i < keyProperties.Count; i++)
			{
				var keyName = keyProperties[i];
				var value = constructorTypes[keyName];
				var defaultValue = i == 0 ? "" : " = null";
				constructorIsValueType.TryGetValue(keyName, out var isValueType);
				constructorParameters.Add((value, keyName, defaultValue, isValueType));
			}

			string getPropertyDefaultValue(string key)
			{
				if (!propertyDefaultValues.TryGetValue(key, out var defaultValue))
					return "default";
				var value = quoteDefaultData.TryGetValue(key, out var shouldQuote) && shouldQuote && defaultValue != "null" ? $"\"{defaultValue}\"" : defaultValue;
				return $"{defaultValue}";
			};
			string getNewName(string key)
			{
				if (propertyNameTransforms.TryGetValue(key, out var defaultValue))
					return defaultValue;
				return key;
			};

			var input = new {
				ClassName = name,
				BaseClassName = $"{baseClass} , {string.Join(",", interfaces.Select(x => GetFullName(x)))}",
				NameSpace = nameSpace,
				Parameters = constructorParameters.Select(x => {
					var type = x.Type ?? typeof(Action).FullName;
					var isDelegate = type.StartsWith("System.Action") || type.StartsWith("System.Func");
					var isNullableValueType = type.StartsWith("System.Nullable<");
					var needsNullableValueType = x.IsValueType && x.defaultValueString == " = null" && !isNullableValueType;
					var valueType = needsNullableValueType ? $"{type}?" : type;
					var allowsNull = !x.IsValueType || needsNullableValueType || isNullableValueType;
					var name = getNewName(x.Name);
					var lowercaseName = name.LowercaseFirst();
					var valueBindingValue = needsNullableValueType ? $"{lowercaseName}.Value" : lowercaseName;
					return new {
						Type = type,
						Name = name,
						LowercaseName = lowercaseName,
						DefaultValueString = x.defaultValueString,
						SignalType = isDelegate ? type : $"Signal<{type}>",
						FuncType = isDelegate ? type : $"Func<{type}>",
						ComputedType = isDelegate ? type : $"Computed<{type}>",
						ValueType = valueType,
						FieldDeclaration = isDelegate
							? $"{type} {lowercaseName};"
							: $"PropertySubscription<{type}> {lowercaseName};",
						PropertyDeclaration = isDelegate
							? $"public {type} {name}\n\t\t{{\n\t\t\tget => {lowercaseName};\n\t\t\tprivate set => {lowercaseName} = value;\n\t\t}}"
							: $"public PropertySubscription<{type}> {name}\n\t\t{{\n\t\t\tget => {lowercaseName};\n\t\t\tprivate set => this.SetPropertySubscription(ref  this.{lowercaseName}, value);\n\t\t}}",
						SignalAssignment = isDelegate
							? $"{name} = {lowercaseName};"
							: $"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromSignal({lowercaseName});",
						FuncAssignment = isDelegate
							? $"{name} = {lowercaseName};"
							: $"{name} = {lowercaseName} == null ? null : PropertySubscription<{type}>.FromFunc({lowercaseName});",
						ComputedAssignment = isDelegate
							? $"{name} = {lowercaseName};"
							: $"{name} = {lowercaseName} == null ? null : new PropertySubscription<{type}>(() => {lowercaseName}.Value);",
						ValueAssignment = isDelegate
							? $"{name} = {lowercaseName};"
							: allowsNull
								? $"{name} = {lowercaseName} == null ? null : new PropertySubscription<{type}>({valueBindingValue});"
								: $"{name} = new PropertySubscription<{type}>({lowercaseName});"
					};
				}).ToList(),
				HasParameters = constructorParameters.Where(x => x.Type != "System.Action").Any(),
				Properties = properties.Select(x => new {
					Type = string.IsNullOrWhiteSpace(x.Type) ? typeof(Action).FullName : x.Type,
					CleanType = string.IsNullOrWhiteSpace(x.Type) ? typeof(Action).FullName : x.CleanType,
					Name = getNewName(x.Name),
					x.FullName,
					HasSet = propertiesWithSetters.Contains(x.FullName),
					HasGet = propertiesWithGetters.Contains(x.FullName),
					x.ShouldBeExtension,
					ClassName = name,
					DefaultValue = getPropertyDefaultValue(x.Name),
					LowercaseName = x.Name.LowercaseFirst(),
					Parameter = x.Parameters,					
					ActionsParameters = string.Join(", ", ((List<(string Type, string Name)>)x.Parameters).Select(x => $"{x.Type} {x.Name}")),
					ActionsInvokeParameters = x.Parameters.Any() ? x.Parameters.Count == 1 ? x.Parameters[0].Name : "(" + string.Join(", ", ((List<(string Type, string Name)>)x.Parameters).Select(x => x.Name)) + ")" : "",
					x.Skip
				}).ToList(),
				ParametersFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					x.Type,
					x.Name,
					x.LowercaseName,
					x.DefaultValueString
				}).Replace("Binding<System.Action>", "System.Action")))),

				SignalParametersFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					Type = x.SignalType,
					x.Name,
					x.LowercaseName,
					x.DefaultValueString
				})))),

				FuncParametersFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					Type = x.FuncType,
					x.Name,
					x.LowercaseName,
					x.DefaultValueString
				})))),

				ComputedParametersFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					Type = x.ComputedType,
					x.Name,
					x.LowercaseName,
					x.DefaultValueString
				})))),

				ValueParametersFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					Type = x.ValueType,
					x.Name,
					x.LowercaseName,
					x.DefaultValueString
				})))),

				ParameterNamesFunction = new Func<dynamic, string, object>((dyn, str) => string.Join(",", ((IEnumerable<dynamic>)dyn.Parameters).Select(x => stubble.Render(str, new {
					x.LowercaseName
				})))),

				SignalConstructorFunction = new Func<dynamic, string, object>((dyn, str) =>
					dyn.HasParameters ? stubble.Render(str, dyn) : ""),

				FuncConstructorFunction = new Func<dynamic, string, object>((dyn, str) =>
					dyn.HasParameters ? stubble.Render(str, dyn) : ""),

				ComputedConstructorFunction = new Func<dynamic, string, object>((dyn, str) =>
					dyn.HasParameters ? stubble.Render(str, dyn) : ""),

				ValueConstructorFunction = new Func<dynamic, string, object>((dyn, str) =>
					dyn.HasParameters ? stubble.Render(str, dyn) : ""),
				PropertiesFunc = new Func<dynamic, string, object>((dyn, str) => {
					var templateGroup = interfacePropertyDictionary[(dyn.HasGet, dyn.HasSet)];
					var template = dyn.ShouldBeExtension ? templateGroup.FromEnvironment : templateGroup.FromProperty;
					return stubble.Render(template, dyn);
				}),

				ExtensionPropertiesFunc = new Func<dynamic, string, object>((dyn, str) => {
					if (!dyn.ShouldBeExtension || dyn.Skip) return "";
					string typeStr = dyn.Type;
					var result = stubble.Render(typeStr.StartsWith("System.Action") ? extensionActionProperty : extensionProperty, dyn);
					// Phase 2.2: Add "On" prefixed alias for Action-type extension properties
					if (typeStr.StartsWith("System.Action"))
						result += stubble.Render(onPrefixedExtensionActionProperty, dyn);
					// View-aware Token<T> overloads for scoped theme resolution (spec §8.8, D6)
					if (!typeStr.StartsWith("System.Action") && !typeStr.StartsWith("System.Func"))
						result += stubble.Render(tokenExtensionProperty, dyn);
					return result;
				}),
				HasRenamedProperties = propertyNameTransforms.Any(),
				RenamedProperties = propertyNameTransforms.Select(x => new {
					OldName = x.Key,
					NewName = x.Value
				}).ToList(),

				// Phase 2.3: Style builder properties (non-Action, non-Skip extension properties)
				StyleProperties = properties
					.Where(x => x.ShouldBeExtension && !x.Skip)
					.Where(x => !string.IsNullOrWhiteSpace(x.Type))
					.Where(x => !x.Type.StartsWith("System.Action") && !x.Type.StartsWith("System.Func"))
					.Where(x => {
						var n = getNewName(x.Name);
						return n != "Background" && n != "Color" && n != "TextColor";
					})
					.Select(x => new {
						Type = x.Type,
						Name = getNewName(x.Name),
						x.FullName,
						ClassName = name,
						LowercaseName = getNewName(x.Name).LowercaseFirst(),
					}).ToList(),
				StylePropertyFunc = new Func<dynamic, string, object>((dyn, str) =>
					stubble.Render(styleBuilderPropertyMustache, dyn)),

			};
			return input;
		}

		public void Initialize(GeneratorInitializationContext context)
		{
			//if (!Debugger.IsAttached)
			//{
			//	Debugger.Launch();
			//}

			context.RegisterForPostInitialization((pi) => pi.AddSource("CometGenerationAttribute__", attributeSource));
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}
		static INamedTypeSymbol cometViewSymbol;
		class SyntaxReceiver : ISyntaxContextReceiver
		{
			public List<(string name, INamedTypeSymbol interfaceType, List<string> keyProperties, string nameSpace, INamedTypeSymbol baseClass, Dictionary<string, string> propertyNameTransforms, Dictionary<string, string> propertyDefaultValues, List<string> skippedProperties)> TemplateInfo = new();

			public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
			{
				var cometView = context.SemanticModel.Compilation.GetTypeByMetadataName("Comet.View");
				cometViewSymbol ??= cometView;
				var attrib = context.Node as AttributeSyntax;
				if (attrib != null)
				{
					var type = context.SemanticModel.GetTypeInfo(attrib);
					if (context.SemanticModel.GetTypeInfo(attrib).Type?.ToDisplayString() == "CometGenerateAttribute")
					{
						{

							var f = attrib.ArgumentList.Arguments[0].Expression as TypeOfExpressionSyntax;
							var realClass = GetType(context, f);

							var keyProperties = new List<string>();
							string name = null;
							string nameSpace = null;
							INamedTypeSymbol baseClass = cometView;
							List<string> defaultValues = new();
							List<string> skippedProperties = new();

							foreach (var arg in attrib.ArgumentList.Arguments.Skip(1))
							{
								var constVal = context.SemanticModel.GetConstantValue(arg.Expression);
								var symbol = context.SemanticModel.GetSymbolInfo(arg.Expression);
								var argName = arg.NameEquals?.Name.Identifier.ValueText;
								if (argName == "ClassName")
								{
									name = constVal.ToString();
									continue;
								}

								if (argName == "Namespace")
								{
									nameSpace = constVal.ToString();
									continue;
								}
								if (argName == "DefaultValues")
								{
									var iac = (arg.Expression as ImplicitArrayCreationExpressionSyntax).Initializer.Expressions;
									foreach (var i in iac)
									{
										var fif = context.SemanticModel.GetConstantValue(i);
										defaultValues.Add(fif.ToString());
									}
								}
								if (argName == "Skip")
								{
									var iac = (arg.Expression as ImplicitArrayCreationExpressionSyntax).Initializer.Expressions;
									foreach (var i in iac)
									{
										var fif = context.SemanticModel.GetConstantValue(i);
										skippedProperties.Add(fif.ToString());
									}
								}
								//Strings which can only be key properties
								if (arg.Expression is LiteralExpressionSyntax || (arg.Expression is InvocationExpressionSyntax && constVal.HasValue) || arg.Expression is InterpolatedStringExpressionSyntax)
								{
									keyProperties.Add(constVal.ToString());
									continue;
								}


								if (arg.Expression is TypeOfExpressionSyntax toe)
								{
									//this is our base class!
									baseClass = GetType(context, toe);
									continue;
								}

								var exp = arg.Expression;
							}
							name ??= realClass.Name?.TrimStart('I');
							nameSpace ??= GetFullName(realClass.ContainingNamespace);

							Dictionary<string, string> propertyTransform = new();
							Dictionary<string, string> propertyDefaultValues = new();
							(bool hasParts, string key) getParts(string oldKey)
							{
								var hasName = oldKey.Contains(':');
								var hasValue = oldKey.Contains('=');

								if (!hasName && !hasValue)
								{
									return (false, null);
								}
								var parts = oldKey.Split(':', '=');
								var key = parts[0].Trim();
								var newName = hasName ? parts[1].Trim() : null;
								var defaultValue = hasValue ? parts.Last().Trim() : null;


								if (newName != null)
									propertyTransform[key] = newName;

								if (defaultValue != null)
									propertyDefaultValues[key] = defaultValue;
								return (true, key);
							}
							foreach (var oldKey in keyProperties.ToList())
							{
								(bool hasParts, string key) = getParts(oldKey);
								if (!hasParts)
								{
									continue;
								}
								var oldIndex = keyProperties.IndexOf(oldKey);
								keyProperties.RemoveAt(oldIndex);
								keyProperties.Insert(oldIndex, key);
							}


							foreach (var oldKey in defaultValues)
							{
								(bool hasParts, string key) = getParts(oldKey);
							}
							foreach (var oldKey in skippedProperties.ToList())
							{
								(bool hasParts, string key) = getParts(oldKey);
								if (!hasParts)
								{
									continue;
								}
								skippedProperties.Remove(oldKey);
								skippedProperties.Add(key);
							}


							//string name = context.SemanticModel.GetTypeInfo(attrib.ArgumentList.Arguments[0].Expression).ToString();
							//string template = context.SemanticModel.GetConstantValue(attrib.ArgumentList.Arguments[1].Expression).ToString();
							//string hash = context.SemanticModel.GetConstantValue(attrib.ArgumentList.Arguments[2].Expression).ToString();

							TemplateInfo.Add((name, realClass, keyProperties, nameSpace, baseClass, propertyTransform, propertyDefaultValues, skippedProperties));
						}
					}
				}
			}

			static INamedTypeSymbol GetType(GeneratorSyntaxContext context, TypeOfExpressionSyntax expression)
			{
				var interfaceType = context.SemanticModel.GetSymbolInfo(expression.Type);
				var symbol = interfaceType.Symbol;
				if (symbol == null && interfaceType.CandidateSymbols.Length > 1)
				{
					symbol = interfaceType.CandidateSymbols.OfType<INamedTypeSymbol>().Where(x=> IsSublcassOfCometView(x)).FirstOrDefault();
				}
				var s = GetFullName(symbol);
				return context.SemanticModel.Compilation.GetTypeByMetadataName(s);
			}
			static bool IsSublcassOfCometView(INamedTypeSymbol symbol)
			{
				if (symbol.BaseType == null)
					return false;
				if (symbol.BaseType == cometViewSymbol)
					return true;

				return IsSublcassOfCometView(symbol.BaseType);
			}

		}
	}
}
