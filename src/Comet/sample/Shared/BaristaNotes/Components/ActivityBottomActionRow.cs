#nullable enable
using System;
using Comet;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Styles;

namespace CometSamples.BaristaNotes.Components;

public sealed class ActivityBottomActionRow : View
{
    readonly Action _newDrink;
    readonly Action _settings;
    readonly Action _filter;
    readonly Action _voice;
    readonly bool _filtersActive;

    public ActivityBottomActionRow(
        Action newDrink,
        Action settings,
        Action filter,
        Action voice,
        bool filtersActive)
    {
        _newDrink = newDrink;
        _settings = settings;
        _filter = filter;
        _voice = voice;
        _filtersActive = filtersActive;
    }

    [Body]
    View body()
    {
        var padding = BaristaSafeAreaLayout.ActionPadding(this, CoffeeSpacing.M);
        return new Grid(
            columns: new object[] { "*", "*", "*", "*" },
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
        {
            ActionTile(CoffeeIcons.Coffee, "nav_new_drink", "New drink", _newDrink, padding).Cell(column: 0),
            ActionTile(CoffeeIcons.Settings, "nav_settings", "Settings", _settings, padding).Cell(column: 1),
            ActionTile(
                CoffeeIcons.Filter,
                "nav_filter",
                _filtersActive ? "Filter activity, filters active" : "Filter activity",
                _filter,
                padding,
                _filtersActive ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextPrimary).Cell(column: 2),
            ActionTile(CoffeeIcons.Mic, "nav_voice", "Voice", _voice, padding).Cell(column: 3),
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId("activity_bottom_actions");
    }

    static View ActionTile(
        string icon,
        string automationId,
        string automationName,
        Action action,
        Thickness padding,
        Microsoft.Maui.Graphics.Color? color = null) =>
        new Grid
        {
            new Image(CoffeeIcons.Source(icon, color ?? CoffeeTheme.TextPrimary, 32))
                .Center(),
        }
        .Padding(padding)
        .MinimumHeight(CoffeeSpacing.ActionRowHeight)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId(automationId)
        .AutomationName(automationName)
        .OnTap(_ => action());
}
