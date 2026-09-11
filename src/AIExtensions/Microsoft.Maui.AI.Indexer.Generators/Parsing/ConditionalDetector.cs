using System.Xml.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>Detects conditional visibility from IsVisible bindings and DataTriggers.</summary>
internal static class ConditionalDetector
{
    /// <summary>
    /// Check if an element has conditional visibility.
    /// </summary>
    public static VisibilityCondition? DetectCondition(XElement element)
    {
        // Check IsVisible attribute binding
        var isVisible = element.Attribute("IsVisible")?.Value;
        if (isVisible != null)
        {
            var binding = MarkupExtensionParser.TryParseBinding(isVisible);
            if (binding != null && binding.Path != null && binding.Path != ".")
            {
                var condition = new VisibilityCondition
                {
                    Property = binding.Path,
                    Value = "true",
                };

                // Check for InverseBooleanConverter or negation patterns
                if (binding.Converter != null &&
                    (binding.Converter.Contains("Inverse", System.StringComparison.OrdinalIgnoreCase) ||
                     binding.Converter.Contains("Not", System.StringComparison.OrdinalIgnoreCase) ||
                     binding.Converter.Contains("Negate", System.StringComparison.OrdinalIgnoreCase)))
                {
                    condition.IsInverted = true;
                }

                return condition;
            }

            // Literal IsVisible="False" means always hidden — skip in practice
            if (isVisible.Equals("False", System.StringComparison.OrdinalIgnoreCase))
            {
                return new VisibilityCondition { Property = "(always hidden)", Value = "false" };
            }
        }

        // Check for DataTrigger on IsVisible in child elements
        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;
            if (localName.EndsWith(".Triggers"))
            {
                foreach (var trigger in child.Elements())
                {
                    var triggerCondition = ParseDataTrigger(trigger);
                    if (triggerCondition != null)
                        return triggerCondition;
                }
            }
        }

        return null;
    }

    private static VisibilityCondition? ParseDataTrigger(XElement trigger)
    {
        var localName = trigger.Name.LocalName;
        if (localName != "DataTrigger")
            return null;

        var bindingStr = trigger.Attribute("Binding")?.Value;
        var value = trigger.Attribute("Value")?.Value;

        if (bindingStr == null || value == null)
            return null;

        var binding = MarkupExtensionParser.TryParseBinding(bindingStr);
        if (binding?.Path == null)
            return null;

        // Check if this trigger sets IsVisible
        foreach (var setter in trigger.Elements())
        {
            if (setter.Name.LocalName == "Setter")
            {
                var property = setter.Attribute("Property")?.Value;
                var setterValue = setter.Attribute("Value")?.Value;
                if (property == "IsVisible" && setterValue != null)
                {
                    // If the setter sets IsVisible to False, it's a "hidden when" condition
                    var isHiddenTrigger = setterValue.Equals("False", System.StringComparison.OrdinalIgnoreCase);

                    return new VisibilityCondition
                    {
                        Property = binding.Path,
                        Value = value,
                        IsInverted = isHiddenTrigger,
                    };
                }
            }
        }

        return null;
    }
}
