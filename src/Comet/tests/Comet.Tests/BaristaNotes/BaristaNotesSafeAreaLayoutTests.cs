#nullable enable
using System;
using System.IO;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Comet.Tests.Backend;
using CometSamples.BaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class BaristaNotesSafeAreaLayoutTests
{
    static BaristaNotesSafeAreaLayoutTests() =>
        ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

    static readonly BackendContext Context = new(new EmptyServiceProvider());

    [Theory]
    [InlineData(0, 30)]
    [InlineData(24, 30)]
    [InlineData(34, 34)]
    [InlineData(48, 48)]
    public void FooterPadding_RespectsNativeBottomGuideWithoutAddingAnotherInset(
        double nativeBottom,
        double expectedBottom)
    {
        var padding = BaristaSafeAreaLayout.ActionPadding(new Thickness(0, 62, 0, nativeBottom));

        Assert.Equal(expectedBottom, padding.Bottom);
        Assert.Equal(CoffeeSpacing.ActionRowTopPadding, padding.Top);
    }

    [Fact]
    public void EdgeToEdgeRoot_DoesNotCascadeIntoLeafHeader()
    {
        var header = new Grid().AutomationId("safe_header");
        var root = new Grid { header }
            .Background(CoffeeTheme.OutlineColor)
            .IgnoreSafeArea()
            .AutomationId("edge_to_edge_root");

        Materialize(root);

        Assert.True(root.GetIgnoreSafeArea(false));
        Assert.False(header.GetIgnoreSafeArea(false));
        Assert.Equal(
            CoffeeTheme.OutlineColor,
            ((SolidPaint)root.GetBackground()).Color);
    }

    [Fact]
    public void SafeTopChrome_RerendersWithoutAccumulatingHeaderOrPickerInsets()
    {
        var metrics = new CometWindowMetrics();
        metrics.UpdateSafeArea(new Thickness(0, 59, 0, 34));
        var probe = new SafeChromeProbe();
        probe.WindowMetrics(metrics);

        Materialize(probe);
        AssertChrome(probe, isPicker: false, topInset: 59);
        var retainedRootNode = probe.GetView().Node;

        metrics.UpdateSafeArea(new Thickness(0, 47, 0, 34));
        ReactiveScheduler.FlushSync();

        AssertChrome(probe, isPicker: false, topInset: 47);
        Assert.Same(retainedRootNode, probe.GetView().Node);

        probe.ShowPicker.Value = true;
        ReactiveScheduler.FlushSync();

        AssertChrome(probe, isPicker: true, topInset: 47);
        Assert.Same(retainedRootNode, probe.GetView().Node);
    }

    [Fact]
    public void SettingsProfilesBackNewDrinkCycle_ReappliesOneStableTopInset()
    {
        var metrics = new CometWindowMetrics();
        metrics.UpdateSafeArea(new Thickness(0, 59, 0, 34));
        var newDrinkNavigation = new NavigationView();
        var settingsNavigation = new NavigationView();
        using var active = new OwnedContentSlot<FakeBackendNode>(
            newDrinkNavigation,
            view => new FakeBackendNode(view.GetType().Name),
            Context);

        var newDrink = Page(metrics, "new_drink_page");
        active.Materialize(newDrink);
        AssertChrome(newDrink, isPicker: false, topInset: 59);

        active.TransferOwner(settingsNavigation);
        var settings = Page(metrics, "settings_section_root");
        active.Materialize(settings);
        AssertChrome(settings, isPicker: false, topInset: 59);

        var profiles = Page(metrics, "profile_management_page");
        active.Materialize(profiles);
        AssertChrome(profiles, isPicker: false, topInset: 59);

        var settingsAfterBack = Page(metrics, "settings_section_root");
        active.Materialize(settingsAfterBack);
        AssertChrome(settingsAfterBack, isPicker: false, topInset: 59);

        active.TransferOwner(newDrinkNavigation);
        var newDrinkAfterCycle = Page(metrics, "new_drink_page");
        active.Materialize(newDrinkAfterCycle);
        AssertChrome(newDrinkAfterCycle, isPicker: false, topInset: 59);
    }

    [Fact]
    public void BaristaTopChrome_UsesSafeInsetWhileShellKeepsEdgeToEdgeBackground()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var app = Read(root!, "sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var shot = Read(root!, "sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var settings = Read(root!, "sample/Shared/BaristaNotes/Pages/SettingsPage.cs");
        var profile = Read(root!, "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs");
        var bean = Read(root!, "sample/Shared/BaristaNotes/Pages/BeanDetailPage.cs");
        var bag = Read(root!, "sample/Shared/BaristaNotes/Pages/BagDetailPage.cs");
        var equipment = Read(root!, "sample/Shared/BaristaNotes/Pages/EquipmentDetailPage.cs");

        Assert.Contains(".IgnoreSafeArea()", app);
        Assert.Contains("BaristaEdgeFrame.CreateEqualDataRows", shot);
        Assert.DoesNotContain("topInset: topInset", shot);
        Assert.Contains("BaristaSafeAreaLayout.PickerHeaderPadding(safeArea)", shot);
        Assert.Contains("BaristaSafeAreaLayout.HeaderPadding(safeArea)", settings);
        Assert.Contains("BaristaSafeAreaLayout.HeaderPadding(safeArea)", profile);
        Assert.Contains("BaristaEdgeFrame.Build(", bean);
        Assert.Contains("safeArea),", bean);
        Assert.Contains("BaristaEdgeFrame.Build(", bag);
        Assert.Contains("safeArea),", bag);
        Assert.Contains("BaristaSafeAreaLayout.HeaderPadding(safeArea)", equipment);
    }

    static SafeChromeProbe Page(CometWindowMetrics metrics, string automationId)
    {
        var page = new SafeChromeProbe(automationId);
        page.WindowMetrics(metrics);
        return page;
    }

    static FakeBackendNode Materialize(View root) =>
        (FakeBackendNode)CometBackendBridge.Materialize(
            root,
            view => new FakeBackendNode(view.GetType().Name),
            Context);

    static void AssertChrome(SafeChromeProbe probe, bool isPicker, float topInset)
    {
        var root = Assert.IsType<Grid>(probe.GetView());
        var header = FindByAutomationId(root, "safe_header");
        var padding = header.GetPadding();
        var expectedBasePadding = isPicker
            ? BaristaSafeAreaLayout.PickerHeaderVerticalPadding
            : BaristaSafeAreaLayout.HeaderVerticalPadding;
        var expectedMinimumHeight = isPicker
            ? BaristaSafeAreaLayout.PickerHeaderContentHeight
            : BaristaSafeAreaLayout.HeaderMinimumHeight(
                new Thickness(0, topInset, 0, 0));

        Assert.True(root.GetIgnoreSafeArea(false));
        Assert.False(header.GetIgnoreSafeArea(false));
        Assert.Equal(expectedBasePadding + topInset, padding.Top, 3);
        Assert.Null(header.GetFrameConstraints()?.Height);
        Assert.Equal((double)expectedMinimumHeight, ((IView)header).MinimumHeight);
        Assert.Equal(
            CoffeeTheme.OutlineColor,
            ((SolidPaint)root.GetBackground()).Color);
        Assert.Equal(
            CoffeeTheme.SurfaceColor,
            ((SolidPaint)header.GetBackground()).Color);
    }

    static View FindByAutomationId(View view, string automationId)
    {
        var rendered = view.GetView() ?? view;
        if (rendered.AutomationId == automationId)
            return rendered;
        if (rendered is IContainerView container)
        {
            foreach (var child in container.GetChildren())
            {
                if (FindByAutomationIdOrNull(child as View, automationId) is { } match)
                    return match;
            }
        }

        throw new InvalidOperationException($"Could not find {automationId}.");
    }

    static View? FindByAutomationIdOrNull(View? view, string automationId)
    {
        if (view is null)
            return null;
        var rendered = view.GetView() ?? view;
        if (rendered.AutomationId == automationId)
            return rendered;
        if (rendered is not IContainerView container)
            return null;
        foreach (var child in container.GetChildren())
        {
            if (FindByAutomationIdOrNull(child as View, automationId) is { } match)
                return match;
        }

        return null;
    }

    static string Read(string root, string relativePath) =>
        File.ReadAllText(IOPath.Combine(root, relativePath));

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 10 && directory is not null; index++)
        {
            if (File.Exists(IOPath.Combine(directory, "global.json"))
                && Directory.Exists(IOPath.Combine(directory, "sample")))
            {
                return directory;
            }

            directory = IOPath.GetDirectoryName(directory);
        }

        return null;
    }

    sealed class SafeChromeProbe : View
    {
        readonly string _automationId;

        public SafeChromeProbe(string automationId = "safe_area_probe") =>
            _automationId = automationId;

        public Signal<bool> ShowPicker { get; } = new(false);

        [Body]
        View body()
        {
            var topInset = BaristaSafeAreaLayout.GetTopInset(this, applyInset: true);
            var isPicker = ShowPicker.Value;
            var padding = isPicker
                ? BaristaSafeAreaLayout.PickerHeaderPadding(topInset)
                : BaristaSafeAreaLayout.HeaderPadding(topInset);
            var safeArea = new Thickness(0, topInset, 0, 0);
            var minimumHeight = isPicker
                ? BaristaSafeAreaLayout.PickerHeaderContentHeight
                : BaristaSafeAreaLayout.HeaderMinimumHeight(safeArea);

            return new Grid
            {
                new Grid()
                    .Padding(padding)
                    .MinimumHeight(minimumHeight)
                    .Background(CoffeeTheme.SurfaceColor)
                    .AutomationId("safe_header"),
            }
            .Background(CoffeeTheme.OutlineColor)
            .IgnoreSafeArea()
            .AutomationId(_automationId);
        }
    }

    sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
