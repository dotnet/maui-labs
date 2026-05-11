using System.Xml.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>Extracts SemanticProperties from XAML element attributes.</summary>
internal static class AccessibilityExtractor
{
    // SemanticProperties namespace — these are attached properties on the element
    // In XAML they appear as: SemanticProperties.Description="..."
    private const string SemanticDescription = "SemanticProperties.Description";
    private const string SemanticHint = "SemanticProperties.Hint";
    private const string SemanticHeadingLevel = "SemanticProperties.HeadingLevel";

    /// <summary>
    /// Extract SemanticProperties from an XElement's attributes.
    /// These appear as "SemanticProperties.Description" etc. in XAML.
    /// </summary>
    public static SemanticInfo Extract(XElement element)
    {
        var info = new SemanticInfo();

        foreach (var attr in element.Attributes())
        {
            var name = attr.Name.LocalName;

            if (name == SemanticDescription || name == "Description" && IsSemanticNamespace(attr))
            {
                info.Description = attr.Value;
            }
            else if (name == SemanticHint || name == "Hint" && IsSemanticNamespace(attr))
            {
                info.Hint = attr.Value;
            }
            else if (name == SemanticHeadingLevel || name == "HeadingLevel" && IsSemanticNamespace(attr))
            {
                info.HeadingLevel = ParseHeadingLevel(attr.Value);
            }
        }

        return info;
    }

    /// <summary>
    /// Check if the element is explicitly marked as decorative (empty Description).
    /// </summary>
    public static bool IsDecorative(SemanticInfo info)
    {
        return info.Description != null && info.Description.Length == 0;
    }

    private static bool IsSemanticNamespace(XAttribute attr)
    {
        // Check if the attribute has a namespace prefix that resolves to SemanticProperties
        return attr.Name.NamespaceName.Contains("SemanticProperties");
    }

    private static int? ParseHeadingLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // Handle "Level1", "Level2", etc.
        if (value.StartsWith("Level", StringComparison.OrdinalIgnoreCase) && value.Length > 5)
        {
            if (int.TryParse(value.Substring(5), out var level) && level >= 1 && level <= 9)
                return level;
        }

        // Handle plain numbers
        if (int.TryParse(value, out var num) && num >= 1 && num <= 9)
            return num;

        return null;
    }
}
