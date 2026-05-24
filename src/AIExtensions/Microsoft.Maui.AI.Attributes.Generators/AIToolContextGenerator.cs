using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.AI.Attributes.Generators;

/// <summary>
/// Incremental source generator that emits a fully-typed <c>AIFunction</c> subclass per
/// <c>[ExportAIFunction]</c> method on each type referenced by an
/// <c>[AIToolSource(typeof(...))]</c> attribute on an <c>AIToolContext</c> partial class.
/// </summary>
/// <remarks>
/// The emitted class:
/// <list type="bullet">
/// <item>Overrides <c>Name</c>, <c>Description</c>, <c>JsonSchema</c>, <c>ReturnJsonSchema</c>.</item>
/// <item>Resolves its backing service from <c>AIFunctionArguments.Services</c> per invocation — no
/// <c>AIFunctionFactory.Create</c>, no <c>MethodInfo.Invoke</c>.</item>
/// <item>Binds each parameter at compile time:
/// <c>CancellationToken</c>/<c>IServiceProvider</c>/<c>AIFunctionArguments</c> get special cases;
/// <c>[FromServices]</c>/<c>[FromKeyedServices]</c> parameters resolve from DI; everything else
/// binds from the argument dictionary.</item>
/// </list>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class AIToolContextGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contextDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SymbolAnalysis.AIToolSourceAttributeFullName,
                predicate: static (node, _) => node is ClassDeclarationSyntax cds &&
                    cds.Modifiers.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, ct) => SymbolAnalysis.GetContextModel(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        // Merge duplicate context entries (multiple [AIToolSource] attributes → multiple invocations).
        var grouped = contextDeclarations
            .Collect()
            .SelectMany(static (items, _) =>
            {
                var dict = new Dictionary<string, ContextModel>();
                foreach (var item in items)
                {
                    var key = item.FullyQualifiedName;
                    if (!dict.TryGetValue(key, out var existing))
                    {
                        dict[key] = item;
                    }
                    else
                    {
                        var merged = existing.WithAdditionalSourceTypes(item.SourceTypes);
                        dict[key] = merged;
                    }
                }
                return dict.Values.ToImmutableArray();
            });

        context.RegisterSourceOutput(grouped, static (spc, model) =>
        {
            var source = CodeEmitter.GenerateContextSource(model);
            var hintName = model.ContainingTypes.Length > 0
                ? $"{string.Join("_", model.ContainingTypes.Select(c => c.Name))}_{model.ClassName}.g.cs"
                : $"{model.ClassName}.g.cs";
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));

            foreach (var diag in model.Diagnostics)
            {
                spc.ReportDiagnostic(diag.ToDiagnostic());
            }
        });

        // ─── Assembly-wide ToolContext ───────────────────────────────
        // Discovers ALL [ExportAIFunction] methods/properties in the assembly
        // and emits a single <RootNamespace>.<AssemblyName>ToolContext class.
        var allExportedMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SymbolAnalysis.ExportAIFunctionAttributeFullName,
                predicate: static (node, _) => node is MethodDeclarationSyntax or PropertyDeclarationSyntax,
                transform: static (ctx, ct) => SymbolAnalysis.GetExportedMemberInfo(ctx, ct))
            .Where(static m => m is not null)
            .Select(static (m, _) => m!);

        var assemblyWide = allExportedMethods
            .Collect()
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(assemblyWide, static (spc, pair) =>
        {
            var (members, compilation) = pair;
            if (members.IsEmpty)
                return;

            var assemblyName = compilation.AssemblyName ?? "Assembly";
            var rootNamespace = compilation.Options is CSharpCompilationOptions opts
                ? SymbolAnalysis.GetRootNamespace(compilation)
                : assemblyName;

            // Sanitize assembly name for use as an identifier.
            var safeAssemblyName = CodeEmitter.SanitizeIdentifier(assemblyName.Replace(".", ""));
            var className = $"{safeAssemblyName}ToolContext";

            // Group by containing type.
            var sourceTypes = new Dictionary<string, (string fqn, string simpleName, List<MethodModel> methods)>();
            foreach (var m in members)
            {
                if (!sourceTypes.TryGetValue(m.ContainingTypeFQN, out var entry))
                {
                    entry = (m.ContainingTypeFQN, m.ContainingTypeSimpleName, new List<MethodModel>());
                    sourceTypes[m.ContainingTypeFQN] = entry;
                }
                entry.methods.Add(m.Method);
            }

            var model = new ContextModel(
                rootNamespace,
                className,
                $"global::{rootNamespace}.{className}",
                "internal",
                [],
                [.. sourceTypes.Values.Select(st => new SourceTypeModel(st.fqn, st.simpleName, [.. st.methods]))],
                [],
                EmitBaseClass: true);

            var source = CodeEmitter.GenerateContextSource(model);
            spc.AddSource($"{className}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }
}
