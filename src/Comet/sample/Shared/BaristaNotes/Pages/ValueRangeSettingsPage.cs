#nullable enable
using System;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;
using CometSamples.BaristaNotes.Components;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Pages;

public class ValueRangeSettingsPage : View
{
    readonly DrinkValueMetric _metric;
    readonly SettingsPage? _settingsPage;
    readonly Signal<ValueRangeMode> _mode = new(ValueRangeMode.Auto);
    readonly Signal<int> _overrideCount = new(0);
    readonly Signal<string?> _loadWarning = new(null);
    readonly Signal<int> _refreshToken = new(0);
    readonly Signal<bool> _resetDialogOpen = new(false);

    IDrinkValueRangeService Svc => BaristaServiceLocator.RangeService;

    public ValueRangeSettingsPage(DrinkValueMetric metric, SettingsPage? settingsPage = null)
    {
        _metric = metric;
        _settingsPage = settingsPage;
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
        });
        Reload();
    }

    public override void ViewDidAppear()
    {
        base.ViewDidAppear();
        Reload();
    }

    new void Reload()
    {
        var snapshot = Svc.GetSettings();
        _mode.Value = snapshot.Modes.TryGetValue(_metric, out var mode)
            ? mode
            : ValueRangeMode.Auto;
        _overrideCount.Value = snapshot.Overrides.Count(item => item.Metric == _metric);
        _loadWarning.Value = snapshot.LoadWarning;
        _settingsPage?.RefreshRanges();
        _refreshToken.Value++;
    }

    void SelectMode(ValueRangeMode mode)
    {
        Svc.SetMode(_metric, mode);
        Reload();
    }

    [Body]
    View body()
    {
        _ = _refreshToken.Value;
        _ = CoffeeTheme.IsLight;
        var title = $"{DrinkValueRangeFormatting.MetricTitle(_metric)} Ranges";
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return new Grid
        {
            BaristaEdgeFrame.Build(
                HeaderTile(title, safeArea),
                BodyContent(),
                BackAction(safeArea),
                "value_range_settings_frame"),
            ResetDialog(),
        }
        .CoffeePageBackground()
        .AutomationId("value_range_settings_page");
    }

    static View HeaderTile(string title, Thickness safeArea) => BaristaPageHeader.Build(
        "VALUE RANGES", title, safeArea, "ValueRangeHeader", rangeCaption: true);

    View BodyContent()
    {
        var content = BaristaSections.Create(
            ModeHelpTile(),
            ModePickerRow());

        if (_loadWarning.Value is not null)
            content.Add(WarningTile(_loadWarning.Value!));

        foreach (var method in BrewMethodExtensions.All)
            content.Add(MethodTile(method));

        if (_overrideCount.Value > 0)
            content.Add(ResetAllAction());
        else
            content.Add(new Grid().Frame(height: CoffeeSpacing.M).Background(CoffeeTheme.SurfaceColor));

        return BaristaSections.Scroll(content);
    }

    View ModeHelpTile() => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("MODE").RangeCaption(),
        new Text(_mode.Value == ValueRangeMode.Auto
                ? "Uses recommended ranges for each drink method."
                : "Edited methods use custom ranges. Other methods stay automatic.")
            .SecondaryText()
            .Color(CoffeeTheme.TextPrimary),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .AutomationId("RangeModeHelp");

    View ModePickerRow() => new Grid(
        columns: new object[] { "*", "*" },
        rows: new object[] { 56 },
        columnSpacing: CoffeeSpacing.Divider)
    {
        ModeTile(ValueRangeMode.Auto, "AUTO", "RangeMode_Auto").Cell(column: 0),
        ModeTile(ValueRangeMode.Custom, "CUSTOM", "RangeMode_Custom").Cell(column: 1),
    }
    .Background(CoffeeTheme.OutlineColor);

    View ModeTile(ValueRangeMode mode, string label, string automationId)
    {
        var selected = _mode.Value == mode;
        return new Button(label, () => SelectMode(mode))
            .FontFamily("ManropeSemibold")
            .FontSize(13)
            .CharacterSpacing(2)
            .Color(selected ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary)
            .Background(selected ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor)
            .CornerRadius(0)
            .AutomationId(automationId);
    }

    View MethodTile(BrewMethod method)
    {
        var effective = Svc.Resolve(_metric, method);
        var source = effective.Source switch
        {
            ValueRangeSource.Custom => "CUSTOM",
            ValueRangeSource.AutoFallback => "AUTO FALLBACK",
            _ => "AUTO",
        };

        View tile = new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "Auto" },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text(method.DisplayName().ToUpperInvariant())
                .RangeCaption()
                .CharacterSpacing(1.5)
                .Cell(row: 0, column: 0),
            new Text(DrinkValueRangeFormatting.FormatRange(_metric, effective.Range))
                .FontFamily("ManropeSemibold")
                .FontSize(18)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1)
                .Cell(row: 1, column: 0),
            StatusWithChevron(
                source,
                effective.Source == ValueRangeSource.Custom
                    ? CoffeeTheme.PrimaryColor
                    : CoffeeTheme.TextSecondary,
                _mode.Value == ValueRangeMode.Custom
                    ? CoffeeTheme.TextPrimary
                    : CoffeeTheme.TextSecondary.WithAlpha(.35f))
                .Cell(row: 0, column: 1, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(80)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId($"RangeMethod_{method}");

        if (_mode.Value == ValueRangeMode.Custom)
        {
            tile = tile.OnTap(_ => NavigationView.Navigate(
                this,
                new ValueRangeEditorPage(_metric, method, this)));
        }

        return tile;
    }

    static View StatusWithChevron(string status, Color statusColor, Color chevronColor) =>
        new HStack(spacing: CoffeeSpacing.S)
        {
            new Text(status)
                .FontFamily("Manrope")
                .FontSize(10)
                .CharacterSpacing(1)
                .Color(statusColor)
                .Center(),
            new Text(CoffeeIcons.Chevron)
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(24)
                .Color(chevronColor)
                .Center(),
        }
        .Center();

    static View WarningTile(string warning) =>
        new Text(warning)
            .FontFamily("Manrope")
            .FontSize(13)
            .Color(CoffeeTheme.SurfaceColor)
            .Padding(new Thickness(CoffeeSpacing.M, 12))
            .MinimumHeight(56)
            .Background(CoffeeTheme.Warning)
            .AutomationId("RangeLoadWarning");

    View ResetAllAction() => new Button(
        "RESET ALL CUSTOM RANGES",
        () => _resetDialogOpen.Value = true)
        .FontFamily("ManropeSemibold")
        .FontSize(13)
        .CharacterSpacing(1)
        .Color(CoffeeTheme.Error)
        .Background(CoffeeTheme.SurfaceColor)
        .CornerRadius(0)
        .MinimumHeight(56)
        .AutomationId("RangeResetAll");

    View BackAction(Thickness safeArea) => new Button("BACK", () => NavigationView.Pop(this))
        .FontFamily("ManropeSemibold")
        .FontSize(18)
        .CharacterSpacing(1)
        .Color(CoffeeTheme.TextPrimary)
        .Background(CoffeeTheme.SurfaceColor)
        .CornerRadius(0)
        .Padding(BaristaSafeAreaLayout.ActionPadding(safeArea))
        .MinimumHeight(CoffeeSpacing.ActionRowHeight)
        .AutomationId("RangeBack");

    View ResetDialog() => new AlertDialog(
        _resetDialogOpen,
        text: new Text(
            $"Remove all custom {DrinkValueRangeFormatting.MetricTitle(_metric).ToLowerInvariant()} ranges?")
            .SecondaryText(),
        title: new Text("Reset custom ranges?").SubHeadline(),
        confirmButton: new Button("RESET", () =>
        {
            Svc.ResetOverrides(_metric);
            _resetDialogOpen.Value = false;
            Reload();
        })
        .TextButton()
        .Color(CoffeeTheme.Error)
        .CornerRadius(0)
        .AutomationId("RangeResetConfirm"),
        dismissButton: new Button("CANCEL", () => _resetDialogOpen.Value = false)
            .TextButton()
            .Color(CoffeeTheme.PrimaryColor)
            .CornerRadius(0)
            .AutomationId("RangeResetCancel"));

    public void RefreshAfterEdit() => Reload();
}
