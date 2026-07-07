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

            if (name == "TabBar" || name == "Tab" || name == "FlyoutItem" || name == "ShellContent")
            {
                ParseShellNavigationElement(child, elements);
            }
            else if (!name.Contains("."))
            {
                // Recurse into non-property elements
                elements.AddRange(ParseShell(child));
            }
        }

        return elements;
    }

    private static void ParseShellNavigationElement(XElement element, List<SemanticNode> elements)
    {
        var name = element.Name.LocalName;
        var title = element.Attribute("Title")?.Value;

        if (name == "ShellContent")
        {
            // Routes are an implementation detail and are never captured. Only the human-visible
            // Title (if any) and the hosted page (used to identify HOME) are kept.
            var ui = new SemanticNode
            {
                TypeName = "ShellContent",
                Text = title ?? "",
                NavigationTarget = ExtractContentPage(element),
            };

            elements.Add(ui);
        }
        else if (name == "TabBar" || name == "FlyoutItem")
        {
            // Walk children for ShellContent/Tab items
            foreach (var child in element.Elements())
            {
                if (!child.Name.LocalName.Contains("."))
                    ParseShellNavigationElement(child, elements);
            }
        }
        else if (name == "Tab")
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
                    var shellContentTitle = child.Attribute("Title")?.Value;
                    ui.Children.Add(new SemanticNode
                    {
                        TypeName = "ShellContent",
                        Text = shellContentTitle ?? "",
                        NavigationTarget = ExtractContentPage(child),
                    });
                }
            }

            elements.Add(ui);
        }
    }

    /// <summary>
    /// Extract the hosted page's simple class name from a ShellContent, whether declared as
    /// <c>ContentTemplate="{DataTemplate pages:MainPage}"</c> or a nested
    /// <c>&lt;ShellContent.ContentTemplate&gt;&lt;DataTemplate&gt;&lt;pages:MainPage/&gt;...</c>.
    /// </summary>
    private static string? ExtractContentPage(XElement shellContent)
    {
        // Inline markup extension form: ContentTemplate="{DataTemplate pages:MainPage}"
        var attr = shellContent.Attribute("ContentTemplate")?.Value;
        var fromAttr = ExtractTypeFromDataTemplate(attr);
        if (fromAttr != null)
            return fromAttr;

        // Property-element form: <ShellContent.ContentTemplate><DataTemplate><pages:MainPage/>...
        foreach (var propEl in shellContent.Elements())
        {
            if (!propEl.Name.LocalName.EndsWith(".ContentTemplate"))
                continue;
            foreach (var dt in propEl.Elements())
            {
                foreach (var content in dt.Elements())
                    return content.Name.LocalName;
            }
        }

        return null;
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
        // Drop any xmlns prefix (e.g. "pages:MainPage" -> "MainPage")
        var colonIdx = typeRef.LastIndexOf(':');
        if (colonIdx >= 0)
            typeRef = typeRef.Substring(colonIdx + 1);

        return typeRef.Length > 0 ? typeRef : null;
    }
}
