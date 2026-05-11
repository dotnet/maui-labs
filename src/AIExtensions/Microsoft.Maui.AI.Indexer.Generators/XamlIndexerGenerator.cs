using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Maui.AI.Indexer.Generators.Generation;
using Microsoft.Maui.AI.Indexer.Generators.Models;
using Microsoft.Maui.AI.Indexer.Generators.Parsing;

namespace Microsoft.Maui.AI.Indexer.Generators;

/// <summary>
/// Incremental source generator that reads XAML files via AdditionalTexts
/// and emits per-page .g.cs files with embedded markdown UI indexes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class XamlIndexerGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Collect XAML files via AdditionalTextsProvider
        var xamlFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase));

        // 2. Parse each XAML file individually
        var parsedFiles = xamlFiles.Select(static (file, ct) =>
        {
            var text = file.GetText(ct);
            return XamlFileParser.Parse(file.Path, text?.ToString());
        }).Where(static x => x is not null);

        // 3. Collect all parsed files
        var allParsed = parsedFiles.Collect();

        // 4. Combine with compilation to get assembly name / root namespace
        var combined = allParsed.Combine(context.CompilationProvider);

        // 5. Register output
        context.RegisterSourceOutput(combined, static (spc, data) =>
        {
            var (files, compilation) = data;
            EmitSources(spc, files!, compilation);
        });
    }

    private static void EmitSources(
        SourceProductionContext spc,
        ImmutableArray<PageModel?> files,
        Compilation compilation)
    {
        var pages = files
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();

        if (pages.Count == 0)
            return;

        var rootNamespace = compilation.AssemblyName ?? "";

        var projectIndex = new ProjectIndex { Pages = pages };

        // Emit per-page files
        foreach (var page in pages)
        {
            var source = PageCodeEmitter.Emit(page);
            var hintName = $"{SanitizeIdentifier(page.ClassName)}_UiIndex.g.cs";
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }

        // Emit aggregate index
        var aggregateSource = AggregateCodeEmitter.Emit(projectIndex, rootNamespace);
        spc.AddSource("UiIndex.g.cs", SourceText.From(aggregateSource, Encoding.UTF8));
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
            else
                sb.Append('_');
        }
        return sb.ToString();
    }
}
