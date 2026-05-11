using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>
/// Parses XAML files into a semantic UI model, filtering to accessibility-relevant elements only.
/// </summary>
internal static class XamlFileParser
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2009/xaml";
    private static readonly XNamespace Maui = "http://schemas.microsoft.com/dotnet/2021/maui";

    // Elements that ARE semantic — included in output
    private static readonly HashSet<string> SemanticElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Label", "Button", "ImageButton", "Entry", "Editor", "SearchBar",
        "Slider", "Stepper", "Switch", "CheckBox", "RadioButton",
        "Picker", "DatePicker", "TimePicker",
        "Image", "ActivityIndicator", "ProgressBar",
        "CollectionView", "ListView", "CarouselView",
        "WebView",
    };

    // Layout containers — skipped but children walked
    private static readonly HashSet<string> StructuralElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "Grid", "StackLayout", "VerticalStackLayout", "HorizontalStackLayout",
        "FlexLayout", "AbsoluteLayout", "ScrollView",
        "Border", "Frame", "BoxView", "ContentView", "ContentPresenter",
        "Shadow", "RefreshView", "SwipeView",
    };

    // Elements we completely ignore (resources, styles, non-visual)
    private static readonly HashSet<string> IgnoredElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "ResourceDictionary", "Style", "Setter", "VisualStateManager.VisualStateGroups",
        "VisualStateGroupList", "VisualStateGroup", "VisualState",
        "Brush", "SolidColorBrush", "LinearGradientBrush", "GradientStop",
        "Shadow", "ColumnDefinition", "RowDefinition",
        "ColumnDefinitionCollection", "RowDefinitionCollection",
        "FlyoutItem.Icon", "Tab.Icon",
    };

    /// <summary>Parse a XAML file from its content string.</summary>
    public static PageModel? Parse(string filePath, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(content!);
        }
        catch
        {
            return null;
        }

        if (doc.Root == null)
            return null;

        var root = doc.Root;
        var xClass = root.Attribute(X + "Class")?.Value;
        if (string.IsNullOrWhiteSpace(xClass))
            return null;

        var lastDot = xClass!.LastIndexOf('.');
        var ns = lastDot > 0 ? xClass.Substring(0, lastDot) : "";
        var className = lastDot > 0 ? xClass.Substring(lastDot + 1) : xClass;
        var rootType = root.Name.LocalName;

        // Extract relative file path
        var relPath = ExtractRelativePath(filePath);

        var page = new PageModel
        {
            ClassName = className,
            Namespace = ns,
            RootType = rootType,
            FilePath = relPath,
        };

        // Special handling for Shell
        if (rootType == "Shell")
        {
            page.Elements.AddRange(ParseShellElements(root));
            return page;
        }

        // Walk the tree
        page.Elements.AddRange(WalkElement(root));

        return page;
    }

    private static List<UiElement> WalkElement(XElement element)
    {
        var results = new List<UiElement>();
        var localName = element.Name.LocalName;

        // Skip property elements (e.g., Grid.RowDefinitions, CollectionView.ItemTemplate)
        if (localName.Contains("."))
            return WalkPropertyElement(element);

        // Completely ignored elements
        if (IgnoredElements.Contains(localName))
            return results;

        // Check for BindableLayout on structural elements
        var bindableItemsSource = GetAttachedPropertyValue(element, "BindableLayout.ItemsSource");
        if (bindableItemsSource != null)
        {
            var blElement = CreateBindableLayoutElement(element, localName, bindableItemsSource);
            results.Add(blElement);
            return results;
        }

        // Semantic elements get rendered
        if (SemanticElements.Contains(localName))
        {
            var uiElement = ExtractSemanticElement(element, localName);
            if (uiElement != null)
                results.Add(uiElement);
            return results;
        }

        // Structural elements — check for SemanticProperties promotion
        if (StructuralElements.Contains(localName) || IsUnknownElement(localName))
        {
            var semantics = AccessibilityExtractor.Extract(element);
            if (semantics.Description != null && semantics.Description.Length > 0)
            {
                // Promoted: developer explicitly set a description
                var promoted = new UiElement
                {
                    TypeName = localName,
                    Text = semantics.Description,
                    Semantics = semantics,
                };
                results.Add(promoted);
                return results;
            }

            if (AccessibilityExtractor.IsDecorative(semantics))
                return results; // Explicitly decorative, skip entirely

            // Walk children
            foreach (var child in element.Elements())
            {
                results.AddRange(WalkElement(child));
            }
            return results;
        }

        // For anything else (custom controls), try walking children
        foreach (var child in element.Elements())
        {
            results.AddRange(WalkElement(child));
        }
        return results;
    }

    private static UiElement? ExtractSemanticElement(XElement element, string typeName)
    {
        var semantics = AccessibilityExtractor.Extract(element);

        // If explicitly decorative, skip
        if (AccessibilityExtractor.IsDecorative(semantics))
            return null;

        var ui = new UiElement
        {
            TypeName = typeName,
            Semantics = semantics,
        };

        // Extract text/content based on element type
        switch (typeName)
        {
            case "Label":
                ExtractTextProperty(element, ui);
                break;

            case "Button":
            case "ImageButton":
                ExtractTextProperty(element, ui);
                ExtractCommand(element, ui);
                if (typeName == "ImageButton" && ui.Text == null && ui.TextBinding == null)
                    ui.Source = GetAttr(element, "Source");
                break;

            case "Entry":
            case "Editor":
            case "SearchBar":
                ExtractTextProperty(element, ui);
                ui.Placeholder = GetAttr(element, "Placeholder");
                break;

            case "Slider":
                ui.Minimum = GetAttr(element, "Minimum") ?? "0";
                ui.Maximum = GetAttr(element, "Maximum") ?? "1";
                var valueStr = GetAttr(element, "Value");
                var valueBind = MarkupExtensionParser.TryParseBinding(valueStr);
                ui.ValueBinding = valueBind?.Path;
                break;

            case "Stepper":
                ui.Minimum = GetAttr(element, "Minimum") ?? "0";
                ui.Maximum = GetAttr(element, "Maximum") ?? "100";
                var stepValueStr = GetAttr(element, "Value");
                var stepValueBind = MarkupExtensionParser.TryParseBinding(stepValueStr);
                ui.ValueBinding = stepValueBind?.Path;
                break;

            case "Switch":
            case "CheckBox":
                var isToggled = GetAttr(element, "IsToggled") ?? GetAttr(element, "IsChecked");
                var toggleBind = MarkupExtensionParser.TryParseBinding(isToggled);
                if (toggleBind != null) ui.TextBinding = toggleBind;
                else if (isToggled != null) ui.Text = isToggled;
                break;

            case "RadioButton":
                ExtractTextProperty(element, ui);
                break;

            case "Picker":
                ui.Title = GetAttr(element, "Title");
                var selectedBind = MarkupExtensionParser.TryParseBinding(GetAttr(element, "SelectedItem"));
                ui.SelectedItemBinding = selectedBind?.Path;
                break;

            case "DatePicker":
            case "TimePicker":
                var dateBind = MarkupExtensionParser.TryParseBinding(
                    GetAttr(element, typeName == "DatePicker" ? "Date" : "Time"));
                if (dateBind != null) ui.TextBinding = dateBind;
                break;

            case "Image":
                ui.Source = GetAttr(element, "Source");
                break;

            case "ActivityIndicator":
                var runBind = MarkupExtensionParser.TryParseBinding(GetAttr(element, "IsRunning"));
                if (runBind != null) ui.TextBinding = runBind;
                break;

            case "ProgressBar":
                var progBind = MarkupExtensionParser.TryParseBinding(GetAttr(element, "Progress"));
                if (progBind != null) ui.TextBinding = progBind;
                break;

            case "CollectionView":
            case "ListView":
            case "CarouselView":
                ExtractCollectionView(element, ui);
                break;
        }

        // Extract visibility condition
        ui.Condition = ConditionalDetector.DetectCondition(element);

        return ui;
    }

    private static void ExtractTextProperty(XElement element, UiElement ui)
    {
        var text = GetAttr(element, "Text") ?? GetAttr(element, "Content");
        if (text != null)
        {
            var binding = MarkupExtensionParser.TryParseBinding(text);
            if (binding != null)
                ui.TextBinding = binding;
            else
                ui.Text = text;
        }
    }

    private static void ExtractCommand(XElement element, UiElement ui)
    {
        var cmd = GetAttr(element, "Command");
        if (cmd != null)
        {
            var cmdBinding = MarkupExtensionParser.TryParseBinding(cmd);
            ui.CommandName = cmdBinding?.Path ?? cmd;
        }

        var param = GetAttr(element, "CommandParameter");
        if (param != null)
        {
            var paramBinding = MarkupExtensionParser.TryParseBinding(param);
            ui.CommandParameter = paramBinding?.ToDisplayString() ?? param;
        }
    }

    private static void ExtractCollectionView(XElement element, UiElement ui)
    {
        var itemsSource = GetAttr(element, "ItemsSource");
        var binding = MarkupExtensionParser.TryParseBinding(itemsSource);
        ui.ItemsSourceBinding = binding?.Path ?? itemsSource;

        ui.IsGrouped = GetAttr(element, "IsGrouped")?.Equals("True", StringComparison.OrdinalIgnoreCase) == true;

        // Extract templates from child property elements
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name.EndsWith(".ItemTemplate"))
            {
                ui.ItemTemplate = ExtractTemplateContent(child);
            }
            else if (name.EndsWith(".HeaderTemplate"))
            {
                ui.HeaderTemplate = ExtractTemplateContent(child);
            }
            else if (name.EndsWith(".FooterTemplate"))
            {
                ui.FooterTemplate = ExtractTemplateContent(child);
            }
            else if (name.EndsWith(".GroupHeaderTemplate"))
            {
                ui.GroupHeaderTemplate = ExtractTemplateContent(child);
            }
            else if (name.EndsWith(".GroupFooterTemplate"))
            {
                ui.GroupFooterTemplate = ExtractTemplateContent(child);
            }
            else if (name.EndsWith(".EmptyView"))
            {
                ui.EmptyView = ExtractChildElements(child);
            }
            else if (name.EndsWith(".ItemTemplate") == false && name.Contains(".") == false)
            {
                // Direct child DataTemplate without property element wrapper
                if (name == "DataTemplate")
                {
                    ui.ItemTemplate = ExtractTemplateContent(child);
                }
            }
        }

        // Check for EmptyView as direct attribute or child element
        if (ui.EmptyView == null)
        {
            foreach (var child in element.Elements())
            {
                if (!child.Name.LocalName.Contains("."))
                {
                    var emptyViewChildren = new List<UiElement>();
                    foreach (var inner in child.Elements())
                    {
                        emptyViewChildren.AddRange(WalkElement(inner));
                    }
                    // If it's not a template, walk it as regular content (these are EmptyView inline elements)
                }
            }
        }

        // Extract DataTemplateSelector variants from the ItemTemplate if it's a selector reference
        // (Tier 2 - we detect the selector usage but can't resolve it at XAML level without code-behind)

        ui.Condition = ConditionalDetector.DetectCondition(element);
    }

    private static List<UiElement> ExtractTemplateContent(XElement templatePropertyElement)
    {
        var results = new List<UiElement>();
        foreach (var child in templatePropertyElement.Elements())
        {
            if (child.Name.LocalName == "DataTemplate")
            {
                foreach (var inner in child.Elements())
                {
                    results.AddRange(WalkElement(inner));
                }
            }
            else
            {
                results.AddRange(WalkElement(child));
            }
        }
        return results;
    }

    private static List<UiElement> ExtractChildElements(XElement parent)
    {
        var results = new List<UiElement>();
        foreach (var child in parent.Elements())
        {
            results.AddRange(WalkElement(child));
        }
        return results;
    }

    private static UiElement CreateBindableLayoutElement(XElement element, string typeName, string itemsSource)
    {
        var binding = MarkupExtensionParser.TryParseBinding(itemsSource);
        var ui = new UiElement
        {
            TypeName = typeName,
            IsBindableLayout = true,
            BindableLayoutItemsSource = binding?.Path ?? itemsSource,
            Semantics = AccessibilityExtractor.Extract(element),
            Condition = ConditionalDetector.DetectCondition(element),
        };

        // Find ItemTemplate
        var itemTemplate = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "BindableLayout.ItemTemplate");
        if (itemTemplate != null)
        {
            ui.BindableLayoutItemTemplate = ExtractTemplateContent(itemTemplate);
        }

        return ui;
    }

    private static List<UiElement> WalkPropertyElement(XElement element)
    {
        // Property elements like Grid.RowDefinitions — skip most
        var localName = element.Name.LocalName;

        // Special handling for known useful property elements
        if (localName.EndsWith(".EmptyView") || localName.EndsWith(".EmptyViewTemplate"))
        {
            return ExtractChildElements(element);
        }

        // Walk through for nested content (like CollectionView.Header containing real content)
        if (localName.EndsWith(".Header") || localName.EndsWith(".Footer"))
        {
            return ExtractChildElements(element);
        }

        return new List<UiElement>();
    }

    private static List<UiElement> ParseShellElements(XElement root)
    {
        return ShellParser.ParseShell(root);
    }

    private static string? GetAttr(XElement element, string name)
    {
        return element.Attribute(name)?.Value;
    }

    private static string? GetAttachedPropertyValue(XElement element, string attachedPropName)
    {
        // Look for BindableLayout.ItemsSource as an attribute
        var attr = element.Attribute(attachedPropName);
        if (attr != null) return attr.Value;

        // Also check child property elements
        var propElement = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName == attachedPropName);
        return propElement?.Value;
    }

    private static bool IsUnknownElement(string name)
    {
        // If not in semantic or structural lists, it could be a custom control
        return !SemanticElements.Contains(name) && !StructuralElements.Contains(name) && !IgnoredElements.Contains(name);
    }

    private static string ExtractRelativePath(string fullPath)
    {
        // Try to get a clean relative path
        if (string.IsNullOrEmpty(fullPath))
            return "";

        var normalized = fullPath.Replace('\\', '/');

        // Look for common project markers
        var markers = new[] { "/Pages/", "/Views/", "/Resources/" };
        foreach (var marker in markers)
        {
            var idx = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return normalized.Substring(idx + 1);
        }

        // Fallback: just the file name
        return Path.GetFileName(fullPath);
    }
}
