using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AndroidX.Compose.SourceGenerators;

/// <summary>
/// Generates <c>[Flags]</c> enum bodies that name each bit in a Kotlin
/// <c>$default</c> bitmask. Triggered by an assembly-level
/// <c>[assembly: ComposeDefaults&lt;FooKt&gt;("Foo", "FooDefault")]</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComposeDefaultsGenerator : IIncrementalGenerator
{
    const string GenericAttributeMetadataName = "AndroidX.Compose.ComposeDefaultsAttribute`1";
    const string DeclarativeAttributeMetadataName = "AndroidX.Compose.ComposeDefaultsAttribute";
    const string ComposerNamespace = "AndroidX.Compose.Runtime";
    const string ComposerName = "IComposer";
    const string KotlinFunctionNamespace = "Kotlin.Jvm.Functions";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("ComposeDefaultsAttribute.g.cs",
                SourceText.From(Attributes.Source, Encoding.UTF8)));

        var requests = context.CompilationProvider.Select(static (compilation, _) => BuildAll(compilation));

        context.RegisterSourceOutput(requests, static (spc, results) =>
        {
            foreach (var result in results)
            {
                foreach (var diag in result.Diagnostics)
                    spc.ReportDiagnostic(diag);

                if (result.Source is { } source && result.HintName is { } hint)
                    spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    static ImmutableArray<GenerationResult> BuildAll(Compilation compilation)
    {
        var genericAttr = compilation.GetTypeByMetadataName(GenericAttributeMetadataName);
        var declarativeAttr = compilation.GetTypeByMetadataName(DeclarativeAttributeMetadataName);
        if (genericAttr is null && declarativeAttr is null)
            return ImmutableArray<GenerationResult>.Empty;

        var assemblyAttributes = compilation.Assembly.GetAttributes();
        var builder = ImmutableArray.CreateBuilder<GenerationResult>();

        foreach (var attr in assemblyAttributes)
        {
            if (attr.AttributeClass is not { } attrClass) continue;

            if (genericAttr is not null &&
                SymbolEqualityComparer.Default.Equals(attrClass.ConstructedFrom, genericAttr))
            {
                builder.Add(BuildFromSymbol(attr));
                continue;
            }

            if (declarativeAttr is not null &&
                SymbolEqualityComparer.Default.Equals(attrClass, declarativeAttr))
            {
                builder.Add(BuildFromNames(attr));
                continue;
            }
        }

        return builder.ToImmutable();
    }

    static GenerationResult BuildFromSymbol(AttributeData attr)
    {
        var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

        if (attr.AttributeClass is not { TypeArguments.Length: 1 } attrClass ||
            attrClass.TypeArguments[0] is not INamedTypeSymbol containingType ||
            attr.ConstructorArguments.Length < 2 ||
            attr.ConstructorArguments[0].Value is not string methodName ||
            attr.ConstructorArguments[1].Value is not string enumName ||
            string.IsNullOrWhiteSpace(enumName))
        {
            return new GenerationResult(null, null,
                new[] { Diagnostic.Create(Diagnostics.MalformedAttribute, loc, "<unknown>") });
        }

        var candidates = containingType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.IsStatic)
            .ToList();

        if (candidates.Count == 0)
        {
            return new GenerationResult(null, null,
                new[] { Diagnostic.Create(Diagnostics.MethodNotFound, loc, methodName, containingType.ToDisplayString()) });
        }

        var method = candidates.OrderByDescending(m => m.Parameters.Length).First();

        int composerIndex = -1;
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            if (IsComposer(method.Parameters[i].Type))
            {
                composerIndex = i;
                break;
            }
        }

        if (composerIndex < 0)
        {
            return new GenerationResult(null, null,
                new[] { Diagnostic.Create(Diagnostics.NotComposable, loc, method.ToDisplayString()) });
        }

        var slots = ComposeDefaultsEmitter.SlotsFromSymbol(method, composerIndex);
        var sourceComment = $"{containingType.ToDisplayString()}.{method.Name}";
        var source = ComposeDefaultsEmitter.Emit(enumName, sourceComment, slots);
        return new GenerationResult(source, $"AndroidX.Compose.{enumName}.g.cs", Array.Empty<Diagnostic>());
    }

    static GenerationResult BuildFromNames(AttributeData attr)
    {
        var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

        if (attr.ConstructorArguments.Length < 2 ||
            attr.ConstructorArguments[0].Value is not string enumName ||
            string.IsNullOrWhiteSpace(enumName))
        {
            return new GenerationResult(null, null,
                new[] { Diagnostic.Create(Diagnostics.MalformedAttribute, loc, "<unknown>") });
        }

        // params string[] arrives as a single TypedConstant of Kind=Array.
        var arr = attr.ConstructorArguments[1];
        if (arr.Kind != TypedConstantKind.Array)
        {
            return new GenerationResult(null, null,
                new[] { Diagnostic.Create(Diagnostics.MalformedAttribute, loc, enumName) });
        }

        var names = arr.Values
            .Select(v => v.Value as string ?? string.Empty)
            .ToArray();

        var slots = ComposeDefaultsEmitter.SlotsFromNames(names);
        var source = ComposeDefaultsEmitter.Emit(enumName, $"declarative names for '{enumName}'", slots);
        return new GenerationResult(source, $"AndroidX.Compose.{enumName}.g.cs", Array.Empty<Diagnostic>());
    }

    internal static bool IsComposer(ITypeSymbol type) =>
        type is INamedTypeSymbol n &&
        n.Name == ComposerName &&
        n.ContainingNamespace?.ToDisplayString() == ComposerNamespace;

    internal static bool IsKotlinFunction(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol n) return false;
        var ns = n.ContainingNamespace?.ToDisplayString();
        return ns == KotlinFunctionNamespace && n.Name.StartsWith("IFunction", StringComparison.Ordinal);
    }
}
