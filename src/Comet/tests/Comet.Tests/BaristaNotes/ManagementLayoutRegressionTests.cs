#nullable enable
using System;
using System.Linq;
using Comet;
using Comet.Backend;
using Comet.Tests.Backend;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.BaristaNotes;

public sealed class ManagementLayoutRegressionTests
{
    static ManagementLayoutRegressionTests() =>
        ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

    static readonly BackendContext Context = new(new EmptyServiceProvider());

    [Fact]
    public void ManagementPage_HidesNativeBackChromeWithoutDisablingBackNavigation()
    {
        var page = new ManagementPageProbe();
        var behavior = page.GetBackButtonBehavior();
        var state = NavigationBackChromeState.Resolve(new View[] { new Grid(), page });

        Assert.False(behavior.IsVisible);
        Assert.True(behavior.IsEnabled);
        Assert.False(state.IsVisible);
        Assert.True(state.IsEnabled);
    }

    [Theory]
    [InlineData(0, 52, 13, 36, 77)]
    [InlineData(62, 0, 14, 39, 143)]
    [InlineData(0, 0, 13, 36, 120)]
    [InlineData(0, 52, 13, 72, 113)]
    public void SharedHeader_MeasuresTextAndAccountsForTheInsetOnlyOnce(
        double insideTop,
        double outsideTop,
        double captionHeight,
        double titleHeight,
        double expectedHeight)
    {
        var header = BaristaPageHeader.Build(
            "BEANS", "Coffee beans", new Thickness(0, insideTop, 0, 0), "header")
            .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight(outsideTop));
        var body = new Grid();
        var root = new Grid(rows: new object[] { "Auto", "*" }, rowSpacing: 1)
        {
            header.Cell(row: 0),
            body.Cell(row: 1),
        };
        Materialize(root);
        var children = Children(header);
        Node(children[0]).MeasureResult = new Size(70, captionHeight);
        Node(children[1]).MeasureResult = new Size(200, titleHeight);

        CometBackendLayoutEngine.Layout(root, new Size(427, 900));

