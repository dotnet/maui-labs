using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

/// <summary>A parsed XAML path entry: source position, the declared (expected) element type, the
/// expected number of content children (to detect runtime insert/remove shifts), and optional
/// per-instance identity — the element's <c>AutomationId</c> and its resolved full CLR type (from a
/// <c>clr-namespace</c> xmlns) — used to disambiguate same-type siblings and namespace collisions.</summary>
public readonly record struct XamlSourceEntry(
    int Line,
    int Column,
    string TypeName,
    int ChildCount,
    string? AutomationId = null,
    string? FullTypeName = null);

/// <summary>
/// Maps a logical child-path from a XAML file's root element (e.g. <c>"0/2/1"</c>) to the source
/// position of the element declared there. Built by statically parsing a <c>.xaml</c> file; the
/// runtime <see cref="VisualTreeWalker"/> computes the same child-path from
/// <c>GetVisualChildren()</c> and looks it up to attach source locations to elements.
/// </summary>
public sealed class XamlSourceMap
{
    // Well-known MAUI content properties that FLATTEN their object-element children into the
    // owner's content-child sequence (so explicit <Owner.Content>/<Owner.Children> and implicit
    // content index identically). Deliberately narrow: an unrecognized property element is treated
    // as non-visual and skipped, so we stay conservative ("precise or null", never a wrong line).
    private static readonly HashSet<string> ContentProperties = new(StringComparer.Ordinal)
    {
        "Content", "Children",
    };

    private readonly IReadOnlyDictionary<string, XamlSourceEntry> _paths;

    public XamlSourceMap(string file, IReadOnlyDictionary<string, XamlSourceEntry> paths, string? contentHash = null)
    {
        File = file;
        _paths = paths;
        ContentHash = contentHash;
    }

    /// <summary>Absolute path to the source <c>.xaml</c> file.</summary>
    public string File { get; }

    /// <summary>
    /// Short hash of the parsed XAML text at build time, or null for a hand-built map. A click-to-
    /// source consumer can hash the current file and treat a mismatch as "source unavailable / stale"
    /// (the XAML changed since the app was built) rather than navigating to a wrong line.
    /// </summary>
    public string? ContentHash { get; }

    public int Count => _paths.Count;

    /// <summary>Look up the source entry for a child-path (<c>""</c> is the file root).</summary>
    public bool TryGet(string childPath, out XamlSourceEntry entry) => _paths.TryGetValue(childPath, out entry);

    /// <summary>
    /// Parse a XAML document into a child-path → location map. Returns null if the content is empty
    /// or not well-formed XML.
    /// </summary>
    public static XamlSourceMap? Parse(string xaml, string file)
    {
        if (string.IsNullOrWhiteSpace(xaml)) return null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return null;
        }

        var root = doc.Root;
        if (root is null) return null;

        var map = new Dictionary<string, XamlSourceEntry>(StringComparer.Ordinal);
        Visit(root, "", map, root.Attribute("AutomationId")?.Value);
        return new XamlSourceMap(file, map, ComputeHash(xaml));
    }

    /// <summary>Short (64-bit) content hash over the XAML TEXT (UTF-8, no BOM), used only to detect
    /// that the .xaml changed since build. A consumer must hash the file's text the same way.</summary>
    private static string ComputeHash(string xaml)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xaml)), 0, 8).ToLowerInvariant();

    private static void Visit(XElement element, string path, Dictionary<string, XamlSourceEntry> map, string? usableAutomationId)
    {
        var children = ContentChildren(element).ToList();

        if (element is IXmlLineInfo li && li.HasLineInfo())
        {
            map[path] = new XamlSourceEntry(
                li.LineNumber,
                li.LinePosition,
                element.Name.LocalName,
                children.Count,
                AutomationId: usableAutomationId,
                FullTypeName: ResolveFullTypeName(element));
        }

        // Only trust an AutomationId as identity when it is UNIQUE among the parent's content
        // children — duplicate AutomationIds would give false confidence in a reorder.
        var childIds = children.Select(c => c.Attribute("AutomationId")?.Value).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cid in childIds)
            if (!string.IsNullOrEmpty(cid))
                counts[cid!] = counts.TryGetValue(cid!, out var n) ? n + 1 : 1;

        var index = 0;
        foreach (var child in children)
        {
            var childId = childIds[index];
            var usable = !string.IsNullOrEmpty(childId) && counts[childId!] == 1 ? childId : null;
            var childPath = path.Length == 0 ? index.ToString() : $"{path}/{index}";
            Visit(child, childPath, map, usable);
            index++;
        }
    }

    /// <summary>
    /// Resolves an element's full CLR type name when its xmlns is a <c>clr-namespace</c> declaration
    /// (e.g. <c>clr-namespace:MyApp.Views;assembly=MyApp</c> → <c>MyApp.Views.MyView</c>). Returns
    /// null for MAUI-schema/http namespaces, where the short name is used instead.
    /// </summary>
    private static string? ResolveFullTypeName(XElement element)
    {
        const string prefix = "clr-namespace:";
        var ns = element.Name.NamespaceName;
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var rest = ns.Substring(prefix.Length);
        var semicolon = rest.IndexOf(';');
        var clrNamespace = (semicolon >= 0 ? rest.Substring(0, semicolon) : rest).Trim();
        return clrNamespace.Length == 0 ? null : $"{clrNamespace}.{element.Name.LocalName}";
    }

    /// <summary>
    /// Yields an element's CONTENT children in document order — the set that
    /// <c>GetVisualChildren()</c> is expected to return for the common content model: direct object
    /// elements, plus the object children of a flattened Content/Children property element.
    /// Non-content property elements (Grid.RowDefinitions, *.Resources, *.ItemTemplate, Style, ...)
    /// are skipped.
    /// </summary>
    private static IEnumerable<XElement> ContentChildren(XElement element)
    {
        foreach (var child in element.Elements())
        {
            var local = child.Name.LocalName;
            var dot = local.LastIndexOf('.');
            if (dot < 0)
            {
                // Object element → a content child.
                yield return child;
                continue;
            }

            // Property element "Owner.Prop".
            var prop = local[(dot + 1)..];
            if (ContentProperties.Contains(prop))
            {
                foreach (var inner in child.Elements())
                {
                    // Only object elements inside the content property flatten in.
                    if (inner.Name.LocalName.LastIndexOf('.') < 0)
                        yield return inner;
                }
            }
            // Non-content property element → skip (its children are property values, not visual children).
        }
    }
}
