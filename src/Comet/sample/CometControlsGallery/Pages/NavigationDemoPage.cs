using System;
using Comet;
using Comet.Styles;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
public class NavigationDemoPageState
{
public string ToolbarStamp { get; set; } = DateTimeOffset.Now.ToString("T");
}

public class NavigationDemoPage : Component<NavigationDemoPageState>
{
readonly DateTimeOffset createdAt = DateTimeOffset.Now;
readonly int depth;

public NavigationDemoPage(int depth = 0)
{
this.depth = depth;
}

protected override void OnLoaded()
{
base.OnLoaded();
ConfigureToolbar();
}

protected override void OnParentChange(View parent)
{
base.OnParentChange(parent);
ConfigureToolbar();
}

protected override void OnWillUnmount()
{
if (Navigation != null)
Navigation.ToolbarItems.Clear();
base.OnWillUnmount();
}

public override View Render() =>
GalleryPageHelpers.Scaffold($"Nav Depth {depth}",
GalleryPageHelpers.Section("Depth indicator", "Each page instance reports its place in the stack with a unique accent color and creation timestamp.",
GalleryPageHelpers.ColorBlock($"Page at Depth {depth}", GetDepthColor(), Colors.White, 72),
Text($"Page at Depth {depth}")
.Typography(TypographyTokens.TitleLarge)
.Color(GetDepthColor()),
GalleryPageHelpers.BodyText(GetDepthDescription()),
GalleryPageHelpers.Caption($"Page created at: {createdAt:hh:mm:ss tt}"),
				GalleryPageHelpers.Caption($"Toolbar stamp: {State.ToolbarStamp}")
			),
			GalleryPageHelpers.Section("Navigation actions", "Push a fresh page to go deeper or pop back toward the root. The buttons mirror the reference stack demo.",
				GalleryPageHelpers.NavButton("Push next page", () => Comet.NavigationView.Navigate(this, new NavigationDemoPage(depth + 1))),
				GalleryPageHelpers.NavButton("Pop current page", () => Comet.NavigationView.Pop(this))
					.IsEnabled(depth > 0),
				depth == 0
					? GalleryPageHelpers.Caption("Depth 0 is the root of this demo, so pop is disabled until you push another page.")
: GalleryPageHelpers.Caption("Pop returns to the previous page and restores the earlier stack depth.")
),
GalleryPageHelpers.Section("Toolbar parity", "While this page is active, toolbar items expose quick push/pop helpers plus a live timestamp stamp action.",
GalleryPageHelpers.BodyText("Toolbar item 1: Stamp — refreshes the timestamp shown above."),
GalleryPageHelpers.BodyText(depth == 0
? "Toolbar item 2: Push — opens the next depth from the navigation bar."
: "Toolbar item 2: Pop — returns to the previous depth from the navigation bar.")
)
);

void ConfigureToolbar()
{
if (Navigation == null)
return;

Navigation.ToolbarItems.Clear();
Navigation.ToolbarItems.Add(new ToolbarItem("Stamp", () =>
SetState(s => s.ToolbarStamp = DateTimeOffset.Now.ToString("T"))));
			Navigation.ToolbarItems.Add(new ToolbarItem(depth == 0 ? "Push" : "Pop", () =>
			{
				if (depth == 0)
					Comet.NavigationView.Navigate(this, new NavigationDemoPage(depth + 1));
				else
					Comet.NavigationView.Pop(this);
			}));
		}

string GetDepthDescription() => depth switch
{
0 => "You are at the root of the navigation stack.",
1 => "One level deep — a common detail page depth.",
2 => "Two pushes deep, useful for nested drills and setup flows.",
3 => "Three levels deep — a good stress test for the stack UI.",
_ => $"Depth {depth} keeps the same pattern going for deeper navigation tests."
};

Color GetDepthColor() => depth switch
{
0 => Colors.DodgerBlue,
1 => Colors.MediumSeaGreen,
2 => Colors.Orange,
3 => Colors.MediumPurple,
_ => Colors.Crimson
};
}
}
