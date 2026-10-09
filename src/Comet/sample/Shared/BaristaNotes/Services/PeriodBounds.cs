#nullable enable
using System;

namespace CometBaristaNotes.Services;

/// <summary>
/// Resolves a voice/UI period string to start (inclusive) and end (exclusive) UTC bounds.
/// Matches <c>BaristaVoiceCommandService.UtcPeriodBounds</c> semantics.
/// </summary>
public static class PeriodBounds
{
    /// <summary>
    /// Returns (start inclusive, end exclusive) for the given period using UTC now.
    /// </summary>
    public static (DateTime? Start, DateTime? End) Resolve(string? period)
        => Resolve(period, DateTime.UtcNow);

    /// <summary>
    /// Returns (start inclusive, end exclusive) for the given period.
    /// Injectable <paramref name="utcNow"/> for deterministic tests.
    /// "yesterday" = [utcToday-1, utcToday).
    /// "today" = [utcToday, utcToday+1).
    /// "this week" = [Sunday 00:00 UTC, next Sunday 00:00 UTC).
    /// "last week" = [prev Sunday 00:00 UTC, this Sunday 00:00 UTC).
    /// "this month" = [1st UTC, 1st next month UTC).
    /// "last month" = [1st prev month UTC, 1st this month UTC).
    /// null / "all" = (null, null).
    /// </summary>
    public static (DateTime? Start, DateTime? End) Resolve(string? period, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(period) ||
            string.Equals(period, "all", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        var today = utcNow.Date;
        var thisWeek = today.AddDays(-(int)today.DayOfWeek);
        var thisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        return period.ToLowerInvariant() switch
        {
            "today" => (today, today.AddDays(1)),
            "yesterday" => (today.AddDays(-1), today),
            "this week" or "week" => (thisWeek, thisWeek.AddDays(7)),
            "last week" => (thisWeek.AddDays(-7), thisWeek),
            "this month" or "month" => (thisMonth, thisMonth.AddMonths(1)),
            "last month" => (thisMonth.AddMonths(-1), thisMonth),
            _ => (null, null),
        };
    }

    /// <summary>Parse a simple voice filter token like "rating:3", "bean:42", "made-for:7".</summary>
    public static FilterToken? ParseFilterToken(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;
        var colon = filter.IndexOf(':');
        if (colon < 1 || colon >= filter.Length - 1)
            return null;
        var key = filter[..colon].Trim().ToLowerInvariant();
        var raw = filter[(colon + 1)..].Trim();
        if (!int.TryParse(raw, out var value))
            return null;
        return key switch
        {
            "rating" when value is >= 0 and <= 4 => new FilterToken(FilterKind.Rating, value),
            "bean" when value > 0 => new FilterToken(FilterKind.Bean, value),
            "made-for" when value > 0 => new FilterToken(FilterKind.MadeFor, value),
            _ => null,
        };
    }
}

public enum FilterKind { Rating, Bean, MadeFor }

public sealed record FilterToken(FilterKind Kind, int Value);
