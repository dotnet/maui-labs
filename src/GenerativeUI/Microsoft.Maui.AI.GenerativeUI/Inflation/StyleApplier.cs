using System.Diagnostics;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.AI.GenerativeUI.Inflation;

/// <summary>
/// Applies registered style tokens to inflated controls. Base tokens use built-in visual treatment
/// (so they work without app-authored resources); app-registered tokens resolve a
/// <c>StaticResource</c> by key. Tokens applied outside their <c>appliesTo</c> list are dropped and
/// logged. See <c>docs/GenerativeUI/spec/appendix-ui-dsl.md §7</c>.
/// </summary>
internal sealed class StyleApplier(GenerativeUiRegistry registry)
{
    private static readonly Color Accent = Color.FromArgb("#512BD4");
    private static readonly Color Danger = Color.FromArgb("#D13438");

    /// <summary>Applies every style token on a node to its inflated <paramref name="view"/>.</summary>
    public void Apply(VisualElement view, string nodeType, IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
            ApplyOne(view, nodeType, token);
    }

    private void ApplyOne(VisualElement view, string nodeType, string token)
    {
        var reg = registry.GetStyle(token);
        if (reg is null)
        {
            Debug.WriteLine($"[GenerativeUI] Unknown style token '{token}' on {nodeType}; ignored.");
            return;
        }

        if (reg.AppliesTo.Count > 0 && !AppliesTo(reg, nodeType))
        {
            Debug.WriteLine($"[GenerativeUI] Style '{token}' not valid on {nodeType} (applies to {string.Join("/", reg.AppliesTo)}); dropped.");
            return;
        }

        if (reg.IsBuiltIn)
            ApplyBuiltIn(view, token);
        else
            ApplyResource(view, reg);
    }

    private static bool AppliesTo(UiStyleRegistration reg, string nodeType)
    {
        foreach (var t in reg.AppliesTo)
        {
            if (string.Equals(t, nodeType, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ApplyBuiltIn(VisualElement view, string token)
    {
        switch (token)
        {
            case "Title" when view is Label t:
                t.FontSize = 24; t.FontAttributes = FontAttributes.Bold; break;
            case "Subtitle" when view is Label s:
                s.FontSize = 18; s.FontAttributes = FontAttributes.Bold; break;
            case "Body" when view is Label b:
                b.FontSize = 14; break;
            case "Caption" when view is Label c:
                c.FontSize = 12; c.TextColor = Colors.Gray; break;
            case "Mono" when view is Label m:
                m.FontFamily = DeviceInfo.Platform == DevicePlatform.WinUI ? "Consolas" : "Menlo"; break;

            case "primary" when view is Button p:
                p.BackgroundColor = Accent; p.TextColor = Colors.White; break;
            case "secondary" when view is Button sec:
                sec.BackgroundColor = Colors.Transparent; sec.TextColor = Accent; sec.BorderColor = Accent; sec.BorderWidth = 1; break;
            case "danger" when view is Button d:
                d.BackgroundColor = Danger; d.TextColor = Colors.White; break;
        }
    }

    private static void ApplyResource(VisualElement view, UiStyleRegistration reg)
    {
        if (Application.Current?.Resources.TryGetValue(reg.EffectiveResourceKey, out var resource) != true)
        {
            Debug.WriteLine($"[GenerativeUI] Style resource '{reg.EffectiveResourceKey}' not found; ignored.");
            return;
        }

        switch (resource)
        {
            case Style style when view is VisualElement ve:
                ve.Style = style;
                break;
            case Color color:
                ApplyColor(view, color);
                break;
            default:
                Debug.WriteLine($"[GenerativeUI] Style resource '{reg.EffectiveResourceKey}' is an unsupported type; ignored.");
                break;
        }
    }

    private static void ApplyColor(VisualElement view, Color color)
    {
        switch (view)
        {
            case Label l: l.TextColor = color; break;
            case Button b: b.TextColor = color; break;
        }
    }
}