        Assert.Equal(expectedHeight, header.Frame.Height, 3);
        Assert.Equal(expectedHeight + 1, body.Frame.Y, 3);
        Assert.Equal(insideTop + 14, children[0].Frame.Y, 3);
        Assert.Equal(14, header.Frame.Height - children[1].Frame.Bottom, 3);
        Assert.Null(header.GetFrameConstraints()?.Height);
        if (expectedHeight == insideTop + 28 + captionHeight + titleHeight)
            Assert.Equal(children[0].Frame.Bottom, children[1].Frame.Y, 3);
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(0, 120)]
    [InlineData(52, 68)]
    [InlineData(120, 0)]
    [InlineData(140, 0)]
    public void HeaderMinimum_OnlySubtractsSpaceAlreadyReservedByTheHost(
        double reservedTop,
        double expectedMinimum) =>
        Assert.Equal(expectedMinimum, BaristaSafeAreaLayout.HeaderMinimumHeight(reservedTop));

    [Fact]
    public void ProfileRow_PutsCaptionAboveAvatarAndCentersNameWithAvatar()
    {
        var avatar = new Grid().Frame(width: 48, height: 48);
        var row = ProfileListRow.Build("David", avatar, "profile", () => { });
        var root = new VStack(spacing: 0) { row };
        Materialize(root);
        var children = Children(row);
        var caption = children[0];
        var name = children[2];
        var chevron = children[3];
        Node(caption).MeasureResult = new Size(70, 13);
        Node(name).MeasureResult = new Size(70, 26);
        Node(chevron).MeasureResult = new Size(24, 24);

        var measured = CometBackendLayoutEngine.LayoutContent(root, 427);

        Assert.Equal(16, caption.Frame.X, 3);
        Assert.Equal(caption.Frame.X, avatar.Frame.X, 3);
        Assert.True(caption.Frame.Bottom <= avatar.Frame.Y);
        Assert.Equal(CenterY(avatar), CenterY(name), 3);
        Assert.Equal(row.Frame.Height / 2, CenterY(chevron), 3);
        Assert.Equal(12, name.Frame.X - avatar.Frame.Right, 3);
        Assert.Equal(20, name.GetFont(null).Size);
        Assert.Equal(48, avatar.Frame.Height);
        Assert.Equal(row.Frame.Height + 1, measured.Height, 3);
    }

    [Fact]
    public void ManagementRow_UsesSourceNameSizeAndCenteredTwoLineContent()
    {
        var row = ManagementListRow.Build("ROASTER", "Coffee", "bean", () => { });
        var root = new VStack(spacing: 0) { row };
        Materialize(root);
        var children = Children(row);
        Node(children[0]).MeasureResult = new Size(70, 13);
        Node(children[1]).MeasureResult = new Size(120, 26);
        Node(children[2]).MeasureResult = new Size(24, 24);

        var measured = CometBackendLayoutEngine.LayoutContent(root, 427);

        Assert.Equal(80, row.Frame.Height, 3);
        Assert.Equal(81, measured.Height, 3);
        Assert.Equal(20, children[1].GetFont(null).Size);
        Assert.Equal(
            row.Frame.Height / 2,
            (children[0].Frame.Y + children[1].Frame.Bottom) / 2,
            3);
        Assert.Equal(row.Frame.Height / 2, CenterY(children[2]), 3);
    }

    [Fact]
    public void DetailSections_PaintOneUnitGapsWithoutExpandingTheContent()
    {
        var first = new Grid().Frame(height: 90).Background(CoffeeTheme.SurfaceColor);
        var second = new Grid().Frame(height: 150).Background(CoffeeTheme.SurfaceColor);
        var third = new Grid().Frame(height: 100).Background(CoffeeTheme.SurfaceColor);
        var sections = BaristaSections.Create(first, second);
        sections.Add(third);
        Materialize(sections);

        var measured = CometBackendLayoutEngine.LayoutContent(sections, 427);

        Assert.Equal(342, measured.Height, 3);
        Assert.Equal(1, second.Frame.Y - first.Frame.Bottom, 3);
        Assert.Equal(1, third.Frame.Y - second.Frame.Bottom, 3);
        Assert.Equal(CoffeeTheme.OutlineColor, ((SolidPaint)sections.GetBackground()).Color);
        Assert.All(Children(sections), child =>
            Assert.Equal(CoffeeTheme.SurfaceColor, ((SolidPaint)child.GetBackground()).Color));
    }

    [Fact]
    public void DetailViewport_PaintsTheAreaBelowShortNativeScrollContent()
    {
        var sections = BaristaSections.Create(new Grid().Frame(height: 90));
        var viewport = BaristaSections.Scroll(sections, "form_scroll");
        var scroll = Assert.IsType<ScrollView>(Assert.Single(Children(viewport)));
        CometBackendBridge.Materialize(
            viewport,
            view => view is ScrollView
                ? new FakeNativeScrollNode()
                : new FakeBackendNode(view.GetType().Name),
            Context);

        CometBackendLayoutEngine.Layout(viewport, new Size(427, 600));

        Assert.Equal(600, viewport.Frame.Height, 3);
        Assert.Equal(600, scroll.Frame.Height, 3);
        Assert.Equal("form_scroll", scroll.AutomationId);
        Assert.Equal(CoffeeTheme.SurfaceColor, ((SolidPaint)viewport.GetBackground()).Color);
        Assert.Equal(CoffeeTheme.OutlineColor, ((SolidPaint)sections.GetBackground()).Color);
    }

    static double CenterY(View view) => view.Frame.Y + view.Frame.Height / 2;

    static View[] Children(View view) =>
        ((IContainerView)view).GetChildren().Select(child => Assert.IsAssignableFrom<View>(child)).ToArray();

    static FakeBackendNode Node(View view) => (FakeBackendNode)view.Node!;

    static void Materialize(View root) => CometBackendBridge.Materialize(
        root, view => new FakeBackendNode(view.GetType().Name), Context);

    sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    sealed class ManagementPageProbe : BaristaManagementPage
    {
        [Body]
        View body() => new Grid();
    }

    sealed class FakeNativeScrollNode() : FakeBackendNode(nameof(ScrollView)), IBackendManagesOwnContent;
}
