using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Generators.Tests;

internal static class GeneratorTestHarness
{
    private static readonly ImmutableArray<MetadataReference> References =
        BuildReferences();

    internal static GeneratorDriver Run(
        string source,
        out Compilation outputCompilation,
        out ImmutableArray<Diagnostic> diagnostics)
    {
        var compilation = CreateCompilation(source);
        var driver = CreateDriver(trackSteps: false);
        return driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out diagnostics);
    }

    internal static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            "ToolBlockConsumer",
            [syntaxTree],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    internal static GeneratorDriver CreateDriver(bool trackSteps)
    {
        var generator = new ToolBlockGenerator().AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest),
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackSteps));
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = paths
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToList();

        Add(typeof(FunctionInvocationContentBlock));
        Add(typeof(AIFunction));
        return references.ToImmutableArray();

        void Add(Type type)
        {
            var location = type.Assembly.Location;
            if (!string.IsNullOrEmpty(location)
                && !references.OfType<PortableExecutableReference>().Any(
                    reference => string.Equals(
                        reference.FilePath,
                        location,
                        StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(MetadataReference.CreateFromFile(location));
            }
        }
    }
}
