using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Maui.AI.Indexer.Generators;

namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Test harness for running the XamlIndexerGenerator on XAML input.</summary>
public static class GeneratorTestHarness
{
    /// <summary>
    /// Run the generator with the given XAML files and return the output compilation and diagnostics.
    /// </summary>
    public static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        params (string Path, string Content)[] xamlFiles)
    {
        // Create a minimal compilation with the attribute types
        var attributeSource = @"
namespace Microsoft.Maui.AI.Indexer
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UiPageIndexAttribute : System.Attribute
    {
        public UiPageIndexAttribute(string pageName) { PageName = pageName; }
        public string PageName { get; }
        public string? Route { get; set; }
        public string? FilePath { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class UiProjectIndexAttribute : System.Attribute { }
}
";
        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(attributeSource) },
            GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new XamlIndexerGenerator();

        // Create additional texts from XAML files
        var additionalTexts = xamlFiles
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(additionalTexts)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (output, diagnostics);
    }

    /// <summary>
    /// Run the generator and return just the generated source texts keyed by hint name.
    /// </summary>
    public static Dictionary<string, string> GetGeneratedSources(
        params (string Path, string Content)[] xamlFiles)
    {
        var (output, _) = RunGenerator(xamlFiles);
        var result = new Dictionary<string, string>();

        foreach (var tree in output.SyntaxTrees)
        {
            var path = tree.FilePath;
            if (path.Contains("XamlIndexerGenerator"))
            {
                var fileName = System.IO.Path.GetFileName(path);
                result[fileName] = tree.GetText().ToString();
            }
        }

        return result;
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,           // System.Runtime
            typeof(Attribute).Assembly,        // System.Runtime
            typeof(System.Linq.Enumerable).Assembly, // System.Linq
        };

        var references = new List<MetadataReference>();

        // Use trusted platform assemblies for full framework coverage
        var trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(System.IO.Path.PathSeparator);
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies)
            {
                if (System.IO.File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }
        else
        {
            foreach (var assembly in assemblies)
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references.ToArray();
    }
}

/// <summary>In-memory AdditionalText for testing.</summary>
internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly string _text;

    public InMemoryAdditionalText(string path, string text)
    {
        Path = path;
        _text = text;
    }

    public override string Path { get; }

    public override Microsoft.CodeAnalysis.Text.SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
    {
        return Microsoft.CodeAnalysis.Text.SourceText.From(_text);
    }
}
