using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.DevFlow.Tests;

public class HitTestInputVisibilityTests
{
    [Fact]
    public void IsPointVisibleToElement_ScrolledItemOverHeader_IsExcluded()
    {
        var item = new Border { Frame = new Rect(0, 0, 380, 98) };
        var scroll = new ScrollView { Frame = new Rect(0, 112, 400, 700), Content = item };
        var walker = new BoundsWalker();

        Assert.False(walker.IsPointVisibleToElement(item, 38, 82));
        Assert.True(walker.IsPointVisibleToElement(scroll, 38, 150));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsPointVisibleToElement_Overflow_RespectsParentClipping(bool clipped, bool expected)
    {
        var child = new Button { Frame = new Rect(0, 0, 100, 100) };
        var parent = new Grid { Frame = new Rect(50, 50, 50, 50), IsClippedToBounds = clipped, Children = { child } };

        Assert.Equal(expected, new BoundsWalker().IsPointVisibleToElement(child, 20, 20));
    }

    [Fact]
    public void IsPointVisibleToElement_InvisibleAncestor_IsExcluded()
    {
        var child = new Button { Frame = new Rect(0, 0, 40, 40) };
        var parent = new Grid { IsVisible = false, Children = { child } };

        Assert.False(new BoundsWalker().IsPointVisibleToElement(child, 20, 20));
    }

    [Fact]
    public void IsPointVisibleToElement_HiddenShellTab_UsesCurrentPage()
    {
        var homeButton = new Button { Frame = new Rect(0, 0, 40, 40) };
        var catalogButton = new Button { Frame = new Rect(0, 0, 40, 40) };
        var home = new ContentPage { Content = homeButton, Frame = new Rect(0, 0, 400, 800) };
        var catalog = new ContentPage { Content = catalogButton, Frame = new Rect(0, 0, 400, 800) };
        var first = new ShellSection { Items = { new ShellContent { Content = home } } };
        var second = new ShellSection { Items = { new ShellContent { Content = catalog } } };
        var tabs = new TabBar { Items = { first, second } };
        var shell = new Shell { Items = { tabs }, CurrentItem = tabs };
        Shell.SetTabBarIsVisible(shell, false);
        tabs.CurrentItem = second;
        var walker = new BoundsWalker();

        Assert.False(walker.IsPointVisibleToElement(homeButton, 20, 20));
        Assert.True(walker.IsPointVisibleToElement(catalogButton, 20, 20));
    }

    private sealed class BoundsWalker : VisualTreeWalker
    {
        protected override BoundsInfo? ResolveWindowBounds(VisualElement view)
            => view.Width < 0 || view.Height < 0 ? null : new BoundsInfo
            {
                X = view.Frame.X,
                Y = view.Frame.Y,
                Width = view.Frame.Width,
                Height = view.Frame.Height
            };
    }
}
