#nullable enable
using System;
using System.Collections.Generic;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Components;

public sealed record BeanBagAction(
    string Label,
    string AutomationId,
    Action Invoke,
    bool Primary = false,
    bool Danger = false);

public sealed class BeanBagHeaderTile : View
{
    readonly string _label;
    readonly Func<string> _title;
    readonly Thickness _safeArea;

    public BeanBagHeaderTile(string label, Func<string> title, Thickness safeArea = default)
    {
        _label = label;
        _title = title;
        _safeArea = safeArea;
    }

    [Body]
    View body()
    {
        var title = _title();
        return BaristaPageHeader.Build(
            _label, title, _safeArea, "bean_bag_header",
            BaristaPageHeader.TitleFontSize(title));
    }
}

public sealed class BeanBagActionRow : View
{
    readonly IReadOnlyList<BeanBagAction> _actions;

    public BeanBagActionRow(params BeanBagAction[] actions) => _actions = actions;

    [Body]
    View body()
    {
        var columns = new object[_actions.Count];
        Array.Fill(columns, "*");
        var row = new Grid(
            columns: columns,
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor);
        var padding = BaristaSafeAreaLayout.ActionPadding(this);

        for (var i = 0; i < _actions.Count; i++)
        {
            var action = _actions[i];
            var background = action.Danger
                ? CoffeeTheme.Error
                : action.Primary ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor;
            var foreground = action.Danger || action.Primary
                ? CoffeeTheme.SurfaceColor
                : CoffeeTheme.TextPrimary;

            row.Add(new Button(action.Label, action.Invoke)
                .FontFamily("ManropeSemibold")
                .FontSize(18)
                .Color(foreground)
                .Padding(padding)
                .MinimumHeight(CoffeeSpacing.ActionRowHeight)
                .Background(background)
                .CornerRadius(0)
                .AutomationId(action.AutomationId)
                .Cell(column: i));
        }

        return row;
    }
}

public sealed class BeanBagDateTile : View
{
    readonly Signal<DateTime?> _date;
    readonly string _automationId;
    readonly Signal<bool> _isOpen = new(false);

    public BeanBagDateTile(Signal<DateTime?> date, string automationId)
    {
        _date = date;
        _automationId = automationId;
    }

    [Body]
    View body() => new Grid
    {
        new VStack(spacing: CoffeeSpacing.S)
        {
            new Text("ROAST DATE").SectionLabel(),
            new Text(() => (_date.Value ?? DateTime.Today).ToString("MMMM d, yyyy"))
                .FontFamily("ManropeSemibold")
                .FontSize(20)
                .Color(CoffeeTheme.TextPrimary),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.SurfaceColor)
        .MinimumHeight(90)
        .AutomationId(_automationId)
        .OnTap(_ => _isOpen.Value = true),
        new DatePicker(_date, maximumDate: DateTime.Today)
        {
            IsOpen = _isOpen,
        }.AutomationId($"{_automationId}_native"),
    };
}

public sealed class BeanBagRatingSummary : View
{
    readonly Func<RatingAggregateDto?> _aggregate;
    readonly string _automationId;

    public BeanBagRatingSummary(Func<RatingAggregateDto?> aggregate, string automationId)
    {
        _aggregate = aggregate;
        _automationId = automationId;
    }

    [Body]
    View body()
    {
        var aggregate = _aggregate();
        if (aggregate is null || !aggregate.HasRatings)
            return new Text("No ratings yet").SecondaryText().AutomationId(_automationId);

        return new VStack(spacing: CoffeeSpacing.S)
        {
            new VStack(spacing: CoffeeSpacing.XS)
            {
                new Text(aggregate.FormattedAverage)
                    .FontFamily("ManropeSemibold")
                    .FontSize(28)
                    .Color(CoffeeTheme.TextPrimary),
                new Text($"{aggregate.TotalShots} shots").SecondaryText(),
            }.Center(),
            DistributionRow(aggregate, 4),
            DistributionRow(aggregate, 3),
            DistributionRow(aggregate, 2),
            DistributionRow(aggregate, 1),
            DistributionRow(aggregate, 0),
        }.AutomationId(_automationId);
    }

