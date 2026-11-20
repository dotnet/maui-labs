#nullable enable
using System;
using System.Linq;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Pages;

public class SettingsPage : View
{
    readonly BaristaServices _services;
    readonly IPreferencesService _preferences;
    readonly IDrinkValueRangeService _ranges;
    readonly Action _openNewDrink;
    readonly Action _openActivity;
    readonly Action _openVoice;
    readonly Action _openEquipment;
    readonly Action<int> _openShot;
    readonly Action<string> _openExternal;
    readonly Action _onBeansExit;
    readonly Signal<CoffeeThemeMode> _themeMode;
    readonly Signal<TemperatureUnit> _temperatureUnit;
    readonly Signal<int> _refreshToken = new(0);

    public SettingsPage(
        BaristaServices services,
        Action openNewDrink,
        Action openActivity,
        Action openVoice,
        Action openEquipment,
        Action<int> openShot,
        Action<string> openExternal,
        Action onBeansExit)
    {
        _services = services;
        _openNewDrink = openNewDrink;
        _openActivity = openActivity;
        _openVoice = openVoice;
        _openEquipment = openEquipment;
        _openShot = openShot;
        _openExternal = openExternal;
        _onBeansExit = onBeansExit;
        BaristaServiceLocator.Initialize(
            services.Store,
            services.DataChangeNotifier,
            services.Preferences);
        _preferences = services.Preferences;
        _ranges = services.RangeService;
        _themeMode = new Signal<CoffeeThemeMode>(services.ThemePreferences.Load());
        _temperatureUnit = services.TemperatureUnit;
        _temperatureUnit.Value = _preferences.GetTemperatureUnit();
        CoffeeTheme.SetMode(_themeMode.Value);
    }

    [Body]
    View body()
    {
        _ = _refreshToken.Value;
        _ = CoffeeTheme.IsLight;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return BaristaEdgeFrame.Build(
            HeaderTile(safeArea),
            BodyContent(),
            new FixedBottomActionRow(
                new("nav_new_drink", CoffeeIcons.Coffee, _openNewDrink),
                new("nav_activity", CoffeeIcons.Feed, _openActivity),
                new("nav_voice", CoffeeIcons.Mic, _openVoice)),
            "SettingsPage");
    }

    View HeaderTile(Thickness safeArea) => BaristaPageHeader.Build(
        "SETTINGS",
        _themeMode.Value switch
        {
            CoffeeThemeMode.Light => "Light theme",
            CoffeeThemeMode.Dark => "Dark theme",
            _ => "System theme",
        },
        safeArea,
        "SettingsHeader");

    View BodyContent() => new ScrollView
    {
        new VStack(spacing: CoffeeSpacing.Divider)
        {
            SectionHeader("APPEARANCE", "SettingsAppearance"),
            ThemePickerRow(),
            SectionHeader("MANAGE", "SettingsManage"),
            ManageTile(
                "EQUIPMENT",
                "Machines, grinders, accessories",
                "ManageEquipment",
                _openEquipment),
            ManageTile(
                "BEANS",
                "Coffee beans and roasters",
                "ManageBeans",
                OpenBeans),
            ManageTile(
                "PROFILES",
                "Coffee lovers",
                "ManageProfiles",
                () => NavigationView.Navigate(
                    this,
                    new ProfileManagementPage(
                        _openNewDrink,
                        _openActivity,
                        () => NavigationView.Pop(this)))),
            SectionHeader("UNITS", "SettingsUnits"),
            TemperaturePickerRow(),
            SectionHeader("VALUE RANGES", "SettingsValueRanges"),
            ValueRangeTile(DrinkValueMetric.DoseIn),
            ValueRangeTile(DrinkValueMetric.Yield),
            ValueRangeTile(DrinkValueMetric.GrindMicrons),
            ValueRangeTile(DrinkValueMetric.Time),
            SectionHeader("ABOUT", "SettingsAbout"),
            AboutTile(),
            new Grid().Frame(height: CoffeeSpacing.M).Background(CoffeeTheme.SurfaceColor),
        }
        .Background(CoffeeTheme.OutlineColor)
    }
    .Background(CoffeeTheme.SurfaceColor);

    static View SectionHeader(string text, string automationId) =>
        new Grid
        {
            new Text(text)
                .SectionLabel()
                .Alignment(Comet.Alignment.BottomLeading),
        }
        .Padding(new Thickness(
            BaristaSourceVisualContract.ContentInset,
            BaristaSourceVisualContract.SectionTopPadding,
            BaristaSourceVisualContract.ContentInset,
            BaristaSourceVisualContract.SectionBottomPadding))
        .Background(CoffeeTheme.SurfaceColor)
        .FillHorizontal()
        .AutomationId(automationId);

    void OpenBeans() => NavigationView.Navigate(
        this,
        new BeanManagementPage(
            _services,
            _openNewDrink,
            _openActivity,
            _openShot,
            _openExternal,
            _onBeansExit));

