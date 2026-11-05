#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Layout.Yoga;
using Comet.Reactive;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Components;

public sealed class ActivityShotFilters
{
    public List<int> BeanIds { get; init; } = new();
    public List<int> MadeForIds { get; init; } = new();
    public List<int> Ratings { get; init; } = new();
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public bool HasFilters => BeanIds.Count > 0 || MadeForIds.Count > 0 || Ratings.Count > 0 || PeriodStart.HasValue;
    public int Count => BeanIds.Count + MadeForIds.Count + Ratings.Count + (PeriodStart.HasValue ? 1 : 0);

    public ActivityShotFilters Clone() => new()
    {
        BeanIds = BeanIds.ToList(),
        MadeForIds = MadeForIds.ToList(),
        Ratings = Ratings.ToList(),
        PeriodStart = PeriodStart,
        PeriodEnd = PeriodEnd,
    };

    public ShotFilterCriteriaDto ToDto() => new()
    {
        BeanIds = BeanIds.Count == 0 ? null : BeanIds,
        MadeForIds = MadeForIds.Count == 0 ? null : MadeForIds,
        Ratings = Ratings.Count == 0 ? null : Ratings,
        PeriodStart = PeriodStart,
        PeriodEnd = PeriodEnd,
    };

    /// <summary>
    /// Apply a period string to set start (inclusive) and end (exclusive) bounds.
    /// Shared with <see cref="PeriodBounds"/> for test-reachable logic.
    /// </summary>
    public void ApplyPeriod(string? period)
    {
        var (start, end) = PeriodBounds.Resolve(period);
        PeriodStart = start;
        PeriodEnd = end;
    }
}

public sealed class ActivityFilterView : View
{
    readonly Signal<int> _version;
    readonly Func<IReadOnlyList<BeanFilterOptionDto>> _beans;
    readonly Func<IReadOnlyList<UserProfileDto>> _people;
    readonly Func<ActivityShotFilters> _filters;
    readonly Action<int> _toggleBean;
    readonly Action<int> _togglePerson;
    readonly Action<int> _toggleRating;
    readonly Action _apply;
    readonly Action _clear;

    public ActivityFilterView(
        Signal<int> version,
        Func<IReadOnlyList<BeanFilterOptionDto>> beans,
        Func<IReadOnlyList<UserProfileDto>> people,
        Func<ActivityShotFilters> filters,
        Action<int> toggleBean,
        Action<int> togglePerson,
        Action<int> toggleRating,
        Action apply,
        Action clear)
    {
        _version = version;
        _beans = beans;
        _people = people;
        _filters = filters;
        _toggleBean = toggleBean;
        _togglePerson = togglePerson;
        _toggleRating = toggleRating;
        _apply = apply;
        _clear = clear;
    }

    [Body]
    View body()
    {
        _ = _version.Value;
        var filters = _filters();
        var content = new VStack(spacing: CoffeeSpacing.M)
        {
            FilterSection(
                "Beans",
                _beans().Select(bean => new FilterOption(bean.Id, bean.Name)),
                filters.BeanIds,
                _toggleBean,
                "No beans with shots",
                "bean"),
            FilterSection(
                "Made For",
                _people().Select(person => new FilterOption(person.Id, person.Name)),
                filters.MadeForIds,
                _togglePerson,
                "No people with shots",
                "person"),
            RatingSection(filters.Ratings),
            new Button("Clear All", _clear)
                .TextButton()
                .Color(filters.HasFilters ? CoffeeTheme.Dark.Primary : CoffeeTheme.Dark.TextMuted)
                .IsEnabled(filters.HasFilters)
                .CornerRadius(0)
                .AutomationId("activity_filter_clear"),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 0, CoffeeSpacing.M, CoffeeSpacing.M));

        var scroll = new ScrollView { content }
            .Background(CoffeeTheme.Dark.Surface)
            .AutomationId("activity_filter_content");

