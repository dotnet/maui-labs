#nullable enable
using System;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

public class BagDetailPage : View
{
    readonly int _beanId;
    readonly string _beanName;
    readonly int? _bagId;
    readonly IBagService _bagService;
    readonly IDataChangeNotifier _notifier;
    readonly IRatingService? _ratingService;
    readonly Action? _onExit;

    readonly Signal<DateTime?> _roastDate = new(DateTime.Today);
    readonly Signal<string> _notes = new(string.Empty);
    readonly Signal<bool> _isComplete = new(false);
    readonly Signal<RatingAggregateDto?> _rating = new(null);
    readonly Signal<int> _shotCount = new(0);
    readonly Signal<bool> _isLoading = new(false);
    readonly Signal<bool> _isSaving = new(false);
    readonly Signal<string?> _error = new(null);
    readonly Signal<bool> _deleteDialogOpen = new(false);
    readonly Signal<bool> _statusDialogOpen = new(false);
    readonly Signal<string> _statusTitle = new(string.Empty);
    readonly Signal<string> _statusMessage = new(string.Empty);

    bool _subscribed;
    bool _exitNotified;

    bool IsEdit => _bagId is > 0;

    public BagDetailPage(int beanId, string beanName, int? bagId)
        : this(
            beanId,
            beanName,
            bagId,
            BaristaServiceLocator.BagService,
            BaristaServiceLocator.DataChangeNotifier,
            null,
            null)
    {
    }

    public BagDetailPage(
        int beanId,
        string beanName,
        int? bagId,
        BaristaServices services,
        Action? onExit = null)
        : this(
            beanId,
            beanName,
            bagId,
            services.BagService,
            services.DataChangeNotifier,
            new InMemoryRatingService(services.Store),
            onExit)
    {
    }

