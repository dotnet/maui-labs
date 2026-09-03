using Fizzler;

namespace Microsoft.Maui.DevFlow.Agent.Core.Css;

/// <summary>
/// Implements Fizzler's IElementOps for ElementInfo, enabling CSS selector
/// matching against the MAUI visual tree.
/// </summary>
public class ElementInfoOps : IElementOps<ElementInfo>
{
    readonly Dictionary<string, ElementInfo> _parentMap = new();
    readonly Dictionary<string, List<ElementInfo>> _childrenMap = new();
    readonly List<ElementInfo> _allElements = new();

    public ElementInfoOps(List<ElementInfo> tree)
    {
        foreach (var root in tree)
            Index(root, null);
    }

    void Index(ElementInfo element, ElementInfo? parent)
    {
        _allElements.Add(element);
        if (parent != null)
            _parentMap[element.Id] = parent;

        if (element.Children is { Count: > 0 })
        {
            _childrenMap[element.Id] = element.Children;
            foreach (var child in element.Children)
                Index(child, element);
        }
    }

    ElementInfo? GetParent(ElementInfo el) =>
        _parentMap.TryGetValue(el.Id, out var p) ? p : null;

    List<ElementInfo> GetChildren(ElementInfo el) =>
        _childrenMap.TryGetValue(el.Id, out var c) ? c : [];

    int GetChildIndex(ElementInfo el)
    {
        var parent = GetParent(el);
        if (parent == null) return 0;
        var siblings = GetChildren(parent);
        return siblings.IndexOf(el);
    }

    // --- Selectors ---

    public Selector<ElementInfo> Type(NamespacePrefix prefix, string name) =>
        elements => elements.Where(e =>
            e.Type.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            e.FullType.Equals(name, StringComparison.OrdinalIgnoreCase));

    public Selector<ElementInfo> Universal(NamespacePrefix prefix) =>
        elements => elements;

    public Selector<ElementInfo> Id(string id) =>
        elements => elements.Where(e =>
            string.Equals(e.AutomationId, id, StringComparison.OrdinalIgnoreCase));

    public Selector<ElementInfo> Class(string clazz) =>
        elements => elements.Where(e =>
            e.StyleClass != null && e.StyleClass.Any(sc =>
                sc.Equals(clazz, StringComparison.OrdinalIgnoreCase)));

    // --- Attribute selectors ---

    string? GetAttribute(ElementInfo el, string name)
    {
        // Synthetic MAUI pseudo-class attributes
        if (name.StartsWith("__maui-", StringComparison.OrdinalIgnoreCase))
        {
            return name.ToLowerInvariant() switch
            {
                "__maui-visible" => el.IsVisible ? "true" : null,
                "__maui-hidden" => !el.IsVisible ? "true" : null,
                "__maui-enabled" => el.IsEnabled ? "true" : null,
                "__maui-disabled" => !el.IsEnabled ? "true" : null,
                "__maui-focused" => el.IsFocused ? "true" : null,
                _ => null
            };
        }

        return name.ToLowerInvariant() switch
        {
            "text" => el.Text,
            "value" => el.Value,
            "automationid" => el.AutomationId,
            "type" => el.Type,
            "fulltype" => el.FullType,
            "id" => el.Id,
            "opacity" => el.Opacity.ToString("F2"),
            "isvisible" => el.IsVisible.ToString(),
            "isenabled" => el.IsEnabled.ToString(),
            "isfocused" => el.IsFocused.ToString(),
            "nativetype" => el.NativeType,
            _ => el.NativeProperties?.TryGetValue(name, out var v) == true ? v : null
        };
    }

    public Selector<ElementInfo> AttributeExists(NamespacePrefix prefix, string name) =>
        elements => elements.Where(e => !string.IsNullOrEmpty(GetAttribute(e, name)));

    public Selector<ElementInfo> AttributeExact(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
            string.Equals(GetAttribute(e, name), value, StringComparison.OrdinalIgnoreCase));

