using System.Xml;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

public readonly record struct XamlSourceEntry(
    int Line,
    int Column,
    string TypeName,
    int ChildCount,
    string? AutomationId = null,
    string? FullTypeName = null);

public sealed class XamlSourceMap
{
    private static readonly HashSet<string> s_contentProperties = new(StringComparer.Ordinal)
    {
        "Content",
        "Children"
    };

    private readonly IReadOnlyDictionary<string, XamlSourceEntry> _paths;

    public XamlSourceMap(
        string file,
        IReadOnlyDictionary<string, XamlSourceEntry> paths,
        string? contentHash = null)
    {
        File = file;
        _paths = paths;
        ContentHash = contentHash;
    }

    public string File { get; }

    /// <summary>
    /// Short hash of the .xaml content at build time. A click-to-source consumer hashes the
    /// current file and, on mismatch, reports the source as stale instead of navigating to a line
    /// that may have moved. Also gates <c>XamlSourcePropertyEditor</c> write-back.
    /// </summary>
    public string? ContentHash { get; }

    public int Count => _paths.Count;

    public bool TryGet(string childPath, out XamlSourceEntry entry)
        => _paths.TryGetValue(childPath, out entry);

    public static XamlSourceMap? Parse(string xaml, string file)
        => Parse(xaml, file, contentHash: null);

    public static XamlSourceMap? Parse(string xaml, string file, string? contentHash)
    {
        if (string.IsNullOrWhiteSpace(xaml))
            return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is not { } root)
            return null;

        var map = new Dictionary<string, XamlSourceEntry>(StringComparer.Ordinal);
        Visit(root, string.Empty, map, root.Attribute("AutomationId")?.Value);
        return new XamlSourceMap(file, map, contentHash ?? ComputeContentHash(xaml));
    }

    /// <summary>
    /// Build-time content hash of the .xaml text. Must stay byte-identical to
    /// <c>XamlSourcePropertyEditor.ComputeSourceHash</c> (first 8 bytes of SHA-256 over the UTF-8
    /// text, lowercase hex) — the inspector compares them to decide whether source is stale.
    /// </summary>
    internal static string ComputeContentHash(string xaml)
        => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(xaml)),
            0,
            8).ToLowerInvariant();

    private static void Visit(
        XElement element,
        string path,
        Dictionary<string, XamlSourceEntry> map,
        string? usableAutomationId)
    {
        var children = ContentChildren(element).ToList();
        if (element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo())
        {
            map[path] = new XamlSourceEntry(
                lineInfo.LineNumber,
                lineInfo.LinePosition,
                element.Name.LocalName,
                children.Count,
                usableAutomationId,
                ResolveFullTypeName(element));
        }

        var childIds = children.Select(child => child.Attribute("AutomationId")?.Value).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var childId in childIds)
        {
            if (!string.IsNullOrEmpty(childId))
                counts[childId] = counts.TryGetValue(childId, out var count) ? count + 1 : 1;
        }

        for (var index = 0; index < children.Count; index++)
        {
            var childId = childIds[index];
            var uniqueId = !string.IsNullOrEmpty(childId) && counts[childId] == 1
                ? childId
                : null;
            var childPath = path.Length == 0 ? index.ToString() : $"{path}/{index}";
            Visit(children[index], childPath, map, uniqueId);
        }
    }

    private static string? ResolveFullTypeName(XElement element)
    {
        const string prefix = "clr-namespace:";
        var xmlNamespace = element.Name.NamespaceName;
        if (!xmlNamespace.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var remainder = xmlNamespace[prefix.Length..];
        var semicolon = remainder.IndexOf(';');
        var clrNamespace = (semicolon >= 0 ? remainder[..semicolon] : remainder).Trim();
        return clrNamespace.Length == 0
            ? null
            : $"{clrNamespace}.{element.Name.LocalName}";
    }

    private static IEnumerable<XElement> ContentChildren(XElement element)
    {
        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;
            var dot = localName.LastIndexOf('.');
            if (dot < 0)
            {
                yield return child;
                continue;
            }

            var property = localName[(dot + 1)..];
            if (!s_contentProperties.Contains(property))
                continue;

            foreach (var nested in child.Elements())
            {
                if (nested.Name.LocalName.LastIndexOf('.') < 0)
                    yield return nested;
            }
        }
    }
}
