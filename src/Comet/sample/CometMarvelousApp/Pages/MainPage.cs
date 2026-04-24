using CometMarvelousApp.Models;
using CometMarvelousApp.Pages.Components;

namespace CometMarvelousApp.Pages;

public class MainPageState
{
	public WonderType CurrentWonderType { get; set; }
	public NavigatorTabKey CurrentTab { get; set; }
	public bool ShowNavigator { get; set; } = true;
}

public class MainPage : Component<MainPageState>
{
	public override View Render()
	{
		var showNavigator = State.ShowNavigator;
		var currentType = State.CurrentWonderType;

		return new Comet.Grid
		{
			new MainCarouselView(currentType, showNavigator, OnSelectWonder),

			!showNavigator
				? new WonderWiki(currentType)
				: null,

			!showNavigator
				? new WonderNavigator(
					currentType,
					tab => SetState(s => s.CurrentTab = tab),
					() => SetState(s => s.ShowNavigator = true))
				: null,
		};
	}

	void OnSelectWonder(WonderType wonderType)
	{
		SetState(s =>
		{
			s.CurrentWonderType = wonderType;
			s.ShowNavigator = false;
		});
	}
}
