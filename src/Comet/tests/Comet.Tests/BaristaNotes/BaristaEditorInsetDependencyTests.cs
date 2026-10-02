#if BARISTA_LIFECYCLE_TESTS
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Comet.Tests.Backend;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;
using CometSamples.BaristaNotes;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Pages;
using CometSamples.BaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using Xunit.Abstractions;

namespace Comet.Tests.BaristaLifecycle;

[CollectionDefinition("Barista inset dependency", DisableParallelization = true)]
public sealed class BaristaInsetDependencyCollection;

[Collection("Barista inset dependency")]
public sealed class BaristaEditorInsetDependencyTests
{
    readonly ITestOutputHelper _output;
    public BaristaEditorInsetDependencyTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(32, 0)]
    [InlineData(0, 24)]
    public void NormalEditor_IrrelevantVerticalInset_DoesNotRebuild(double top, double bottom)
    {
        using var fixture = new EditorFixture();
        var frame = fixture.Frame;
        var before = frame.GetPadding();
        fixture.Metrics.UpdateSafeArea(new Thickness(0, top, 0, bottom));
        ReactiveScheduler.FlushSync();
        _output.WriteLine($"top={top}, bottom={bottom}, editor reloads={fixture.Reloads}");
        Assert.Equal(before, fixture.Frame.GetPadding());
#if INSET_ANDROID_TEST
        Assert.Equal(0, fixture.Reloads);
        Assert.Same(frame, fixture.Frame);
#else
        Assert.Equal(top > 0 ? 1 : 0, fixture.Reloads);
#endif
        fixture.Layout(new Size(400, 800));
        var content = Find(fixture.Page, "new_drink_content_grid");
#if INSET_ANDROID_TEST
        Assert.Equal(0, Node(content).ArrangedFrame!.Value.Y, 3);
#else
        Assert.Equal(top, Node(content).ArrangedFrame!.Value.Y, 3);
#endif
    }

    [Fact]
    public void NormalEditor_CutoutsAndRotation_UpdateActualContentGeometry()
    {
        using var fixture = new EditorFixture();
        foreach (var insets in new[] { new Thickness(28, 0, 0, 0),
                     new Thickness(0, 0, 36, 0), new Thickness(12, 0, 20, 0) })
        {
            var before = fixture.Reloads;
            fixture.Metrics.UpdateSafeArea(insets);
            fixture.Layout(new Size(800, 400));
            Assert.Equal(before + 1, fixture.Reloads);
            Assert.Equal(insets, fixture.Frame.GetPadding());
            var rect = Node(Find(fixture.Page, "new_drink_content_grid")).ArrangedFrame!.Value;
            Assert.Equal(insets.Left, rect.X, 3);
            Assert.Equal(800 - insets.Left - insets.Right, rect.Width, 3);
        }
    }

    [Fact]
    public void NormalEditor_InitialInsets_AreUsedOnFirstBody()
    {
        using var fixture = new EditorFixture(new Thickness(12, 32, 20, 24));
        Assert.Equal(new Thickness(12, 0, 20, 0), fixture.Frame.GetPadding());
        Assert.Equal(0, fixture.Reloads);
    }

    [Fact]
    public void NormalEditor_EqualNormalizedEdges_DoNotRebuild()
    {
        using var fixture = new EditorFixture();
        fixture.Metrics.UpdateSafeArea(new Thickness(-3, -8, -5, 24));
        fixture.Metrics.UpdateSafeArea(new Thickness(-9, -12, -7, 48));
        Assert.Equal(0, fixture.Reloads);
        Assert.Equal(Thickness.Zero, fixture.Frame.GetPadding());
    }

    [Fact]
    public void NormalEditor_DirectSignalWrites_KeepExistingMetricApi()
    {
        using var fixture = new EditorFixture();
        fixture.Metrics.SafeAreaDp.Value = new Thickness(15, 0, 25, 42);
        Assert.Equal(1, fixture.Reloads);
        Assert.Equal(new Thickness(15, 0, 25, 0), fixture.Frame.GetPadding());
        fixture.Metrics.SafeAreaDp.Value = new Thickness(15, 0, 25, 52);
        Assert.Equal(1, fixture.Reloads);
    }

    [Fact]
    public void NormalEditor_HeldUpdates_KeepExistingBatchSemantics()
    {
        using var fixture = new EditorFixture();
        using (ReactiveScheduler.HoldFlushes())
        {
            fixture.Metrics.UpdateSafeArea(new Thickness(12, 0, 0, 24));
            fixture.Metrics.UpdateSafeArea(new Thickness(0, 0, 20, 36));
            Assert.Equal(0, fixture.Reloads);
        }
        Assert.Equal(1, fixture.Reloads);
        Assert.Equal(new Thickness(0, 0, 20, 0), fixture.Frame.GetPadding());
    }

    [Fact]
    public void NormalEditor_HeightChange_ResizesWithoutSafeAreaDependency()
    {
        using var fixture = new EditorFixture();
        fixture.Layout(new Size(400, 800));
        var content = Find(fixture.Page, "new_drink_content_grid");
        var before = Node(content).ArrangedFrame!.Value.Height;
        fixture.Metrics.Update(new Size(400, 500));
        fixture.Layout(new Size(400, 500));
        Assert.Equal(0, fixture.Reloads);
        Assert.Equal(before - 300, Node(content).ArrangedFrame!.Value.Height, 3);
    }

    [Fact]
    public async Task PeoplePicker_BottomChangesStillApply_ReturnToEditorFiltersBottom()
    {
        using var fixture = new EditorFixture();
        await fixture.Services.ProfileService.CreateProfileAsync(new CreateUserProfileDto { Name = "Test person" });
        Node(Find(fixture.Page, "shot_tile_people")).Sink!.OnGesture(
            GestureKind.Tap, new GestureData(GestureState.Ended, default));
        Assert.Equal("picker_people", fixture.Page.GetView().AutomationId);
        var before = fixture.Reloads;
        fixture.Metrics.UpdateSafeArea(new Thickness(0, 0, 0, 42));
        Assert.Equal(before + 1, fixture.Reloads);
        var columns = Walk(fixture.Page).OfType<Grid>().Single(grid =>
            ((IContainerView)grid).GetChildren().OfType<ListView<UserProfile>>().Count() == 2);
        Assert.Equal(42, columns.GetPadding().Bottom);
        Node(Find(fixture.Page, "picker_close")).Sink!.OnEvent(EventIds.Clicked);
        Assert.Equal("new_drink_page", fixture.Page.GetView().AutomationId);
        before = fixture.Reloads;
        fixture.Metrics.UpdateSafeArea(new Thickness(0, 0, 0, 52));
        Assert.Equal(before, fixture.Reloads);
    }

    [Fact]
    public void OtherConsumer_SharedMetricBottomChange_RemainsReactive()
    {
        using var fixture = new EditorFixture();
        using var actions = new ActivityBottomActionRow(() => { }, () => { }, () => { }, () => { }, false)
            .WindowMetrics(fixture.Metrics);
        Materialize(actions);
        fixture.Metrics.UpdateSafeArea(new Thickness(0, 0, 0, 48));
        Assert.Equal(48, Find(actions, "nav_new_drink").GetPadding().Bottom);
        Assert.Equal(0, fixture.Reloads);
    }

    [Fact]
    public void NormalEditor_NormalSettingChange_StillUpdatesVisibleData()
    {
        using var fixture = new EditorFixture();
        var before = Texts(Find(fixture.Page, "shot_tile_temperature"));
        Assert.Equal(TemperatureUnit.Fahrenheit, fixture.Services.TemperatureUnit.Value);
        fixture.Services.Preferences.SetTemperatureUnit(TemperatureUnit.Celsius);
        fixture.Page.RefreshForActivation();
        Assert.True(fixture.Reloads > 0);
        Assert.NotEqual(before, Texts(Find(fixture.Page, "shot_tile_temperature")));
        Assert.Contains("93", Texts(Find(fixture.Page, "shot_tile_temperature")));
    }

    [Fact]
    public void NormalEditor_DifferentMetricOwners_DoNotShareProjection()
    {
        using var fixture = new EditorFixture();
        var otherMetrics = new CometWindowMetrics();
        using var other = new ShotLoggingPage(fixture.Services).WindowMetrics(otherMetrics);
        Materialize(other);
        fixture.Metrics.UpdateSafeArea(new Thickness(12, 0, 20, 0));
        Assert.Equal(Thickness.Zero, Find(other, "new_drink_edge_frame").GetPadding());
        otherMetrics.UpdateSafeArea(new Thickness(3, 0, 5, 0));
        Assert.Equal(new Thickness(12, 0, 20, 0), fixture.Frame.GetPadding());
        Assert.Equal(new Thickness(3, 0, 5, 0), Find(other, "new_drink_edge_frame").GetPadding());
    }

    [Fact]
    public void NormalEditor_RebuiltUnderNewOwner_DetachesOldMetric()
    {
        using var fixture = new EditorFixture();
        var next = new CometWindowMetrics();
        next.UpdateSafeArea(new Thickness(9, 0, 17, 0));
        fixture.Page.WindowMetrics(next);
        fixture.Page.Reload();
        Assert.Equal(new Thickness(9, 0, 17, 0), fixture.Frame.GetPadding());
        var before = fixture.Reloads;
        fixture.Metrics.UpdateSafeArea(new Thickness(41, 0, 43, 0));
        Assert.Equal(before, fixture.Reloads);
        next.UpdateSafeArea(new Thickness(19, 0, 27, 0));
        Assert.Equal(before + 1, fixture.Reloads);
        Assert.Equal(new Thickness(19, 0, 27, 0), fixture.Frame.GetPadding());
    }

    [Fact]
    public void NormalEditor_DisposedOwner_DetachesProjectionNotSharedMetrics()
    {
        using var fixture = new EditorFixture();
        var projected = Assert.IsType<PropertySubscription<Thickness>>(
            typeof(ShotLoggingPage).GetField("_editorInsetsProjection", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Page));
        fixture.Page.Dispose();
        var oldValue = projected.Value;
        var before = fixture.Reloads;
        using var actions = new ActivityBottomActionRow(() => { }, () => { }, () => { }, () => { }, false)
            .WindowMetrics(fixture.Metrics);
        Materialize(actions);
        fixture.Metrics.UpdateSafeArea(new Thickness(12, 0, 20, 48));
        Assert.Equal(oldValue, projected.Value);
        Assert.Equal(before, fixture.Reloads);
        Assert.Equal(48, Find(actions, "nav_new_drink").GetPadding().Bottom);
    }

    sealed class EditorFixture : IDisposable
    {
        readonly bool _previousDiagnostics;
        readonly IDisposable _diagnostics;
        public BaristaServices Services { get; }
        public CometWindowMetrics Metrics { get; } = new();
        public ShotLoggingPage Page { get; }
        public int Reloads { get; private set; }
        public View Frame => Find(Page.GetView(), "new_drink_edge_frame");

        public EditorFixture(Thickness initialInsets = default)
        {
            ThreadHelper.SetFireOnMainThread(action => action());
            CoffeeTheme.SetMode(CoffeeThemeMode.Dark);
            Services = new BaristaServices(new InMemoryDataStore());
            Metrics.UpdateSafeArea(initialInsets);
            Page = new ShotLoggingPage(Services);
            Page.WindowMetrics(Metrics);
            Materialize(Page);
            Assert.Equal("new_drink_page", Page.GetView().AutomationId);
            _previousDiagnostics = ReactiveDiagnostics.IsEnabled;
            ReactiveDiagnostics.IsEnabled = true;
            _diagnostics = ReactiveDiagnostics.OnViewRebuilt(e =>
            {
                if (e.ViewType == nameof(ShotLoggingPage))
                    Reloads++;
            });
        }

        public void Layout(Size size) => CometBackendLayoutEngine.Layout(Page, size);

        public void Dispose()
        {
            Page.Dispose();
            _diagnostics.Dispose();
            Services.Dispose();
            ReactiveDiagnostics.IsEnabled = _previousDiagnostics;
        }
    }

    static void Materialize(View view) =>
        CometBackendBridge.Materialize(view, child => new FakeBackendNode(child.GetType().Name),
            new BackendContext(new EmptyServices()));

    static FakeBackendNode Node(View view) => Assert.IsType<FakeBackendNode>(
        typeof(View).GetProperty("Node", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view));

    static string[] Texts(View view) => Walk(view).OfType<Text>()
        .Select(text => Node(text).Get(PropertyIds.Text_Value).AsString!).ToArray();

    static IEnumerable<View> Walk(View view)
    {
        var actual = view.GetView() ?? view;
        yield return actual;
        if (actual is IContainerView container)
            foreach (var child in container.GetChildren())
                foreach (var descendant in Walk(child))
                    yield return descendant;
    }

    static View Find(View view, string id)
    {
        var actual = view.GetView() ?? view;
        if (actual.AutomationId == id)
            return actual;
        if (actual is IContainerView container)
            foreach (var child in container.GetChildren())
                if (TryFind(child, id) is { } found)
                    return found;
        throw new InvalidOperationException("View not found: " + id);
    }

    static View? TryFind(View view, string id)
    {
        var actual = view.GetView() ?? view;
        if (actual.AutomationId == id)
            return actual;
        if (actual is IContainerView container)
            foreach (var child in container.GetChildren())
                if (TryFind(child, id) is { } found)
                    return found;
        return null;
    }

    sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
#endif
