#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

public sealed class ActivityFeedPage : View
{
    const int PageSize = 50;

    readonly BaristaServices _services;
    readonly Action<ShotRecordDto>? _openShot;
    readonly Action? _showFilters;
    readonly Signal<bool> _isLoading = new(true);
    readonly Signal<bool> _isRefreshing = new(false);
    readonly Signal<bool> _isLoadingMore = new(false);
    readonly Signal<string?> _error = new(null);
    readonly Signal<string?> _loadMoreError = new(null);
    readonly Signal<int> _totalShotCount = new(0);
    readonly Signal<int> _filteredShotCount = new(0);
    readonly Signal<int> _filterVersion = new(0);
    readonly Signal<int> _rowsVersion = new(0);

    List<ShotRecordDto> _shots = new();
    List<BeanFilterOptionDto> _availableBeans = new();
    List<UserProfileDto> _availablePeople = new();
    ActivityShotFilters _activeFilters = new();
    ActivityShotFilters _draftFilters = new();
    CollectionView<object>? _shotList;
    int _pageIndex;
    bool _hasMore;
    bool _subscribed;
    int _requestVersion;

    public ActivityFeedPage(
        BaristaServices services,
        Action<ShotRecordDto>? openShot = null,
        Action? showFilters = null)
    {
        _services = services;
        _openShot = openShot;
        _showFilters = showFilters;
        _services.DataChangeNotifier.DataChanged += OnDataChanged;
        _subscribed = true;
        _ = ReloadAsync();
    }

    public bool HasActiveFilters => _activeFilters.HasFilters;

    public View BottomActions(Action newDrink, Action settings, Action voice)
    {
        _ = _filterVersion.Value;
        return new ActivityBottomActionRow(
            newDrink,
            settings,
            OpenFilters,
            voice,
            HasActiveFilters);
    }

    public async void OpenFilters()
    {
        try
        {
            var beansTask = _services.ShotService.GetBeansWithShotsAsync();
            var peopleTask = _services.ShotService.GetPeopleWithShotsAsync();
            await Task.WhenAll(beansTask, peopleTask);
            _availableBeans = await beansTask;
            _availablePeople = await peopleTask;
            _draftFilters = _activeFilters.Clone();
            _filterVersion.Value++;
            _showFilters?.Invoke();
        }
        catch (Exception ex)
        {
            _error.Value = ex.Message;
        }
    }

    public View FilterContent(Action dismiss) => new ActivityFilterView(
        _filterVersion,
        () => _availableBeans,
        () => _availablePeople,
        () => _draftFilters,
        ToggleBean,
        TogglePerson,
        ToggleRating,
        () => ApplyFilters(dismiss),
        () => ClearFilters(dismiss));

    public void CancelFilters()
    {
        _draftFilters = _activeFilters.Clone();
        _filterVersion.Value++;
    }

    /// <summary>
    /// Apply a voice/programmatic period and optional structured filter token.
    /// Returns false if the filter string was non-empty but invalid.
    /// </summary>
    public bool ApplyProgrammaticFilter(string? period, string? filter)
    {
        _activeFilters.ApplyPeriod(period);

        var filterValid = true;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var token = PeriodBounds.ParseFilterToken(filter);
            if (token is null)
            {
                filterValid = false;
            }
            else
            {
                switch (token.Kind)
                {
                    case FilterKind.Rating:
                        if (!_activeFilters.Ratings.Contains(token.Value))
                            _activeFilters.Ratings.Add(token.Value);
                        break;
                    case FilterKind.Bean:
                        if (!_activeFilters.BeanIds.Contains(token.Value))
                            _activeFilters.BeanIds.Add(token.Value);
                        break;
                    case FilterKind.MadeFor:
                        if (!_activeFilters.MadeForIds.Contains(token.Value))
                            _activeFilters.MadeForIds.Add(token.Value);
                        break;
                }
            }
        }

