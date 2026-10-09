using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;


namespace Comet.SourceGenerator
{
	[Generator]
	public class AutoNotifyGenerator : IIncrementalGenerator
	{
		private const string attributeText = @"
using System;
namespace Comet
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class AutoNotifyAttribute : Attribute
    {
        public AutoNotifyAttribute()
        {
        }
        public string PropertyName { get; set; }
    }
}
";

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			context.RegisterPostInitializationOutput((i) => i.AddSource("AutoNotifyAttribute", attributeText));

			var fields = context.SyntaxProvider.ForAttributeWithMetadataName(
					"Comet.AutoNotifyAttribute",
					predicate: static (node, _) => node is VariableDeclaratorSyntax,
					transform: static (ctx, _) => ctx.TargetSymbol as IFieldSymbol)
				.Where(static f => f != null)
				.Collect();

			var compilationAndFields = context.CompilationProvider.Combine(fields);

			context.RegisterSourceOutput(compilationAndFields, static (spc, source) => Execute(source.Left, source.Right, spc));
		}

		static void Execute(Compilation compilation, ImmutableArray<IFieldSymbol> fields, SourceProductionContext context)
		{
			if (fields.IsDefaultOrEmpty)
				return;

			var attributeSymbol = compilation.GetTypeByMetadataName("Comet.AutoNotifyAttribute");
			var notifySymbol = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
			var notifyReadSymbol = compilation.GetTypeByMetadataName("Comet.INotifyPropertyRead");
			var autoImplementedSymbol = compilation.GetTypeByMetadataName("Comet.IAutoImplemented");

			// group the fields by class, and generate the source
			foreach (IGrouping<INamedTypeSymbol, IFieldSymbol> group in fields.GroupBy<IFieldSymbol, INamedTypeSymbol>(f => f.ContainingType, SymbolEqualityComparer.Default))
			{
				string classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, notifySymbol, notifyReadSymbol, autoImplementedSymbol, context);
				if(!string.IsNullOrWhiteSpace(classSource))
					context.AddSource($"{group.Key.Name}_autoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
			}
		}

		private static string ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, ISymbol notifySymbol, ISymbol notifyReadSymbol, ISymbol autoImplementedSymbol, SourceProductionContext context)
		{
			if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
			{
				context.ReportDiagnostic(Diagnostic.Create("AutoGen101", "Compiler", message: $"{classSymbol.ToDisplayString()} cannot be a nested class in order to use the [AutoNotify] attribute.", DiagnosticSeverity.Error, defaultSeverity: DiagnosticSeverity.Error,true,0)) ;
				return null; //TODO: issue a diagnostic that it must be top level
			}

			string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

			// begin building the generated source
			StringBuilder source = new StringBuilder($@"
using Comet;
namespace {namespaceName}
{{
    public partial class {classSymbol.Name} : {notifyReadSymbol.ToDisplayString()} , {autoImplementedSymbol.ToDisplayString()}
    {{
");

			// if the class doesn't implement INotifyPropertyChanged already, add it
			if (!classSymbol.Interfaces.Contains(notifySymbol))
			{
				source.Append("public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;");
			}
			// if the class doesn't implement INotifyPropertyChanged already, add it
			if (!classSymbol.Interfaces.Contains(notifyReadSymbol))
			{
				source.Append("public event System.ComponentModel.PropertyChangedEventHandler PropertyRead;");
			}

			// create properties for each field 
			foreach (IFieldSymbol fieldSymbol in fields)
			{
				ProcessField(source, fieldSymbol, attributeSymbol);
			}

			source.Append("} }");
			return source.ToString();
		}

		private static void ProcessField(StringBuilder source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
		{
			// get the name and type of the field
			string fieldName = fieldSymbol.Name;
			ITypeSymbol fieldType = fieldSymbol.Type;

			// get the AutoNotify attribute from the field, and any associated data
			AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
			TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

			string propertyName = chooseName(fieldName, overridenNameOpt);
			if (propertyName.Length == 0 || propertyName == fieldName)
			{
				//TODO: issue a diagnostic that we can't process this field
				return;
			}

			source.Append($@"
public {fieldType} {propertyName} 
{{
    get 
    {{
		this.PropertyRead?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof({propertyName})));
        return this.{fieldName};
    }}
    set
    {{
        this.{fieldName} = value;
        this.PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof({propertyName})));
    }}
}}
");

			string chooseName(string fieldName, TypedConstant overridenNameOpt)
			{
				if (!overridenNameOpt.IsNull)
				{
					return overridenNameOpt.Value.ToString();
				}

				fieldName = fieldName.TrimStart('_');
				if (fieldName.Length == 0)
					return string.Empty;

				if (fieldName.Length == 1)
					return fieldName.ToUpper();

				return fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);
			}

		}

	}
}
