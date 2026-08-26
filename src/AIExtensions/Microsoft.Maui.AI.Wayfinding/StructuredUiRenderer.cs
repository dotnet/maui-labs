using System.Text;
using Microsoft.Maui.AI.Indexer;

namespace Microsoft.Maui.AI.Wayfinding;

internal static class StructuredUiRenderer
{
    public static string Render(IReadOnlyList<IndexedElement> elements)
        => string.Join('\n', elements.Select(RenderElement));

    public static string RenderElement(IndexedElement element)
    {
        var line = new StringBuilder();
        line.Append(' ', element.Depth * 2);
        line.Append("- ");
        line.Append(GetLabel(element.Kind));
        line.Append(':');
        if (element.IsDynamic)
            line.Append(" \"[dynamic text]\"");
        else if (!string.IsNullOrWhiteSpace(element.Text))
            line.Append($" \"{Escape(element.Text)}\"");

        var annotations = new List<string>();
        if (!string.IsNullOrWhiteSpace(element.Placeholder))
            annotations.Add($"placeholder: \"{Escape(element.Placeholder)}\"");
        if (!string.IsNullOrWhiteSpace(element.AutomationId))
            annotations.Add($"automationId: \"{Escape(element.AutomationId)}\"");
        if (!string.IsNullOrWhiteSpace(element.Hint))
            annotations.Add($"hint: {element.Hint}");
        if (element.IsConditional)
            annotations.Add("shown in some states");
        if (element.IsActionable && element.Kind != IndexedElementKind.Action)
            annotations.Add("actionable");
        annotations.AddRange(element.State);
        if (annotations.Count > 0)
            line.Append($" [{string.Join(", ", annotations)}]");
        return line.ToString();
    }

    public static string GetSearchText(
        string? title,
        IReadOnlyList<IndexedElement> elements)
        => string.Join(
            ' ',
            new[] { title }
                .Concat(elements.SelectMany(element => new[]
                {
                    element.Text,
                    element.Hint,
                    element.Placeholder,
                    GetSearchTerms(element.Kind),
                }))
                .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string GetSearchTerms(IndexedElementKind kind)
        => kind switch
        {
            IndexedElementKind.Action => "action button link tap",
            IndexedElementKind.Input => "input entry editor field search",
            IndexedElementKind.Selection => "selection picker date time",
            IndexedElementKind.Toggle => "toggle switch checkbox radio",
            IndexedElementKind.Range => "range slider stepper",
            IndexedElementKind.Collection => "collection list carousel items",
            IndexedElementKind.Visual => "visual graphics chart image",
            _ => kind.ToString(),
        };

    private static string GetLabel(IndexedElementKind kind)
        => kind switch
        {
            IndexedElementKind.Unknown => "Control",
            IndexedElementKind.Group => "Group",
            IndexedElementKind.Text => "Text",
            IndexedElementKind.Heading => "Heading",
            IndexedElementKind.Action => "Action",
            IndexedElementKind.Input => "Input",
            IndexedElementKind.Selection => "Selection",
            IndexedElementKind.Toggle => "Toggle",
            IndexedElementKind.Range => "Range",
            IndexedElementKind.Collection => "Collection",
            IndexedElementKind.Image => "Image",
            IndexedElementKind.Visual => "Visual",
            IndexedElementKind.Status => "Status",
            IndexedElementKind.WebContent => "Web content",
            _ => "Control",
        };

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
