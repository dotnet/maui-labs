using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ScrollViewHandler : GtkViewHandler<IScrollView, Gtk.ScrolledWindow>
{
	public static new IPropertyMapper<IScrollView, ScrollViewHandler> Mapper =
		new PropertyMapper<IScrollView, ScrollViewHandler>(ViewMapper)
		{
			[nameof(IScrollView.Content)] = MapContent,
			[nameof(IScrollView.Orientation)] = MapOrientation,
			[nameof(IScrollView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
			[nameof(IScrollView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
		};

	public static new CommandMapper<IScrollView, ScrollViewHandler> CommandMapper =
		new(ViewCommandMapper)
		{
			[nameof(IScrollView.RequestScrollTo)] = MapRequestScrollTo,
		};

	public ScrollViewHandler() : base(Mapper, CommandMapper)
	{
	}

	Gtk.Adjustment? _vAdj;
	Gtk.Adjustment? _hAdj;

	protected override Gtk.ScrolledWindow CreatePlatformView()
	{
		var scrolled = Gtk.ScrolledWindow.New();
		scrolled.SetVexpand(true);
		scrolled.SetHexpand(true);
		// Prevent GTK from expanding ScrolledWindow to full content height —
		// MAUI drives sizing through PlatformArrange / SetSizeRequest.
		scrolled.SetPropagateNaturalHeight(false);
		return scrolled;
	}

	protected override void ConnectHandler(Gtk.ScrolledWindow platformView)
	{
		base.ConnectHandler(platformView);

		_vAdj = platformView.GetVadjustment();
		_hAdj = platformView.GetHadjustment();

		if (_vAdj != null)
			_vAdj.OnValueChanged += OnScrollChanged;
		if (_hAdj != null)
			_hAdj.OnValueChanged += OnScrollChanged;
	}

	protected override void DisconnectHandler(Gtk.ScrolledWindow platformView)
	{
		if (_vAdj != null)
			_vAdj.OnValueChanged -= OnScrollChanged;
		if (_hAdj != null)
			_hAdj.OnValueChanged -= OnScrollChanged;

		_vAdj = null;
		_hAdj = null;

		base.DisconnectHandler(platformView);
	}

	void OnScrollChanged(Gtk.Adjustment sender, EventArgs args)
	{
		if (VirtualView == null) return;

		double scrollX = _hAdj?.GetValue() ?? 0;
		double scrollY = _vAdj?.GetValue() ?? 0;

		VirtualView.HorizontalOffset = scrollX;
		VirtualView.VerticalOffset = scrollY;

		if (VirtualView is Microsoft.Maui.Controls.ScrollView sv)
		{
			// Fire the Scrolled event
			sv.SetScrolledPosition(scrollX, scrollY);
		}
	}

	static void MapRequestScrollTo(ScrollViewHandler handler, IScrollView scrollView, object? args)
	{
		if (args is not ScrollToRequest request)
			return;

		var vAdj = handler._vAdj;
		var hAdj = handler._hAdj;

		if (vAdj != null)
			vAdj.SetValue(request.VerticalOffset);
		if (hAdj != null)
			hAdj.SetValue(request.HorizontalOffset);

		scrollView.ScrollFinished();
	}

	[System.Runtime.InteropServices.DllImport("libgtk-4.so.1")]
	static extern void gtk_scrollable_set_vscroll_policy(IntPtr scrollable, int policy);
	[System.Runtime.InteropServices.DllImport("libgtk-4.so.1")]
	static extern void gtk_scrollable_set_hscroll_policy(IntPtr scrollable, int policy);

	const int GTK_SCROLL_NATURAL = 1;

	public static void MapContent(ScrollViewHandler handler, IScrollView scrollView)
	{
		_ = handler.MauiContext ?? throw new InvalidOperationException("MauiContext not set.");

		if (scrollView.PresentedContent != null)
		{
			var platformContent = (Gtk.Widget)scrollView.PresentedContent.ToPlatform(handler.MauiContext);
			handler.PlatformView?.SetChild(platformContent);

			// ScrolledWindow wraps non-scrollable children in a Viewport.
			// The Viewport's default scroll policy is MINIMUM, but our CustomLayout
			// returns minimum=0 (to prevent window growth). Set policy to NATURAL
			// so the Viewport uses natural size for scroll extent.
			var viewport = handler.PlatformView?.GetFirstChild();
			if (viewport != null)
			{
				var handle = viewport.Handle.DangerousGetHandle();
				gtk_scrollable_set_vscroll_policy(handle, GTK_SCROLL_NATURAL);
				gtk_scrollable_set_hscroll_policy(handle, GTK_SCROLL_NATURAL);
			}
		}
	}

	public static void MapOrientation(ScrollViewHandler handler, IScrollView scrollView)
	{
		var (hPolicy, vPolicy) = scrollView.Orientation switch
		{
			ScrollOrientation.Horizontal => (Gtk.PolicyType.Automatic, Gtk.PolicyType.Never),
			ScrollOrientation.Vertical => (Gtk.PolicyType.Never, Gtk.PolicyType.Automatic),
			ScrollOrientation.Both => (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic),
			ScrollOrientation.Neither => (Gtk.PolicyType.Never, Gtk.PolicyType.Never),
			_ => (Gtk.PolicyType.Automatic, Gtk.PolicyType.Automatic)
		};
		handler.PlatformView?.SetPolicy(hPolicy, vPolicy);
	}

	public static void MapHorizontalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
	{
		UpdateScrollBarPolicies(handler, scrollView);
	}

	public static void MapVerticalScrollBarVisibility(ScrollViewHandler handler, IScrollView scrollView)
	{
		UpdateScrollBarPolicies(handler, scrollView);
	}

	static void UpdateScrollBarPolicies(ScrollViewHandler handler, IScrollView scrollView)
	{
		handler.PlatformView?.SetPolicy(
			MapScrollBarVisibility(scrollView.HorizontalScrollBarVisibility),
			MapScrollBarVisibility(scrollView.VerticalScrollBarVisibility));
	}

	static Gtk.PolicyType MapScrollBarVisibility(ScrollBarVisibility visibility)
	{
		return visibility switch
		{
			ScrollBarVisibility.Always => Gtk.PolicyType.Always,
			ScrollBarVisibility.Never => Gtk.PolicyType.Never,
			ScrollBarVisibility.Default => Gtk.PolicyType.Automatic,
			_ => Gtk.PolicyType.Automatic
		};
	}

	public override void PlatformArrange(Rect rect)
	{
		var platformView = PlatformView;
		if (platformView == null) return;

		if (platformView.GetParent() is Platform.GtkLayoutPanel layoutPanel)
		{
			layoutPanel.SetChildBounds(platformView, rect.X, rect.Y, (int)rect.Width, (int)rect.Height);
		}

		// Re-measure and re-arrange content for the new viewport size
		if (VirtualView is ICrossPlatformLayout crossPlatform)
		{
			crossPlatform.CrossPlatformMeasure(rect.Width, rect.Height);
			crossPlatform.CrossPlatformArrange(new Rect(0, 0, rect.Width, rect.Height));
		}
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var ve = VirtualView as Microsoft.Maui.Controls.View;

		// Honour explicit HeightRequest
		if (ve != null && ve.HeightRequest >= 0)
		{
			var contentSize = (VirtualView is ICrossPlatformLayout cp)
				? cp.CrossPlatformMeasure(widthConstraint, heightConstraint)
				: base.GetDesiredSize(widthConstraint, heightConstraint);
			return new Size(contentSize.Width, Math.Min(ve.HeightRequest, heightConstraint));
		}

		// Measure content for width, but report a small height so the
		// ScrollView doesn't inflate its parent. Scrollable views work at
		// any size — the parent layout drives actual height via Arrange.
		if (VirtualView is ICrossPlatformLayout crossPlatform)
		{
			var measured = crossPlatform.CrossPlatformMeasure(widthConstraint, heightConstraint);
			return new Size(measured.Width, Math.Max(1, Math.Min(50, heightConstraint)));
		}

		return base.GetDesiredSize(widthConstraint, heightConstraint);
	}
}
