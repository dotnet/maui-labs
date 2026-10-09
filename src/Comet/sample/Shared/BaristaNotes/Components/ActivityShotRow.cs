#nullable enable
using System;
using Comet;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Components;

public sealed class ActivityShotRow : View
{
    readonly ShotRecordDto _shot;
    readonly Action _edit;

    public ActivityShotRow(ShotRecordDto shot, Action edit)
    {
        _shot = shot;
        _edit = edit;
    }

    [Body]
    View body()
    {
        var output = _shot.ActualOutput ?? _shot.ExpectedOutput;
        var time = _shot.ActualTime ?? _shot.ExpectedTime;
        var bean = _shot.Bean?.Name ?? _shot.Bag?.BeanName ?? "—";
        var rating = RatingText(_shot.Rating);

        var row = new Grid(
            rows: new object[] { "Auto", "Auto" },
            columns: new object[] { "*", "Auto" })
        {
            new Text(_shot.BrewMethod.DisplayName())
                .FontFamily("ManropeSemibold")
                .FontSize(22)
                .Color(CoffeeTheme.TextPrimary)
                .MaxLines(1)
                .AutomationId($"activity_shot_{_shot.Id}_method")
                .Cell(row: 0, column: 0),
            new Text($"{_shot.DoseIn:0.#}g → {output:0.#}g")
                .FontFamily("ManropeSemibold")
                .FontSize(18)
                .Color(CoffeeTheme.TextPrimary)
                .AutomationId($"activity_shot_{_shot.Id}_ratio")
                .Cell(row: 0, column: 1),
            new Text($"{bean}  ·  {FormatTimestamp(_shot.Timestamp)}")
                .FontFamily("Manrope")
                .FontSize(13)
                .Color(CoffeeTheme.TextSecondary)
                .MaxLines(1)
                .Margin(top: CoffeeSpacing.XS)
                .AutomationId($"activity_shot_{_shot.Id}_details")
                .Cell(row: 1, column: 0),
            new Text($"{time:0}s   {rating}".Trim())
                .FontFamily("Manrope")
                .FontSize(13)
                .Color(CoffeeTheme.TextSecondary)
                .Margin(top: CoffeeSpacing.XS)
                .AutomationId($"activity_shot_{_shot.Id}_result")
                .Cell(row: 1, column: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.SurfaceColor);

        return new VStack(spacing: 0)
        {
            row,
            new HStack()
                .Frame(height: CoffeeSpacing.Divider)
                .Background(CoffeeTheme.OutlineColor),
        }
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId($"activity_shot_{_shot.Id}_edit")
        .AutomationName($"Edit {_shot.BrewMethod.DisplayName()} shot")
        .OnTap(_ =>
        {
            _edit();
        });
    }

    static string RatingText(int? rating)
    {
        if (!rating.HasValue)
            return string.Empty;
        var value = Math.Clamp(rating.Value, 0, 4);
        return new string('★', value + 1) + new string('☆', 4 - value);
    }

    static string FormatTimestamp(DateTime timestamp)
    {
        var local = timestamp.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today)
            return $"Today {local:h:mm tt}";
        if (local.Date == today.AddDays(-1))
            return $"Yesterday {local:h:mm tt}";
        if (local.Date > today.AddDays(-7))
            return local.ToString("ddd h:mm tt");
        return local.ToString("MMM d, yyyy");
    }
}
