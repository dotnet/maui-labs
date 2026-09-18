#nullable enable
using System;
using System.IO;
using System.Linq;
using Comet;
using Comet.Backend;
using Comet.Tests.Backend;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class RecoveryFoundationTests
{
    static RecoveryFoundationTests() =>
        ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

    static readonly BackendContext Context = new(new EmptyServiceProvider());

    [Theory]
    [InlineData(51.809525, 0, 120)]
    [InlineData(0, 51.809525, 68.190475)]
    public void HeaderMinimumPolicy_CountsTheTopInsetAtItsActualOwner(
        double topInset,
        double reservedTopInset,
        double expectedMinimum)
    {
        var metrics = new CometWindowMetrics();
        metrics.UpdateSafeArea(new Thickness(0, topInset, 0, 24));
        var probe = new HeaderPolicyProbe(reservedTopInset);
        probe.WindowMetrics(metrics);
        Materialize(probe);

        CometBackendLayoutEngine.Layout(probe, new Size(411.428558, 914.285706));

        var header = FindByAutomationId(probe, "recovery_policy_header");
        Assert.Equal(expectedMinimum, ((IView)header).MinimumHeight, 3);
        Assert.Equal(Math.Ceiling(expectedMinimum), header.Frame.Height, 3);
        Assert.Equal(
            BaristaSafeAreaLayout.HeaderVerticalPadding + topInset,
            header.GetPadding().Top,
            3);
    }

    [Fact]
    public void RangeCaptionRole_IsDistinctFromSettingsAndDrivesNativeMeasurement()
    {
        var settingsCaption = new Text("SETTINGS")
            .SectionLabel()
            .AutomationId("recovery_settings_caption");
        var rangeCaption = new Text("VALUE RANGES")
            .RangeCaption()
            .AutomationId("recovery_range_caption");
        var methodCaption = new Text("ESPRESSO")
            .RangeCaption()
            .CharacterSpacing(1.5)
            .AutomationId("recovery_method_caption");
        var root = new VStack
        {
            settingsCaption,
            rangeCaption,
            methodCaption,
        };

        CometBackendBridge.Materialize(
            root,
            view => new FakeBackendNode(view.GetType().Name)
            {
                MeasureFunc = (_, _) =>
                {
                    var font = view.GetFont(null);
                    return new Size(font.Size * 8, font.Size * 1.4);
                },
            },
            Context);
        CometBackendLayoutEngine.Layout(root, new Size(320, 200));

        Assert.Equal(10, settingsCaption.GetFont(null).Size);
        Assert.Equal(CoffeeFontSizes.Caption, rangeCaption.GetFont(null).Size);
        Assert.Equal("ManropeSemibold", rangeCaption.GetFont(null).Family);
        Assert.Equal(1.5, ((ITextStyle)methodCaption).CharacterSpacing);
        Assert.True(rangeCaption.Frame.Height > settingsCaption.Frame.Height);

        var projectRoot = FindCometRoot();
        Assert.NotNull(projectRoot);
        var settingsPage = Read(
            projectRoot!,
            "sample/Shared/BaristaNotes/Pages/SettingsPage.cs");
        var rangeSettings = Read(
            projectRoot!,
            "sample/Shared/BaristaNotes/Pages/ValueRangeSettingsPage.cs");
        var rangeEditor = Read(
            projectRoot!,
            "sample/Shared/BaristaNotes/Pages/ValueRangeEditorPage.cs");

        Assert.DoesNotContain(".RangeCaption()", settingsPage);
        Assert.Equal(2, rangeSettings.Split(".RangeCaption()", StringSplitOptions.None).Length - 1);
        Assert.Equal(3, rangeEditor.Split(".RangeCaption()", StringSplitOptions.None).Length - 1);
        Assert.Contains("rangeCaption: true", rangeSettings);
        Assert.Contains("rangeCaption: true", rangeEditor);
    }

    [Theory]
    [InlineData(402, 874, 62)]
    [InlineData(411.428558, 914.285706, 51.809525)]
    public void SharedEdgeFrame_ArrangesIntrinsicHeaderBoundedBodyAndIntrinsicFooter(
        double width,
        double height,
        double topInset)
    {
        var metrics = new CometWindowMetrics();
        metrics.UpdateSafeArea(new Thickness(0, topInset, 0, 34));
        var probe = new EdgeFrameProbe();
        probe.WindowMetrics(metrics);
        Materialize(probe);

        CometBackendLayoutEngine.Layout(probe, new Size(width, height));

        var header = FindByAutomationId(probe, "recovery_header");
        var body = FindByAutomationId(probe, "recovery_body");
        var footer = FindByAutomationId(probe, "recovery_footer");
        var edgeFrame = FindByAutomationId(probe, "recovery_edge_frame");

        Assert.Null(header.GetFrameConstraints()?.Height);
        Assert.Null(footer.GetFrameConstraints()?.Height);
        Assert.True(header.Frame.Height >= CoffeeSpacing.HeaderHeight);
        Assert.True(footer.Frame.Height >= CoffeeSpacing.ActionRowHeight);
        Assert.True(body.Frame.Height > 0);
        Assert.Equal(header.Frame.Bottom + CoffeeSpacing.Divider, body.Frame.Y, 3);
        Assert.Equal(body.Frame.Bottom + CoffeeSpacing.Divider, footer.Frame.Y, 3);
        Assert.True(
            footer.Frame.Bottom <= edgeFrame.Frame.Bottom,
            $"Footer {footer.Frame} escaped the allocated edge frame {edgeFrame.Frame}.");
        Assert.Equal(
            BaristaSafeAreaLayout.HeaderVerticalPadding + topInset,
            header.GetPadding().Top,
            3);
    }

    [Theory]
    [InlineData(402, 874, 62)]
    [InlineData(411.428558, 914.285706, 51.809525)]
    [InlineData(390, 800, 0)]
    public void NewDrinkActualComposition_ArrangesSixEqualDataRowsAndSeventhStarSaveWithoutFooterOverlap(
        double width,
        double rawHeight,
        double topInset)
    {
        var metrics = new CometWindowMetrics();
        metrics.UpdateSafeArea(new Thickness(0, topInset, 0, 34));
        var probe = new NewDrinkFrameProbe();
        probe.WindowMetrics(metrics);
        Materialize(probe);

        CometBackendLayoutEngine.Layout(probe, new Size(width, rawHeight));

        var page = FindByAutomationId(probe, "recovery_new_drink_page");
        var contentGrid = FindByAutomationId(probe, "recovery_content_grid");
        var save = FindByAutomationId(probe, "recovery_save");
        var footer = FindByAutomationId(probe, "bottom_action_row");
        var shell = FindByAutomationId(probe, "recovery_shell");
        var rows = Enumerable.Range(0, BaristaEdgeFrame.NewDrinkDataRowCount)
            .Select(index => FindByAutomationId(probe, $"recovery_row_{index}"))
            .ToArray();

        Assert.All(rows, row => Assert.Equal(rows[0].Frame.Height, row.Frame.Height, 3));
        Assert.Equal(rows[0].Frame.Height, save.Frame.Height, 3);
        Assert.True(rows[0].Frame.Height > 0);
        Assert.True(contentGrid.Frame.Y >= topInset);
        Assert.Equal(0, page.GetPadding().Top);
        Assert.Equal(topInset, contentGrid.Frame.Y, 3);
        Assert.Equal(0, contentGrid.GetPadding().Top);
        Assert.True(footer.Frame.Height > 0);
        Assert.True(page.Frame.Y + contentGrid.Frame.Y + save.Frame.Bottom <= footer.Frame.Y);
        Assert.True(page.Frame.Bottom <= footer.Frame.Y);
        // Yoga rounds the measured Grid root to its pixel grid before arranging children.
        Assert.True(footer.Frame.Bottom <= shell.Frame.Bottom);
    }

    [Fact]
    public void AdaptiveTile_LongValueWithoutUnits_RemainsConstrainedBesideTrailingControl()
    {
        var tile = AdaptiveTwoLineTile.Build(
            "EQUIPMENT",
            "A long equipment name that must truncate before its trailing control",
            "recovery_long_name",
            trailing: new Text(">"),
            singleLineTailTruncation: true);
        var valueLine = Assert.IsType<Grid>(((IContainerView)tile).GetChildren().ElementAt(1));
        var valueText = Assert.IsAssignableFrom<View>(((IContainerView)valueLine).GetChildren().First());
        CometBackendBridge.Materialize(
            tile,
            view => new FakeBackendNode(view.GetType().Name)
            {
                MeasureFunc = (width, _) => new Size(
                    Math.Min(width, ReferenceEquals(view, valueText) ? 1000 : 20), 20),
            },
            Context);

        CometBackendLayoutEngine.Layout(tile, new Size(200, 120));

        Assert.True(valueText.Frame.Width > 0);
        Assert.True(valueText.Frame.Right <= valueLine.Frame.Width);
        Assert.True(valueLine.Frame.Right < tile.Frame.Width);
    }

    [Fact]
    public void R1SourceStructure_HasNoFixedHeaderFooterOrFirstRowCompensation()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var components = Read(root!, "sample/Shared/BaristaNotes/Components/SourceComponents.cs");
        var shot = Read(root!, "sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var settings = Read(root!, "sample/Shared/BaristaNotes/Pages/SettingsPage.cs");
        var app = Read(root!, "sample/Shared/BaristaNotes/BaristaNotesApp.cs");

        Assert.Contains("rows: CreateRows()", components);
        Assert.Contains(".MinimumHeight(CoffeeSpacing.ActionRowHeight)", components);
        Assert.DoesNotContain(".Frame(height: CoffeeSpacing.ActionRowHeight)", components);
        Assert.Contains("BaristaEdgeFrame.CreateEqualDataRows", shot);
        Assert.Contains("BaristaEdgeFrame.NewDrinkContentRowCount", shot);
        Assert.Contains("minimumHeight: 0", shot);
        Assert.DoesNotContain("topInset: topInset", shot);
        Assert.Contains("new object[] { safeArea.Top, \"*\" }", shot);
        Assert.Contains("BaristaEdgeFrame.BuildTopInset(showDivider: true)", shot);
        Assert.Contains(".Padding(new Thickness(safeArea.Left, 0, safeArea.Right, 0))", shot);
        Assert.DoesNotContain(".Frame(height: BaristaSafeAreaLayout", shot);
        Assert.Contains("new Text(_saving.Value ? \"Saving…\"", shot);
        Assert.Contains(".FontSize(28)", shot);
        Assert.DoesNotContain(".MinimumHeight(", SaveTileBody(shot));
        Assert.DoesNotContain(".Frame(height:", SaveTileBody(shot));
        Assert.Contains("new object[] { \"Auto\", \"Auto\" }", components);
        Assert.Contains("BaristaEdgeFrame.Build(", settings);
        Assert.DoesNotContain("new FixedBottomActionRow(", SettingsRootBody(app));
    }

    [Fact]
    public void FooterFamilies_PreserveSourceGlyphsAndDarkAddVariant()
    {
        Assert.Equal("\uefef", CoffeeIcons.Coffee);
        Assert.Equal("\uf009", CoffeeIcons.Feed);
        Assert.Equal("\ue3b0", CoffeeIcons.Camera);

        var root = FindCometRoot();
        Assert.NotNull(root);
        var fixedActions = Read(root!, "sample/Shared/BaristaNotes/Components/SourceComponents.cs");
        var activity = Read(root!, "sample/Shared/BaristaNotes/Components/ActivityBottomActionRow.cs");
        var equipment = Read(root!, "sample/Shared/BaristaNotes/Pages/EquipmentManagementPage.cs");
        var beans = Read(root!, "sample/Shared/BaristaNotes/Pages/BeanManagementPage.cs");
        var profiles = Read(root!, "sample/Shared/BaristaNotes/Components/ProfileComponents.cs");
        var oneAction = Read(root!, "sample/Shared/BaristaNotes/Pages/ValueRangeSettingsPage.cs");
        var twoAction = Read(root!, "sample/Shared/BaristaNotes/Pages/ValueRangeEditorPage.cs");
        var threeAction = Read(root!, "sample/Shared/BaristaNotes/Pages/EquipmentDetailPage.cs");

        Assert.Contains("new Image(CoffeeIcons.Source", fixedActions);
        Assert.Contains("new Image(CoffeeIcons.Source", activity);
        Assert.Contains("new Image(CoffeeIcons.Source", equipment);
        Assert.Contains("EquipmentManagementPage : BaristaManagementPage", equipment);
        Assert.Contains("IsVisible = false", fixedActions);
        Assert.Contains("Inverted: true", beans);
        Assert.Contains("action.inverted", profiles);
        Assert.Contains("new Button(\"BACK\"", oneAction);
        Assert.Contains("\"RangeEditorCancel\"", twoAction);
        Assert.Contains("\"RangeEditorSave\"", twoAction);
        Assert.Contains("\"equipment_archive\"", threeAction);
        Assert.Contains("BaristaEdgeFrame.Build(", oneAction);
        Assert.Contains("rows: new object[] { \"Auto\" }", twoAction);
        Assert.Contains("rows: new object[] { \"Auto\" }", threeAction);
    }

    static string SettingsRootBody(string app)
    {
        const string start = "sealed class SettingsSectionRoot";
        const string end = "sealed class SettingsPageNavigationHost";
        var startIndex = app.IndexOf(start, StringComparison.Ordinal);
        var endIndex = app.IndexOf(end, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return app[startIndex..endIndex];
    }

    static string SaveTileBody(string shot)
    {
        const string start = "View SaveTile()";
        const string end = "View PickerSurface()";
        var startIndex = shot.IndexOf(start, StringComparison.Ordinal);
        var endIndex = shot.IndexOf(end, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return shot[startIndex..endIndex];
    }

    static string Read(string root, string relativePath)
    {
        var path = IOPath.Combine(root, relativePath);
        Assert.True(File.Exists(path), $"Required source file was not found: {path}");
        return File.ReadAllText(path);
    }

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

    static FakeBackendNode Materialize(View root) =>
        (FakeBackendNode)CometBackendBridge.Materialize(
            root,
            view => view is ScrollView
                ? new FakeNativeScrollNode()
                : new FakeBackendNode(view.GetType().Name),
            Context);

    static View FindByAutomationId(View view, string automationId)
    {
        if (FindByAutomationIdOrNull(view, automationId) is { } result)
            return result;

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
            if (FindByAutomationIdOrNull(child as View, automationId) is { } result)
                return result;
        }

        return null;
    }

    sealed class EdgeFrameProbe : View
    {
        [Body]
        View body()
        {
            var safeArea = BaristaSafeAreaLayout.GetInsets(this);
            return BaristaEdgeFrame.Build(
                new Grid
                {
                    new Text("HEADER"),
                }
                .Padding(BaristaSafeAreaLayout.HeaderPadding(safeArea))
                .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight())
                .AutomationId("recovery_header"),
                new ScrollView
                {
                    new Grid().MinimumHeight(1000),
                }.AutomationId("recovery_body"),
                new Button("BACK", () => { })
                    .Padding(BaristaSafeAreaLayout.ActionPadding(safeArea))
                    .MinimumHeight(CoffeeSpacing.ActionRowHeight)
                    .AutomationId("recovery_footer"),
                "recovery_edge_frame");
        }
    }

    sealed class NewDrinkFrameProbe : View
    {
        [Body]
        View body()
        {
            var safeArea = BaristaSafeAreaLayout.GetInsets(this);
            var contentGrid = new Grid(
                columns: new object[] { "*" },
                rows: BaristaEdgeFrame.CreateEqualDataRows(
                    BaristaEdgeFrame.NewDrinkContentRowCount),
                rowSpacing: CoffeeSpacing.Divider)
                .Padding(new Thickness(CoffeeSpacing.Divider, 0, CoffeeSpacing.Divider, CoffeeSpacing.Divider))
                .AutomationId("recovery_content_grid");

            for (var index = 0; index < BaristaEdgeFrame.NewDrinkDataRowCount; index++)
            {
                contentGrid.Add(new Grid
                {
                    new Text($"ROW {index}"),
                }
                .MinimumHeight(0)
                .AutomationId($"recovery_row_{index}")
                .Cell(row: index));
            }

            contentGrid.Add(new Grid
            {
                new Text("SAVE"),
                new Text("Log Drink"),
            }
            .Padding(new Thickness(CoffeeSpacing.M, 14))
            .AutomationId("recovery_save")
            .Cell(row: BaristaEdgeFrame.NewDrinkDataRowCount));

            var page = new Grid(
                columns: new object[] { "*" },
                rows: new object[] { safeArea.Top, "*" })
            {
                BaristaEdgeFrame.BuildTopInset(showDivider: true).Cell(row: 0),
                contentGrid.Cell(row: 1),
            }
            .Padding(new Thickness(safeArea.Left, 0, safeArea.Right, 0))
            .IgnoreSafeArea()
            .AutomationId("recovery_new_drink_page");

            return new Grid(
                columns: new object[] { "*" },
                rows: new object[] { "*", "Auto" },
                rowSpacing: CoffeeSpacing.Divider)
            {
                page.Cell(row: 0),
                new FixedBottomActionRow(
                    new BottomAction("recovery_action", CoffeeIcons.Feed, () => { }))
                    .Cell(row: 1),
            }
            .IgnoreSafeArea()
            .AutomationId("recovery_shell");
        }
    }

    sealed class HeaderPolicyProbe(double reservedTopInset) : View
    {
        [Body]
        View body()
        {
            var safeArea = BaristaSafeAreaLayout.GetInsets(this);
            return BaristaEdgeFrame.Build(
                new Grid(rows: new object[] { "Auto", "*" })
                {
                    new Text("VALUE RANGES").RangeCaption().Cell(row: 0),
                    new Text("Dose In Ranges").Headline().Cell(row: 1),
                }
                .Padding(BaristaSafeAreaLayout.HeaderPadding(safeArea))
                .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight(reservedTopInset))
                .AutomationId("recovery_policy_header"),
                new Grid(),
                new Grid(),
                "recovery_policy_frame");
        }
    }

    sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    sealed class FakeNativeScrollNode : FakeBackendNode, IBackendManagesOwnContent
    {
        public FakeNativeScrollNode() : base("native-scroll")
        {
        }
    }
}