        _draftFilters = _activeFilters.Clone();
        _filterVersion.Value++;
        _ = ReloadAsync();
        return filterValid;
    }

    [Body]
    View body()
    {
        _ = _filterVersion.Value;
        _ = _rowsVersion.Value;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        return new Grid(
            columns: new object[] { "*" },
            rows: new object[] { "Auto", "*" },
            rowSpacing: CoffeeSpacing.Divider)
        {
            Header(safeArea).Cell(row: 0),
            RefreshableBody().Cell(row: 1),
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId("activity_page");
    }

    View Header(Thickness safeArea)
    {
        var countText = HasActiveFilters
            ? $"{_filteredShotCount.Value} of {_totalShotCount.Value} shots"
            : _totalShotCount.Value == 1
                ? "1 shot"
                : $"{_totalShotCount.Value} shots";

        return BaristaPageHeader.Build(
            "ACTIVITY", countText, safeArea, "activity_header",
            titleAutomationId: "activity_count",
            labelAutomationId: "activity_heading");
    }

    View RefreshableBody()
    {
        var refresh = new RefreshView(_isRefreshing)
            .OnRefresh(() => ReloadAsync(isRefresh: true))
            .AutomationId("activity_refresh");
        refresh.Add(RenderBody());
        return refresh;
    }

    View RenderBody()
    {
        if (_isLoading.Value && _shots.Count == 0)
        {
            return new VStack(spacing: CoffeeSpacing.S)
            {
                new ActivityIndicator(true),
                new Text("Loading…").SecondaryText(),
            }
            .Center()
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("activity_loading");
        }

        if (_error.Value is { } error)
        {
            return new VStack(spacing: CoffeeSpacing.M)
            {
                new Text("ERROR").SectionLabel(),
                new Text(error).SubHeadline(),
                new Button("Retry", () => _ = ReloadAsync(isRefresh: true))
                    .Background(CoffeeTheme.PrimaryColor)
                    .Color(CoffeeTheme.OnPrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("activity_retry"),
            }
            .Padding(new Thickness(CoffeeSpacing.L))
            .Center()
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("activity_error");
        }

        if (_shots.Count == 0)
        {
            var empty = new VStack(spacing: CoffeeSpacing.M)
            {
                new Text(HasActiveFilters ? "NO MATCHES" : "NO SHOTS YET")
                    .SectionLabel()
                    .Color(CoffeeTheme.TextSecondary),
                new Text(HasActiveFilters
                        ? "Adjust or clear filters to see results."
                        : "Log a drink to see it here.")
                    .SubHeadline(),
            };
            if (HasActiveFilters)
            {
                empty.Add(new Button("Clear Filters", () => ClearFilters())
                    .Background(CoffeeTheme.PrimaryColor)
                    .Color(CoffeeTheme.OnPrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("activity_empty_clear_filters"));
            }
            return empty
                .Padding(new Thickness(CoffeeSpacing.L))
                .Center()
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("activity_empty");
        }

        var list = new CollectionView<object>(BuildRows)
        {
            ViewFor = RenderRow,
            ItemsLayout = ItemsLayout.Vertical(),
            RemainingItemsThreshold = 5,
            RemainingItemsThresholdReached = () =>
            {
                if (_hasMore && !_isLoadingMore.Value)
                    _ = LoadMoreAsync();
            },
        };
        _shotList = list;
        return list
            .Background(CoffeeTheme.SurfaceColor)
            .AutomationId("activity_shot_list");
    }

    IReadOnlyList<object> BuildRows()
    {
        var rows = _shots.Cast<object>().ToList();
        if (_hasMore)
            rows.Add(ActivityLoadMoreRow.Instance);
        return rows;
    }

    View RenderRow(object item) => item switch
    {
        ShotRecordDto shot => new ActivityShotRow(shot, () => _openShot?.Invoke(shot)),
        _ => new View { Body = LoadMoreRow },
    };

    View LoadMoreRow()
    {
        if (_isLoadingMore.Value)
        {
            return new VStack(spacing: CoffeeSpacing.S)
            {
                new ActivityIndicator(true),
                new Text("Loading more…").SecondaryText(),
            }
            .Padding(new Thickness(CoffeeSpacing.M))
            .Center()
            .AutomationId("activity_loading_more");
        }

        if (_loadMoreError.Value is { } error)
        {
            return new VStack(spacing: CoffeeSpacing.S)
            {
                new Text(error).SecondaryText(),
                new Button("Retry", () => _ = LoadMoreAsync())
                    .TextButton()
                    .Color(CoffeeTheme.PrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("activity_load_more_retry"),
            }
            .Padding(new Thickness(CoffeeSpacing.M))
            .Center()
            .AutomationId("activity_load_more_error");
        }

        return new Button("LOAD MORE", () => _ = LoadMoreAsync())
            .TextButton()
            .Color(CoffeeTheme.PrimaryColor)
            .CornerRadius(0)
            .Padding(new Thickness(CoffeeSpacing.M))
            .AutomationId("activity_load_more");
    }

    async Task ReloadAsync(bool isRefresh = false)
    {
        var requestVersion = ++_requestVersion;
        try
        {
            if (isRefresh)
                _isRefreshing.Value = true;
            else
                _isLoading.Value = true;

            _isLoadingMore.Value = false;
            _error.Value = null;
            _loadMoreError.Value = null;
            var filters = _activeFilters.ToDto();
            var pageTask = filters.HasFilters
                ? _services.ShotService.GetFilteredShotHistoryAsync(filters, 0, PageSize)
                : _services.ShotService.GetShotHistoryAsync(0, PageSize);
            var totalTask = _services.ShotService.GetShotHistoryAsync(0, 1);
            await Task.WhenAll(pageTask, totalTask);
            if (requestVersion != _requestVersion)
                return;

            var page = await pageTask;
            var total = await totalTask;
            _shots = page.Items.ToList();
            _pageIndex = 0;
            _hasMore = page.HasNextPage;
            _filteredShotCount.Value = page.TotalCount;
            _totalShotCount.Value = total.TotalCount;
            _rowsVersion.Value++;
            _shotList?.ReloadData();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
                _error.Value = ex.Message;
        }
        finally
        {
            if (requestVersion == _requestVersion)
            {
                _isLoading.Value = false;
                _isRefreshing.Value = false;
            }
        }
    }

    async Task LoadMoreAsync()
    {
        if (!_hasMore || _isLoadingMore.Value)
            return;

        var requestVersion = _requestVersion;
        try
        {
            _isLoadingMore.Value = true;
            _loadMoreError.Value = null;
            var nextPage = _pageIndex + 1;
            var filters = _activeFilters.ToDto();
            var page = filters.HasFilters
                ? await _services.ShotService.GetFilteredShotHistoryAsync(filters, nextPage, PageSize)
                : await _services.ShotService.GetShotHistoryAsync(nextPage, PageSize);
            if (requestVersion != _requestVersion)
                return;

            _shots = _shots.Concat(page.Items).ToList();
            _pageIndex = nextPage;
            _hasMore = page.HasNextPage;
            _filteredShotCount.Value = page.TotalCount;
            _shotList?.RefreshItems();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
                _loadMoreError.Value = ex.Message;
        }
        finally
        {
            if (requestVersion == _requestVersion)
            {
                _isLoadingMore.Value = false;
                _shotList?.RefreshItems();
            }
        }
    }

    void OnDataChanged(object? sender, DataChangedEventArgs args)
    {
        if (args.ChangeType is not (
            DataChangeType.ShotCreated or
            DataChangeType.ShotUpdated or
            DataChangeType.ShotDeleted))
        {
            return;
        }

        ThreadHelper.RunOnMainThread(() => _ = ReloadAsync(isRefresh: true));
    }

    void ToggleBean(int id) => Toggle(_draftFilters.BeanIds, id);

    void TogglePerson(int id) => Toggle(_draftFilters.MadeForIds, id);

    void ToggleRating(int id) => Toggle(_draftFilters.Ratings, id);

    void Toggle(List<int> values, int id)
    {
        if (!values.Remove(id))
            values.Add(id);
        _filterVersion.Value++;
    }

    void ApplyFilters(Action dismiss)
    {
        _activeFilters = _draftFilters.Clone();
        _filterVersion.Value++;
        dismiss();
        _ = ReloadAsync(isRefresh: true);
    }

    void ClearFilters(Action? dismiss = null)
    {
        _activeFilters = new ActivityShotFilters();
        _draftFilters = new ActivityShotFilters();
        _filterVersion.Value++;
        dismiss?.Invoke();
        _ = ReloadAsync(isRefresh: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _subscribed)
        {
            _services.DataChangeNotifier.DataChanged -= OnDataChanged;
            _subscribed = false;
        }
        base.Dispose(disposing);
    }

    sealed class ActivityLoadMoreRow
    {
        public static ActivityLoadMoreRow Instance { get; } = new();
    }
}
