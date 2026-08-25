namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Condition under which an element is visible.</summary>
internal sealed class VisibilityCondition
{
    public string Property { get; set; } = "";
    public string Value { get; set; } = "true";
    public bool IsInverted { get; set; }

    public override string ToString()
    {
        if (IsInverted)
        {
            // For inverse converters on boolean bindings, show "= false"
            // For DataTrigger hiding, show "hidden when Property = Value"
            if (Value == "true")
                return $"visible when {Property} = false";
            return $"hidden when {Property} = {Value}";
        }
        return $"visible when {Property} = {Value}";
    }
}
