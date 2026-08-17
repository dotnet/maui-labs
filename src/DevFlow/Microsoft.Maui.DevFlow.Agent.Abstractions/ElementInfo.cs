using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Represents a MAUI visual tree element with all inspectable properties.
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

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Origin { get; set; }

    [JsonPropertyName("ownerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OwnerId { get; set; }

    [JsonPropertyName("discriminator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Discriminator { get; set; }

    [JsonPropertyName("boundsQuality")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BoundsQuality { get; set; }

    [JsonPropertyName("captureEpoch")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long CaptureEpoch { get; set; }

    [JsonPropertyName("registryGeneration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long RegistryGeneration { get; set; }

    [JsonPropertyName("windowId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WindowId { get; set; }

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Capabilities { get; set; }

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
    public double Opacity { get; set; } = 1.0;

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

    [JsonPropertyName("windowBounds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundsInfo? WindowBounds { get; set; }

    [JsonPropertyName("gestures")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeType { get; set; }

    [JsonPropertyName("nativeProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

    [JsonIgnore]
    internal object? IdentityToken { get; set; }

    private string? _role;
    private List<string>? _traits;

    private string? InferRole() => Type switch
    {
        // .NET MAUI
        "Window" => "window",
        "Button" or "ImageButton" => "button",
        "Entry" or "Editor" or "SearchBar" => "textbox",
        "CheckBox" => "checkbox",
        "RadioButton" => "radio",
        "Switch" => "switch",
        "Image" => "image",
        "CollectionView" or "ListView" or "CarouselView" => "list",
        "Label" when Gestures?.Contains("tap") == true => "link",

        // Android views
        "AppCompatButton" or "MaterialButton" or "FloatingActionButton" => "button",
        "AppCompatEditText" or "EditText" or "TextInputEditText" or "AutoCompleteTextView" or "SearchView" => "textbox",
        "AppCompatCheckBox" or "MaterialCheckBox" => "checkbox",
        "AppCompatRadioButton" or "MaterialRadioButton" => "radio",
        "SwitchCompat" or "SwitchMaterial" or "ToggleButton" => "switch",
        "ImageView" or "AppCompatImageView" or "ShapeableImageView" => "image",
        "RecyclerView" or "GridView" or "ViewPager2" => "list",
        "SeekBar" or "AppCompatSeekBar" or "Slider" => "slider",
        "ProgressBar" => "progressbar",
        "TextView" or "AppCompatTextView" or "MaterialTextView" => "text",

        // UIKit
        "UIWindow" => "window",
        "UIButton" => "button",
        "UITextField" or "UITextView" or "UISearchBar" => "textbox",
        "UISwitch" => "switch",
        "UIImageView" => "image",
        "UITableView" or "UICollectionView" => "list",
        "UISlider" or "UIStepper" => "slider",
        "UIProgressView" or "UIActivityIndicatorView" => "progressbar",
        "UISegmentedControl" => "tablist",
        "UILabel" => "text",

        // AppKit
        "NSWindow" => "window",
        // AppKit models push buttons, checkboxes and radios all as NSButton (distinguished at
        // runtime by ButtonType, not by class), so a type-name-only switch can only resolve the
        // shared "button" role. NSSwitch is the dedicated 10.15+ toggle and maps cleanly, mirroring
        // UISwitch on UIKit.
        "NSButton" => "button",
        "NSSwitch" => "switch",
        "NSSecureTextField" or "NSSearchField" or "NSTextView" => "textbox",
        "NSImageView" => "image",
        "NSTableView" or "NSOutlineView" or "NSCollectionView" => "list",
        "NSSlider" or "NSStepper" => "slider",
        "NSProgressIndicator" => "progressbar",
        "NSSegmentedControl" => "tablist",
        "NSTextField" => "text",

        _ => null
    };

    private List<string>? BuildTraits()
    {
        var traits = new List<string>();
        var role = Role;
        var hasDeclaredCapabilities = Capabilities is { Count: > 0 };

        if (hasDeclaredCapabilities)
        {
            if (Capabilities!.Contains("invoke") || Capabilities.Contains("set-value"))
                traits.Add("interactive");
        }
        else if (role is "button" or "textbox" or "checkbox" or "radio" or "switch" or "link" or "slider")
        {
            traits.Add("interactive");
        }

        if (Gestures is { Count: > 0 } && !traits.Contains("interactive"))
            traits.Add("interactive");

        if (IsFocused
            || hasDeclaredCapabilities && Capabilities!.Contains("focus")
            || (!hasDeclaredCapabilities
                && role is ("button" or "textbox" or "checkbox" or "radio" or "switch" or "link" or "slider" or "window")))
        {
            traits.Add("focusable");
        }

        if (hasDeclaredCapabilities)
        {
            if (Capabilities!.Contains("scroll"))
                traits.Add("scrollable");
        }
        else if (Type is "ScrollView" or "CollectionView" or "ListView" or "CarouselView"
                 or "HorizontalScrollView" or "NestedScrollView" or "RecyclerView" or "ViewPager2"
                 or "UIScrollView" or "UITableView" or "UICollectionView"
                 or "NSScrollView" or "NSTableView" or "NSOutlineView")
        {
            traits.Add("scrollable");
        }

        if (role == "heading")
            traits.Add("header");

        return traits.Count > 0 ? traits : null;
    }

    internal ElementInfo WithoutChildren() => new()
    {
        Id = Id,
        ParentId = ParentId,
        Type = Type,
        FullType = FullType,
        Framework = Framework,
        Origin = Origin,
        OwnerId = OwnerId,
        Discriminator = Discriminator,
        BoundsQuality = BoundsQuality,
        CaptureEpoch = CaptureEpoch,
        RegistryGeneration = RegistryGeneration,
        WindowId = WindowId,
        Capabilities = Capabilities,
        AutomationId = AutomationId,
        Text = Text,
        Value = Value,
        Role = Role,
        IsVisible = IsVisible,
        IsEnabled = IsEnabled,
        IsFocused = IsFocused,
        IsSelected = IsSelected,
        IdentityToken = IdentityToken,
        Opacity = Opacity,
        Traits = Traits,
        Bounds = Bounds,
        WindowBounds = WindowBounds,
        Gestures = Gestures,
        StyleClass = StyleClass,
        NativeType = NativeType,
        NativeProperties = NativeProperties,
        FrameworkProperties = FrameworkProperties,
        Children = null
    };
}

/// <summary>
/// Element bounding rectangle in screen coordinates.
/// </summary>
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
    public double Opacity { get; set; } = 1.0;
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

/// <summary>
/// Metadata for a registered CDP-capable WebView.
/// </summary>
public class CdpWebViewInfo
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("automationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AutomationId { get; set; }

    [JsonPropertyName("elementId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementId { get; set; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonPropertyName("isReady")]
    public bool IsReady => ReadyCheck?.Invoke() ?? false;

    [JsonIgnore]
    public Func<string, Task<string>> CommandHandler { get; set; } = null!;

    [JsonIgnore]
    public Func<bool> ReadyCheck { get; set; } = () => false;
}
