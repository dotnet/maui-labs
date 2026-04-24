using CometMarvelousApp.Models;
using CometMarvelousApp.Services;
using Comet.Reactive;

namespace CometMarvelousApp.Pages.Components;

/// <summary>
/// Bottom navigation bar showing wonder icon + tab buttons.
/// Ported from MauiReactor Canvas-based navigator to standard Comet controls.
/// </summary>
public class WonderNavigator : View
{
	readonly WonderType _wonderType;
	readonly Action<NavigatorTabKey>? _onTabSelected;
	readonly Action? _onBackToWonderSelect;

	readonly Reactive<NavigatorTabKey> _currentTab = NavigatorTabKey.Editorial;

	static readonly NavigatorTabKey[] _navigatorTabKeys = Enum.GetValues<NavigatorTabKey>();

	public WonderNavigator(
		WonderType wonderType,
		Action<NavigatorTabKey>? onTabSelected,
		Action? onBackToWonderSelect)
	{
		_wonderType = wonderType;
		_onTabSelected = onTabSelected;
		_onBackToWonderSelect = onBackToWonderSelect;
	}

	[Body]
	View body()
	{
		var config = Illustration.Config[_wonderType];

		return HStack(
			// Wonder button (back to carousel)
			Button("", () => _onBackToWonderSelect?.Invoke())
				.Background(AppTheme.DarkTertiaryColor)
				.ClipShape(new Ellipse())
				.Frame(width: 56, height: 56)
				.Margin(new Thickness(12, 0, 8, 0)),

			// Tab buttons
			HStack(
				_navigatorTabKeys.Select(tabKey =>
				{
					var isActive = _currentTab.Value == tabKey;
					var imageName = $"common_tab_{tabKey.ToString().ToLower()}{(isActive ? "_active" : "")}.png";

					return VStack(
						Image(imageName)
							.Frame(width: 24, height: 24),
						new BoxView(isActive ? AppTheme.PrimaryColor : Colors.Transparent)
							.Frame(width: isActive ? 24 : 10, height: 4)
							.ClipShape(new RoundedRectangle(2))
					)
					.Alignment(Alignment.Center)
					.OnTap(_ =>
					{
						_currentTab.Value = tabKey;
						_onTabSelected?.Invoke(tabKey);
					})
					.Frame(width: 60, height: 60);
				}).ToArray()
			)
			.Alignment(Alignment.Center)
		)
		.Background(AppTheme.TertiaryColor)
		.Frame(height: 72)
		.Alignment(Alignment.Bottom);
	}
}
