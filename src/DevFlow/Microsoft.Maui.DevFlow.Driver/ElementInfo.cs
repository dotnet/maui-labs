using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Element info returned from the Agent API (deserialization model).
/// </summary>
public class ElementInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("fullType")]
    public string FullType { get; set; } = string.Empty;

    [JsonPropertyName("framework")]
    public string Framework { get; set; } = "maui";

    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; set; }

    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role
    {
        get => _role ?? InferRole();
        set => _role = value;
    }

    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; }

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; }

    [JsonPropertyName("traits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traits
    {
        get => _traits ?? BuildTraits();
        set => _traits = value;
    }

    [JsonPropertyName("state")]
    public ElementStateInfo State
    {
        get => new()
        {
            Displayed = IsVisible,
            Enabled = IsEnabled,
            Selected = IsSelected,
            Focused = IsFocused,
            Opacity = Opacity
        };
        set
        {
            if (value == null)
                return;

            IsVisible = value.Displayed;
            IsEnabled = value.Enabled;
            IsSelected = value.Selected;
            IsFocused = value.Focused;
            Opacity = value.Opacity;
        }
    }

    [JsonPropertyName("bounds")]
    public BoundsInfo? Bounds { get; set; }

    [JsonPropertyName("gestures")]
    public List<string>? Gestures { get; set; }

    [JsonPropertyName("styleClass")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StyleClass { get; set; }

    [JsonPropertyName("style")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElementStyleInfo? Style
    {
        get => StyleClass is { Count: > 0 } ? new ElementStyleInfo { Classes = StyleClass } : null;
        set => StyleClass = value?.Classes;
    }

    [JsonPropertyName("nativeType")]
    public string? NativeType { get; set; }

    [JsonPropertyName("nativeProperties")]
    public Dictionary<string, string?>? NativeProperties { get; set; }

    [JsonPropertyName("nativeView")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ElementNativeViewInfo? NativeView
    {
        get => NativeType != null || NativeProperties != null
            ? new ElementNativeViewInfo
            {
                Type = NativeType,
                Properties = NativeProperties
            }
            : null;
        set
        {
            NativeType = value?.Type;
            NativeProperties = value?.Properties;
        }
    }

    [JsonPropertyName("frameworkProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? FrameworkProperties { get; set; }

    [JsonPropertyName("children")]
    public List<ElementInfo>? Children { get; set; }

    [JsonIgnore]
    public bool IsSelected { get; set; }

    private string? _role;
    private List<string>? _traits;

    private string? InferRole() => Type switch
    {
        "Window" => "window",
        "Button" or "ImageButton" => "button",
        "Entry" or "Editor" or "SearchBar" => "textbox",
        "CheckBox" => "checkbox",
        "RadioButton" => "radio",
        "Switch" => "switch",
        "Image" => "image",
        "CollectionView" or "ListView" or "CarouselView" => "list",
        "Label" when Gestures?.Contains("tap") == true => "link",
        _ => null
    };

    private List<string>? BuildTraits()
    {
        var traits = new List<string>();
        var role = Role;

        if (role is "button" or "textbox" or "checkbox" or "radio" or "switch" or "link")
            traits.Add("interactive");

        if (Gestures is { Count: > 0 } && !traits.Contains("interactive"))
            traits.Add("interactive");

        if (role is "button" or "textbox" or "checkbox" or "radio" or "switch" or "link" or "window" || IsFocused)
            traits.Add("focusable");

        if (Type is "ScrollView" or "CollectionView" or "ListView" or "CarouselView")
            traits.Add("scrollable");

        if (role == "heading")
            traits.Add("header");

        return traits.Count > 0 ? traits : null;
    }
}

public class BoundsInfo
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class ElementStateInfo
{
    [JsonPropertyName("displayed")]
    public bool Displayed { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }

    [JsonPropertyName("focused")]
    public bool Focused { get; set; }

    [JsonPropertyName("opacity")]
    public double Opacity { get; set; }
}

public class ElementStyleInfo
{
    [JsonPropertyName("classes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Classes { get; set; }
}

public class ElementNativeViewInfo
{
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; set; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string?>? Properties { get; set; }
}
