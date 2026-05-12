using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Maui.AI.Indexer.Generators;

namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Test harness for running the XamlIndexerGenerator on XAML input.</summary>
public static class GeneratorTestHarness
{
    /// <summary>
    /// Run the generator and return the full generated source texts keyed by hint name.
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

    /// <summary>
    /// Run the generator and extract the Markdown constant for a specific page.
    /// Returns null if not found.
    /// </summary>
    public static string? GetMarkdown(string pageClassName,
        params (string Path, string Content)[] xamlFiles)
    {
        var sources = GetGeneratedSources(xamlFiles);
        var key = $"{pageClassName}_UiIndex.g.cs";
        if (!sources.TryGetValue(key, out var source))
            return null;

        return ExtractMarkdownConstant(source);
    }

    /// <summary>
    /// Run the generator and extract the full generated .g.cs source for a specific page.
    /// </summary>
    public static string? GetGeneratedSource(string hintName,
        params (string Path, string Content)[] xamlFiles)
    {
        var sources = GetGeneratedSources(xamlFiles);
        sources.TryGetValue(hintName, out var source);
        return source;
    }

    /// <summary>
    /// Extract the Markdown constant value from a generated source file.
    /// Parses the raw string literal content.
    /// </summary>
    public static string? ExtractMarkdownConstant(string generatedSource)
    {
        // Find the raw string literal: """ ... """
        // Look for the opening delimiter line
        const string marker = "public const string Markdown =";
        var markerIdx = generatedSource.IndexOf(marker);
        if (markerIdx < 0) return null;

        // Find the opening """ after the marker
        var afterMarker = generatedSource.Substring(markerIdx + marker.Length);

        // Count the opening quotes to handle extended delimiters ("""" etc.)
        var delimStart = afterMarker.IndexOf('"');
        if (delimStart < 0) return null;

        var delimLen = 0;
        for (var i = delimStart; i < afterMarker.Length && afterMarker[i] == '"'; i++)
            delimLen++;

        var delimiter = new string('"', delimLen);
        var contentStart = delimStart + delimLen;

        // The opening delimiter is on its own line, so skip to next line
        var nlIdx = afterMarker.IndexOf('\n', contentStart);
        if (nlIdx < 0) return null;
        contentStart = nlIdx + 1;

        // Find the closing delimiter (same number of quotes, on its own line after whitespace)
        var closingMarker = delimiter + ";";
        var closingIdx = afterMarker.IndexOf(closingMarker, contentStart);
        if (closingIdx < 0) return null;

        // Back up to the start of the closing delimiter line
        var lineStart = afterMarker.LastIndexOf('\n', closingIdx - 1);
        if (lineStart < 0) return null;

        var rawContent = afterMarker.Substring(contentStart, lineStart - contentStart + 1);

        // Remove the 8-space indentation from each line
        var lines = rawContent.Split('\n');
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var trimmedLine = line.TrimEnd('\r');
            if (trimmedLine.Length == 0)
            {
                sb.AppendLine();
            }
            else if (trimmedLine.StartsWith("        "))
            {
                sb.AppendLine(trimmedLine.Substring(8));
            }
            else
            {
                sb.AppendLine(trimmedLine);
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunGenerator(
        params (string Path, string Content)[] xamlFiles)
    {
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

        var additionalTexts = xamlFiles
            .Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Content))
            .ToImmutableArray();

        CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(additionalTexts)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        return (output, diagnostics);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var references = new List<MetadataReference>();
        var trustedAssemblies = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)?.Split(System.IO.Path.PathSeparator);
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies)
            {
                if (System.IO.File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }
        return references.ToArray();
    }
}

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
