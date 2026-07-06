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

        // Cross-file resolution: inline user control references
        var resolver = new CrossFileResolver(pages);
        resolver.ResolveAll(pages);

        var rootNamespace = compilation.AssemblyName ?? "";

        var projectIndex = new ProjectIndex { Pages = pages };

        // Resolve Shell navigation: map each ShellContent's route onto its hosted page,
        // and mark the first ShellContent as the app's home/entry screen.
        ResolveShellNavigation(pages, projectIndex);

        // Emit per-page files
        foreach (var page in pages)
        {
            var source = PageCodeEmitter.Emit(page);
            // Use namespace+class for unique hint names (avoids collision when
            // two pages share the same simple class name in different namespaces)
            var qualifiedName = !string.IsNullOrEmpty(page.Namespace)
                ? $"{page.Namespace}.{page.ClassName}"
                : page.ClassName;
            var hintName = $"{SanitizeIdentifier(qualifiedName)}_Indexed.g.cs";
            spc.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }

        // Emit aggregate index
        var aggregateSource = AggregateCodeEmitter.Emit(projectIndex, rootNamespace);
        spc.AddSource("IndexedPageCatalog.g.cs", SourceText.From(aggregateSource, Encoding.UTF8));
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

    /// <summary>
    /// Walk the Shell page(s) to (a) mark the first ShellContent as the entry/home screen and
    /// (b) copy each ShellContent's route onto the page it hosts. This makes the home screen a
    /// discoverable fact and gives Shell-hosted pages their routes.
    /// </summary>
    private static void ResolveShellNavigation(System.Collections.Generic.List<PageModel> pages, ProjectIndex projectIndex)
    {
        PageModel? FindPage(string? className)
            => className == null ? null : pages.Find(p => string.Equals(p.ClassName, className, System.StringComparison.OrdinalIgnoreCase));

        foreach (var shell in pages)
        {
            if (!string.Equals(shell.RootType, "Shell", System.StringComparison.OrdinalIgnoreCase))
                continue;

            var order = 0;
            foreach (var nav in EnumerateShellContent(shell.Elements))
            {
                // Map route onto the hosted page.
                if (nav.NavigationTarget != null && nav.CommandName != null)
                {
                    var target = FindPage(nav.NavigationTarget);
                    if (target != null && target.Route == null)
                        target.Route = nav.CommandName;
                }

                // The first ShellContent that hosts a page is the entry/home screen.
                if (order == 0 && nav.NavigationTarget != null)
                {
                    nav.IsEntry = true;
                    projectIndex.EntryPageName ??= nav.NavigationTarget;
                }

                order++;
            }
        }
    }

    /// <summary>Enumerate ShellContent elements (including those nested under Tab) in document order.</summary>
    private static System.Collections.Generic.IEnumerable<SemanticNode> EnumerateShellContent(System.Collections.Generic.List<SemanticNode> elements)
    {
        foreach (var el in elements)
        {
            if (el.TypeName == "ShellContent")
            {
                yield return el;
            }
            else if (el.Children.Count > 0)
            {
                foreach (var child in EnumerateShellContent(el.Children))
                    yield return child;
            }
        }
    }
}
