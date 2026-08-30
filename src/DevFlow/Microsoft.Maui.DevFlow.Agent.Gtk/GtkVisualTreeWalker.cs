using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

/// <summary>
/// GTK-specific visual tree walker that provides native GTK widget info.
/// </summary>
public partial class GtkVisualTreeWalker : VisualTreeWalker
{
    protected override BoundsInfo? ResolveWindowBounds(VisualElement ve)
    {
        try
        {
            if (ve.Handler?.PlatformView is not global::Gtk.Widget widget)
                return null;

            var root = widget.GetRoot();
            if (root is not global::Gtk.Widget rootWidget) return null;

            if (widget.ComputeBounds(rootWidget, out var rect))
            {
                return new BoundsInfo
                {
                    X = rect.GetX(),
                    Y = rect.GetY(),
                    Width = rect.GetWidth(),
                    Height = rect.GetHeight()
                };
            }
        }
        catch { }
        return null;
    }

    protected override void PopulateNativeInfo(ElementInfo info, VisualElement ve)
    {
        try
        {
            var platformView = ve.Handler?.PlatformView;
            if (platformView == null) return;

            info.NativeType = platformView.GetType().FullName;

            if (platformView is global::Gtk.Widget widget)
            {
                var props = new Dictionary<string, string?>();

                var name = widget.GetName();
                if (!string.IsNullOrEmpty(name))
                    props["name"] = name;

                var tooltip = widget.GetTooltipText();
                if (!string.IsNullOrEmpty(tooltip))
                    props["tooltipText"] = tooltip;

                props["sensitive"] = widget.GetSensitive().ToString();
                props["visible"] = widget.GetVisible().ToString();
                props["widgetType"] = widget.GetType().Name;

                if (widget is global::Gtk.Button button)
                {
                    var label = button.GetLabel();
                    if (!string.IsNullOrEmpty(label))
                        props["label"] = label;
                }
                else if (widget is global::Gtk.CheckButton checkButton)
                {
                    props["active"] = checkButton.GetActive().ToString();
                }
                else if (widget is global::Gtk.Entry entry)
                {
                    props["text"] = entry.GetText();
                    props["visibility"] = entry.GetVisibility().ToString();
                }
                else if (widget is global::Gtk.Label gtkLabel)
                {
                    props["text"] = gtkLabel.GetText();
                    props["selectable"] = gtkLabel.GetSelectable().ToString();
                }
                else if (widget is global::Gtk.ToggleButton toggle)
                {
                    props["active"] = toggle.GetActive().ToString();
                }
                else if (widget is global::Gtk.Switch gtkSwitch)
                {
                    props["active"] = gtkSwitch.GetActive().ToString();
                }
                else if (widget is global::Gtk.SpinButton spinButton)
                {
                    props["value"] = spinButton.GetValue().ToString();
                }
                else if (widget is global::Gtk.Scale scale)
                {
                    var range = (global::Gtk.Range)scale;
                    props["value"] = range.GetValue().ToString();
                }
                else if (widget is global::Gtk.ProgressBar progressBar)
                {
                    props["fraction"] = progressBar.GetFraction().ToString();
                }
                else if (widget is global::Gtk.TextView textView)
                {
                    var buffer = textView.GetBuffer();
                    buffer.GetBounds(out var start, out var end);
                    props["text"] = buffer.GetText(start, end, false);
                }
                else if (widget is global::Gtk.ScrolledWindow scrolled)
                {
                    var hadj = scrolled.GetHadjustment();
                    var vadj = scrolled.GetVadjustment();
                    if (hadj != null) props["scrollX"] = hadj.GetValue().ToString();
                    if (vadj != null) props["scrollY"] = vadj.GetValue().ToString();
                }

                if (props.Count > 0)
                    info.NativeProperties = props;
            }
        }
        catch
        {
            // Native info is best-effort; don't fail the tree walk
        }
    }

    protected override string? EnsurePlatformStableId(object platformObj)
    {
        try
        {
            if (platformObj is global::Gtk.Widget widget)
            {
                var name = widget.GetName();
                if (string.IsNullOrEmpty(name))
                {
                    name = Guid.NewGuid().ToString();
                    widget.SetName(name);
                }
                return name;
            }
        }
        catch { }
        return null;
    }
}
