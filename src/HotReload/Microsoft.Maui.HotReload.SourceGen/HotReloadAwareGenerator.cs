#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.Labs.HotReload.SourceGen
{
	/// <summary>
	/// Generates a <c>HotReloadInitialize()</c> method for every <c>partial</c> class that
	/// implements <c>Microsoft.Maui.Labs.HotReload.IHotReloadAware</c>. Also emits an informational
	/// diagnostic if no constructor appears to call it.
	/// </summary>
	[Generator]
	public sealed class HotReloadAwareGenerator : IIncrementalGenerator
	{
		const string IHotReloadAwareFullName = "Microsoft.Maui.Labs.HotReload.IHotReloadAware";

		static readonly DiagnosticDescriptor MissingInitCallDescriptor = new(
			id: "MUH0001",
			title: "HotReloadInitialize not called",
			messageFormat: "'{0}' implements IHotReloadAware but no constructor calls HotReloadInitialize(). Add a call to HotReloadInitialize() in your constructor to enable hot reload notifications.",
			category: "HotReload",
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true,
			description: "Types implementing IHotReloadAware must call the generated HotReloadInitialize() in their constructor for hot reload registration to take effect.");

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var candidates = context.SyntaxProvider
				.CreateSyntaxProvider(
					predicate: IsPartialClassDeclaration,
					transform: GetHotReloadableClassInfo)
				.Where(static info => info is not null)
				.Select(static (info, _) => info!);

			// Deduplicate: a class split across multiple partial declarations will produce multiple
			// candidates with the same HintName. Only the first one should emit source.
			context.RegisterSourceOutput(candidates.Collect(), EmitAllSources);
		}

		static bool IsPartialClassDeclaration(SyntaxNode node, System.Threading.CancellationToken _) =>
			node is ClassDeclarationSyntax c &&
			c.Modifiers.Any(SyntaxKind.PartialKeyword) &&
			c.BaseList is not null;

		static HotReloadableClassInfo? GetHotReloadableClassInfo(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
		{
			var classDecl = (ClassDeclarationSyntax)ctx.Node;
			if (ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct) is not INamedTypeSymbol classSymbol)
				return null;

			// Skip generic types and nested types: the simple partial class codegen doesn't
			// handle type parameters or containing-type wrappers. Users of those shapes must
			// call HotReloadRegistry.Register manually.
			if (classSymbol.IsGenericType || classSymbol.ContainingType != null)
				return null;

			// Check interfaces: prefer semantic model, but fall back to syntax name matching
			// so the generator works even when the referenced assembly isn't fully loaded
			// in the compilation (e.g. net10.0 lib consumed by a net11.0-* project).
			bool implementsIHotReloadAware = false;

			var iface = ctx.SemanticModel.Compilation.GetTypeByMetadataName(IHotReloadAwareFullName);
			if (iface is not null)
			{
				// Semantic path: exact type identity check
				foreach (var i in classSymbol.AllInterfaces)
				{
					if (SymbolEqualityComparer.Default.Equals(i, iface))
					{
						implementsIHotReloadAware = true;
						break;
					}
				}
			}
			else
			{
				// Syntax fallback: match by short or fully-qualified name in the base list
				foreach (var baseType in classDecl.BaseList?.Types ?? default)
				{
					var typeName = baseType.Type switch
					{
						IdentifierNameSyntax id => id.Identifier.Text,
						QualifiedNameSyntax q => q.Right.Identifier.Text,
						_ => null
					};
					if (typeName == "IHotReloadAware")
					{
						implementsIHotReloadAware = true;
						break;
					}
				}
			}

			if (!implementsIHotReloadAware)
				return null;

			// Check whether any constructor in this partial declaration calls HotReloadInitialize.
			// Walk identifier tokens (skipping trivia/comments) so we don't get false positives
			// from documentation or comments mentioning "HotReloadInitialize".
			bool hasInitCall = false;
			foreach (var member in classDecl.Members)
			{
				if (member is not ConstructorDeclarationSyntax ctor)
					continue;

				SyntaxNode? body = (SyntaxNode?)ctor.Body ?? ctor.ExpressionBody;
				if (body is null)
					continue;

				foreach (var id in body.DescendantNodes().OfType<IdentifierNameSyntax>())
				{
					if (id.Identifier.ValueText == "HotReloadInitialize")
					{
						hasInitCall = true;
						break;
					}
				}

				if (hasInitCall)
					break;
			}

			return new HotReloadableClassInfo(
				ns: classSymbol.ContainingNamespace.IsGlobalNamespace
					? null
					: classSymbol.ContainingNamespace.ToDisplayString(),
				className: classSymbol.Name,
				hintName: classSymbol.ToDisplayString().Replace('.', '_').Replace('<', '_').Replace('>', '_'),
				location: classDecl.GetLocation(),
				hasInitCall: hasInitCall);
		}

		static void EmitAllSources(SourceProductionContext ctx, System.Collections.Immutable.ImmutableArray<HotReloadableClassInfo> allCandidates)
		{
			var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
			foreach (var info in allCandidates)
			{
				// Only emit once per unique class (multiple partial declarations yield the same HintName).
				if (!seen.Add(info.HintName))
					continue;

				EmitSource(ctx, info);
			}
		}

		static void EmitSource(SourceProductionContext ctx, HotReloadableClassInfo info)
		{
			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("#nullable enable");
			sb.AppendLine();

			if (info.Namespace is not null)
			{
				sb.AppendLine($"namespace {info.Namespace}");
				sb.AppendLine("{");
			}

			string indent = info.Namespace is not null ? "    " : string.Empty;
			sb.AppendLine($"{indent}partial class {info.ClassName}");
			sb.AppendLine($"{indent}{{");
			sb.AppendLine($"{indent}    /// <summary>Auto-generated. Call from your constructor to register this instance for hot reload notifications.</summary>");
			sb.AppendLine($"{indent}    private void HotReloadInitialize()");
			sb.AppendLine($"{indent}        => global::Microsoft.Maui.Labs.HotReload.HotReloadRegistry.Register(this);");
			sb.AppendLine($"{indent}}}");

			if (info.Namespace is not null)
				sb.AppendLine("}");

			ctx.AddSource($"{info.HintName}.HotReload.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

			if (!info.HasInitCall)
			{
				ctx.ReportDiagnostic(Diagnostic.Create(
					MissingInitCallDescriptor,
					info.Location,
					info.ClassName));
			}
		}

		sealed class HotReloadableClassInfo
		{
			public string? Namespace { get; }
			public string ClassName { get; }
			public string HintName { get; }
			public Location Location { get; }
			public bool HasInitCall { get; }

			public HotReloadableClassInfo(string? ns, string className, string hintName, Location location, bool hasInitCall)
			{
				Namespace = ns;
				ClassName = className;
				HintName = hintName;
				Location = location;
				HasInitCall = hasInitCall;
			}
		}
	}
}
