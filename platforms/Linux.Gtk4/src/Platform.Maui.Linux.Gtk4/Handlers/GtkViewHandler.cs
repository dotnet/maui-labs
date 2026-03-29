using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Platform.Maui.Linux.Gtk4.Platform;

namespace Platform.Maui.Linux.Gtk4.Handlers;

/// <summary>
/// Base handler for all GTK-backed MAUI views.
/// Bridges MAUI's layout coordinates to GTK widget allocation.
/// </summary>
public abstract class GtkViewHandler<TVirtualView, TPlatformView> : ViewHandler<TVirtualView, TPlatformView>
	where TVirtualView : class, IView
	where TPlatformView : Gtk.Widget
{
	public static IPropertyMapper<IView, GtkViewHandler<TVirtualView, TPlatformView>> ViewMapper =
		new PropertyMapper<IView, GtkViewHandler<TVirtualView, TPlatformView>>(ViewHandler.ViewMapper)
		{
			[nameof(IView.Background)] = MapBackground,
			[nameof(IView.Opacity)] = MapOpacity,
			[nameof(IView.Visibility)] = MapVisibility,
			[nameof(IView.IsEnabled)] = MapIsEnabled,
			[nameof(IView.Semantics)] = MapSemantics,
			[nameof(IView.AutomationId)] = MapAutomationId,
			[nameof(IView.Shadow)] = MapShadow,
			[nameof(IView.InputTransparent)] = MapInputTransparent,
			[nameof(IView.Clip)] = MapClip,
			[nameof(IView.FlowDirection)] = MapFlowDirection,
			[nameof(IView.ZIndex)] = MapZIndex,
			// Transforms — needed for animations (TranslateTo, ScaleTo, RotateTo)
			[nameof(IView.TranslationX)] = MapTransformProperty,
			[nameof(IView.TranslationY)] = MapTransformProperty,
			[nameof(IView.Scale)] = MapTransformProperty,
			[nameof(IView.ScaleX)] = MapTransformProperty,
			[nameof(IView.ScaleY)] = MapTransformProperty,
			[nameof(IView.Rotation)] = MapTransformProperty,
			[nameof(IView.RotationX)] = MapTransformProperty,
			[nameof(IView.RotationY)] = MapTransformProperty,
			[nameof(IView.AnchorX)] = MapTransformProperty,
			[nameof(IView.AnchorY)] = MapTransformProperty,
			// ToolTip
			["ToolTipProperties.Text"] = MapToolTip,
			// ContextFlyout
			["ContextFlyout"] = MapContextFlyout,
		};

	public static CommandMapper<TVirtualView, GtkViewHandler<TVirtualView, TPlatformView>> ViewCommandMapper =
		new(ViewHandler.ViewCommandMapper)
		{
			["Focus"] = MapFocus,
			["Unfocus"] = MapUnfocus,
		};

	protected GtkViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
		: base(mapper, commandMapper ?? ViewCommandMapper)
	{
	}

	Gtk.EventControllerMotion? _vsmMotion;
	Gtk.GestureClick? _vsmClick;
	Gtk.EventControllerFocus? _vsmFocus;
	bool _isPointerOver;
	Rect _lastArrangeRect;

	protected override void ConnectHandler(TPlatformView platformView)
	{
		base.ConnectHandler(platformView);
		SetupVisualStateTracking(platformView);
		ApplyTransitionCss(platformView);
	}

	protected override void DisconnectHandler(TPlatformView platformView)
	{
		CleanupContextFlyout(platformView);
		CleanupVisualStateTracking(platformView);
		base.DisconnectHandler(platformView);
	}

	void SetupVisualStateTracking(Gtk.Widget widget)
	{
		_vsmMotion = Gtk.EventControllerMotion.New();
		_vsmMotion.OnEnter += OnPointerEnter;
		_vsmMotion.OnLeave += OnPointerLeave;
		widget.AddController(_vsmMotion);

		_vsmClick = Gtk.GestureClick.New();
		_vsmClick.OnPressed += OnPressed;
		_vsmClick.OnReleased += OnReleased;
		widget.AddController(_vsmClick);

		_vsmFocus = Gtk.EventControllerFocus.New();
		_vsmFocus.OnEnter += OnFocusIn;
		_vsmFocus.OnLeave += OnFocusOut;
		widget.AddController(_vsmFocus);
	}

	void CleanupVisualStateTracking(Gtk.Widget widget)
	{
		if (_vsmMotion != null) { widget.RemoveController(_vsmMotion); _vsmMotion = null; }
		if (_vsmClick != null) { widget.RemoveController(_vsmClick); _vsmClick = null; }
		if (_vsmFocus != null) { widget.RemoveController(_vsmFocus); _vsmFocus = null; }
	}

	void OnPointerEnter(Gtk.EventControllerMotion sender, Gtk.EventControllerMotion.EnterSignalArgs args)
	{
		_isPointerOver = true;
		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve)
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "PointerOver");
	}

	void OnPointerLeave(Gtk.EventControllerMotion sender, EventArgs args)
	{
		_isPointerOver = false;
		GoToCurrentState();
	}

	void OnPressed(Gtk.GestureClick sender, Gtk.GestureClick.PressedSignalArgs args)
	{
		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve)
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "Pressed");
	}

	void OnReleased(Gtk.GestureClick sender, Gtk.GestureClick.ReleasedSignalArgs args)
	{
		GoToCurrentState();
	}

	void OnFocusIn(Gtk.EventControllerFocus sender, EventArgs args)
	{
		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve)
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "Focused");
	}

	void OnFocusOut(Gtk.EventControllerFocus sender, EventArgs args)
	{
		GoToCurrentState();
	}

	void GoToCurrentState()
	{
		if (VirtualView is not Microsoft.Maui.Controls.VisualElement ve)
			return;

		if (!ve.IsEnabled)
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "Disabled");
		else if (_isPointerOver)
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "PointerOver");
		else
			Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, "Normal");
	}

	static void MapFocus(GtkViewHandler<TVirtualView, TPlatformView> handler, TVirtualView view, object? args)
	{
		if (args is RetrievePlatformValueRequest<bool> request)
		{
			try
			{
				var result = handler.PlatformView?.GrabFocus() ?? false;
				request.SetResult(result);
			}
			catch
			{
				request.SetResult(false);
			}
		}
	}

	static void MapUnfocus(GtkViewHandler<TVirtualView, TPlatformView> handler, TVirtualView view, object? args)
	{
		// GTK4 doesn't have a direct "unfocus" — focus the window root instead
		try
		{
			var root = handler.PlatformView?.GetRoot();
			if (root is Gtk.Window window)
				window.GrabFocus();
		}
		catch { }
	}

	/// <summary>
	/// Applies CSS transitions for smooth property changes (background-color, opacity, box-shadow).
	/// This enables VSM state transitions and property animations to animate smoothly.
	/// </summary>
	void ApplyTransitionCss(Gtk.Widget widget)
	{
		var provider = Gtk.CssProvider.New();
		provider.LoadFromString("* { transition: background-color 200ms ease, opacity 200ms ease, box-shadow 200ms ease, border-radius 200ms ease, color 200ms ease; }");
		widget.GetStyleContext().AddProvider(provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION - 1);
	}

	public override void PlatformArrange(Rect rect)
	{
		var platformView = PlatformView;
		if (platformView == null)
			return;

		_lastArrangeRect = rect;

		// Position the widget inside its parent GtkLayoutPanel
		if (platformView.GetParent() is Platform.GtkLayoutPanel layoutPanel)
		{
			// Check if visual transforms are needed
			bool hasTransform = VirtualView != null && (
				VirtualView.Rotation != 0 ||
				VirtualView.TranslationX != 0 || VirtualView.TranslationY != 0 ||
				VirtualView.Scale != 1 || VirtualView.ScaleX != 1 || VirtualView.ScaleY != 1);

			if (hasTransform)
			{
				// When using SetChildTransform, the transform includes the position.
				layoutPanel.SetChildBounds(platformView, 0, 0, (int)rect.Width, (int)rect.Height);
				ApplyTransform(platformView, layoutPanel, rect);
			}
			else
			{
				layoutPanel.SetChildTransform(platformView, null);
				layoutPanel.SetChildBounds(platformView, rect.X, rect.Y, (int)rect.Width, (int)rect.Height);
			}
		}
		else
		{
			platformView.SetSizeRequest((int)rect.Width, (int)rect.Height);
		}
	}

	void ApplyTransform(Gtk.Widget widget, Platform.GtkLayoutPanel layoutPanel, Rect rect)
	{
		if (VirtualView == null) return;

		var view = VirtualView;
		double sx = view.Scale * view.ScaleX;
		double sy = view.Scale * view.ScaleY;
		bool hasScale = sx != 1.0 || sy != 1.0;
		bool hasRotation = view.Rotation != 0;

		if (!hasScale && !hasRotation)
		{
			// Translation-only: just update position (avoids SetChildTransform issues)
			layoutPanel.SetChildTransform(widget, null);
			layoutPanel.SetChildBounds(widget, rect.X + view.TranslationX, rect.Y + view.TranslationY, (int)rect.Width, (int)rect.Height);
			return;
		}

		var transform = Gsk.Transform.New();

		// Start with the layout position + user translation
		float tx = (float)(rect.X + view.TranslationX);
		float ty = (float)(rect.Y + view.TranslationY);
		if (tx != 0 || ty != 0)
		{
			var pt = Graphene.Point.Alloc();
			pt.Init(tx, ty);
			transform = transform.Translate(pt);
		}

		// Move to anchor, apply rotation/scale, move back
		float anchorX = (float)(view.AnchorX * rect.Width);
		float anchorY = (float)(view.AnchorY * rect.Height);

		var anchorPt = Graphene.Point.Alloc();
		anchorPt.Init(anchorX, anchorY);
		transform = transform.Translate(anchorPt);

		if (hasRotation)
			transform = transform.Rotate((float)view.Rotation);

		if (hasScale)
			transform = transform.Scale((float)sx, (float)sy);

		var negPt = Graphene.Point.Alloc();
		negPt.Init(-anchorX, -anchorY);
		transform = transform.Translate(negPt);

		layoutPanel.SetChildTransform(widget, transform);
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var platformView = PlatformView;
		if (platformView == null)
			return Size.Zero;

		// Save current size request so we can restore it after measurement.
		// SetSizeRequest(-1,-1) is needed to get the natural size, but leaving
		// it cleared causes GTK to re-allocate at natural width, breaking layout.
		platformView.GetSizeRequest(out var prevW, out var prevH);
		platformView.SetSizeRequest(-1, -1);

		// Measure horizontal natural size
		platformView.MeasureNative(Gtk.Orientation.Horizontal, -1, out var minWidth, out var natWidth, out _, out _);

		var width = Math.Min(natWidth, widthConstraint);

		// If MAUI set an explicit request or maximum, use it
		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve)
		{
			if (ve.WidthRequest >= 0)
				width = Math.Min(ve.WidthRequest, widthConstraint);
			if (ve.MaximumWidthRequest >= 0 && ve.MaximumWidthRequest < width)
				width = ve.MaximumWidthRequest;
		}

		// Measure vertical size with the actual width constraint so wrapping
		// widgets (e.g. labels with SetWrap) report the correct wrapped height.
		int forWidth = (int)width;
		platformView.MeasureNative(Gtk.Orientation.Vertical, forWidth, out var minHeight, out var natHeight, out _, out _);

		var height = Math.Min(natHeight, heightConstraint);

		if (VirtualView is Microsoft.Maui.Controls.VisualElement ve2)
		{
			if (ve2.HeightRequest >= 0)
				height = Math.Min(ve2.HeightRequest, heightConstraint);
			if (ve2.MaximumHeightRequest >= 0 && ve2.MaximumHeightRequest < height)
				height = ve2.MaximumHeightRequest;
		}

		// Restore previous size request
		platformView.SetSizeRequest(prevW, prevH);

		return new Size(Math.Max(1, width), Math.Max(1, height));
	}

	static void MapBackground(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		if (view.Background is SolidPaint solidPaint && solidPaint.Color != null)
		{
			// Clear background-image too — GTK4 themes (e.g. Yaru) use
			// background-image: image(white) on buttons, which overrides background-color.
			handler.ApplyCss(handler.PlatformView,
				$"background-color: {ToGtkColor(solidPaint.Color)}; background-image: none;");
		}
		else if (view.Background is LinearGradientPaint lgp)
		{
			handler.ApplyCss(handler.PlatformView, BuildLinearGradientCss(lgp));
		}
		else if (view.Background is RadialGradientPaint rgp)
		{
			handler.ApplyCss(handler.PlatformView, BuildRadialGradientCss(rgp));
		}
	}

	static string BuildLinearGradientCss(LinearGradientPaint paint)
	{
		// MAUI uses StartPoint/EndPoint in 0-1 relative coordinates
		var angle = CalculateGradientAngle(paint.StartPoint, paint.EndPoint);
		var stops = string.Join(", ",
			paint.GradientStops
				.OrderBy(s => s.Offset)
				.Select(s => $"{ToGtkColor(s.Color)} {s.Offset * 100:F0}%"));
		return $"background-image: linear-gradient({angle:F0}deg, {stops}); background-color: transparent;";
	}

	static double CalculateGradientAngle(Point start, Point end)
	{
		// CSS gradient angles: 0deg = bottom-to-top, 90deg = left-to-right
		double dx = end.X - start.X;
		double dy = end.Y - start.Y;
		double radians = Math.Atan2(dx, -dy);
		double degrees = radians * 180.0 / Math.PI;
		return (degrees + 360) % 360;
	}

	static string BuildRadialGradientCss(RadialGradientPaint paint)
	{
		// MAUI: Center (0-1), Radius (0-1 of element size)
		var cx = paint.Center.X * 100;
		var cy = paint.Center.Y * 100;
		var r = paint.Radius * 100;
		var stops = string.Join(", ",
			paint.GradientStops
				.OrderBy(s => s.Offset)
				.Select(s => $"{ToGtkColor(s.Color)} {s.Offset * 100:F0}%"));
		return $"background-image: radial-gradient(circle {r:F0}% at {cx:F0}% {cy:F0}%, {stops}); background-color: transparent;";
	}

	static void MapOpacity(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		handler.PlatformView?.SetOpacity(view.Opacity);
	}

	static void MapVisibility(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		handler.PlatformView?.SetVisible(view.Visibility == Visibility.Visible);
	}

	static void MapIsEnabled(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		handler.PlatformView?.SetSensitive(view.IsEnabled);
		handler.GoToCurrentState();
	}

	/// <summary>
	/// Maps MAUI SemanticProperties to GTK4 accessibility.
	/// GTK4 uses the Gtk.Accessible interface with AT-SPI on Linux.
	/// </summary>
	static void MapSemantics(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null || view.Semantics == null)
			return;

		var semantics = view.Semantics;

		// Use the GTK widget name as a11y label (read by screen readers via AT-SPI)
		if (!string.IsNullOrEmpty(semantics.Description))
		{
			// Gtk.Widget tooltip serves as accessible description
			widget.SetTooltipText(semantics.Description);
		}

		if (!string.IsNullOrEmpty(semantics.Hint))
		{
			widget.SetTooltipText(semantics.Hint);
		}
	}

	/// <summary>
	/// Maps MAUI AutomationId to GTK widget name (used by test frameworks like Dogtail).
	/// </summary>
	static void MapAutomationId(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		if (handler.PlatformView != null && view is Microsoft.Maui.Controls.VisualElement ve
			&& !string.IsNullOrEmpty(ve.AutomationId))
		{
			handler.PlatformView.SetName(ve.AutomationId);
		}
	}

	protected void ApplyCss(Gtk.Widget? widget, string css)
	{
		if (widget == null)
			return;

		var provider = Gtk.CssProvider.New();
		provider.LoadFromString($"* {{ {css} }}");
		widget.GetStyleContext().AddProvider(provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
	}

	protected void ApplyCssWithSelector(Gtk.Widget? widget, string selector, string css)
	{
		if (widget == null)
			return;

		var provider = Gtk.CssProvider.New();
		provider.LoadFromString($"{selector} {{ {css} }}");
		widget.GetStyleContext().AddProvider(provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
	}

	protected string BuildFontCss(Microsoft.Maui.Font font)
	{
		var fontManager = MauiContext?.Services.GetService(typeof(IGtkFontManager)) as IGtkFontManager;
		return fontManager?.BuildFontCss(font) ?? GtkFontManager.BuildFallbackFontCss(font);
	}

	protected static string ToGtkColor(Color color)
	{
		return $"rgba({(int)(color.Red * 255)},{(int)(color.Green * 255)},{(int)(color.Blue * 255)},{color.Alpha})";
	}

	static void MapShadow(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		var shadow = view.Shadow;
		if (shadow == null || shadow.Paint is not SolidPaint paint || paint.Color == null)
		{
			handler.ApplyCss(widget, "box-shadow: none;");
			return;
		}

		var color = ToGtkColor(paint.Color);
		var ox = shadow.Offset.X;
		var oy = shadow.Offset.Y;
		var radius = shadow.Radius;
		handler.ApplyCss(widget, $"box-shadow: {ox:F0}px {oy:F0}px {radius:F0}px {color};");
	}

	static void MapInputTransparent(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		handler.PlatformView?.SetCanTarget(!view.InputTransparent);
	}

	static void MapClip(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		var clip = view.Clip;
		if (clip == null)
		{
			widget.SetOverflow(Gtk.Overflow.Visible);
			// Don't reset border-radius — it overrides native theme styling
			// (e.g. GTK4 Switch pill shape). Overflow.Visible is sufficient.
			return;
		}

		widget.SetOverflow(Gtk.Overflow.Hidden);

		// Try to extract border-radius from the geometry for common cases
		if (view is Microsoft.Maui.Controls.VisualElement ve && ve.Clip is Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry rrg)
		{
			var cr = rrg.CornerRadius;
			handler.ApplyCss(widget,
				$"border-radius: {(int)cr.TopLeft}px {(int)cr.TopRight}px {(int)cr.BottomRight}px {(int)cr.BottomLeft}px;");
		}
		else if (view is Microsoft.Maui.Controls.VisualElement ve2 && ve2.Clip is Microsoft.Maui.Controls.Shapes.EllipseGeometry)
		{
			handler.ApplyCss(widget, "border-radius: 50%;");
		}
		else
		{
			// For other geometry types, use overflow:hidden which clips to widget bounds
			handler.ApplyCss(widget, "border-radius: 0;");
		}
	}

	static void MapFlowDirection(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		if (handler.PlatformView == null) return;

		var dir = view.FlowDirection switch
		{
			FlowDirection.RightToLeft => Gtk.TextDirection.Rtl,
			FlowDirection.LeftToRight => Gtk.TextDirection.Ltr,
			_ => Gtk.TextDirection.None, // inherit from parent
		};
		handler.PlatformView.SetDirection(dir);
	}

	// Track ZIndex per widget
	static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _zIndexMap = new();

	static void MapZIndex(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		var parent = widget.GetParent();
		if (parent == null) return;

		int key = widget.GetHashCode();
		_zIndexMap[key] = view.ZIndex;

		// Reorder siblings: GTK4 draws last child on top.
		// Move widget before the first sibling with higher ZIndex.
		var sibling = parent.GetFirstChild();
		Gtk.Widget? insertBefore = null;

		while (sibling != null)
		{
			if (sibling != widget && _zIndexMap.TryGetValue(sibling.GetHashCode(), out int sibZ) && sibZ > view.ZIndex)
			{
				insertBefore = sibling;
				break;
			}
			sibling = sibling.GetNextSibling();
		}

		if (insertBefore != null)
			widget.InsertBefore(parent, insertBefore);
		else
			widget.InsertAfter(parent, null); // move to end (highest z)
	}

	/// <summary>
	/// Re-applies Gsk.Transform when animation-driven transform properties change
	/// (TranslationX/Y, Scale, Rotation, etc.).
	/// </summary>
	static void MapTransformProperty(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		if (widget.GetParent() is Platform.GtkLayoutPanel layoutPanel)
		{
			var rect = handler._lastArrangeRect;
			if (rect.Width <= 0 || rect.Height <= 0) return;

			bool hasTransform = view.Rotation != 0 ||
				view.TranslationX != 0 || view.TranslationY != 0 ||
				view.Scale != 1 || view.ScaleX != 1 || view.ScaleY != 1;

			if (hasTransform)
			{
				layoutPanel.SetChildBounds(widget, 0, 0, (int)rect.Width, (int)rect.Height);
				handler.ApplyTransform(widget, layoutPanel, rect);
			}
			else
			{
				layoutPanel.SetChildTransform(widget, null);
				layoutPanel.SetChildBounds(widget, rect.X, rect.Y, (int)rect.Width, (int)rect.Height);
			}
		}
	}

	/// <summary>
	/// Maps ToolTipProperties.Text to Gtk.Widget.SetTooltipText().
	/// </summary>
	static void MapToolTip(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		string? tooltipText = null;
		if (view is Microsoft.Maui.Controls.BindableObject bo)
			tooltipText = Microsoft.Maui.Controls.ToolTipProperties.GetText(bo)?.ToString();

		widget.SetTooltipText(string.IsNullOrEmpty(tooltipText) ? null : tooltipText);
	}

	Gtk.GestureClick? _contextGesture;
	Gtk.PopoverMenu? _contextPopover;

	/// <summary>
	/// Maps FlyoutBase.ContextFlyout to a GTK4 right-click popup menu.
	/// </summary>
	static void MapContextFlyout(GtkViewHandler<TVirtualView, TPlatformView> handler, IView view)
	{
		var widget = handler.PlatformView;
		if (widget == null) return;

		// Clean up previous context menu
		handler.CleanupContextFlyout(widget);

		Microsoft.Maui.Controls.FlyoutBase? flyout = null;
		if (view is Microsoft.Maui.Controls.BindableObject bo)
			flyout = Microsoft.Maui.Controls.FlyoutBase.GetContextFlyout(bo);

		if (flyout is not Microsoft.Maui.Controls.MenuFlyout menuFlyout || menuFlyout.Count == 0)
			return;

		handler.SetupContextFlyout(widget, menuFlyout);
	}

	void SetupContextFlyout(Gtk.Widget widget, Microsoft.Maui.Controls.MenuFlyout menuFlyout)
	{
		var menu = Gio.Menu.New();
		var actionGroup = Gio.SimpleActionGroup.New();
		int idx = 0;

		foreach (var item in menuFlyout)
		{
			if (item is Microsoft.Maui.Controls.MenuFlyoutSeparator)
			{
				// Start a new section after separator
				var section = Gio.Menu.New();
				menu.AppendSection(null, section);
				menu = section;
				continue;
			}

			if (item is Microsoft.Maui.Controls.MenuFlyoutItem mfi)
			{
				var actionName = $"ctx{idx++}";
				var action = Gio.SimpleAction.New(actionName, null);
				var captured = mfi;
				action.OnActivate += (_, _) =>
				{
					if (captured.Command?.CanExecute(captured.CommandParameter) == true)
						captured.Command.Execute(captured.CommandParameter);
					((Microsoft.Maui.Controls.IMenuItemController)captured).Activate();
				};
				actionGroup.AddAction(action);
				menu.Append(mfi.Text ?? "", $"ctx.{actionName}");
			}
		}

		widget.InsertActionGroup("ctx", actionGroup);

		var topMenu = Gio.Menu.New();
		topMenu.AppendSection(null, menu);

		_contextPopover = Gtk.PopoverMenu.NewFromModel(topMenu);
		_contextPopover.SetParent(widget);
		_contextPopover.SetHasArrow(false);

		_contextGesture = Gtk.GestureClick.New();
		_contextGesture.SetButton(3); // right-click
		_contextGesture.OnPressed += (sender, args) =>
		{
			if (_contextPopover == null) return;

			var rect = new Gdk.Rectangle { X = (int)args.X, Y = (int)args.Y, Width = 1, Height = 1 };
			_contextPopover.SetPointingTo(rect);
			_contextPopover.Popup();
		};
		widget.AddController(_contextGesture);
	}

	void CleanupContextFlyout(Gtk.Widget widget)
	{
		if (_contextGesture != null)
		{
			widget.RemoveController(_contextGesture);
			_contextGesture = null;
		}
		if (_contextPopover != null)
		{
			_contextPopover.Unparent();
			_contextPopover = null;
		}
		widget.InsertActionGroup("ctx", null);
	}
}

// Convenience overload for measuring GTK widgets
internal static class GtkWidgetMeasureExtensions
{
	public static void Measure(this Gtk.Widget widget, out int minWidth, out int natWidth, out int minHeight, out int natHeight)
	{
		widget.MeasureNative(Gtk.Orientation.Horizontal, -1, out minWidth, out natWidth, out _, out _);
		widget.MeasureNative(Gtk.Orientation.Vertical, -1, out minHeight, out natHeight, out _, out _);
	}

	public static void MeasureNative(this Gtk.Widget widget, Gtk.Orientation orientation, int forSize,
		out int minimum, out int natural, out int minimumBaseline, out int naturalBaseline)
	{
		widget.Measure(orientation, forSize, out minimum, out natural, out minimumBaseline, out naturalBaseline);
	}
}
