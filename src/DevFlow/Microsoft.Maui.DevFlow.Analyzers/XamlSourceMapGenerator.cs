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
    private const string MauiGlobalXmlNamespace = "http://schemas.microsoft.com/dotnet/maui/global";
    private const string XamlXmlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";
    private const string XmlnsPrefixAttributeMetadataName = "Microsoft.Maui.Controls.XmlnsPrefixAttribute";
    private const string XmlnsDefinitionAttributeMetadataName = "Microsoft.Maui.Controls.XmlnsDefinitionAttribute";

    private static readonly XName[] s_classAttributeNames =
    [
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2009/xaml"),
        XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml")
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var namespaceMetadata = context.CompilationProvider
            .Select(static (compilation, _) => GetNamespaceMetadata(compilation))
            .WithComparer(NamespaceMetadataComparer.Instance);

        var models = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(namespaceMetadata)
            .Select(static (pair, cancellationToken) =>
                TryReadXaml(
                    pair.Left.Left,
                    pair.Left.Right,
                    pair.Right,
                    cancellationToken))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value);

        var generatedSource = models.Collect()
            .Combine(namespaceMetadata)
            .Combine(context.CompilationProvider)
            .Select(static (pair, _) => CreateGeneratedSource(
                pair.Left.Left,
                pair.Left.Right,
                pair.Right))
            .WithComparer(GeneratedSourceComparer.Instance);

        context.RegisterSourceOutput(generatedSource, static (productionContext, source) =>
        {
            if (source is not null)
            {
                productionContext.AddSource(
                    "DevFlowXamlSourceMaps.g.cs",
                    SourceText.From(source, Encoding.UTF8));
            }
        });
    }

    private static XamlModel? TryReadXaml(
        AdditionalText file,
        AnalyzerConfigOptionsProvider optionsProvider,
        XamlNamespaceMetadata namespaceMetadata,
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

        var document = TryReadXaml(xaml!, namespaceMetadata.PrefixMappings);
        if (document?.Root is not { } root)
            return null;

        var fullTypeName = TryReadClass(root);
        return fullTypeName is null
            ? null
            : new XamlModel(
                fullTypeName,
                NormalizeSourcePath(
                    file.Path,
                    optionsProvider.GlobalOptions),
                xaml!,
                root.DescendantsAndSelf()
                    .Select(static element => new XmlElementName(
                        element.Name.NamespaceName,
                        element.Name.LocalName))
                    .Distinct()
                    .ToImmutableArray());
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

    private static XamlNamespaceMetadata GetNamespaceMetadata(
        Compilation compilation)
    {
        var prefixAttribute = FindMauiAttribute(
            compilation,
            XmlnsPrefixAttributeMetadataName);
        var definitionAttribute = FindMauiAttribute(
            compilation,
            XmlnsDefinitionAttributeMetadataName);
        var prefixes = new List<XmlnsPrefixMapping>
        {
            new(string.Empty, MauiGlobalXmlNamespace),
            new("x", XamlXmlNamespace)
        };
        var definitions = new List<XmlnsDefinitionMapping>();
        var assemblies = new List<IAssemblySymbol> { compilation.Assembly };
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                assemblies.Add(assembly);
        }

        foreach (var assembly in assemblies)
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                var arguments = attribute.ConstructorArguments;
                if (arguments.Length < 2)
                    continue;

                if (prefixAttribute is not null
                    && SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        prefixAttribute))
                {
                    if (arguments[0].Value is string xmlNamespace
                        && arguments[1].Value is string prefix
                        && prefix.Length > 0)
                    {
                        prefixes.Add(new(prefix, xmlNamespace));
                    }
                }
                else if (definitionAttribute is not null
                    && SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        definitionAttribute)
                    && arguments[0].Value is string xmlNamespace
                    && arguments[1].Value is string clrNamespace)
                {
                    definitions.Add(new(
                        xmlNamespace,
                        clrNamespace,
                        SymbolEqualityComparer.Default.Equals(
                            assembly,
                            compilation.Assembly)));
                }
            }
        }

        var prefixMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in prefixes)
            prefixMappings[mapping.Prefix] = mapping.XmlNamespace;

        return new XamlNamespaceMetadata(
            prefixMappings
                .OrderBy(static mapping => mapping.Key, StringComparer.Ordinal)
                .Select(static mapping => new XmlnsPrefixMapping(
                    mapping.Key,
                    mapping.Value))
                .ToImmutableArray(),
            definitions.ToImmutableArray());
    }

    private static INamedTypeSymbol? FindMauiAttribute(
        Compilation compilation,
        string metadataName)
        => compilation.GetTypesByMetadataName(metadataName)
            .FirstOrDefault(static type =>
                string.Equals(
                    type.ContainingAssembly.Identity.Name,
                    "Microsoft.Maui.Controls",
                    StringComparison.Ordinal));

    private static XDocument? TryReadXaml(
        string xaml,
        ImmutableArray<XmlnsPrefixMapping> namespaceMappings)
    {
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
            return XDocument.Load(reader);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string? TryReadClass(XElement root)
    {
        foreach (var name in s_classAttributeNames)
        {
            var value = root.Attribute(name)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value!.Trim();
        }
        return null;
    }

    private static string? CreateGeneratedSource(
        ImmutableArray<XamlModel> items,
        XamlNamespaceMetadata namespaceMetadata,
        Compilation compilation)
    {
        if (items.IsDefaultOrEmpty)
            return null;

        var unique = new Dictionary<string, XamlModel>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!unique.ContainsKey(item.FullTypeName))
                unique[item.FullTypeName] = item;
        }
        if (unique.Count == 0)
            return null;

        var typeMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in unique.Values)
        {
            foreach (var element in item.Elements)
            {
                var fullTypeName = ResolveFullTypeName(
                    compilation,
                    namespaceMetadata.Definitions,
                    element);
                if (fullTypeName is not null)
                {
                    typeMappings[GetElementTypeKey(
                        element.XmlNamespace,
                        element.LocalName)] = fullTypeName;
                }
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Microsoft.Maui.DevFlow.Analyzers\", \"1.0.0\")]");
        builder.AppendLine("    internal sealed class __DevFlowXamlSourceMapProvider : global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.IXamlSourceMapProvider");
        builder.AppendLine("    {");
        builder.AppendLine("        public global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap? GetMap(string fullTypeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            (string File, string Xaml)? entry = fullTypeName switch");
        builder.AppendLine("            {");
        foreach (var model in unique.Values)
        {
            builder.AppendLine(
                $"                {SymbolDisplay.FormatLiteral(model.FullTypeName, true)} => "
                + $"({SymbolDisplay.FormatLiteral(model.Path, true)}, "
                + $"{SymbolDisplay.FormatLiteral(model.Xaml, true)}),");
        }
        builder.AppendLine("                _ => null");
        builder.AppendLine("            };");
        builder.AppendLine("            if (entry is null) return null;");
        builder.AppendLine("            return global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMap.Parse(entry.Value.Xaml, entry.Value.File, null, GetNamespaces(), GetTypes());");
        builder.AppendLine("        }");
        builder.AppendLine("        private static global::System.Collections.Generic.IReadOnlyDictionary<string, string> GetNamespaces() =>");
        builder.AppendLine("            new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal)");
        builder.AppendLine("            {");
        foreach (var mapping in namespaceMetadata.PrefixMappings)
        {
            builder.AppendLine(
                $"            [{SymbolDisplay.FormatLiteral(mapping.Prefix, true)}] = "
                + $"{SymbolDisplay.FormatLiteral(mapping.XmlNamespace, true)},");
        }
        builder.AppendLine("            };");
        builder.AppendLine("        private static global::System.Collections.Generic.IReadOnlyDictionary<string, string> GetTypes() =>");
        builder.AppendLine("            new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal)");
        builder.AppendLine("            {");
        foreach (var mapping in typeMappings.OrderBy(static mapping => mapping.Key, StringComparer.Ordinal))
        {
            builder.AppendLine(
                $"            [{SymbolDisplay.FormatLiteral(mapping.Key, true)}] = "
                + $"{SymbolDisplay.FormatLiteral(mapping.Value, true)},");
        }
        builder.AppendLine("            };");
        builder.AppendLine("    }");
        builder.AppendLine("    internal static class __DevFlowXamlSourceMapModuleInit");
        builder.AppendLine("    {");
        builder.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        builder.AppendLine("        internal static void Initialize()");
        builder.AppendLine("            => global::Microsoft.Maui.DevFlow.Agent.Core.SourceMapping.XamlSourceMapRegistry.Register(new __DevFlowXamlSourceMapProvider());");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string? ResolveFullTypeName(
        Compilation compilation,
        ImmutableArray<XmlnsDefinitionMapping> definitions,
        XmlElementName element)
    {
        var clrNamespaces = new List<string>();
        foreach (var definition in definitions)
        {
            if (string.Equals(
                    definition.XmlNamespace,
                    element.XmlNamespace,
                    StringComparison.Ordinal))
            {
                AddClrNamespace(clrNamespaces, definition.ClrNamespace);
            }

            if (definition.IsCurrentAssembly
                && string.Equals(
                    element.XmlNamespace,
                    MauiGlobalXmlNamespace,
                    StringComparison.Ordinal)
                && string.Equals(
                    definition.XmlNamespace,
                    MauiGlobalXmlNamespace,
                    StringComparison.Ordinal))
            {
                foreach (var globalTarget in definitions)
                {
                    if (string.Equals(
                        globalTarget.XmlNamespace,
                        definition.ClrNamespace,
                        StringComparison.Ordinal))
                    {
                        AddClrNamespace(clrNamespaces, globalTarget.ClrNamespace);
                    }
                }
            }
        }

        string? assemblyName = null;
        if (clrNamespaces.Count == 0
            && TryGetClrNamespace(element.XmlNamespace) is { } clrNamespace)
        {
            AddClrNamespace(clrNamespaces, clrNamespace.Namespace);
            assemblyName = clrNamespace.AssemblyName;
        }

        if (clrNamespaces.Count == 0)
            return null;

        var candidates = clrNamespaces
            .Select(clrNamespace => $"{clrNamespace}.{element.LocalName}")
            .SelectMany(typeName => compilation.GetTypesByMetadataName(typeName))
            .Where(type => compilation.IsSymbolAccessibleWithin(
                type,
                compilation.Assembly))
            .Distinct(SymbolEqualityComparer.Default)
            .Cast<INamedTypeSymbol>()
            .ToArray();

        var matchingCandidates = assemblyName is null
            ? candidates
            : candidates.Where(type => string.Equals(
                    type.ContainingAssembly.Identity.Name,
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        return candidates.Length == 1 && matchingCandidates.Length == 1
            ? GetRuntimeFullTypeName(matchingCandidates[0])
            : null;
    }

    private static void AddClrNamespace(List<string> namespaces, string value)
    {
        if (!value.StartsWith("http://", StringComparison.Ordinal)
            && !namespaces.Contains(value, StringComparer.Ordinal))
        {
            namespaces.Add(value);
        }
    }

    private static ClrNamespaceReference? TryGetClrNamespace(string xmlNamespace)
    {
        const string clrNamespacePrefix = "clr-namespace:";
        const string usingPrefix = "using:";
        if (xmlNamespace.StartsWith(clrNamespacePrefix, StringComparison.Ordinal))
        {
            var remainder = xmlNamespace.Substring(clrNamespacePrefix.Length);
            var semicolon = remainder.IndexOf(';');
            var clrNamespace = semicolon >= 0
                ? remainder.Substring(0, semicolon).Trim()
                : remainder.Trim();
            string? assemblyName = null;
            while (semicolon >= 0)
            {
                remainder = remainder.Substring(semicolon + 1);
                semicolon = remainder.IndexOf(';');
                var argument = (semicolon >= 0
                    ? remainder.Substring(0, semicolon)
                    : remainder).Trim();
                if (argument.StartsWith("assembly=", StringComparison.OrdinalIgnoreCase))
                    assemblyName = argument.Substring("assembly=".Length).Trim();
            }

            return clrNamespace.Length == 0
                ? null
                : new ClrNamespaceReference(clrNamespace, assemblyName);
        }

        if (xmlNamespace.StartsWith(usingPrefix, StringComparison.Ordinal))
        {
            var clrNamespace = xmlNamespace.Substring(usingPrefix.Length).Trim();
            return clrNamespace.Length == 0
                ? null
                : new ClrNamespaceReference(clrNamespace, null);
        }

        return null;
    }

    private static string GetRuntimeFullTypeName(INamedTypeSymbol type)
    {
        var nestedTypes = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
            nestedTypes.Push(current.MetadataName);

        var prefix = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString() + ".";
        return prefix + string.Join("+", nestedTypes);
    }

    private static string GetElementTypeKey(string xmlNamespace, string localName)
        => xmlNamespace + '\0' + localName;

    private readonly record struct XamlModel(
        string FullTypeName,
        string Path,
        string Xaml,
        ImmutableArray<XmlElementName> Elements);

    private readonly record struct XmlElementName(
        string XmlNamespace,
        string LocalName);

    private readonly record struct ClrNamespaceReference(
        string Namespace,
        string? AssemblyName);

    private readonly record struct XmlnsPrefixMapping(
        string Prefix,
        string XmlNamespace);

    private readonly record struct XmlnsDefinitionMapping(
        string XmlNamespace,
        string ClrNamespace,
        bool IsCurrentAssembly);

    private readonly record struct XamlNamespaceMetadata(
        ImmutableArray<XmlnsPrefixMapping> PrefixMappings,
        ImmutableArray<XmlnsDefinitionMapping> Definitions);

    private sealed class NamespaceMetadataComparer : IEqualityComparer<XamlNamespaceMetadata>
    {
        public static readonly NamespaceMetadataComparer Instance = new();

        public bool Equals(XamlNamespaceMetadata x, XamlNamespaceMetadata y)
            => x.PrefixMappings.SequenceEqual(y.PrefixMappings)
                && x.Definitions.SequenceEqual(y.Definitions);

        public int GetHashCode(XamlNamespaceMetadata metadata)
        {
            unchecked
            {
                var hash = 17;
                foreach (var mapping in metadata.PrefixMappings)
                    hash = hash * 31 + mapping.GetHashCode();
                foreach (var definition in metadata.Definitions)
                    hash = hash * 31 + definition.GetHashCode();
                return hash;
            }
        }
    }

    private sealed class GeneratedSourceComparer : IEqualityComparer<string?>
    {
        public static readonly GeneratedSourceComparer Instance = new();

        public bool Equals(string? x, string? y)
            => string.Equals(x, y, StringComparison.Ordinal);

        public int GetHashCode(string? source)
            => source is null ? 0 : StringComparer.Ordinal.GetHashCode(source);
    }
}
