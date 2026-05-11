using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>Parses Shell XAML for routes, tabs, and flyout items.</summary>
internal static class ShellParser
{
    /// <summary>Parse Shell root element into semantic UI elements representing navigation.</summary>
    public static List<UiElement> ParseShell(XElement shellRoot)
    {
        var elements = new List<UiElement>();

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

    private static void ParseShellNavigationElement(XElement element, List<UiElement> elements)
    {
        var name = element.Name.LocalName;
        var route = element.Attribute("Route")?.Value;
        var title = element.Attribute("Title")?.Value;
        var contentType = element.Attribute("ContentTemplate")?.Value;

        if (name == "ShellContent")
        {
            var pageType = element.Attribute("ContentTemplate")?.Value;
            // In MAUI, ShellContent often uses ContentTemplate="{DataTemplate local:PageType}"
            // or the Type attribute directly
            var ui = new UiElement
            {
                TypeName = "ShellContent",
                Text = title ?? route ?? "",
            };

            if (route != null)
                ui.CommandName = route; // Reuse CommandName to store route

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
            var ui = new UiElement
            {
                TypeName = "Tab",
                Text = title ?? "",
            };

            if (route != null)
                ui.CommandName = route;

            // Walk children for ShellContent
            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "ShellContent")
                {
                    var shellContentRoute = child.Attribute("Route")?.Value;
                    var shellContentTitle = child.Attribute("Title")?.Value;
                    ui.Children.Add(new UiElement
                    {
                        TypeName = "ShellContent",
                        Text = shellContentTitle ?? shellContentRoute ?? "",
                        CommandName = shellContentRoute,
                    });
                }
            }

            elements.Add(ui);
        }
    }
}
