using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Stubble.Core.Builders;

namespace Comet.SourceGenerator
{
	[Generator]
	public class StateWrapperGenerator : IIncrementalGenerator
	{
		private const string attributeSource = @"
  	[System.AttributeUsage(System.AttributeTargets.Assembly | System.AttributeTargets.Class, AllowMultiple = true)]
	public class GenerateStateClassAttribute : System.Attribute
	{
		public GenerateStateClassAttribute(){}
		public GenerateStateClassAttribute(System.Type classType) => ClassType = classType;
		public string ClassName { get; set; }
		public System.Type ClassType { get; }
		public string Namespace { get; set; }
	}
";

		const string classMustacheTemplate = @"
using System;
using Comet;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace {{NameSpace}} {
	public partial class {{ClassName}} :INotifyPropertyRead, IAutoImplemented 
	{
		public event PropertyChangedEventHandler PropertyRead;
		public event PropertyChangedEventHandler PropertyChanged;

		public readonly {{ClassType}} OriginalModel;

		bool shouldNotifyChanged = true;

		public {{ClassName}} ({{ClassType}} model)
		{
			OriginalModel = model;
			InitStateProperties();
			if (model is INotifyPropertyChanged inpc)
			{
				inpc.PropertyChanged += Inpc_PropertyChanged;
				shouldNotifyChanged = false;
			}
		}

		void Inpc_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			PropertyChanged?.Invoke(sender, e);
		}
	
		void NotifyPropertyChanged(object value, [CallerMemberName] string memberName = null){
			if (shouldNotifyChanged) {
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(memberName));
			}
		}
		
		void NotifyPropertyRead([CallerMemberName] string memberName = null){ 
			InitDirtyProperty(memberName);
			PropertyRead?.Invoke(this, new PropertyChangedEventArgs(memberName));
		}
		
