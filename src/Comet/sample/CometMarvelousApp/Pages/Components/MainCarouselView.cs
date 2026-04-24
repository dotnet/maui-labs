using CometMarvelousApp.Models;
using Comet.Reactive;

namespace CometMarvelousApp.Pages.Components;

public class MainCarouselViewState
{
	public WonderType CurrentType { get; set; }
	public int CurrentIndex { get; set; }
}

/// <summary>
/// A carousel-style wonder selector. Users swipe horizontally to browse wonders
/// and tap/swipe up to select one. Uses a single AbsoluteLayout with
/// proportional bounds for all illustration layers (background, main, foreground)
/// matching the MauiReactor reference.
/// </summary>
public class MainCarouselView : View
{
	readonly WonderType _currentType;
	readonly bool _show;
	readonly Action<WonderType>? _onSelected;

	readonly Reactive<int> _selectedIndex = 0;

	static readonly WonderType[] _allWonders = Enum.GetValues<WonderType>();

	public MainCarouselView(WonderType currentType, bool show, Action<WonderType>? onSelected)
	{
		_currentType = currentType;
		_show = show;
		_onSelected = onSelected;
	}

	[Body]
	View body()
	{
		var idx = _selectedIndex.Value;
		var wonderType = _allWonders[Math.Clamp(idx, 0, _allWonders.Length - 1)];
		var config = Illustration.Config[wonderType];

		// Single AbsoluteLayout holds all image layers in z-order.
		// Proportional bounds (AbsoluteLayoutFlags.All) are multiplied
		// by the parent size at layout time, matching GetFinalBounds().
		var imageLayer = new Comet.AbsoluteLayout();

		// Background images (sun, clouds) — drawn first (behind everything)
		foreach (var img in config.BackgroundImages)
			imageLayer.Add(
				Image(img.Source)
					.Opacity(img.Opacity)
					.Aspect(Aspect.Fill)
					.LayoutBounds(img.FinalBounds)
					.LayoutFlags(AbsoluteLayoutFlags.All));

		// Main object (pyramid, statue, etc.)
		imageLayer.Add(
			Image(config.MainObjectImage.Source)
				.Opacity(config.MainObjectImage.Opacity)
				.Aspect(Aspect.Fill)
				.LayoutBounds(config.MainObjectImage.FinalBounds)
				.LayoutFlags(AbsoluteLayoutFlags.All));

		// Foreground images (vegetation, structures) — drawn on top
		foreach (var img in config.ForegroundImages)
			imageLayer.Add(
				Image(img.Source)
					.Opacity(img.Opacity)
					.Aspect(Aspect.Fill)
					.LayoutBounds(img.FinalBounds)
					.LayoutFlags(AbsoluteLayoutFlags.All));

		return new Comet.Grid
		{
			// Layer 1: Background gradient (secondary → primary, top → bottom)
			new BoxView()
				.Background(config.BackgroundBrush)
				.FillHorizontal()
				.FillVertical(),

			// Layer 2: All illustration images in a single AbsoluteLayout
			imageLayer
				.FillHorizontal()
				.FillVertical(),

			// Layer 3: Foreground gradient (transparent top → primary bottom)
			new BoxView()
				.Background(config.ForegroundBrush)
				.FillHorizontal()
				.FillVertical(),

			// Layer 4: Title + indicators + arrow
			VStack(
				Text(() =>
				{
					var i = _selectedIndex.Value;
					var wt = _allWonders[Math.Clamp(i, 0, _allWonders.Length - 1)];
					return Illustration.Config[wt].Title;
				})
					.Color(Colors.White)
					.FontFamily("YesevaOne")
					.FontSize(58)
					.HorizontalTextAlignment(TextAlignment.Center)
					.LineHeight(0.8)
					.Frame(width: 320),

				// Indicator dots
				HStack(
					_allWonders.Select((wt, i) =>
						new BoxView(Colors.White)
							.Frame(width: i == idx ? 20 : 10, height: 6)
							.ClipShape(new RoundedRectangle(3))
							.Margin(2)
					).ToArray()
				).Alignment(Alignment.Center)
					.Margin(new Thickness(0, 20, 0, 0)),

				// Swipe up hint arrow
				Image("common_arrow_indicator.png")
					.Frame(width: 30, height: 30)
					.Margin(new Thickness(0, 20, 0, 0))
					.Alignment(Alignment.Center)
			)
			.Alignment(Alignment.Bottom)
			.Margin(new Thickness(0, 0, 0, 80)),

			// Invisible buttons for navigation (left/right tap zones)
			HStack(
				Button("", () =>
				{
					if (_selectedIndex.Value > 0)
						_selectedIndex.Value--;
				})
					.Background(Colors.Transparent)
					.FillVertical()
					.Frame(width: 80),

				Spacer(),

				Button("", () =>
				{
					if (_selectedIndex.Value < _allWonders.Length - 1)
						_selectedIndex.Value++;
				})
					.Background(Colors.Transparent)
					.FillVertical()
					.Frame(width: 80)
			)
			.FillHorizontal()
			.FillVertical(),

			// Center tap zone to select
			Button("", () =>
			{
				var i = _selectedIndex.Value;
				var wt = _allWonders[Math.Clamp(i, 0, _allWonders.Length - 1)];
				_onSelected?.Invoke(wt);
			})
				.Background(Colors.Transparent)
				.Frame(width: 200, height: 200)
				.Alignment(Alignment.Center),
		}
		.IgnoreSafeArea()
		.Opacity(_show ? 1.0 : 0.0);
	}
}