        var action = new Grid(
            columns: new object[] { "*" },
            rows: new object[] { CoffeeSpacing.Divider, "*" })
        {
            new HStack().Background(CoffeeTheme.Dark.Outline).Cell(row: 0),
            new Button("Apply", _apply)
                .Background(CoffeeTheme.Dark.Primary)
                .Color(CoffeeTheme.Dark.OnPrimary)
                .CornerRadius(0)
                .AutomationId("activity_filter_apply")
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 0, CoffeeSpacing.M, CoffeeSpacing.S))
        .Background(CoffeeTheme.Dark.Surface)
        .AutomationId("activity_filter_action");

        return new Grid(
            columns: new object[] { "*" },
            rows: new object[] { "*", 72 })
        {
            scroll.Cell(row: 0),
            action.Cell(row: 1),
        }
        .Frame(height: 520)
        .Background(CoffeeTheme.Dark.Surface)
        .AutomationId("activity_filter");
    }

    static View FilterSection(
        string title,
        IEnumerable<FilterOption> options,
        IReadOnlyCollection<int> selected,
        Action<int> toggle,
        string emptyText,
        string idPrefix)
    {
        var items = options.ToList();
        var stack = new VStack(spacing: CoffeeSpacing.S)
        {
            new Text(title).FontFamily("ManropeSemibold").FontSize(14)
                .Color(CoffeeTheme.Dark.TextPrimary),
        };
        if (items.Count == 0)
        {
            stack.Add(new Text(emptyText).SecondaryText()
                .Color(CoffeeTheme.Dark.TextSecondary));
            return stack.AutomationId($"activity_filter_{idPrefix}_section");
        }

        var chips = new FlexLayout(
            wrap: FlexWrap.Wrap,
            alignItems: FlexAlign.FlexStart,
            alignContent: FlexAlign.FlexStart,
            gap: CoffeeSpacing.S);
        foreach (var option in items)
        {
            var id = option.Id;
            chips.Add(Chip(
                option.Name,
                selected.Contains(id),
                () => toggle(id),
                $"activity_filter_{idPrefix}_{id}"));
        }
        stack.Add(chips);
        return stack.AutomationId($"activity_filter_{idPrefix}_section");
    }

    View RatingSection(IReadOnlyCollection<int> selected)
    {
        var chips = new FlexLayout(
            wrap: FlexWrap.Wrap,
            alignItems: FlexAlign.FlexStart,
            alignContent: FlexAlign.FlexStart,
            gap: CoffeeSpacing.S);
        for (var rating = 0; rating <= 4; rating++)
        {
            var value = rating;
            chips.Add(Chip(
                CoffeeIcons.RatingIcon(value),
                selected.Contains(value),
                () => _toggleRating(value),
                $"activity_filter_rating_{value}",
                isIcon: true));
        }
        return new VStack(spacing: CoffeeSpacing.S)
        {
            new Text("Rating").FontFamily("ManropeSemibold").FontSize(14)
                .Color(CoffeeTheme.Dark.TextPrimary),
            chips,
        }.AutomationId("activity_filter_rating_section");
    }

    static View Chip(
        string label,
        bool selected,
        Action toggle,
        string automationId,
        bool isIcon = false)
    {
        return new HStack
        {
            new Text(label)
                .FontFamily(isIcon ? CoffeeIcons.FontFamily : "Manrope")
                .FontSize(isIcon ? 24 : 14)
                .Color(selected ? CoffeeTheme.Dark.OnPrimary : CoffeeTheme.Dark.TextPrimary)
                .Center(),
        }
        .Padding(new Thickness(isIcon ? 16 : 12, 0))
        .Frame(height: 40)
        .MinimumWidth(isIcon ? 56 : 60)
        .Background(selected ? CoffeeTheme.Dark.Primary : CoffeeTheme.Dark.SurfaceVariant)
        .Border(1, selected ? CoffeeTheme.Dark.Primary : CoffeeTheme.Dark.Outline)
        .CornerRadius(20)
        .FlexShrink(0)
        .AutomationId(automationId)
        .AutomationName(isIcon
            ? $"Rating {RatingDisplayView.RatingName(RatingFromId(automationId))}, {(selected ? "selected" : "not selected")}"
            : $"{label}, {(selected ? "selected" : "not selected")}")
        .OnTap(_ => toggle());
    }

    static int RatingFromId(string automationId) =>
        int.TryParse(automationId.AsSpan(automationId.LastIndexOf('_') + 1), out var rating)
            ? rating
            : 2;

    readonly record struct FilterOption(int Id, string Name);
}