		/// <summary>
		/// Notifies Comet of all changes in the underlying model (OriginalModel) observed properties 
		/// </summary>
		public void NotifyChanged(){
				{{#Properties}}
				{{#PropertiesUpdateAllFunc}}
				{{/PropertiesUpdateAllFunc}}
				{{/Properties}}
		}

		void InitStateProperties(){
			{{#Properties}}
			{{#PropertiesInitStateFunc}}
			{{/PropertiesInitStateFunc}}
			{{/Properties}}
		}

		void InitDirtyProperty([CallerMemberName] string memberName = null){
			switch (memberName){
				{{#Properties}}
				{{#PropertiesInitFunc}}
				{{/PropertiesInitFunc}}
				{{/Properties}}
			}
		}

		void UpdateDirtyProperty([CallerMemberName] string memberName = null){
			switch (memberName){
				{{#Properties}}
				{{#PropertiesUpdateFunc}}
				{{/PropertiesUpdateFunc}}
				{{/Properties}}
			}
		}
		{{#Properties}}
		{{#PropertiesFunc}}
		{{/PropertiesFunc}}
		{{/Properties}}
	}
}
";

		static StateWrapperGenerator()
		{

			var interfacePropSupportMustache = @"
		{{^HasState}}
		bool {{LName}}IsObserved = false;
		bool {{LName}}IsDirty => {{LName}}IsObserved && !OriginalModel.{{Name}}.Equals({{LName}}LastValue);
		{{{Type}}} {{LName}}LastValue;
		{{/HasState}}
";

			var interfacePropertyMustache = interfacePropSupportMustache +
@"		public {{{Type}}} {{Name}} {
			{{#HasState}}
			get;
			{{/HasState}}
			{{^HasState}}
			get {
				NotifyPropertyRead();
				return OriginalModel.{{Name}};
			}
			{{/HasState}}
			{{#HasState}}
			private set;
			{{/HasState}}
			{{^HasState}}
			set {
				OriginalModel.{{Name}} = value;
				{{LName}}LastValue = value;
				NotifyPropertyChanged(value);
			}
			{{/HasState}}
		}
";
			var interfacePropertySetOnlyMustache = interfacePropSupportMustache +
@"		public {{{Type}}} {{Name}} {
			set {
				OriginalModel.{{Name}} = value;
				{{LName}}LastValue = value;
				NotifyPropertyChanged(value);
			}
		}
";

			var interfacePropertyGetOnlyMustache = interfacePropSupportMustache +
@"		public {{{Type}}} {{Name}} {
			get {
				NotifyPropertyRead();
				return OriginalModel.{{Name}};
			}
		}
";
			interfacePropertyDictionary = new()
			{
				[(true, true)] = interfacePropertyMustache,
				[(true, false)] = interfacePropertyGetOnlyMustache,
				[(false, true)] = interfacePropertySetOnlyMustache,

			};
		}

		static readonly Stubble.Core.StubbleVisitorRenderer stubble = new StubbleBuilder().Build();
		static Dictionary<(bool HasGet, bool HasSet), string> interfacePropertyDictionary;

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput((pi) => pi.AddSource("GenerateStateClassAttribute__", attributeSource));

			var candidates = context.SyntaxProvider.CreateSyntaxProvider(
					predicate: static (node, _) => IsCandidateNode(node),
					transform: static (ctx, _) => GetCandidateType(ctx))
				.Where(static t => t != null)
				.Collect();

			var compilationAndCandidates = context.CompilationProvider.Combine(candidates);

			context.RegisterSourceOutput(compilationAndCandidates, static (spc, source) => Execute(source.Left, source.Right, spc));
		}

		static bool IsCandidateNode(Microsoft.CodeAnalysis.SyntaxNode node)
		{
			if (node is AttributeSyntax attrib)
				return IsGenerateStateClassName(attrib);
			if (node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0)
				return cds.AttributeLists.SelectMany(al => al.Attributes).Any(IsGenerateStateClassName);
			return false;
		}

		static bool IsGenerateStateClassName(AttributeSyntax attrib)
		{
			var name = attrib.Name;
			while (name is QualifiedNameSyntax qn)
				name = qn.Right;
			var id = (name as SimpleNameSyntax)?.Identifier.ValueText;
			return id == "GenerateStateClass" || id == "GenerateStateClassAttribute";
		}

		static INamedTypeSymbol GetCandidateType(GeneratorSyntaxContext context)
		{
			if (context.Node is ClassDeclarationSyntax cds && context.SemanticModel.GetDeclaredSymbol(cds).GetAttributes().Any(x => x.AttributeClass.Name == "GenerateStateClassAttribute"))
			{
				return context.SemanticModel.GetDeclaredSymbol(cds).OriginalDefinition as INamedTypeSymbol;
			}

			if (context.Node is AttributeSyntax attrib)
			{
				if (context.SemanticModel.GetTypeInfo(attrib).Type?.ToDisplayString() == "GenerateStateClassAttribute")
				{
					if (attrib.ArgumentList == null)
						return null;
					var f = attrib.ArgumentList.Arguments[0].Expression as TypeOfExpressionSyntax;
					return GetType(context.SemanticModel, f);
				}
			}
			return null;
		}

		static void Execute(Compilation compilation, ImmutableArray<INamedTypeSymbol> candidates, SourceProductionContext context)
		{
			if (candidates.IsDefaultOrEmpty)
				return;

			var rx = new TemplateCollector();
			foreach (var candidate in candidates)
				rx.AddTemplate(candidate);

			var st = compilation.SyntaxTrees;

			void addChildStateClasses(IEnumerable<INamedTypeSymbol> match)
			{
				var matchWith = match.ToList();
				matchWith.AddRange(match.SelectMany(m => GetAllBaseTypes(m)));
				//var parentStateClassesProps =

				//select all the class SyntaxNodes for match classes
				var classes = st.SelectMany(st => st.GetRoot().DescendantNodes()).Where(
					sn => {
						var select = false;
						if (sn is ClassDeclarationSyntax cds)
						{
							var sm = compilation.GetSemanticModel(cds.SyntaxTree);
							var ts = sm.GetDeclaredSymbol(cds).OriginalDefinition as INamedTypeSymbol;
							select = matchWith.Any(c => c == ts || c.OriginalDefinition == ts);
						}
						return select;
					});

				//select all property SyntaxNodes for matched state classes that return a user class to be wrapped
				var props = classes.SelectMany(c => c.ChildNodes().Where(cn => {
					var select = false;
					if (cn is PropertyDeclarationSyntax pds)
					{
						var sm = compilation.GetSemanticModel(pds.SyntaxTree);
						var realClass = StateWrapperGenerator.GetType(sm, pds.Type);
						select = !(pds.Type is NullableTypeSyntax) && IsUserClass(realClass);
					}
					return select;
				}));

				var parentStateClassesProps = props.Select(pds => {
					var p = (PropertyDeclarationSyntax)pds;
					var sm = compilation.GetSemanticModel(p.SyntaxTree);
					var realClass = StateWrapperGenerator.GetType(sm, p.Type);
					return realClass;
				}).ToList();

				parentStateClassesProps.ForEach(itns => rx.AddTemplate(itns));

				if (parentStateClassesProps.Any())
				{
					addChildStateClasses(parentStateClassesProps);
				}
			}

			var match = rx.TemplateInfo.Select(x => x.classType);
			addChildStateClasses(match);
			var templates = rx.TemplateInfo.ToList();
			foreach (var item in templates)
			{
				var input = GetModelData(item.name, item.classType, item.nameSpace, rx);
				var classSource = stubble.Render(classMustacheTemplate, input);
				context.AddSource($"{item.name}.g.cs", classSource);
			}
		}

		static bool IsUserClass(ITypeSymbol realClass) => realClass?.TypeKind == TypeKind.Class && realClass.SpecialType == SpecialType.None && !realClass.ToString().StartsWith("System"); //&& realClass.isnu;

		static IEnumerable<INamedTypeSymbol> GetAllBaseTypes(INamedTypeSymbol classType)
		{
			yield return classType;
			if (classType.BaseType != null)
			{
				foreach (var baseType in GetAllBaseTypes(classType.BaseType))
					yield return baseType;
			}
		}

		static dynamic GetModelData(string className, INamedTypeSymbol classType, string nameSpace, TemplateCollector sr)
		{
			var baseClasses = GetAllBaseTypes(classType).ToList();
			List<(string Type, string Name)> properties = new();
			List<(string Type, string Name)> methods = new();

			List<string> propertiesWithSetters = new();
			List<string> propertiesWithGetters = new();
			List<string> propertiesWithState = new();

			foreach (var i in baseClasses)
			{
				var members = i.GetMembers();
				foreach (var m in members)
				{
					if (m is IPropertySymbol pi && pi.DeclaredAccessibility == Accessibility.Public)
					{
						string type = null;
						string name = pi.Name;
						bool isUserClass = IsUserClass(pi.Type);

						bool isPublicSet = pi.OriginalDefinition.SetMethod?.DeclaredAccessibility == Accessibility.Public;
						bool isAccessibleSet = isPublicSet && (!pi.IsReadOnly && !pi.OriginalDefinition.SetMethod.IsReadOnly && !pi.OriginalDefinition.SetMethod.IsInitOnly);

						propertiesWithGetters.Add(name);
						if (isAccessibleSet)
							propertiesWithSetters.Add(name);

						if (isUserClass)
						{
							var theType = CometViewSourceGenerator.GetFullName(pi.Type);
							theType += "State";
							var exists = sr.TemplateInfo.Any(i => $"{i.nameSpace}.{i.name}" == theType);
							if (exists)
							{
								propertiesWithState.Add(name);
								type = theType;
							}
						}


						type ??= pi.Type.ToString();

						var t = (type, name);
						if (!properties.Contains(t))
							properties.Add(t);
					}
				}


			}

			var interfacePropInitStateMustache =
@"				{{Name}} = new {{Type}}(OriginalModel.{{Name}});
";

			var interfacePropInitMustache =
@"				case ""{{Name}}"":
					if (!{{LName}}IsObserved){
						{{LName}}LastValue = OriginalModel.{{Name}};
						{{LName}}IsObserved = true;
					}
				break;
";

			var interfacePropUpdateMustache =
@"				case ""{{Name}}"":
					{{#HasState}}
					{{Name}}.NotifyChanged();
					{{/HasState}}
					{{^HasState}}
					if ({{LName}}IsDirty){
						{{LName}}LastValue = OriginalModel.{{Name}};
						NotifyPropertyChanged({{LName}}LastValue, memberName);
					}
					{{/HasState}}
				break;
";

			var interfacePropUpdateAllMustache =
@"				{{#HasState}}
				UpdateDirtyProperty(""{{Name}}"");
				{{/HasState}}
				{{^HasState}}
				if (shouldNotifyChanged && {{LName}}IsObserved) UpdateDirtyProperty(""{{Name}}"");
				{{/HasState}}
";

			var input = new {
				ClassName = className,
				ClassType = CometViewSourceGenerator.GetFullName(classType),
				NameSpace = nameSpace,
				Properties = properties.Select(x => new {
					Type = x.Type,
					Name = x.Name,
					LName = x.Name.LowercaseFirst(),
					HasSet = propertiesWithSetters.Contains(x.Name),
					HasGet = propertiesWithGetters.Contains(x.Name),
					HasState = propertiesWithState.Contains(x.Name)
				}).Where(p => !p.Name.StartsWith("this")).ToList(),
				PropertiesFunc = new Func<dynamic, string, object>((dyn, str) => {
					var template = (dyn.HasGet || dyn.HasSet || dyn.HasState) ? interfacePropertyDictionary[(dyn.HasGet, (dyn.HasSet || dyn.HasState))] : "";
					return stubble.Render(template, dyn);
				}),
				PropertiesUpdateFunc = new Func<dynamic, string, object>((dyn, str) => {
					var template = dyn.HasGet ? interfacePropUpdateMustache : "";
					return stubble.Render(template, dyn);
				}),
				PropertiesUpdateAllFunc = new Func<dynamic, string, object>((dyn, str) => {
					var template = dyn.HasGet ? interfacePropUpdateAllMustache : "";
					return stubble.Render(template, dyn);
				}),
				PropertiesInitFunc = new Func<dynamic, string, object>((dyn, str) => {
					var template = dyn.HasGet && !dyn.HasState ? interfacePropInitMustache : "";
					return stubble.Render(template, dyn);
				}),
				PropertiesInitStateFunc = new Func<dynamic, string, object>((dyn, str) => {
					var template = dyn.HasState ? interfacePropInitStateMustache : "";
					return stubble.Render(template, dyn);
				})
			};
			return input;
		}

		class TemplateCollector
		{
			public List<(string name, INamedTypeSymbol classType, string nameSpace)> TemplateInfo = new();

			public void AddTemplate(INamedTypeSymbol classType)
			{
				string name = $"{classType.Name}State";
				string nameSpace = CometViewSourceGenerator.GetFullName(classType.ContainingNamespace);
				if (!TemplateInfo.Exists(t => t.name == name && t.nameSpace == nameSpace))
				{
					TemplateInfo.Add((name, classType, nameSpace));
				}
			}
		}

		static INamedTypeSymbol GetType(SemanticModel sm, TypeOfExpressionSyntax expression)
		{
			return GetType(sm, expression.Type);
		}

		static INamedTypeSymbol GetType(SemanticModel sm, TypeSyntax type)
		{
			SymbolInfo? interfaceType = sm.GetSymbolInfo(type);
			var s = CometViewSourceGenerator.GetFullName(interfaceType?.Symbol);
			return sm.Compilation.GetTypeByMetadataName(s);
		}

	}
}
