using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Generators.Tests;

internal static class GeneratorTestHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trustedAssembliesPaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var refs = trustedAssembliesPaths
            .Select(p => MetadataReference.CreateFromFile(p))
            .Cast<MetadataReference>()
            .ToList();

        // Ensure the types we reference from test inputs are available.
        void AddIfMissing(Type t)
        {
            var loc = t.Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && !refs.Any(r => r is PortableExecutableReference pe && string.Equals(pe.FilePath, loc, StringComparison.OrdinalIgnoreCase)))
                refs.Add(MetadataReference.CreateFromFile(loc));
        }

        AddIfMissing(typeof(object));
        AddIfMissing(typeof(AIFunction));
        AddIfMissing(typeof(IServiceProvider));
        AddIfMissing(typeof(IServiceCollection));
        AddIfMissing(typeof(AIToolContext));

        return refs.ToImmutableArray();
    }

    public static GeneratorDriver RunGenerator(string source, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            assemblyName: "MauiAIAttributesGeneratorTests",
            syntaxTrees: new[] { syntaxTree },
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new AIToolContextGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            parseOptions: (CSharpParseOptions)syntaxTree.Options);

        var result = driver.RunGeneratorsAndUpdateCompilation(compilation, out outputCompilation, out diagnostics);
        return result;
    }
}