    public Selector<ElementInfo> AttributeIncludes(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
        {
            var attr = GetAttribute(e, name);
            if (attr == null) return false;
            return attr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                       .Any(w => w.Equals(value, StringComparison.OrdinalIgnoreCase));
        });

    public Selector<ElementInfo> AttributeDashMatch(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
        {
            var attr = GetAttribute(e, name);
            if (attr == null) return false;
            return attr.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                   attr.StartsWith(value + "-", StringComparison.OrdinalIgnoreCase);
        });

    public Selector<ElementInfo> AttributePrefixMatch(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
            GetAttribute(e, name)?.StartsWith(value, StringComparison.OrdinalIgnoreCase) == true);

    public Selector<ElementInfo> AttributeSuffixMatch(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
            GetAttribute(e, name)?.EndsWith(value, StringComparison.OrdinalIgnoreCase) == true);

    public Selector<ElementInfo> AttributeSubstring(NamespacePrefix prefix, string name, string value) =>
        elements => elements.Where(e =>
            GetAttribute(e, name)?.Contains(value, StringComparison.OrdinalIgnoreCase) == true);

    // --- Pseudo-classes ---

    public Selector<ElementInfo> FirstChild() =>
        elements => elements.Where(e => GetChildIndex(e) == 0);

    public Selector<ElementInfo> LastChild() =>
        elements => elements.Where(e =>
        {
            var parent = GetParent(e);
            if (parent == null) return true;
            var siblings = GetChildren(parent);
            return siblings.Count > 0 && siblings[^1].Id == e.Id;
        });

    public Selector<ElementInfo> NthChild(int a, int b) =>
        elements => elements.Where(e =>
        {
            var index = GetChildIndex(e) + 1; // 1-based
            return MatchesNth(a, b, index);
        });

    public Selector<ElementInfo> NthLastChild(int a, int b) =>
        elements => elements.Where(e =>
        {
            var parent = GetParent(e);
            if (parent == null) return true;
            var siblings = GetChildren(parent);
            var index = siblings.Count - GetChildIndex(e); // 1-based from end
            return MatchesNth(a, b, index);
        });

    public Selector<ElementInfo> OnlyChild() =>
        elements => elements.Where(e =>
        {
            var parent = GetParent(e);
            if (parent == null) return true;
            return GetChildren(parent).Count == 1;
        });

    public Selector<ElementInfo> Empty() =>
        elements => elements.Where(e => e.Children is null or { Count: 0 });

    // --- Combinators ---

    public Selector<ElementInfo> Child() =>
        elements => elements.SelectMany(e => GetChildren(e));

    public Selector<ElementInfo> Descendant() =>
        elements =>
        {
            var ancestorIds = new HashSet<string>(elements.Select(e => e.Id));
            return _allElements.Where(e =>
            {
                var current = GetParent(e);
                while (current != null)
                {
                    if (ancestorIds.Contains(current.Id))
                        return true;
                    current = GetParent(current);
                }
                return false;
            });
        };

    public Selector<ElementInfo> Adjacent() =>
        elements =>
        {
            var results = new List<ElementInfo>();
            foreach (var el in elements)
            {
                var parent = GetParent(el);
                if (parent == null) continue;
                var siblings = GetChildren(parent);
                var idx = siblings.IndexOf(el);
                if (idx >= 0 && idx + 1 < siblings.Count)
                    results.Add(siblings[idx + 1]);
            }
            return results;
        };

    public Selector<ElementInfo> GeneralSibling() =>
        elements =>
        {
            var results = new List<ElementInfo>();
            foreach (var el in elements)
            {
                var parent = GetParent(el);
                if (parent == null) continue;
                var siblings = GetChildren(parent);
                var idx = siblings.IndexOf(el);
                if (idx >= 0)
                {
                    for (int i = idx + 1; i < siblings.Count; i++)
                        results.Add(siblings[i]);
                }
            }
            return results.Distinct();
        };

    // --- Helpers ---

    static bool MatchesNth(int a, int b, int index)
    {
        if (a == 0)
            return index == b;

        var n = (index - b) / (double)a;
        return n >= 0 && n == Math.Floor(n);
    }
}