    internal BagDetailPage(
        int beanId,
        string beanName,
        int? bagId,
        IBagService bagService,
        IDataChangeNotifier notifier,
        IRatingService? ratingService,
        Action? onExit)
    {
        _beanId = beanId;
        _beanName = beanName;
        _bagId = bagId;
        _bagService = bagService;
        _notifier = notifier;
        _ratingService = ratingService;
        _onExit = onExit;
        _notifier.DataChanged += OnDataChanged;
        _subscribed = true;
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
        });

        if (IsEdit)
        {
            _isLoading.Value = true;
            _ = LoadAsync();
        }
    }

    void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (IsEdit && e.ChangeType is DataChangeType.ShotCreated
            or DataChangeType.ShotUpdated
            or DataChangeType.ShotDeleted)
        {
            _ = LoadAggregateAsync();
        }
    }

    async Task LoadAsync()
    {
        try
        {
            var bag = await _bagService.GetBagByIdAsync(_bagId!.Value);
            if (bag is null)
            {
                _error.Value = "Bag not found";
                return;
            }

            _roastDate.Value = bag.RoastDate;
            _notes.Value = bag.Notes ?? string.Empty;
            _isComplete.Value = bag.IsComplete;
            await LoadAggregateAsync();
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to load bag: {ex.Message}";
        }
        finally
        {
            _isLoading.Value = false;
        }
    }

    async Task LoadAggregateAsync()
    {
        if (_ratingService is null || !IsEdit)
            return;

        try
        {
            var aggregate = await _ratingService.GetBagRatingAsync(_bagId!.Value);
            _rating.Value = aggregate;
            _shotCount.Value = aggregate.TotalShots;
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to load bag ratings: {ex.Message}";
        }
    }

    bool Validate()
    {
        var date = (_roastDate.Value ?? DateTime.Today).Date;
        var validation = date > DateTime.Today
            ? "Roast date cannot be in the future"
            : _notes.Value.Length > 500
                ? "Notes cannot exceed 500 characters"
                : null;
        _error.Value = validation;
        return validation is null;
    }

    async Task SaveAsync()
    {
        if (!Validate() || _isSaving.Value)
            return;

        try
        {
            _isSaving.Value = true;
            _error.Value = null;
            var bag = new Bag
            {
                Id = _bagId ?? 0,
                BeanId = _beanId,
                RoastDate = (_roastDate.Value ?? DateTime.Today).Date,
                Notes = string.IsNullOrWhiteSpace(_notes.Value) ? null : _notes.Value.Trim(),
                IsComplete = _isComplete.Value,
                IsActive = true,
            };
            var result = IsEdit
                ? await _bagService.UpdateBagAsync(bag)
                : await _bagService.CreateBagAsync(bag);
            if (!result.Success)
            {
                _error.Value = result.ErrorMessage ?? "Failed to save bag";
                return;
            }

            Close();
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to save bag: {ex.Message}";
        }
        finally
        {
            _isSaving.Value = false;
        }
    }

    async Task ToggleStatusAsync()
    {
        if (!IsEdit)
            return;

        try
        {
            if (_isComplete.Value)
            {
                await _bagService.ReactivateBagAsync(_bagId!.Value);
                _isComplete.Value = false;
                ShowStatus("Bag reactivated", "This bag is available for new drinks.");
            }
            else
            {
                await _bagService.MarkBagCompleteAsync(_bagId!.Value);
                _isComplete.Value = true;
                ShowStatus("Bag complete", "This bag is no longer offered for new drinks.");
            }
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to update status: {ex.Message}";
        }
    }

    async Task DeleteAsync()
    {
        if (!IsEdit)
            return;

        try
        {
            await _bagService.DeleteBagAsync(_bagId!.Value);
            _notifier.NotifyDataChanged(DataChangeType.BagUpdated, _bagId.Value);
            _deleteDialogOpen.Value = false;
            Close();
        }
        catch (Exception ex)
        {
            _deleteDialogOpen.Value = false;
            _error.Value = $"Failed to delete bag: {ex.Message}";
        }
    }

    [Body]
    View body()
    {
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        return new Grid
        {
            BaristaEdgeFrame.Build(
                new BeanBagHeaderTile(
                    IsEdit ? "EDIT BAG" : "NEW BAG",
                    () => string.IsNullOrWhiteSpace(_beanName) ? "Bag" : _beanName,
                    safeArea),
                RenderBody(),
                BottomActions(),
                "bag_detail_frame"),
            DeleteDialog(),
            StatusDialog(),
        }.AutomationId(IsEdit ? "bag_detail_page" : "bag_create_page");
    }

    View RenderBody()
    {
        if (_isLoading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading bag")
                .Background(CoffeeTheme.SurfaceColor);

        var sections = BaristaSections.Create(
            new BeanBagDateTile(_roastDate, "bag_roast_date"),
            NotesTile());

        if (IsEdit)
        {
            sections.Add(StatusTile());
            sections.Add(StatsTile());
            sections.Add(RatingsTile());
        }
        if (_error.Value is { } error)
            sections.Add(ErrorTile(error));
        sections.Add(new Grid().Background(CoffeeTheme.SurfaceColor).MinimumHeight(CoffeeSpacing.L));

        return BaristaSections.Scroll(sections);
    }

    View NotesTile() => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("NOTES").SectionLabel(),
        _notes.Value.Length == 0
            ? new Text("From Trader Joe's, gift from friend…").MutedText()
            : new Grid().Frame(height: 0),
        SignalExtensions.TextEditor(_notes)
            .SourceInputChrome()
            .SourceText()
            .Frame(height: 100)
            .AutomationId("bag_notes"),
        new Text(() => $"{_notes.Value.Length}/500").MutedText(),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(150);

    View StatusTile()
    {
        var status = _isComplete.Value ? "COMPLETE" : "ACTIVE";
        var action = _isComplete.Value ? "REACTIVATE" : "MARK COMPLETE";
        return new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "*" })
        {
            new Text("STATUS").SectionLabel().Cell(row: 0, column: 0),
            new Text(status)
                .FontFamily("ManropeSemibold")
                .FontSize(22)
                .Color(CoffeeTheme.TextPrimary)
                .Cell(row: 1, column: 0),
            new Button(action, () => _ = ToggleStatusAsync())
                .Color(CoffeeTheme.SurfaceColor)
                .Background(CoffeeTheme.TextPrimary)
                .CornerRadius(0)
                .FontFamily("ManropeSemibold")
                .FontSize(BaristaSourceVisualContract.BagStatusFontSize)
                .CharacterSpacing(1.5)
                .MaxLines(1)
                .LineBreakMode(LineBreakMode.NoWrap)
                .Padding(new Thickness(
                    BaristaSourceVisualContract.SourceButtonHorizontalPadding,
                    BaristaSourceVisualContract.SourceButtonVerticalPadding))
                .MinimumWidth(154)
                .MinimumHeight(BaristaSourceVisualContract.SourceButtonMinimumHeight)
                .AutomationId("bag_toggle_status")
                .Center()
                .Cell(row: 0, column: 1, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.SurfaceColor)
        .MinimumHeight(100);
    }

    View StatsTile() => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("SHOTS LOGGED").SectionLabel(),
        new Text(() => _shotCount.Value.ToString()).Headline(),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(100)
    .AutomationId("bag_shot_count");

    View RatingsTile() => new VStack(spacing: CoffeeSpacing.S)
    {
        new Text("RATINGS").SectionLabel(),
        new BeanBagRatingSummary(() => _rating.Value, "bag_rating_summary"),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(90);

    static View ErrorTile(string message) => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("ERROR").SectionLabel().Color(CoffeeTheme.SurfaceColor),
        new Text(message)
            .FontFamily("ManropeSemibold")
            .FontSize(16)
            .Color(CoffeeTheme.SurfaceColor),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 12))
    .Background(CoffeeTheme.Error)
    .MinimumHeight(60)
    .AutomationId("bag_error");

    View BottomActions() => IsEdit
        ? new BeanBagActionRow(
            new BeanBagAction("CANCEL", "bag_cancel", Close),
            new BeanBagAction("DELETE", "bag_delete", () => _deleteDialogOpen.Value = true, Danger: true),
            new BeanBagAction(
                _isSaving.Value ? "SAVING…" : "SAVE",
                "bag_save",
                () => _ = SaveAsync(),
                Primary: true))
        : new BeanBagActionRow(
            new BeanBagAction("CANCEL", "bag_cancel", Close),
            new BeanBagAction(
                _isSaving.Value ? "SAVING…" : "ADD",
                "bag_save",
                () => _ = SaveAsync(),
                Primary: true));

    View DeleteDialog() => new AlertDialog(
        _deleteDialogOpen,
        text: new Text(
            $"Are you sure you want to delete this bag? Its {_shotCount.Value} associated shot record(s) will remain in history. This action cannot be undone.")
            .SecondaryText(),
        title: new Text("Delete Bag?").SubHeadline(),
        confirmButton: new Button("DELETE", () => _ = DeleteAsync())
            .TextButton().Color(CoffeeTheme.Error).CornerRadius(0)
            .AutomationId("bag_delete_confirm"),
        dismissButton: new Button("CANCEL", () => _deleteDialogOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0)
            .AutomationId("bag_delete_cancel"))
        .AutomationId("bag_delete_dialog");

    View StatusDialog() => new AlertDialog(
        _statusDialogOpen,
        text: new Text(() => _statusMessage.Value).SecondaryText(),
        title: new Text(() => _statusTitle.Value).SubHeadline(),
        confirmButton: new Button("OK", () => _statusDialogOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0))
        .AutomationId("bag_status_dialog");

    void ShowStatus(string title, string message)
    {
        _statusTitle.Value = title;
        _statusMessage.Value = message;
        _statusDialogOpen.Value = true;
    }

    void Close()
    {
        NavigationView.Pop(this);
        NotifyExit();
    }

    void NotifyExit()
    {
        if (_exitNotified)
            return;
        _exitNotified = true;
        _onExit?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _subscribed)
        {
            _notifier.DataChanged -= OnDataChanged;
            _subscribed = false;
        }
        if (disposing)
            NotifyExit();
        base.Dispose(disposing);
    }
}