    static View DistributionRow(RatingAggregateDto aggregate, int rating)
    {
        var width = aggregate.GetPercentageForRating(rating) * 2;
        return new Grid(
            columns: new object[] { 30, 200, 30 },
            rows: new object[] { 20 },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text(CoffeeIcons.RatingIcon(rating))
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(20)
                .Color(CoffeeTheme.TextSecondary)
                .Center()
                .Cell(column: 0),
            new Grid
            {
                new Grid().Background(CoffeeTheme.OutlineColor),
                new Grid(
                    columns: new object[] { width, "*" },
                    rows: new object[] { "*" })
                {
                    new Grid().Background(CoffeeTheme.PrimaryColor).Cell(column: 0),
                },
            }.Frame(width: 200, height: 20).Cell(column: 1),
            new Text(aggregate.GetCountForRating(rating).ToString())
                .MutedText()
                .Center()
                .Cell(column: 2),
        };
    }
}

public sealed class BeanBagShotRow : View
{
    readonly ShotRecordDto _shot;
    readonly Action<int>? _openShot;

    public BeanBagShotRow(ShotRecordDto shot, Action<int>? openShot)
    {
        _shot = shot;
        _openShot = openShot;
    }

    [Body]
    View body()
    {
        var actualOutput = _shot.ActualOutput ?? _shot.ExpectedOutput;
        var actualTime = _shot.ActualTime ?? _shot.ExpectedTime;
        View row = new VStack(spacing: CoffeeSpacing.XS)
        {
            new Grid(columns: new object[] { "*", "Auto" })
            {
                new HStack(spacing: CoffeeSpacing.XS)
                {
                    new Text(CoffeeIcons.Coffee)
                        .FontFamily(CoffeeIcons.FontFamily)
                        .FontSize(18)
                        .Color(CoffeeTheme.TextPrimary),
                    new Text(string.IsNullOrWhiteSpace(_shot.DrinkType) ? _shot.BrewMethod.DisplayName() : _shot.DrinkType)
                        .FontFamily("ManropeSemibold")
                        .FontSize(18)
                        .Color(CoffeeTheme.TextPrimary),
                    _shot.BrewMethod == BrewMethod.Espresso
                        ? new Grid().Frame(width: 0)
                        : new Text(_shot.BrewMethod.DisplayName())
                            .SectionLabel()
                            .Color(CoffeeTheme.PrimaryColor),
                }.Cell(column: 0),
                new Text(CoffeeIcons.RatingIcon(_shot.Rating ?? 0))
                    .FontFamily(CoffeeIcons.FontFamily)
                    .FontSize(24)
                    .Color(CoffeeTheme.PrimaryColor)
                    .Cell(column: 1),
            },
            new Text(_shot.Bean?.Name ?? "Unknown Bean")
                .FontSize(16)
                .Color(CoffeeTheme.TextPrimary),
            new Text($"{_shot.DoseIn:0.0}g in → {actualOutput:0.0}g out ({actualTime:0.0}s)")
                .SecondaryText(),
            ProfileAndTimestamp(),
        }
        .Padding(new Thickness(12))
        .Background(CoffeeTheme.SurfaceVariant)
        .AutomationId($"shot_{_shot.Id}");

        return _openShot is null ? row : row.OnTap(_ => _openShot(_shot.Id));
    }

    View ProfileAndTimestamp()
    {
        var parts = new List<string>();
        if (_shot.MadeBy is not null)
            parts.Add($"By: {_shot.MadeBy.Name}");
        if (_shot.MadeFor is not null)
            parts.Add($"For: {_shot.MadeFor.Name}");
        return parts.Count == 0
            ? new Grid().Frame(height: 0)
            : new Text($"{FormatTimestamp(_shot.Timestamp)} • {string.Join(" • ", parts)}").MutedText();
    }

    static string FormatTimestamp(DateTime timestamp)
    {
        var difference = DateTime.Now - timestamp;
        if (difference.TotalMinutes < 1) return "Just now";
        if (difference.TotalMinutes < 60) return $"{(int)difference.TotalMinutes}m ago";
        if (difference.TotalHours < 24) return timestamp.ToString("h:mm tt");
        if (difference.TotalDays < 7) return timestamp.ToString("ddd h:mm tt");
        return timestamp.ToString("MMM d, h:mm tt");
    }
}
