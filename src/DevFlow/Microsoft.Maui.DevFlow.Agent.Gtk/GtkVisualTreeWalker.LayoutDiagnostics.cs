using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

public partial class GtkVisualTreeWalker
{
    protected override void PopulatePlatformLayoutSnapshot(
        LayoutElementSnapshot snapshot,
        object runtimeElement,
        LayoutInspectionRequest request)
    {
        if (runtimeElement is not Microsoft.Maui.Controls.VisualElement element ||
            element.Handler?.PlatformView is not global::Gtk.Widget widget ||
            widget.GetRoot() is not global::Gtk.Widget root ||
            !widget.ComputeBounds(root, out var bounds))
            return;

        var full = new LayoutRect
        {
            X = bounds.GetX(),
            Y = bounds.GetY(),
            Width = bounds.GetWidth(),
            Height = bounds.GetHeight(),
        };
        snapshot.FullRegion = LayoutPlatformEvidence.Region(full, "exact");
        snapshot.PlatformEvidenceLimitations.Add(
            "GTK transformed bounds are exact, but an authoritative effective visible region was not available; clipping remains incomplete.");
    }
}
