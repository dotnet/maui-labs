namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Framework-neutral description of a single native view, produced by the
/// platform layer and consumed by <see cref="NativeDevFlowAgentService"/>.
/// </summary>
/// <remarks>
/// The platform layer never builds <c>ElementInfo</c> directly. It reports raw facts about a view;
/// shaping those facts into the DevFlow wire protocol stays in shared code so every platform
/// produces an identical payload.
/// </remarks>
internal sealed class NativeViewDescriptor
{
    /// <summary>Short type name, e.g. <c>Button</c>.</summary>
    public string Type { get; set; } = "View";

    /// <summary>Fully qualified native type name, e.g. <c>Android.Widget.Button</c>.</summary>
    public string FullType { get; set; } = "View";

    /// <summary>Automation identifier — resource name, accessibility identifier, or tag.</summary>
    public string? AutomationId { get; set; }

    /// <summary>Primary display text.</summary>
    public string? Text { get; set; }

    /// <summary>Editable value, when different from <see cref="Text"/>.</summary>
    public string? Value { get; set; }

    /// <summary>Accessibility label, used as a text fallback for icon-only controls.</summary>
    public string? AccessibilityLabel { get; set; }

    public bool IsVisible { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public bool IsFocused { get; set; }

    public bool IsSelected { get; set; }

    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// Bounds in device-independent units, in window-logical coordinates: top-left origin,
    /// relative to the containing window (not the screen). This matches the
    /// <c>window-logical-coordinates</c> feature every native backend advertises under
    /// <c>ui.hit-test</c>, so hit testing and Inspector overlays stay correct even when the
    /// window isn't at the screen origin.
    /// </summary>
    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>True when the view can receive text input.</summary>
    public bool IsTextInput { get; set; }

    /// <summary>True when the view scrolls its content.</summary>
    public bool IsScrollable { get; set; }

    /// <summary>True when the view responds to taps.</summary>
    public bool IsTappable { get; set; }

    /// <summary>Additional native properties surfaced under <c>nativeProperties</c>.</summary>
    public Dictionary<string, string?>? Properties { get; set; }
}
