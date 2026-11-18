using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
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
    private const string MauiXmlNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";
    private const string XamlXmlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";
    private const string XmlnsPrefixAttributeMetadataName = "Microsoft.Maui.Controls.XmlnsPrefixAttribute";

    private static readonly XName[] s_classAttributeNames =
    [
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2009/xaml"),
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml")
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var namespaceMappings = context.CompilationProvider
            .Select(static (compilation, _) => GetNamespaceMappings(compilation))
            .WithComparer(NamespaceMappingsComparer.Instance);

        var models = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(namespaceMappings)
            .Select(static (pair, cancellationToken) =>
                TryReadXaml(
                    pair.Left.Left,
                    pair.Left.Right,
                    pair.Right,
                    cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value);

        context.RegisterSourceOutput(
            models.Collect().Combine(namespaceMappings),
            static (productionContext, pair) =>
                Emit(productionContext, pair.Left, pair.Right));
    }

    private static XamlModel? TryReadXaml(
        AdditionalText file,
        AnalyzerConfigOptionsProvider optionsProvider,
        ImmutableArray<XmlnsPrefixMapping> namespaceMappings,
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

        var fullTypeName = TryReadClass(xaml!, namespaceMappings);
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

    private static ImmutableArray<XmlnsPrefixMapping> GetNamespaceMappings(
        Compilation compilation)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [string.Empty] = MauiXmlNamespace,
            ["x"] = XamlXmlNamespace
        };
        var xmlnsPrefixAttribute =
            compilation.GetTypeByMetadataName(XmlnsPrefixAttributeMetadataName);
        if (xmlnsPrefixAttribute is not null)
        {
            var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<IAssemblySymbol>();
            pending.Push(compilation.Assembly);

            // Prefix attributes may be declared by the app or any referenced assembly.
            while (pending.Count > 0)
            {
                var assembly = pending.Pop();
                if (!visited.Add(assembly))
                    continue;

                foreach (var attribute in assembly.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        xmlnsPrefixAttribute)
                        || attribute.ConstructorArguments.Length < 2)
                    {
                        continue;
                    }

                    var xmlNamespace = attribute.ConstructorArguments[0].Value as string;
                    var prefix = attribute.ConstructorArguments[1].Value as string;
                    if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(xmlNamespace))
                        mappings[prefix!] = xmlNamespace!;
                }

                foreach (var module in assembly.Modules)
                {
                    foreach (var referencedAssembly in module.ReferencedAssemblySymbols)
                        pending.Push(referencedAssembly);
                }
            }
        }

        return mappings
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new XmlnsPrefixMapping(pair.Key, pair.Value))
            .ToImmutableArray();
    }

    private static string? TryReadClass(
        string xaml,
        ImmutableArray<XmlnsPrefixMapping> namespaceMappings)
    {
        XDocument document;
        try
        {
            var nameTable = new NameTable();
            var namespaceManager = new XmlNamespaceManager(nameTable);
            foreach (var mapping in namespaceMappings)
                namespaceManager.AddNamespace(mapping.Prefix, mapping.XmlNamespace);
            var parserContext = new XmlParserContext(
                nameTable,
                namespaceManager,
                null,
                XmlSpace.None);
            using var reader = XmlReader.Create(
                new StringReader(xaml),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Parse,
                    XmlResolver = null
                },
                parserContext);
            document = XDocument.Load(reader);
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
        ImmutableArray<XamlModel> items,
        ImmutableArray<XmlnsPrefixMapping> namespaceMappings)
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
        builder.AppendLine("        private static readonly global::System.Collections.Generic.IReadOnlyDictionary<string, string> _namespaces =");
        builder.AppendLine("            new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal)");
        builder.AppendLine("        {");
        foreach (var mapping in namespaceMappings)
        {
            builder.AppendLine(
                $"            [{SymbolDisplay.FormatLiteral(mapping.Prefix, true)}] = "
                + $"{SymbolDisplay.FormatLiteral(mapping.XmlNamespace, true)},");
        }
        builder.AppendLine("        };");
        builder.AppendLine("        private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap> _cache =");
        builder.AppendLine("            new global::System.Collections.Concurrent.ConcurrentDictionary<string, global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap>(global::System.StringComparer.Ordinal);");
        builder.AppendLine("        public global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap? GetMap(string fullTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_cache.TryGetValue(fullTypeName, out var cached)) return cached;");
        builder.AppendLine("            if (!_sources.TryGetValue(fullTypeName, out var entry)) return null;");
        builder.AppendLine("            var map = global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap.Parse(entry.Xaml, entry.File, null, _namespaces);");
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

    private readonly record struct XmlnsPrefixMapping(
        string Prefix,
        string XmlNamespace);

    private sealed class NamespaceMappingsComparer : IEqualityComparer<ImmutableArray<XmlnsPrefixMapping>>
    {
        public static readonly NamespaceMappingsComparer Instance = new();

        public bool Equals(ImmutableArray<XmlnsPrefixMapping> x, ImmutableArray<XmlnsPrefixMapping> y)
            => x.SequenceEqual(y);

        public int GetHashCode(ImmutableArray<XmlnsPrefixMapping> mappings)
        {
            unchecked
            {
                var hash = 17;
                foreach (var mapping in mappings)
                    hash = hash * 31 + mapping.GetHashCode();
                return hash;
            }
        }
    }
}
