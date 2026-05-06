using CoreGraphics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

/// <summary>
/// IndicatorView handler for macOS. Renders page indicator dots.
/// </summary>
public partial class IndicatorViewHandler : MacOSViewHandler<IndicatorView, NSView>
{
	public static readonly IPropertyMapper<IndicatorView, IndicatorViewHandler> Mapper =
		new PropertyMapper<IndicatorView, IndicatorViewHandler>(ViewMapper)
		{
			[nameof(IndicatorView.Count)] = MapIndicators,
			[nameof(IndicatorView.Position)] = MapIndicators,
			[nameof(IndicatorView.IndicatorColor)] = MapIndicators,
			[nameof(IndicatorView.SelectedIndicatorColor)] = MapIndicators,
			[nameof(IndicatorView.IndicatorSize)] = MapIndicators,
			[nameof(IndicatorView.IndicatorsShape)] = MapIndicators,
		};

	MacOSContainerView? _container;
	readonly List<NSView> _dots = new();

	public IndicatorViewHandler() : base(Mapper) { }

	protected override NSView CreatePlatformView()
	{
		_container = new MacOSContainerView();
		_container.WantsLayer = true;
		return _container;
	}

	public override void PlatformArrange(Rect rect)
	{
		base.PlatformArrange(rect);
		LayoutDots(rect);
	}

	public static void MapIndicators(IndicatorViewHandler handler, IndicatorView view)
	{
		handler.UpdateIndicators();
	}

	void UpdateIndicators()
	{
		if (_container == null) return;

		foreach (var dot in _dots)
			dot.RemoveFromSuperview();
		_dots.Clear();

		if (VirtualView == null) return;

		var count = VirtualView.Count;
		var position = VirtualView.Position;
		var size = VirtualView.IndicatorSize > 0 ? VirtualView.IndicatorSize : 8;
		var normalColor = VirtualView.IndicatorColor ?? Colors.LightGray;
		var selectedColor = VirtualView.SelectedIndicatorColor ?? Colors.DarkGray;
		var isSquare = VirtualView.IndicatorsShape == IndicatorShape.Square;

		for (int i = 0; i < count; i++)
		{
			var dot = new NSView(new CGRect(0, 0, size, size));
			dot.WantsLayer = true;
			dot.Layer!.BackgroundColor = (i == position ? selectedColor : normalColor).ToPlatformColor().CGColor;
			if (!isSquare)
				dot.Layer.CornerRadius = (nfloat)(size / 2.0);
			_dots.Add(dot);
			_container.AddSubview(dot);
		}

		if (PlatformView.Frame.Width > 0)
			LayoutDots(new Rect(0, 0, PlatformView.Frame.Width, PlatformView.Frame.Height));
	}

	void LayoutDots(Rect rect)
	{
		if (_dots.Count == 0) return;

		var size = VirtualView?.IndicatorSize > 0 ? VirtualView.IndicatorSize : 8;
		var spacing = size * 0.75;
		var totalWidth = _dots.Count * size + (_dots.Count - 1) * spacing;
		var startX = (rect.Width - totalWidth) / 2;
		var y = (rect.Height - size) / 2;

		for (int i = 0; i < _dots.Count; i++)
		{
			_dots[i].Frame = new CGRect(startX + i * (size + spacing), y, size, size);
		}
	}

	public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
	{
		var size = VirtualView?.IndicatorSize > 0 ? VirtualView.IndicatorSize : 8;
		var count = VirtualView?.Count ?? 0;
		var spacing = size * 0.75;
		var width = count * size + Math.Max(0, count - 1) * spacing;
		return new Size(width, size);
	}
}
