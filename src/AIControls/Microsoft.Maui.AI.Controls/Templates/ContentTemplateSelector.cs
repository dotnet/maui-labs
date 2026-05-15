using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Templates;

/// <summary>
/// Selects a <see cref="DataTemplate"/> based on the runtime type of an <see cref="AIContent"/> item.
/// </summary>
public class ContentTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for <see cref="TextContent"/>.</summary>
    public DataTemplate? TextContentTemplate { get; set; }

    /// <summary>Template for <see cref="FunctionCallContent"/>.</summary>
    public DataTemplate? FunctionCallContentTemplate { get; set; }

    /// <summary>Template for <see cref="FunctionResultContent"/>.</summary>
    public DataTemplate? FunctionResultContentTemplate { get; set; }

    /// <summary>Template for <see cref="DataContent"/>.</summary>
    public DataTemplate? DataContentTemplate { get; set; }

    /// <summary>Fallback template when no specific match is found.</summary>
    public DataTemplate? DefaultContentTemplate { get; set; }

    /// <summary>
    /// Optional dictionary mapping function names to custom templates.
    /// Checked for <see cref="FunctionCallContent"/> before the general template.
    /// </summary>
    public Dictionary<string, DataTemplate> FunctionTemplates { get; } = [];

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            FunctionCallContent fc when FunctionTemplates.TryGetValue(fc.Name, out var ft) => ft,
            FunctionCallContent => FunctionCallContentTemplate ?? DefaultContentTemplate ?? new DataTemplate(),
            FunctionResultContent => FunctionResultContentTemplate ?? DefaultContentTemplate ?? new DataTemplate(),
            TextContent => TextContentTemplate ?? DefaultContentTemplate ?? new DataTemplate(),
            DataContent => DataContentTemplate ?? DefaultContentTemplate ?? new DataTemplate(),
            _ => DefaultContentTemplate ?? new DataTemplate(),
        };
    }
}
