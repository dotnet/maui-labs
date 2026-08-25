using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>Parses Shell XAML for tabs, flyout items, and the app's home screen.</summary>
internal static class ShellParser
{
    /// <summary>Parse Shell root element into semantic UI elements representing navigation.</summary>
    public static List<SemanticNode> ParseShell(XElement shellRoot)
    {
        var elements = new List<SemanticNode>();

        foreach (var child in shellRoot.Elements())
        {
            var name = child.Name.LocalName;

            if (IsNavigationElement(name))
            {
                ParseShellNavigationElement(child, elements, new List<string>());
            }
            else if (!name.Contains("."))
            {
                // Recurse into non-property elements
                elements.AddRange(ParseShell(child));
            }
        }

        return elements;
    }

    private static void ParseShellNavigationElement(
        XElement element,
        List<SemanticNode> elements,
        List<string> parentRouteSegments)
    {
        if (IsExcludedWithChildren(element))
            return;

        var name = element.Name.LocalName;
        var title = element.Attribute("Title")?.Value;
        var routeSegments = AppendRoute(parentRouteSegments, element.Attribute("Route")?.Value);

        if (name == "ShellContent")
        {
            var (targetName, targetTypeName) = ExtractContentPage(element);
            var ui = new SemanticNode
            {
                TypeName = "ShellContent",
                Text = title ?? "",
                NavigationTarget = targetName,
                NavigationTargetTypeName = targetTypeName,
                NavigationRoute = BuildAbsoluteRoute(routeSegments),
            };

            elements.Add(ui);
        }
        else if (name == "TabBar" || name == "FlyoutItem" || name == "ShellItem")
        {
            // Walk children for ShellContent/Tab items
            foreach (var child in element.Elements())
            {
                if (!child.Name.LocalName.Contains("."))
                    ParseShellNavigationElement(child, elements, routeSegments);
            }
        }
        else if (name == "Tab" || name == "ShellSection")
        {
            var ui = new SemanticNode
            {
                TypeName = "Tab",
                Text = title ?? "",
            };

            // Walk children for ShellContent
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "ShellContent")
                {
                    if (IsExcludedWithChildren(child))
                        continue;

                    var shellContentTitle = child.Attribute("Title")?.Value;
                    var (targetName, targetTypeName) = ExtractContentPage(child);
                    ui.Children.Add(new SemanticNode
                    {
                        TypeName = "ShellContent",
                        Text = shellContentTitle ?? "",
                        NavigationTarget = targetName,
                        NavigationTargetTypeName = targetTypeName,
                        NavigationRoute = BuildAbsoluteRoute(
                            AppendRoute(routeSegments, child.Attribute("Route")?.Value)),
                    });
                }
            }

            elements.Add(ui);
        }
    }

    private static List<string> AppendRoute(List<string> parent, string? route)
    {
        var result = new List<string>(parent);
        if (!string.IsNullOrWhiteSpace(route))
            result.Add(route!);
        return result;
    }

    private static string? BuildAbsoluteRoute(List<string> segments)
        => segments.Count == 0 ? null : $"//{string.Join("/", segments)}";

    private static bool IsNavigationElement(string name)
        => name == "TabBar"
            || name == "Tab"
            || name == "FlyoutItem"
            || name == "ShellItem"
            || name == "ShellSection"
            || name == "ShellContent";

    private static bool IsExcludedWithChildren(XElement element)
    {
        var value = element.Attributes()
            .FirstOrDefault(static attribute =>
                string.Equals(
                    attribute.Name.LocalName,
                    "IndexingProperties.ExcludeWithChildren",
                    StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return bool.TryParse(value, out var excluded) && excluded;
    }

    /// <summary>
    /// Extract the hosted page's simple class name from a ShellContent, whether declared as
    /// <c>ContentTemplate="{DataTemplate pages:MainPage}"</c> or a nested
    /// <c>&lt;ShellContent.ContentTemplate&gt;&lt;DataTemplate&gt;&lt;pages:MainPage/&gt;...</c>.
    /// </summary>
    private static (string? Name, string? TypeName) ExtractContentPage(
        XElement shellContent)
    {
        // Inline markup extension form: ContentTemplate="{DataTemplate pages:MainPage}"
        var attr = shellContent.Attribute("ContentTemplate")?.Value;
        var typeReference = ExtractTypeFromDataTemplate(attr);
        if (typeReference != null)
            return ResolveTypeReference(shellContent, typeReference);

        // Property-element form: <ShellContent.ContentTemplate><DataTemplate><pages:MainPage/>...
        foreach (var propEl in shellContent.Elements())
        {
            if (!propEl.Name.LocalName.EndsWith(".ContentTemplate"))
                continue;
            foreach (var dt in propEl.Elements())
            {
                foreach (var content in dt.Elements())
                {
                    return (
                        content.Name.LocalName,
                        QualifyTypeName(
                            content.Name.NamespaceName,
                            content.Name.LocalName));
                }
            }
        }

        return (null, null);
    }

    /// <summary>Pull the type local name out of a <c>{DataTemplate prefix:TypeName}</c> expression.</summary>
    private static string? ExtractTypeFromDataTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var v = value!.Trim();
        if (!v.StartsWith("{") || !v.EndsWith("}"))
            return null;

        // Strip braces and the markup-extension name (DataTemplate / x:Type / Type)
        var inner = v.Substring(1, v.Length - 2).Trim();
        var spaceIdx = inner.IndexOf(' ');
        if (spaceIdx < 0)
            return null;

        var typeRef = inner.Substring(spaceIdx + 1).Trim();
        return typeRef.Length > 0 ? typeRef : null;
    }

    private static (string Name, string TypeName) ResolveTypeReference(
        XElement element,
        string typeReference)
    {
        var colonIndex = typeReference.LastIndexOf(':');
        if (colonIndex < 0)
            return (typeReference, typeReference);

        var prefix = typeReference.Substring(0, colonIndex);
        var name = typeReference.Substring(colonIndex + 1);
        var xmlNamespace = element.GetNamespaceOfPrefix(prefix)?.NamespaceName;
        return (name, QualifyTypeName(xmlNamespace, name));
    }

    private static string QualifyTypeName(string? xmlNamespace, string name)
    {
        const string clrNamespacePrefix = "clr-namespace:";
        if (xmlNamespace?.StartsWith(
            clrNamespacePrefix,
            StringComparison.Ordinal) != true)
        {
            return name;
        }

        var clrNamespace = xmlNamespace.Substring(clrNamespacePrefix.Length);
        var assemblySeparator = clrNamespace.IndexOf(';');
        if (assemblySeparator >= 0)
            clrNamespace = clrNamespace.Substring(0, assemblySeparator);

        return string.IsNullOrWhiteSpace(clrNamespace)
            ? name
            : $"{clrNamespace}.{name}";
    }
}
