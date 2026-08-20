using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.DevFlow.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class XamlSourceMapGenerator : IIncrementalGenerator
{
    private const string XamlMarkerMetadata = "build_metadata.AdditionalFiles.DevFlowXaml";

    private static readonly XName[] s_classAttributeNames =
    [
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2009/xaml"),
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml")
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) =>
                TryReadXaml(pair.Left, pair.Right, cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value);

        context.RegisterSourceOutput(
            models.Collect(),
            static (productionContext, items) => Emit(productionContext, items));
    }

    private static XamlModel? TryReadXaml(
        AdditionalText file,
        AnalyzerConfigOptionsProvider optionsProvider,
        CancellationToken cancellationToken)
    {
        var options = optionsProvider.GetOptions(file);
        if (!options.TryGetValue(XamlMarkerMetadata, out var marker)
            || !string.Equals(marker, "true", StringComparison.OrdinalIgnoreCase)
            || !file.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var xaml = file.GetText(cancellationToken)?.ToString();
        if (string.IsNullOrWhiteSpace(xaml))
            return null;

        var fullTypeName = TryReadClass(xaml!);
        return fullTypeName is null
            ? null
            : new XamlModel(
                fullTypeName,
                NormalizeSourcePath(
                    file.Path,
                    optionsProvider.GlobalOptions),
                xaml!);
    }

    private static string NormalizeSourcePath(
        string path,
        AnalyzerConfigOptions globalOptions)
    {
        if (globalOptions.TryGetValue(
                "build_property.MSBuildProjectDirectory",
                out var projectDirectory)
            && !string.IsNullOrWhiteSpace(projectDirectory))
        {
            var relative = GetRelativePath(projectDirectory!, path);
            if (!relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
                && !Path.IsPathRooted(relative))
            {
                return relative.Replace(
                    Path.DirectorySeparatorChar,
                    '/');
            }
        }

        return Path.GetFileName(path);
    }

    private static string GetRelativePath(
        string baseDirectory,
        string path)
    {
        var normalizedBase = Path.GetFullPath(baseDirectory);
        if (!normalizedBase.EndsWith(
            Path.DirectorySeparatorChar.ToString(),
            StringComparison.Ordinal))
        {
            normalizedBase += Path.DirectorySeparatorChar;
        }

        var baseUri = new Uri(normalizedBase);
        var pathUri = new Uri(Path.GetFullPath(path));
        if (!string.Equals(
            baseUri.Scheme,
            pathUri.Scheme,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                baseUri.Host,
                pathUri.Host,
                StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var relativeUri = baseUri.MakeRelativeUri(pathUri);
        if (relativeUri.IsAbsoluteUri)
            return path;

        return Uri.UnescapeDataString(relativeUri.ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string? TryReadClass(string xaml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xaml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        if (document.Root is not { } root)
            return null;
        foreach (var name in s_classAttributeNames)
        {
            var value = root.Attribute(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value!.Trim();
        }
        return null;
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<XamlModel> items)
    {
        if (items.IsDefaultOrEmpty)
            return;

        var unique = new Dictionary<string, XamlModel>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!unique.ContainsKey(item.FullTypeName))
                unique[item.FullTypeName] = item;
        }
        if (unique.Count == 0)
            return;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Microsoft.Maui.DevFlow.Analyzers\", \"1.0.0\")]");
        builder.AppendLine("    internal sealed class __DevFlowXamlSourceMapProvider : global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.IXamlSourceMapProvider");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly global::System.Collections.Generic.Dictionary<string, (string File, string Xaml)> _sources =");
        builder.AppendLine("            new global::System.Collections.Generic.Dictionary<string, (string, string)>(global::System.StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (var model in unique.Values)
        {
            builder.AppendLine(
                $"            [{SymbolDisplay.FormatLiteral(model.FullTypeName, true)}] = "
                + $"({SymbolDisplay.FormatLiteral(model.Path, true)}, "
                + $"{SymbolDisplay.FormatLiteral(model.Xaml, true)}),");
        }
        builder.AppendLine("        };");
        builder.AppendLine("        private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap> _cache =");
        builder.AppendLine("            new global::System.Collections.Concurrent.ConcurrentDictionary<string, global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("        public global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap? GetMap(string fullTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_cache.TryGetValue(fullTypeName, out var cached)) return cached;");
        builder.AppendLine("            if (!_sources.TryGetValue(fullTypeName, out var entry)) return null;");
        builder.AppendLine("            var map = global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap.Parse(entry.Xaml, entry.File);");
        builder.AppendLine("            if (map is not null) _cache[fullTypeName] = map;");
        builder.AppendLine("            return map;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("    internal static class __DevFlowXamlSourceMapModuleInit");
        builder.AppendLine("    {");
        builder.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        builder.AppendLine("        internal static void Initialize()");
        builder.AppendLine("            => global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMapRegistry.Register(new __DevFlowXamlSourceMapProvider());");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        context.AddSource(
            "DevFlowXamlSourceMaps.g.cs",
            SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private readonly record struct XamlModel(
        string FullTypeName,
        string Path,
        string Xaml);
}