    View ThemePickerRow() => new Grid(
        columns: new object[] { "*", "*", "*" },
        rows: new object[] { 96 },
        columnSpacing: CoffeeSpacing.Divider)
    {
        ThemeOption(CoffeeThemeMode.Light, "LIGHT", "SettingsThemeLight").Cell(column: 0),
        ThemeOption(CoffeeThemeMode.Dark, "DARK", "SettingsThemeDark").Cell(column: 1),
        ThemeOption(CoffeeThemeMode.System, "AUTO", "SettingsThemeSystem").Cell(column: 2),
    }
    .Background(CoffeeTheme.OutlineColor);

    View ThemeOption(CoffeeThemeMode mode, string label, string automationId)
    {
        var selected = _themeMode.Value == mode;
        var background = selected ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor;
        var foreground = selected ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;

        return new Grid(rows: new object[] { "Auto", "*" })
        {
            new Text(label).SectionLabel().Color(foreground.WithAlpha(.7f)).Cell(row: 0),
            new Text(selected ? "●" : "○")
                .FontFamily("ManropeSemibold")
                .FontSize(28)
                .Color(foreground)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(background)
        .AutomationId(automationId)
        .OnTap(_ => SelectTheme(mode));
    }

    void SelectTheme(CoffeeThemeMode mode)
    {
        _themeMode.Value = mode;
        CoffeeTheme.SetMode(mode);
        _services.ThemePreferences.Save(mode);
    }

    View TemperaturePickerRow() => new Grid(
        columns: new object[] { "*", "*" },
        rows: new object[] { 96 },
        columnSpacing: CoffeeSpacing.Divider)
    {
        TemperatureOption(
            TemperatureUnit.Fahrenheit,
            "FAHRENHEIT",
            "°F",
            "SettingsTemperatureFahrenheit").Cell(column: 0),
        TemperatureOption(
            TemperatureUnit.Celsius,
            "CELSIUS",
            "°C",
            "SettingsTemperatureCelsius").Cell(column: 1),
    }
    .Background(CoffeeTheme.OutlineColor);

    View TemperatureOption(
        TemperatureUnit unit,
        string label,
        string glyph,
        string automationId)
    {
        var selected = _temperatureUnit.Value == unit;
        var background = selected ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor;
        var foreground = selected ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;

        return new Grid(rows: new object[] { "Auto", "*" })
        {
            new Text(label).SectionLabel().Color(foreground.WithAlpha(.7f)).Cell(row: 0),
            new Text(glyph)
                .FontFamily("ManropeSemibold")
                .FontSize(28)
                .Color(foreground)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(background)
        .AutomationId(automationId)
        .OnTap(_ =>
        {
            _preferences.SetTemperatureUnit(unit);
            _temperatureUnit.Value = unit;
        });
    }

    static View ManageTile(
        string label,
        string subtitle,
        string automationId,
        Action onTap) =>
        new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "Auto" })
        {
            new Text(label).SectionLabel().Cell(row: 0, column: 0),
            new Text(subtitle)
                .FontFamily("ManropeSemibold")
                .FontSize(20)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1, column: 0),
            Chevron().Cell(row: 0, column: 1, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(80)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId(automationId)
        .OnTap(_ => onTap());

    View ValueRangeTile(DrinkValueMetric metric)
    {
        var mode = _ranges.GetMode(metric);
        var count = _ranges.GetSettings().Overrides.Count(item => item.Metric == metric);
        var subtitle = mode == ValueRangeMode.Auto
            ? "Automatic by drink method"
            : count == 1
                ? "Custom - 1 drink method"
                : $"Custom - {count} drink methods";
        var status = mode == ValueRangeMode.Auto ? "AUTO" : "CUSTOM";

        return new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "Auto" },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text(DrinkValueRangeFormatting.MetricTitle(metric).ToUpperInvariant())
                .SectionLabel()
                .Cell(row: 0, column: 0),
            new Text(subtitle)
                .FontFamily("ManropeSemibold")
                .FontSize(18)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1, column: 0),
            StatusWithChevron(
                status,
                mode == ValueRangeMode.Custom
                    ? CoffeeTheme.PrimaryColor
                    : CoffeeTheme.TextSecondary)
                .Cell(row: 0, column: 1, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(80)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId($"ValueRange_{metric}")
        .OnTap(_ => NavigationView.Navigate(
            this,
            new ValueRangeSettingsPage(metric, this)));
    }

    static View AboutTile() => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("BARISTANOTES").SectionLabel(),
        new Text("Version 1.0")
            .FontFamily("ManropeSemibold")
            .FontSize(18)
            .Color(CoffeeTheme.TextPrimary),
        new Text("Track your espresso journey").SecondaryText(),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14, CoffeeSpacing.M, 18))
    .MinimumHeight(96)
    .Background(CoffeeTheme.SurfaceColor)
    .AutomationId("SettingsAboutTile");

    static View StatusWithChevron(string status, Color statusColor) =>
        new HStack(spacing: CoffeeSpacing.S)
        {
            new Text(status)
                .FontFamily("Manrope")
                .FontSize(10)
                .CharacterSpacing(1)
                .Color(statusColor)
                .Center(),
            Chevron(),
        }
        .Center();

    static View Chevron() => new Text(CoffeeIcons.Chevron)
        .FontFamily(CoffeeIcons.FontFamily)
        .FontSize(24)
        .Color(CoffeeTheme.TextPrimary)
        .Center();

    public void RefreshRanges() => _refreshToken.Value++;
}
