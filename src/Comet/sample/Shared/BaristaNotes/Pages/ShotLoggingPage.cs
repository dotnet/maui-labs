#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Comet;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.Grind;
using CometBaristaNotes.Services.Voice;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

enum DrinkPickerKind
{
    None,
    BrewMethod,
    Bag,
    DrinkType,
    Rating,
    People,
    Machine,
    Grinder,
    Dose,
    Yield,
    Time,
    Grind,
    Temperature,
    AddCoffee,
    CreateBag,
}

public sealed class ShotLoggingPage : View
{
#if IOS || __IOS__
    const double RatingValueFontSize = 34;
#else
    const double RatingValueFontSize = 38;
#endif

    readonly BaristaServices _services;
    readonly BaristaNavigationCoordinator _navigation;
    int? _shotId;

    readonly Signal<bool> _loading = new(true);
    readonly Signal<int> _editorStateVersion = new(0);
    readonly Signal<bool> _saving = new(false);
    readonly Signal<bool> _dirty = new(false);
    readonly Signal<string?> _loadError = new(null);
    readonly Signal<BrewMethod> _method = new(BrewMethod.Espresso);
    readonly Signal<string> _drinkType = new("Espresso");
    readonly Signal<decimal> _dose = new(18);
    readonly Signal<decimal> _expectedYield = new(36);
    readonly Signal<decimal> _expectedTime = new(28);
    readonly Signal<decimal?> _actualYield = new(null);
    readonly Signal<decimal?> _actualTime = new(null);
    readonly Signal<int?> _grind = new(270);
    readonly Signal<decimal?> _temperatureC = new(93);
    readonly Signal<TemperatureUnit> _temperatureUnit;
    readonly Signal<int> _rating = new(2);
    readonly Signal<string> _notes = new(string.Empty);
    readonly Signal<int?> _bagId = new(null);
    readonly Signal<int?> _madeById = new(null);
    readonly Signal<int?> _madeForId = new(null);
    readonly Signal<int?> _machineId = new(null);
    readonly Signal<int?> _grinderId = new(null);
    readonly Signal<DrinkPickerKind> _picker = new(DrinkPickerKind.None);

    readonly Signal<BrewMethod> _pendingMethod = new(BrewMethod.Espresso);
    readonly Signal<string> _pendingDrinkType = new("Espresso");
    readonly Signal<int?> _pendingId = new(null);
    readonly Signal<int> _pendingRating = new(2);
    readonly Signal<int?> _pendingMadeById = new(null);
    readonly Signal<int?> _pendingMadeForId = new(null);
    readonly Signal<int?> _pendingGrinderId = new(null);
    readonly Signal<bool> _returnToGrind = new(false);
    readonly Signal<decimal> _pickerValue = new(0);
    readonly Signal<bool> _pickerValueChanged = new(false);
    readonly Signal<bool> _pickerShowsFullRange = new(false);

    readonly Signal<string> _createName = new(string.Empty);
    readonly Signal<string> _createSecondary = new(string.Empty);
    readonly Signal<string> _createNotes = new(string.Empty);
    readonly Signal<int?> _createBeanId = new(null);
    readonly Signal<DateTime?> _createRoastDate = new(DateTime.Today);

    readonly Signal<bool> _discardDialogOpen = new(false);
    readonly Signal<bool> _deleteDialogOpen = new(false);
    readonly Signal<bool> _feedbackOpen = new(false);
    readonly Signal<string> _feedbackTitle = new(string.Empty);
    readonly Signal<string> _feedbackMessage = new(string.Empty);
    readonly Signal<long?> _leaveRequestGeneration = new(null);
    readonly EditorLeaveGuard _leaveGuard = new();
    readonly ActionCommand _backCommand;
    readonly Signal<int> _editorInsetsVersion = new(0);
    PropertySubscription<Thickness>? _editorInsetsProjection;
    CometWindowMetrics? _editorInsetsOwner;

    List<int> _accessoryIds = new();
    List<decimal> _numericValues = new();
    ListView<decimal>? _numericList;
    bool _hydrating = true;
    int _loadVersion;

    public ShotLoggingPage(
        BaristaServices services,
        int? shotId = null,
        BaristaNavigationCoordinator? navigation = null,
        ShotRecordDto? initialShot = null)
    {
        _services = services;
        _temperatureUnit = services.TemperatureUnit;
        _shotId = shotId;
        _navigation = navigation ?? new BaristaNavigationCoordinator(services);
        _backCommand = new ActionCommand(
            HandleSystemBack,
            () => _picker.Value != DrinkPickerKind.None
                || _navigation.IsRootShotEditorActive(this));
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
            Command = _backCommand,
        });
        _picker.PropertyChanged += (_, _) => _backCommand.RaiseCanExecuteChanged();
        _notes.PropertyChanged += (_, _) =>
        {
            if (!_hydrating)
                _dirty.Value = true;
        };
        if (initialShot is not null)
        {
            _hydrating = true;
            HydrateForEdit(initialShot);
            _hydrating = false;
            _loading.Value = false;
        }
        if (initialShot is null)
            _ = LoadAsync();
    }

    public int? EditingShotId => _shotId;

    public void BeginEdit(ShotRecordDto shot)
    {
        Interlocked.Increment(ref _loadVersion);
        _shotId = shot.Id;
        _returnToGrind.Value = false;
        _picker.Value = DrinkPickerKind.None;
        _leaveGuard.Cancel();
        _leaveRequestGeneration.Value = null;
        _discardDialogOpen.Value = false;
        _deleteDialogOpen.Value = false;
        _feedbackOpen.Value = false;
        _saving.Value = false;
        _pickerValueChanged.Value = false;
        _pickerShowsFullRange.Value = false;
        _createName.Value = string.Empty;
        _createSecondary.Value = string.Empty;
        _createNotes.Value = string.Empty;
        _createBeanId.Value = null;
        _createRoastDate.Value = DateTime.Today;
        _numericValues.Clear();
        _numericList = null;
        _hydrating = true;
        HydrateForEdit(shot);
        _hydrating = false;
        _loadError.Value = null;
        _loading.Value = false;
        _dirty.Value = false;
        _editorStateVersion.Value++;
        _backCommand.RaiseCanExecuteChanged();
    }

    public void ResetForNewDrink()
    {
        Interlocked.Increment(ref _loadVersion);
        _shotId = null;
        _returnToGrind.Value = false;
        _picker.Value = DrinkPickerKind.None;
        _leaveGuard.Cancel();
        _leaveRequestGeneration.Value = null;
        _discardDialogOpen.Value = false;
        _deleteDialogOpen.Value = false;
        _feedbackOpen.Value = false;
        _saving.Value = false;
        _pickerValueChanged.Value = false;
        _pickerShowsFullRange.Value = false;
        _createName.Value = string.Empty;
        _createSecondary.Value = string.Empty;
        _createNotes.Value = string.Empty;
        _createBeanId.Value = null;
        _createRoastDate.Value = DateTime.Today;
        _numericValues.Clear();
        _numericList = null;
        _hydrating = true;
        _method.Value = BrewMethod.Espresso;
        _drinkType.Value = "Espresso";
        _dose.Value = 18;
        _expectedYield.Value = 36;
        _expectedTime.Value = 28;
        _actualYield.Value = null;
        _actualTime.Value = null;
        _grind.Value = 270;
        _temperatureC.Value = 93;
        _rating.Value = 2;
        _notes.Value = string.Empty;
        _bagId.Value = null;
        _madeById.Value = null;
        _madeForId.Value = null;
        _machineId.Value = null;
        _grinderId.Value = null;
        _accessoryIds = new();
        _hydrating = false;
        _loadError.Value = null;
        _loading.Value = false;
        _dirty.Value = false;
        _editorStateVersion.Value++;
        _backCommand.RaiseCanExecuteChanged();
        _ = LoadAsync(showLoading: false);
    }

    public void RequestLeave(Action confirmedLeave)
    {
        var request = _leaveGuard.Begin(_dirty.Value, confirmedLeave);
        if (request.RequiresConfirmation)
        {
            _leaveRequestGeneration.Value = request.Generation;
            _discardDialogOpen.Value = true;
        }
        else
        {
            _leaveRequestGeneration.Value = null;
            _discardDialogOpen.Value = false;
        }
    }

    internal async Task<EditorLeaveOutcome> RequestLeaveAsync(
        Action confirmedLeave,
        CancellationToken cancellationToken = default)
    {
        var request = _leaveGuard.Begin(
            _dirty.Value,
            confirmedLeave,
            cancellationToken);
        if (request.RequiresConfirmation)
        {
            _leaveRequestGeneration.Value = request.Generation;
            _discardDialogOpen.Value = true;
        }

        var outcome = await request.Completion;
        if (outcome == EditorLeaveOutcome.Cancelled)
        {
            ThreadHelper.RunOnMainThread(() =>
            {
                if (_leaveRequestGeneration.Value != request.Generation)
                    return;
                _leaveRequestGeneration.Value = null;
                _discardDialogOpen.Value = false;
            });
        }
        return outcome;
    }

    public void ApplyVoiceFields(VoiceFieldUpdates updates)
    {
        if (updates.DoseGrams.HasValue)
            _dose.Value = updates.DoseGrams.Value;
        if (updates.YieldGrams.HasValue)
            _expectedYield.Value = updates.YieldGrams.Value;
        if (updates.TimeSeconds.HasValue)
        {
            _expectedTime.Value = updates.TimeSeconds.Value;
            _actualTime.Value = updates.TimeSeconds.Value;
        }
        if (updates.GrindMicrons.HasValue)
            _grind.Value = updates.GrindMicrons.Value;
        if (updates.Rating.HasValue)
            _rating.Value = Math.Clamp(updates.Rating.Value, 0, 4);
        if (updates.TastingNotes is not null)
            _notes.Value = updates.TastingNotes;
        MarkDirty();
    }

    /// <summary>
    /// Opens the real AddCoffee picker form with prefilled vision data.
    /// <see cref="CreateCoffeeAndBag"/> handles creation, sets <see cref="_bagId"/>,
    /// marks dirty, closes picker, and gives feedback — same as manual entry.
    /// </summary>
    public void OpenAddCoffeeFromPhoto(CometBaristaNotes.Services.DTOs.BeanLabelExtraction prefill)
    {
        _createName.Value = prefill.Name ?? string.Empty;
        _createSecondary.Value = prefill.Roaster ?? string.Empty;
        _createRoastDate.Value = prefill.RoastDate ?? DateTime.Today;
        _picker.Value = DrinkPickerKind.AddCoffee;
        _navigation.BeginTransientSurface(ClosePicker);
    }

    public async Task<CometBaristaNotes.Services.Voice.VoiceToolResultDto> CommitVoiceAsync(
        VoiceFieldUpdates updates,
        CancellationToken cancellationToken)
    {
        ApplyVoiceFields(updates);
        var validation = Validate();
        if (validation is not null)
            return new VoiceToolResultDto(false, validation);
        try
        {
            _saving.Value = true;
            if (_shotId.HasValue)
            {
                await _services.ShotService.UpdateShotAsync(_shotId.Value, new CometBaristaNotes.Services.DTOs.UpdateShotDto
                {
                    BagId = _bagId.Value, MachineId = _machineId.Value, GrinderId = _grinderId.Value,
                    ClearMachine = !_machineId.Value.HasValue, ClearGrinder = !_grinderId.Value.HasValue,
                    AccessoryIds = _accessoryIds, MadeById = _madeById.Value, MadeForId = _madeForId.Value,
                    BrewMethod = _method.Value, DrinkType = _drinkType.Value, DoseIn = _dose.Value,
                    ExpectedOutput = _expectedYield.Value, ExpectedTime = _expectedTime.Value,
                    ActualOutput = _actualYield.Value, ActualTime = _actualTime.Value,
                    GrindMicrons = _grind.Value, WaterTempC = _temperatureC.Value,
                    Rating = _rating.Value, TastingNotes = _notes.Value.Trim(),
                });
                _dirty.Value = false;
                return new VoiceToolResultDto(true, "Drink updated.", EntityId: _shotId.Value);
            }
            else
            {
                var created = await _services.ShotService.CreateShotAsync(new CometBaristaNotes.Services.DTOs.CreateShotDto
                {
                    BagId = _bagId.Value, MachineId = _machineId.Value, GrinderId = _grinderId.Value,
                    AccessoryIds = _accessoryIds, MadeById = _madeById.Value, MadeForId = _madeForId.Value,
                    BrewMethod = _method.Value, DrinkType = _drinkType.Value, DoseIn = _dose.Value,
                    ExpectedOutput = _expectedYield.Value, ExpectedTime = _expectedTime.Value,
                    ActualOutput = _actualYield.Value, ActualTime = _actualTime.Value,
                    GrindMicrons = _grind.Value, WaterTempC = _temperatureC.Value,
                    Rating = _rating.Value, TastingNotes = string.IsNullOrWhiteSpace(_notes.Value) ? null : _notes.Value.Trim(),
                });
                PersistLastSelections();
                _dirty.Value = false;
                return new VoiceToolResultDto(true, $"{_drinkType.Value} logged.", CreatedEntity: created, EntityId: created?.Id);
            }
        }
        catch (Exception ex)
        {
            return new VoiceToolResultDto(false, ex.Message);
        }
        finally
        {
            _saving.Value = false;
        }
    }

    public override void ViewDidAppear()
    {
        base.ViewDidAppear();
        RefreshForActivation();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Increment(ref _loadVersion);
            _editorInsetsProjection?.Dispose();
            _editorInsetsVersion.Dispose();
            _navigation.NotifyShotEditorDisposed(this);
        }
        base.Dispose(disposing);
    }

    public void RefreshForActivation()
    {
        var unit = _services.Preferences.GetTemperatureUnit();
        if (_temperatureUnit.Value != unit)
            _temperatureUnit.Value = unit;
    }

    [Body]
    View body()
    {
        _ = _editorStateVersion.Value;
        _ = CoffeeTheme.IsLight;
        _ = _temperatureUnit.Value;

        if (_loading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading drink").CoffeePageBackground();

        if (_loadError.Value is { } error)
        {
            return new Grid
            {
                new ContentStateView(ContentStateKind.Error, "Drink unavailable", error, () => _ = LoadAsync())
                    .CoffeePageBackground(),
                FeedbackDialog(),
            };
        }

        if (_picker.Value != DrinkPickerKind.None)
            return PickerSurface();

        var result = new Grid
        {
            EditorGrid(),
            DiscardDialog(),
            DeleteDialog(),
            FeedbackDialog(),
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId(_shotId.HasValue ? "edit_drink_page" : "new_drink_page");
        return result;
    }

    View EditorGrid()
    {
        var safeArea = EditorInsets();
        var contentGrid = new Grid(
            columns: new object[] { "*", "*", "*", "*" },
            rows: BaristaEdgeFrame.CreateEqualDataRows(BaristaEdgeFrame.NewDrinkContentRowCount),
            columnSpacing: CoffeeSpacing.Divider,
            rowSpacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor)
            .Padding(new Thickness(CoffeeSpacing.Divider, 0, CoffeeSpacing.Divider, CoffeeSpacing.Divider))
            .AutomationId("new_drink_content_grid");

        contentGrid.Add(Tile("METHOD", () => _method.Value.DisplayName(), "shot_tile_method",
            () => OpenPicker(DrinkPickerKind.BrewMethod))
            .Cell(row: 0, column: 0, colSpan: 2));
        contentGrid.Add(Tile("BAG", BagName, "shot_tile_bag",
            () => OpenPicker(DrinkPickerKind.Bag),
            onLongPress: OpenRecipeForSelectedBag)
            .Cell(row: 0, column: 2, colSpan: 2));

        contentGrid.Add(Tile("DRINK TYPE", () => _drinkType.Value, "shot_tile_drink_type",
            () => OpenPicker(DrinkPickerKind.DrinkType)).Cell(row: 1, column: 0, colSpan: 2));
        contentGrid.Add(Tile("RATING", RatingStars, "shot_tile_rating",
            () => OpenPicker(DrinkPickerKind.Rating),
            singleLineValue: true,
            valueFontSize: RatingValueFontSize)
            .Cell(row: 1, column: 2, colSpan: 2));

        contentGrid.Add(Tile("DOSE IN", () => $"{_dose.Value:0.#}", "shot_tile_dose",
            () => OpenPicker(DrinkPickerKind.Dose), () => "g").Cell(row: 2, column: 0, colSpan: 2));
        contentGrid.Add(Tile("YIELD", () => $"{_expectedYield.Value:0.#}", "shot_tile_yield",
            () => OpenPicker(DrinkPickerKind.Yield), () => "g").Cell(row: 2, column: 2, colSpan: 2));

        contentGrid.Add(Tile("TIME", () => FormatTime(_actualTime.Value ?? _expectedTime.Value), "shot_tile_time",
            () => OpenPicker(DrinkPickerKind.Time),
            () => TimeUnitFor(_actualTime.Value ?? _expectedTime.Value)).Cell(row: 3, column: 0, colSpan: 2));
        contentGrid.Add(Tile("GRIND", () => _grind.Value?.ToString() ?? "—", "shot_tile_grind",
            () => OpenPicker(DrinkPickerKind.Grind), () => _grind.Value.HasValue ? "µm" : null)
            .Cell(row: 3, column: 2, colSpan: 2));

        contentGrid.Add(Tile("WATER TEMP", TemperatureValue, "shot_tile_temperature",
            () => OpenPicker(DrinkPickerKind.Temperature), TemperatureUnitLabel)
            .Cell(row: 4, column: 0, colSpan: 2));
        contentGrid.Add(PeopleTile().Cell(row: 4, column: 2, colSpan: 2));

        contentGrid.Add(Tile("MACHINE", MachineName, "shot_tile_machine",
            () => OpenPicker(DrinkPickerKind.Machine)).Cell(row: 5, column: 0, colSpan: 2));
        contentGrid.Add(Tile("GRINDER", GrinderName, "shot_tile_grinder",
            () => OpenPicker(DrinkPickerKind.Grinder)).Cell(row: 5, column: 2, colSpan: 2));

        contentGrid.Add(SaveTile().Cell(row: 6, column: 0, colSpan: 4));

        var frame = new Grid(
            columns: new object[] { "*" },
            rows: new object[] { safeArea.Top, "*" })
        {
            contentGrid.Cell(row: 1),
        };
        if (safeArea.Top > 0)
            frame.Add(BaristaEdgeFrame.BuildTopInset(showDivider: true).Cell(row: 0));

        return frame
        .Padding(new Thickness(safeArea.Left, 0, safeArea.Right, 0))
        .Background(CoffeeTheme.SurfaceColor)
        .IgnoreSafeArea()
        .AutomationId("new_drink_edge_frame");
    }

    Thickness EditorInsets()
    {
        var owner = this.GetWindowMetrics();
        if (_editorInsetsProjection is null || !ReferenceEquals(owner, _editorInsetsOwner))
        {
            _editorInsetsProjection?.Dispose();
            _editorInsetsOwner = owner;
            // Keep full-inset reads out of the body scope. Only changed editor edges
            // notify the body; the normal editor does not consume the bottom inset.
            _editorInsetsProjection = new PropertySubscription<Thickness>(() =>
            {
                var safeArea = BaristaSafeAreaLayout.GetInsets(this);
                return new Thickness(safeArea.Left, safeArea.Top, safeArea.Right, 0);
            });
            _editorInsetsProjection.PropertyChangedCallback = _ => _editorInsetsVersion.Value++;
        }
        _ = _editorInsetsVersion.Value;
        return _editorInsetsProjection.Value;
    }

    static View Tile(
        string label,
        Func<string> value,
        string automationId,
        Action onTap,
        Func<string?>? unit = null,
        bool inverted = false,
        Action? onLongPress = null,
        bool singleLineValue = false,
        double valueCharacterSpacing = 0,
        double? valueFontSize = null) =>
        AdaptiveTwoLineTile.Build(
            label,
            value(),
            automationId,
            onTap,
            unit?.Invoke(),
            inverted,
            minimumHeight: 0,
            onLongPress: onLongPress,
            singleLineValue: singleLineValue,
            valueCharacterSpacing: valueCharacterSpacing,
            valueFontSize: valueFontSize);

    View SaveTile()
    {
        View tile = new Grid(rows: new object[] { "Auto", "*" }, columns: new object[] { "*" })
        {
            new Text(_shotId.HasValue ? "UPDATE" : "SAVE")
                .SectionLabel().Color(CoffeeTheme.SurfaceColor.WithAlpha(.72f)).Cell(row: 0),
            new Text(_saving.Value ? "Saving…" : _shotId.HasValue ? "Update" : "Log Drink")
                .FontFamily("ManropeSemibold").FontSize(28).Color(CoffeeTheme.SurfaceColor)
                .Alignment(Comet.Alignment.BottomLeading)
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.TextPrimary)
        .AutomationId("shot_save")
        .OnTap(_ => Save());

        if (_shotId.HasValue)
            tile = tile.OnLongPress(_ => _deleteDialogOpen.Value = true);
        return tile;
    }

    View PeopleTile()
    {
        var maker = _services.Store.Profiles.FirstOrDefault(item => item.Id == _madeById.Value);
        var recipient = _services.Store.Profiles.FirstOrDefault(item => item.Id == _madeForId.Value);

        return new Grid(
            columns: new object[] { "*" },
            rows: new object[] { "Auto", "*" })
        {
            new Text("MADE BY / FOR")
                .SectionLabel()
                .Cell(row: 0),
            new HStack(spacing: CoffeeSpacing.S)
            {
                PersonChip(maker, "shot_made_by"),
                new Text(CoffeeIcons.ArrowRight)
                    .FontFamily(CoffeeIcons.FontFamily)
                    .FontSize(22)
                    .Color(CoffeeTheme.PrimaryColor)
                    .Center(),
                PersonChip(recipient, "shot_made_for"),
            }
            .Center()
            .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, BaristaSafeAreaLayout.HeaderVerticalPadding))
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("shot_tile_people")
        .OnTap(_ => OpenPicker(DrinkPickerKind.People));
    }

    View PersonChip(UserProfile? profile, string automationId)
    {
        var name = profile?.Name;
        var avatar = new Grid
        {
            new Text(CoffeeIcons.Person)
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(22)
                .Color(CoffeeTheme.TextSecondary)
                .Center(),
        }
        .Frame(width: 40, height: 40)
        .Background(CoffeeTheme.TextSecondary.WithAlpha(.12f))
        .Border(1, CoffeeTheme.TextSecondary.WithAlpha(.4f))
        .CornerRadius(20)
        .AutomationId($"{automationId}_avatar");

        return new VStack(spacing: 3)
        {
            avatar,
            new Text(string.IsNullOrWhiteSpace(name) ? "—" : name)
                .FontFamily("Manrope")
                .FontSize(11)
                .Color(string.IsNullOrWhiteSpace(name)
                    ? CoffeeTheme.TextSecondary
                    : CoffeeTheme.TextPrimary)
                .MaxLines(1)
                .Center(),
        }
        .Center()
        .AutomationId(automationId);
    }

    View PickerSurface()
    {
        var kind = _picker.Value;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        Action? done = RequiresDone(kind) ? CommitPicker : null;
        Action? clear = kind is DrinkPickerKind.Machine or DrinkPickerKind.Grinder
            ? ClearSingleEquipment
            : null;
        var surface = new Grid(
            columns: new object[] { "*" },
            rows: new object[] { "Auto", "*" })
        {
            PickerHeader(PickerTitle(kind), done, clear, safeArea).Cell(row: 0),
            PickerBody(kind).Cell(row: 1),
            FeedbackDialog(),
        };
        return surface.Background(CoffeeTheme.SurfaceColor)
            .AutomationId($"picker_{kind.ToString().ToLowerInvariant()}");
    }

    View PickerHeader(string title, Action? done, Action? clear, Thickness safeArea)
    {
        var columns = clear is null
            ? new object[] { "Auto", "*", "Auto" }
            : new object[] { "Auto", "*", "Auto", "Auto" };
        var header = new Grid(columns: columns, rows: new object[] { "*" })
        {
            new Button("Close", ClosePicker).TextButton().Color(CoffeeTheme.TextSecondary)
                .CornerRadius(0).AutomationId("picker_close").Cell(column: 0),
            new Text(title.ToUpperInvariant())
                .FontFamily("ManropeSemibold")
                .FontSize(12)
                .Color(CoffeeTheme.TextSecondary)
                .SetEnvironment(nameof(ITextStyle.CharacterSpacing), 3d, true)
                .Center()
                .Cell(column: 0, colSpan: columns.Length),
        };
        if (clear is not null)
        {
            header.Add(new Button("Clear", clear).TextButton().Color(CoffeeTheme.TextSecondary)
                .CornerRadius(0).AutomationId("picker_clear").Cell(column: columns.Length - 2));
        }
        if (done is not null)
        {
            header.Add(new Button("Done", done).TextButton().Color(CoffeeTheme.PrimaryColor)
                .CornerRadius(0).AutomationId("picker_done").Cell(column: columns.Length - 1));
        }
        return header.Padding(BaristaSafeAreaLayout.PickerHeaderPadding(safeArea))
            .MinimumHeight(BaristaSafeAreaLayout.PickerHeaderContentHeight)
            .Background(CoffeeTheme.SurfaceColor);
    }

    View PickerBody(DrinkPickerKind kind) => kind switch
    {
        DrinkPickerKind.BrewMethod => MethodPicker(),
        DrinkPickerKind.Bag => BagPicker(),
        DrinkPickerKind.DrinkType => DrinkTypePicker(),
        DrinkPickerKind.Rating => RatingPicker(),
        DrinkPickerKind.People => PeoplePicker(),
        DrinkPickerKind.Machine or DrinkPickerKind.Grinder => EquipmentSinglePicker(kind),
        DrinkPickerKind.AddCoffee => AddCoffeeForm(),
        DrinkPickerKind.CreateBag => CreateBagForm(),
        _ => NumericPicker(),
    };

    View MethodPicker() => CategoricalRows(BrewMethodExtensions.All.Select(method =>
        new PickerChoice(
            method.ToString(),
            method.DisplayName(),
            () => _method.Value == method,
            () =>
            {
                ApplyMethod(method);
                ClosePicker();
            })));

    View BagPicker()
    {
        var rows = ActiveBags()
            .Select(bag => new PickerChoice(
                bag.Id.ToString(),
                $"{bag.Bean?.Name ?? $"Bag {bag.Id}"} · Roasted {bag.RoastDate:MMM d}",
                () => _bagId.Value == bag.Id,
                () =>
                {
                    _bagId.Value = bag.Id;
                    MarkDirty();
                    ClosePicker();
                }))
            .ToList();
        rows.Add(new PickerChoice("add-coffee", "Add coffee", () => false,
            () => OpenCreation(DrinkPickerKind.AddCoffee), CoffeeIcons.Add));
        rows.Add(new PickerChoice("create-bag", "Create bag", () => false,
            () => OpenCreation(DrinkPickerKind.CreateBag), CoffeeIcons.Bag));
        return CategoricalRows(rows);
    }

    View DrinkTypePicker() => CategoricalRows(
        _method.Value.DrinkTypesFor().Select(type =>
            new PickerChoice(type, type, () => _drinkType.Value == type,
                () =>
                {
                    _drinkType.Value = type;
                    MarkDirty();
                    ClosePicker();
                })));

    View RatingPicker()
    {
        var rows = Enumerable.Range(0, 5).Select(value =>
            new PickerChoice(
                value.ToString(),
                new string('★', value + 1) + new string('☆', 4 - value),
                () => _rating.Value == value,
                () =>
                {
                    _rating.Value = value;
                    MarkDirty();
                    ClosePicker();
                }));
        return CategoricalRows(rows);
    }

    View PeoplePicker()
    {
        var profiles = ActiveProfiles();
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        var subheader = new Grid(
            columns: new object[] { "*", "Auto", "*" },
            rows: new object[] { "*" })
        {
            PickerPeopleColumnLabel("BY").Cell(row: 0, column: 0),
            new Text(CoffeeIcons.ArrowRight)
                .FontFamily(CoffeeIcons.FontFamily)
                .FontSize(20)
                .Color(CoffeeTheme.PrimaryColor)
                .Center()
                .AutomationId("picker_people_direction")
                .Cell(row: 0, column: 1),
            PickerPeopleColumnLabel("FOR").Cell(row: 0, column: 2),
        }.Padding(new Thickness(CoffeeSpacing.M, CoffeeSpacing.XS, CoffeeSpacing.M, CoffeeSpacing.S));
        var columns = new Grid(
            columns: new object[] { "*", CoffeeSpacing.Divider, "*" },
            rows: new object[] { "*" })
        {
            PersonColumn(profiles, _pendingMadeById, "made_by").Cell(row: 0, column: 0),
            new Grid().Background(CoffeeTheme.OutlineColor).Cell(row: 0, column: 1),
            PersonColumn(profiles, _pendingMadeForId, "made_for").Cell(row: 0, column: 2),
        };

        return new Grid(rows: new object[] { 44, "*" })
        {
            subheader.Cell(row: 0),
            columns
                .Padding(new Thickness(0, 0, 0, safeArea.Bottom))
                .Cell(row: 1),
        }.Background(CoffeeTheme.SurfaceColor);
    }

    static View PickerPeopleColumnLabel(string text) =>
        new Text(text)
            .FontFamily("ManropeSemibold")
            .FontSize(11)
            .Color(CoffeeTheme.TextSecondary)
            .SetEnvironment(nameof(ITextStyle.CharacterSpacing), 2d, true)
            .Center();

    ListView<UserProfile> PersonColumn(
        List<UserProfile> profiles,
        Signal<int?> selected,
        string automationPrefix)
    {
        var selectedIndex = selected.Value.HasValue
            ? Math.Max(0, profiles.FindIndex(profile => profile.Id == selected.Value))
            : 0;
        ListView<UserProfile>? list = null;
        list = new ListView<UserProfile>(() => profiles)
        {
            ViewFor = profile =>
            {
                var isSelected = selected.Value == profile.Id;
                var automationId = $"picker_{automationPrefix}_{profile.Id}";
                return new VStack(spacing: 4)
                {
                    new ProfileAvatar(
                        profile.AvatarPath,
                        isSelected ? 72 : 52,
                        $"{automationId}_avatar",
                        highlighted: isSelected)
                        .Center(),
                    new Text(profile.Name)
                        .FontFamily(isSelected ? "ManropeSemibold" : "Manrope")
                        .FontSize(isSelected ? 18 : 14)
                        .Color(isSelected ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextPrimary)
                        .Center(),
                }
                .Padding(new Thickness(CoffeeSpacing.S, 14))
                .MinimumHeight(isSelected ? 112 : 92)
                .AutomationId(automationId)
                .OnTap(tap =>
                {
                    if (list is null)
                        return;

                    var index = profiles.FindIndex(candidate => candidate.Id == profile.Id);
                    list.InitialScrollIndex = index;
                    list.InitialScrollPosition = ListScrollPosition.Center;
                    list.ScrollTo(index, ListScrollPosition.Center, animate: true);
                    selected.Value = profile.Id;
                    list.ReloadData();
                    _ = ScrollProfileAfterSelectionAsync(list, index);
                });
            },
        }
        .InitialScrollTo(selectedIndex, ListScrollPosition.Center)
        .AutomationId($"picker_{automationPrefix}_list")
        .Key($"people_{automationPrefix}_{profiles.Count}");
        return list;
    }

    static async Task ScrollProfileAfterSelectionAsync(
        ListView<UserProfile>? list,
        int index)
    {
        await Task.Yield();
        if (list is not null)
            ThreadHelper.RunOnMainThread(() =>
                list.ScrollTo(index, ListScrollPosition.Center, animate: true));
    }

    View EquipmentSinglePicker(DrinkPickerKind kind)
    {
        var equipment = ActiveEquipment();
        var type = kind == DrinkPickerKind.Machine
            ? EquipmentType.Machine
            : EquipmentType.Grinder;
        var selectedId = kind == DrinkPickerKind.Machine
            ? _machineId.Value
            : _returnToGrind.Value ? _pendingGrinderId.Value : _grinderId.Value;
        var choices = equipment
            .Where(item => item.Type == type)
            .Select(item => new PickerChoice(
                item.Id.ToString(),
                item.Name,
                () => selectedId == item.Id,
                () =>
                {
                    SelectEquipment(kind, selectedId == item.Id ? null : item.Id);
                }))
            .ToList();
        if (choices.Count == 0)
        {
            return EmptyCreateRoute(
                $"No {type.ToString().ToLowerInvariant()}s yet",
                $"Add a {type.ToString().ToLowerInvariant()} to select it for this drink.",
                $"ADD {type.ToString().ToUpperInvariant()}",
                () => OpenFullEquipmentCreator(type),
                $"picker_add_first_{type.ToString().ToLowerInvariant()}");
        }
        return CategoricalRows(choices);
    }

    void OpenFullEquipmentCreator(EquipmentType type)
    {
        var returnToGrind = _returnToGrind.Value;
        var loadVersion = _loadVersion;
        if (returnToGrind)
        {
            _returnToGrind.Value = false;
            _picker.Value = DrinkPickerKind.None;
            _numericList = null;
            _navigation.EndTransientSurface();
        }
        else
        {
            ClosePicker();
        }

        _navigation.OpenEquipmentCreator(type, (equipmentId, actualType) =>
        {
            if (loadVersion != _loadVersion)
                return;
            if (returnToGrind)
            {
                if (actualType == EquipmentType.Grinder)
                    _pendingGrinderId.Value = equipmentId;
                else
                    ShowFeedback("Equipment created", "This equipment is not a grinder. Choose a grinder for the dial setting.");
                return;
            }
            if (actualType == EquipmentType.Machine)
                _machineId.Value = equipmentId;
            else if (actualType == EquipmentType.Grinder)
                _grinderId.Value = equipmentId;
            else if (!_accessoryIds.Contains(equipmentId))
                _accessoryIds.Add(equipmentId);
            MarkDirty();
        }, afterReturn: returnToGrind ? () =>
        {
            if (loadVersion != _loadVersion)
                return;
            _picker.Value = DrinkPickerKind.Grind;
            _navigation.BeginTransientSurface(DismissPicker);
        } : null);
    }

    View NumericPicker()
    {
        var effective = NumericRange(_picker.Value);
        var original = NumericOriginal(_picker.Value);
        if (_picker.Value is DrinkPickerKind.Dose or DrinkPickerKind.Yield)
            return MassPicker(effective, original);
        if (_picker.Value == DrinkPickerKind.Time)
            return TimePicker(effective, original);

        var snappedIndex = _numericValues.FindIndex(value => value == _pickerValue.Value);
        ListView<decimal>? list = null;
        list = new ListView<decimal>(() => _numericValues)
        {
            ViewFor = value => NumericPickerRow(
                NumericValue(value),
                value == _pickerValue.Value,
                $"picker_value_{value:0.##}".Replace('.', '_'),
                () =>
                {
                    if (_picker.Value == DrinkPickerKind.Temperature)
                    {
                        _pickerValue.Value = value;
                        _pickerValueChanged.Value = true;
                        CommitPicker();
                        return;
                    }
                    if (list is null)
                        return;

                    var index = _numericValues.FindIndex(candidate => candidate == value);
                    list.InitialScrollIndex = index;
                    list.InitialScrollPosition = ListScrollPosition.Center;
                    list.ScrollTo(index, ListScrollPosition.Center, animate: true);
                    _pickerValue.Value = value;
                    _pickerValueChanged.Value = true;
                    list.ReloadData();
                    _ = ScrollToAfterSelectionAsync(list, index);
                }),
        }
        .InitialScrollTo(snappedIndex, ListScrollPosition.Center)
        .AutomationId("picker_values")
        .Key($"numeric_picker_{_picker.Value}_{_pickerShowsFullRange.Value}");
        _numericList = list;

        var rows = _picker.Value == DrinkPickerKind.Grind
            ? new object[] { 48, "*", 64 }
            : new object[] { 48, "*" };
        var body = new Grid(columns: new object[] { "*" }, rows: rows)
        {
            RangeScopeBar(effective, original).Cell(row: 0),
            list.Cell(row: 1),
        };
        if (_picker.Value == DrinkPickerKind.Grind)
            body.Add(GrindTranslationEntry().Cell(row: 2));
        return body.Background(CoffeeTheme.SurfaceColor);
    }

    View TimePicker(EffectiveDrinkValueRange effective, decimal original)
    {
        var lastMinute = TimeWheelMaximumMinute(effective);
        var current = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);
        var selectedMinutes = current / 60;
        var selectedSeconds = current % 60;
        var minuteValues = Enumerable.Range(0, lastMinute + 1).ToList();
        // Both wheels expose every representable component. Recommended-range outliers
        // remain selectable and are styled separately instead of being omitted.
        var secondValues = Enumerable.Range(0, 60).ToList();

        var minuteList = CenteredMassColumn(
            minuteValues,
            selectedMinutes,
            value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) / 60,
            "time_minute",
            value => value.ToString(),
            value =>
            {
                var pending = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);
                var candidate = value * 60 + pending % 60;
                SelectTimeValue(candidate);
            },
            isPreferred: value =>
            {
                var pending = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);
                return effective.Range.Contains(value * 60 + pending % 60);
            });
        var secondList = CenteredMassColumn(
            secondValues,
            selectedSeconds,
            value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) % 60,
            "time_second",
            value => value.ToString("00"),
            value =>
            {
                var pending = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);
                var candidate = (pending / 60) * 60 + value;
                SelectTimeValue(candidate);
            },
            isPreferred: value =>
            {
                var pending = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);
                return effective.Range.Contains((pending / 60) * 60 + value);
            });

        return new Grid(
            columns: new object[] { "*", "Auto", "*", "Auto" },
            rows: new object[] { 48, "*" })
        {
            RangeScopeBar(effective, original, allowToggle: false).Cell(row: 0, column: 0, colSpan: 4),
            minuteList.Cell(row: 1, column: 0),
            new Text(":").FontFamily("ManropeSemibold").FontSize(48)
                .Color(CoffeeTheme.TextSecondary).Center().Cell(row: 1, column: 1),
            secondList.Cell(row: 1, column: 2),
            new Text("s").FontFamily("Manrope").FontSize(20)
                .Color(CoffeeTheme.TextSecondary).Center()
                .Padding(new Thickness(0, 0, CoffeeSpacing.M, 0))
                .Cell(row: 1, column: 3),
        }.Background(CoffeeTheme.SurfaceColor);
    }

    void SelectTimeValue(int seconds)
    {
        _pickerValue.Value = seconds;
        _pickerValueChanged.Value = true;
    }

    View MassPicker(EffectiveDrinkValueRange effective, decimal original)
    {
        var activeRange = _pickerShowsFullRange.Value ? effective.HardRange : effective.Range;
        var current = activeRange.Clamp(_pickerValue.Value);
        var whole = (int)decimal.Truncate(current);
        var tenth = (int)((current - decimal.Truncate(current)) * 10);
        var firstWhole = (int)Math.Floor(activeRange.Minimum);
        var lastWhole = (int)Math.Floor(activeRange.Maximum);
        var wholeValues = Enumerable.Range(firstWhole, lastWhole - firstWhole + 1).ToList();
        var tenthValues = Enumerable.Range(0, 10).ToList();

        var wholeList = CenteredMassColumn(
            wholeValues,
            whole,
            value => value == (int)decimal.Truncate(activeRange.Clamp(_pickerValue.Value)),
            "mass_whole",
            value =>
            {
                var pending = activeRange.Clamp(_pickerValue.Value);
                var fraction = pending - decimal.Truncate(pending);
                _pickerValue.Value = ClampMass(value + fraction, activeRange);
                _pickerValueChanged.Value = true;
            });
        var tenthList = CenteredMassColumn(
            tenthValues,
            tenth,
            value =>
            {
                var pending = activeRange.Clamp(_pickerValue.Value);
                return value == (int)((pending - decimal.Truncate(pending)) * 10);
            },
            "mass_tenth",
            value =>
            {
                var pending = activeRange.Clamp(_pickerValue.Value);
                _pickerValue.Value = ClampMass(decimal.Truncate(pending) + value / 10m, activeRange);
                _pickerValueChanged.Value = true;
            });

        return new Grid(
            columns: new object[] { "*", "Auto", "*", "Auto" },
            rows: new object[] { 48, "*" })
        {
            RangeScopeBar(effective, original).Cell(row: 0, column: 0, colSpan: 4),
            wholeList.Cell(row: 1, column: 0),
            new Text(".").FontFamily("ManropeSemibold").FontSize(48)
                .Color(CoffeeTheme.TextSecondary).Center().Cell(row: 1, column: 1),
            tenthList.Cell(row: 1, column: 2),
            new Text("g").FontFamily("Manrope").FontSize(20)
                .Color(CoffeeTheme.TextSecondary).Center()
                .Padding(new Thickness(0, 0, CoffeeSpacing.M, 0))
                .Cell(row: 1, column: 3),
        }.Background(CoffeeTheme.SurfaceColor);
    }

    ListView<int> CenteredMassColumn(
        List<int> values,
        int selectedValue,
        Func<int, bool> isSelected,
        string automationPrefix,
        Action<int> select,
        Func<int, string>? format = null,
        Func<int, bool>? isPreferred = null)
    {
        return CenteredMassColumn(
            values,
            selectedValue,
            isSelected,
            automationPrefix,
            format ?? (value => value.ToString()),
            select,
            isPreferred: isPreferred);
    }

    ListView<int> CenteredMassColumn(
        List<int> values,
        int selectedValue,
        Func<int, bool> isSelected,
        string automationPrefix,
        Func<int, string> format,
        Action<int> select,
        Action? afterSelect = null,
        Func<int, bool>? isPreferred = null)
    {
        var selectedIndex = values.FindIndex(value => value == selectedValue);
        ListView<int>? list = null;
        list = new ListView<int>(() => values)
        {
            SnapToCenter = true,
            ViewFor = value => NumericPickerRow(
                format(value),
                isSelected(value),
                $"{automationPrefix}_{value}",
                () =>
                {
                    if (list is null)
                        return;

                    var index = values.FindIndex(candidate => candidate == value);
                    // Issue the request before the state-driven retained-list update. The
                    // current native list can resolve the stable row ID immediately; the
                    // deferred replay below covers the replacement generation.
                    list.InitialScrollIndex = index;
                    list.InitialScrollPosition = ListScrollPosition.Center;
                    list.ScrollTo(index, ListScrollPosition.Center, animate: true);
                    select(value);
                    list.ReloadData();
                    _ = ScrollToAfterSelectionAsync(list, index);
                    afterSelect?.Invoke();
                },
                rowHeight: 96,
                outsidePreferredRange: isPreferred is not null ? !isPreferred(value) : false),
        }
        .InitialScrollTo(selectedIndex, ListScrollPosition.Center)
        .AutomationId($"{automationPrefix}_list")
        .Key($"{_picker.Value}_{automationPrefix}_{_pickerShowsFullRange.Value}");
        return list;
    }

    static async Task ScrollToAfterSelectionAsync<T>(ListView<T>? list, int index)
    {
        await Task.Yield();
        if (list is not null)
            ThreadHelper.RunOnMainThread(() =>
                list.ScrollTo(index, ListScrollPosition.Center, animate: true));
    }

    static int TimeWheelMaximumMinute(EffectiveDrinkValueRange effective)
    {
        var maximum = Math.Max(effective.Range.Maximum, effective.HardRange.Maximum);
        return Math.Max(1, (int)Math.Ceiling(maximum / 60m));
    }

    static int TimeWheelMaximumSeconds(EffectiveDrinkValueRange effective) =>
        TimeWheelMaximumMinute(effective) * 60 + 59;

    static decimal ClampTimeToWheelDomain(decimal seconds, int lastMinute) =>
        Math.Clamp(
            Math.Round(seconds, 0, MidpointRounding.AwayFromZero),
            0,
            lastMinute * 60 + 59);

    static decimal ClampMass(decimal value, DrinkValueRange range) =>
        Math.Round(range.Clamp(value), 1, MidpointRounding.AwayFromZero);

    View RangeScopeBar(
        EffectiveDrinkValueRange range,
        decimal original,
        bool allowToggle = true)
    {
        var outside = !range.Range.Contains(original);
        var showingFullRange = !allowToggle || _pickerShowsFullRange.Value;
        var description = outside
            ? "Current value is outside your recommended range."
            : showingFullRange
                ? "Showing the full recommended range."
                : "Showing your recommended range.";
        var result = new Grid(columns: new object[] { "*", "Auto" }, rows: new object[] { "*" })
        {
            new Text(description).FontFamily("Manrope").FontSize(12)
                .Color(outside ? CoffeeTheme.Warning : CoffeeTheme.TextSecondary)
                .Center().Cell(column: 0),
            new Text(showingFullRange ? (allowToggle ? "PREFERRED" : "FULL RANGE") : "FULL RANGE")
                .SectionLabel().Color(CoffeeTheme.PrimaryColor).Center().Cell(column: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, CoffeeSpacing.S))
        .Background(CoffeeTheme.TextPrimary.WithAlpha(.05f))
        .AutomationId("range_scope_toggle");
        if (!allowToggle)
            return result;

        return result.OnTap(_ =>
        {
            using var hold = ReactiveScheduler.HoldFlushes();
            _pickerShowsFullRange.Value = !_pickerShowsFullRange.Value;
            if (_picker.Value is not (DrinkPickerKind.Dose or DrinkPickerKind.Yield))
                _numericValues = NumericValues(_picker.Value, _pickerValue.Value);
        });
    }

    View GrindTranslationEntry()
    {
        var grinder = _services.Store.Equipment.FirstOrDefault(item => item.Id == _pendingGrinderId.Value);
        var profile = grinder is null
            ? null
            : _services.Store.GrinderProfiles.FirstOrDefault(item =>
                item.EquipmentId == grinder.Id && !item.IsDeleted);
        IReadOnlyList<GrindAnchor> anchors =
            DeterministicGrindInterpolator.ParseAnchors(profile?.AnchorsJson);
        var hasPersistedCalibration = anchors.Count >= 2;
        if (!hasPersistedCalibration)
            anchors = KnownGrinderSeeds.TryGet(grinder?.Name) ?? anchors;
        var dial = DeterministicGrindInterpolator.Interpolate(
            anchors,
            _pickerValue.Value,
            minSetting: hasPersistedCalibration ? profile?.MinSetting : null,
            maxSetting: hasPersistedCalibration ? profile?.MaxSetting : null)
            ?.Suggested;
        var calibrated = dial.HasValue;
        var text = grinder is null
            ? "Select grinder for dial setting"
            : calibrated
                ? $"{grinder.Name} · {dial:0.#}"
                : grinder.Name;
        var row = new Grid(
            columns: grinder is not null && !calibrated
                ? new object[] { "*", "Auto" }
                : new object[] { "*" },
            rows: new object[] { "*" })
        {
            new Button(text, OpenGrinderFromGrind).TextButton()
                .FontFamily("ManropeSemibold").FontSize(16)
                .Color(CoffeeTheme.TextPrimary).CornerRadius(0)
                .AutomationId("grind_choose_grinder")
                .Cell(column: 0),
        };
        if (grinder is not null && !calibrated)
        {
            row.Add(new Button("Set up scale", () =>
                    ShowFeedback("Grind translation", $"Set up the scale for {grinder.Name} from Equipment."))
                .TextButton().FontSize(14).Color(CoffeeTheme.PrimaryColor).CornerRadius(0)
                .AutomationId("grind_scale_help")
                .Cell(column: 1));
        }

        return row
            .Padding(new Thickness(CoffeeSpacing.M, 0))
            .Background(CoffeeTheme.TextPrimary.WithAlpha(.05f))
            .AutomationId("grind_translation_entry");
    }

    void OpenGrinderFromGrind()
    {
        _returnToGrind.Value = true;
        _picker.Value = DrinkPickerKind.Grinder;
    }

    View AddCoffeeForm() => new ScrollView
    {
        new VStack(spacing: CoffeeSpacing.Divider)
        {
            new FormInputRow(
                "COFFEE NAME",
                SignalExtensions.TextField(_createName, "Coffee name").Borderless(),
                "create_coffee_name"),
            new FormInputRow(
                "ROASTER",
                SignalExtensions.TextField(_createSecondary, "Roaster (optional)").Borderless(),
                "create_coffee_roaster"),
            new BeanBagDateTile(_createRoastDate, "create_coffee_roast_date"),
            ActionRow("CREATE COFFEE AND BAG", "create_coffee_submit", CreateCoffeeAndBag),
        }
    }.Background(CoffeeTheme.OutlineColor);

    View CreateBagForm()
    {
        var beans = ActiveBeans();
        if (beans.Count == 0)
            return EmptyCreateRoute("No coffee yet", "Add coffee before creating a bag.", "ADD COFFEE",
                () => OpenCreation(DrinkPickerKind.AddCoffee), "bag_add_coffee");

        var rows = new VStack(spacing: CoffeeSpacing.Divider)
        {
            PickerSectionLabel("COFFEE"),
        };
        foreach (var bean in beans)
        {
            rows.Add(PickerRow(bean.Name, _createBeanId.Value == bean.Id, $"create_bag_bean_{bean.Id}",
                () => _createBeanId.Value = bean.Id));
        }
        rows.Add(new BeanBagDateTile(_createRoastDate, "create_bag_roast_date"));
        rows.Add(new FormInputRow(
            "NOTES",
            SignalExtensions.TextField(_createNotes, "Bag notes (optional)").Borderless(),
            "create_bag_notes"));
        rows.Add(ActionRow("CREATE BAG", "create_bag_submit", CreateBag));
        return new ScrollView { rows }.Background(CoffeeTheme.OutlineColor);
    }

    static View ActionRow(string title, string automationId, Action action) =>
        new Button(title, action)
            .Background(CoffeeTheme.TextPrimary)
            .Color(CoffeeTheme.SurfaceColor)
            .CornerRadius(0)
            .Frame(height: 72)
            .AutomationId(automationId);

    static View EmptyCreateRoute(
        string title,
        string message,
        string action,
        Action invoke,
        string automationId) =>
        new VStack(spacing: CoffeeSpacing.M)
        {
            new ContentStateView(ContentStateKind.Empty, title, message),
            new Button(action, invoke).TextButton().Color(CoffeeTheme.PrimaryColor)
                .CornerRadius(0).AutomationId(automationId),
        }.Center();

    View CategoricalRows(IEnumerable<PickerChoice> choices)
    {
        var items = choices.ToList();
        var selectedIndex = Math.Max(0, items.FindIndex(choice => choice.Selected()));
        return new ListView<PickerChoice>(() => items)
        {
            ViewFor = choice => CategoricalPickerRow(
                choice.Display,
                choice.Selected(),
                $"picker_choice_{choice.Key}",
                choice.Select),
        }
        .InitialScrollTo(selectedIndex, ListScrollPosition.Center)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("picker_choices")
        .Key($"categorical_picker_{_picker.Value}");
    }

    static View CategoricalPickerRow(
        string text,
        bool selected,
        string automationId,
        Action onTap) =>
        new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto" })
        {
            new Text(text)
                .FontFamily(selected ? "ManropeSemibold" : "Manrope")
                .FontSize(selected
                    ? BaristaSourceVisualContract.PickerSelectedFontSize
                    : BaristaSourceVisualContract.PickerNormalFontSize)
                .Color(selected ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextPrimary)
                .Alignment(Comet.Alignment.Leading)
                .Cell(column: 0),
            new Text(selected ? "●" : string.Empty)
                .FontFamily("Manrope")
                .FontSize(14)
                .Color(CoffeeTheme.PrimaryColor)
                .Center()
                .Cell(column: 1),
        }
        .Padding(new Thickness(
            BaristaSourceVisualContract.PickerHorizontalPadding,
            BaristaSourceVisualContract.PickerVerticalPadding))
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId(automationId)
        .OnTap(_ => onTap());

    View PickerRow(string text, bool selected, string automationId, Action onTap, string? leadingIcon = null)
    {
        return new Grid(
            columns: new object[] { 44, "*", 44 },
            rows: new object[] { 64 })
        {
            new Text(leadingIcon ?? string.Empty).FontFamily(CoffeeIcons.FontFamily).FontSize(22)
                .Color(CoffeeTheme.PrimaryColor).Center().Cell(column: 0),
            new Text(text).FontFamily(selected ? "ManropeSemibold" : "Manrope")
                .FontSize(selected ? 22 : 18).Color(selected ? CoffeeTheme.PrimaryColor : CoffeeTheme.TextPrimary)
                .Center().Cell(column: 1),
            new Text(selected ? "●" : string.Empty)
                .FontFamily("Manrope").FontSize(14).Color(CoffeeTheme.PrimaryColor)
                .Center().Cell(column: 2),
        }
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId(automationId)
        .OnTap(_ => onTap());
    }

    static View NumericPickerRow(
        string text,
        bool selected,
        string automationId,
        Action onTap,
        double? rowHeight = null,
        bool outsidePreferredRange = false)
    {
        return new Button(text, onTap)
            .TextButton()
            .FontFamily(selected ? "ManropeSemibold" : "Manrope")
            .FontSize(selected ? 56 : 32)
            .Color(selected
                ? CoffeeTheme.PrimaryColor
                : outsidePreferredRange
                    ? CoffeeTheme.TextSecondary.WithAlpha(.72f)
                    : CoffeeTheme.TextPrimary)
            .Padding(0)
            .CornerRadius(0)
            .Background(CoffeeTheme.SurfaceColor)
            .Frame(height: (float)(rowHeight ?? (selected ? 96 : 72)))
            .FillHorizontal()
            .AutomationId(automationId);
    }

    static View PickerSectionLabel(string text) =>
        new Text(text).SectionLabel()
            .Padding(new Thickness(CoffeeSpacing.M, CoffeeSpacing.L, CoffeeSpacing.M, CoffeeSpacing.S))
            .Background(CoffeeTheme.SurfaceColor);

    void OpenPicker(DrinkPickerKind kind)
    {
        if (kind == DrinkPickerKind.People && ActiveProfiles().Count == 0)
        {
            NavigationView.Navigate(
                this,
                new ProfileDetailPage(null, OnProfileCreatedFromShotAsync));
            return;
        }

        _pendingMethod.Value = _method.Value;
        _pendingDrinkType.Value = _drinkType.Value;
        _pendingId.Value = _bagId.Value;
        _pendingRating.Value = _rating.Value;
        _pendingMadeById.Value = _madeById.Value;
        _pendingMadeForId.Value = _madeForId.Value;
        _pendingGrinderId.Value = _grinderId.Value;
        _returnToGrind.Value = false;
        _pickerShowsFullRange.Value = false;
        _pickerValueChanged.Value = false;
        _pickerValue.Value = NumericOriginal(kind);
        _numericValues = kind is DrinkPickerKind.Dose or DrinkPickerKind.Yield
            or DrinkPickerKind.Time
            ? new()
            : NumericValues(kind);
        _picker.Value = kind;
        _navigation.BeginTransientSurface(DismissPicker);
    }

    Task OnProfileCreatedFromShotAsync(string? _, CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
            _editorStateVersion.Value++;
        return Task.CompletedTask;
    }

    void OpenCreation(DrinkPickerKind kind)
    {
        _createName.Value = string.Empty;
        _createSecondary.Value = string.Empty;
        _createNotes.Value = string.Empty;
        _createBeanId.Value = ActiveBeans().FirstOrDefault()?.Id;
        _createRoastDate.Value = DateTime.Today;
        _picker.Value = kind;
    }

    void CommitPicker()
    {
        switch (_picker.Value)
        {
            case DrinkPickerKind.People:
                _madeById.Value = _pendingMadeById.Value;
                _madeForId.Value = _pendingMadeForId.Value;
                MarkDirty();
                break;
            case DrinkPickerKind.Dose:
                if (_pickerValueChanged.Value)
                {
                    _dose.Value = CommittedNumericValue();
                    MarkDirty();
                }
                break;
            case DrinkPickerKind.Yield:
                if (_pickerValueChanged.Value)
                {
                    _expectedYield.Value = CommittedNumericValue();
                    MarkDirty();
                }
                break;
            case DrinkPickerKind.Time:
                if (_pickerValueChanged.Value)
                {
                    var time = CommittedNumericValue();
                    _actualTime.Value = time;
                    _expectedTime.Value = time;
                    MarkDirty();
                }
                break;
            case DrinkPickerKind.Grind:
                var grinderChanged = _grinderId.Value != _pendingGrinderId.Value;
                _grinderId.Value = _pendingGrinderId.Value;
                if (_pickerValueChanged.Value)
                    _grind.Value = (int)CommittedNumericValue();
                if (_pickerValueChanged.Value || grinderChanged)
                    MarkDirty();
                break;
            case DrinkPickerKind.Temperature:
                if (_pickerValueChanged.Value)
                {
                    var display = CommittedNumericValue();
                    _temperatureC.Value = PrefersFahrenheit
                        ? TemperaturePickerConversion.ToCelsiusStorage(display)
                        : display;
                    MarkDirty();
                }
                break;
        }
        ClosePicker();
    }

    decimal CommittedNumericValue() =>
        NumericPickerCommitContract.Resolve(
            NumericOriginal(_picker.Value),
            _pickerValue.Value,
            _pickerValueChanged.Value,
            NumericPickerDismissal.Done);

    void ClearSingleEquipment()
    {
        SelectEquipment(_picker.Value, null);
    }

    void SelectEquipment(DrinkPickerKind kind, int? equipmentId)
    {
        if (kind == DrinkPickerKind.Grinder && _returnToGrind.Value)
        {
            _pendingGrinderId.Value = equipmentId;
            ClosePicker();
            return;
        }

        if (kind == DrinkPickerKind.Machine)
            _machineId.Value = equipmentId;
        else
            _grinderId.Value = equipmentId;
        MarkDirty();
        ClosePicker();
    }

    void ClosePicker()
    {
        if (_returnToGrind.Value)
        {
            _returnToGrind.Value = false;
            _picker.Value = DrinkPickerKind.Grind;
            return;
        }
        DismissPicker();
    }

    void DismissPicker()
    {
        _returnToGrind.Value = false;
        _picker.Value = DrinkPickerKind.None;
        _numericValues = new();
        _numericList = null;
        _pickerValueChanged.Value = false;
        _pickerShowsFullRange.Value = false;
        _navigation.EndTransientSurface();
    }

    void ApplyMethod(BrewMethod method)
    {
        if (method == _method.Value)
            return;

        if (!_shotId.HasValue)
        {
            _dose.Value = _services.RangeService.Resolve(DrinkValueMetric.DoseIn, method).Default;
            _expectedYield.Value = _services.RangeService.Resolve(DrinkValueMetric.Yield, method).Default;
            _expectedTime.Value = _services.RangeService.Resolve(DrinkValueMetric.Time, method).Default;
            _actualYield.Value = null;
            _actualTime.Value = null;
        }

        var drinks = method.DrinkTypesFor();
        if (!drinks.Contains(_drinkType.Value))
            _drinkType.Value = drinks[0];
        _method.Value = method;
        MarkDirty();
    }

    EffectiveDrinkValueRange NumericRange(DrinkPickerKind kind) => kind switch
    {
        DrinkPickerKind.Dose => _services.RangeService.Resolve(DrinkValueMetric.DoseIn, _method.Value),
        DrinkPickerKind.Yield => _services.RangeService.Resolve(DrinkValueMetric.Yield, _method.Value),
        DrinkPickerKind.Time => _services.RangeService.Resolve(DrinkValueMetric.Time, _method.Value),
        DrinkPickerKind.Grind => _services.RangeService.Resolve(DrinkValueMetric.GrindMicrons, _method.Value),
        DrinkPickerKind.Temperature when PrefersFahrenheit => new(
            new DrinkValueRange(150, 212), new DrinkValueRange(150, 212), 200, 1, "F", ValueRangeSource.Auto),
        _ => new(
            new DrinkValueRange(65, 100), new DrinkValueRange(65, 100), 93, .5m, "C", ValueRangeSource.Auto),
    };

    decimal NumericOriginal(DrinkPickerKind kind) => kind switch
    {
        DrinkPickerKind.Dose => _dose.Value,
        DrinkPickerKind.Yield => _expectedYield.Value,
        DrinkPickerKind.Time => _actualTime.Value ?? _expectedTime.Value,
        DrinkPickerKind.Grind => _grind.Value
            ?? _services.RangeService.Resolve(DrinkValueMetric.GrindMicrons, _method.Value).Default,
        DrinkPickerKind.Temperature when _temperatureC.Value.HasValue && PrefersFahrenheit
            => TemperaturePickerConversion.ToFahrenheitDisplay(_temperatureC.Value.Value),
        DrinkPickerKind.Temperature when _temperatureC.Value.HasValue => _temperatureC.Value.Value,
        DrinkPickerKind.Temperature => PrefersFahrenheit ? 200 : 93,
        _ => 0,
    };

    List<decimal> NumericValues(DrinkPickerKind kind, decimal? pendingValue = null)
    {
        if (kind is not (DrinkPickerKind.Dose or DrinkPickerKind.Yield or DrinkPickerKind.Time
            or DrinkPickerKind.Grind or DrinkPickerKind.Temperature))
            return new();

        var effective = NumericRange(kind);
        var range = _pickerShowsFullRange.Value ? effective.HardRange : effective.Range;
        var values = new List<decimal>();
        if (kind == DrinkPickerKind.Grind && _pickerShowsFullRange.Value)
        {
            for (var value = range.Minimum; value < effective.Range.Minimum; value += 50)
                values.Add(value);
            for (var value = effective.Range.Minimum; value <= effective.Range.Maximum; value += effective.Step)
                values.Add(value);
            for (var value = effective.Range.Maximum + 50; value <= range.Maximum; value += 50)
                values.Add(value);
            values.Add(range.Maximum);
        }
        else
        {
            for (var value = range.Minimum; value <= range.Maximum; value += effective.Step)
                values.Add(value);
            if (values.Count == 0 || values[^1] != range.Maximum)
                values.Add(range.Maximum);
        }

        var current = pendingValue ?? NumericOriginal(kind);
        if (range.Contains(current))
            values.Add(current);
        values = values.Distinct().OrderBy(value => value).ToList();
        var snapped = values.OrderBy(value => Math.Abs(value - current)).First();
        _pickerValue.Value = snapped;
        return values;
    }

    string NumericValue(decimal value) => _picker.Value switch
    {
        DrinkPickerKind.Dose or DrinkPickerKind.Yield => $"{value:0.#} g",
        DrinkPickerKind.Time => $"{FormatTime(value)}{TimeUnitFor(value)}",
        DrinkPickerKind.Grind => $"{value:0} µm",
        DrinkPickerKind.Temperature => $"{value:0.#} {(PrefersFahrenheit ? "°F" : "°C")}",
        _ => value.ToString("0.#"),
    };

    async void CreateCoffeeAndBag()
    {
        if (string.IsNullOrWhiteSpace(_createName.Value))
        {
            ShowFeedback("Coffee name required", "Enter a coffee name before continuing.");
            return;
        }

        try
        {
            var bean = await _services.BeanService.CreateBeanAsync(new CreateBeanDto
            {
                Name = _createName.Value,
                Roaster = string.IsNullOrWhiteSpace(_createSecondary.Value) ? null : _createSecondary.Value,
            });
            if (!bean.Success || bean.Data is null)
            {
                ShowFeedback("Coffee not saved", bean.ErrorMessage ?? bean.Message);
                return;
            }
            var bag = await _services.BagService.CreateNewBagForBeanAsync(
                bean.Data.Id,
                _createRoastDate.Value ?? DateTime.Today);
            if (!bag.Success || bag.Data is null)
            {
                ShowFeedback("Bag not saved", bag.ErrorMessage ?? bag.Message);
                return;
            }
            _bagId.Value = bag.Data.Id;
            MarkDirty();
            ClosePicker();
            ShowFeedback("Coffee added", $"{bean.Data.Name} is selected for this drink.");
        }
        catch (Exception ex)
        {
            ShowFeedback("Coffee not saved", ex.Message);
        }
    }

    async void CreateBag()
    {
        if (!_createBeanId.Value.HasValue)
        {
            ShowFeedback("Coffee required", "Select a coffee for this bag.");
            return;
        }
        try
        {
            var result = await _services.BagService.CreateNewBagForBeanAsync(
                _createBeanId.Value.Value,
                _createRoastDate.Value ?? DateTime.Today,
                string.IsNullOrWhiteSpace(_createNotes.Value) ? null : _createNotes.Value);
            if (!result.Success || result.Data is null)
            {
                ShowFeedback("Bag not saved", result.ErrorMessage ?? result.Message);
                return;
            }
            _bagId.Value = result.Data.Id;
            MarkDirty();
            ClosePicker();
            ShowFeedback("Bag created", $"{result.Data.BeanName} is selected for this drink.");
        }
        catch (Exception ex)
        {
            ShowFeedback("Bag not saved", ex.Message);
        }
    }

    async void Save()
    {
        if (_saving.Value)
            return;

        var validation = Validate();
        if (validation is not null)
        {
            ShowFeedback("Validation error", validation);
            return;
        }

        try
        {
            _saving.Value = true;
            if (_shotId.HasValue)
            {
                await _services.ShotService.UpdateShotAsync(_shotId.Value, new UpdateShotDto
                {
                    BagId = _bagId.Value,
                    MachineId = _machineId.Value,
                    GrinderId = _grinderId.Value,
                    ClearMachine = !_machineId.Value.HasValue,
                    ClearGrinder = !_grinderId.Value.HasValue,
                    AccessoryIds = _accessoryIds,
                    MadeById = _madeById.Value,
                    MadeForId = _madeForId.Value,
                    BrewMethod = _method.Value,
                    DrinkType = _drinkType.Value,
                    DoseIn = _dose.Value,
                    ExpectedOutput = _expectedYield.Value,
                    ExpectedTime = _expectedTime.Value,
                    ActualOutput = _actualYield.Value,
                    ActualTime = _actualTime.Value,
                    GrindMicrons = _grind.Value,
                    WaterTempC = _temperatureC.Value,
                    Rating = _rating.Value,
                    TastingNotes = _notes.Value.Trim(),
                });
                ShowFeedback("Drink updated", "Your changes were saved.");
            }
            else
            {
                await _services.ShotService.CreateShotAsync(new CreateShotDto
                {
                    BagId = _bagId.Value,
                    MachineId = _machineId.Value,
                    GrinderId = _grinderId.Value,
                    AccessoryIds = _accessoryIds,
                    MadeById = _madeById.Value,
                    MadeForId = _madeForId.Value,
                    BrewMethod = _method.Value,
                    DrinkType = _drinkType.Value,
                    DoseIn = _dose.Value,
                    ExpectedOutput = _expectedYield.Value,
                    ExpectedTime = _expectedTime.Value,
                    ActualOutput = _actualYield.Value,
                    ActualTime = _actualTime.Value,
                    GrindMicrons = _grind.Value,
                    WaterTempC = _temperatureC.Value,
                    Rating = _rating.Value,
                    TastingNotes = string.IsNullOrWhiteSpace(_notes.Value) ? null : _notes.Value.Trim(),
                });
                PersistLastSelections();
                ShowFeedback("Drink logged", $"{_drinkType.Value} was added to Activity.");
            }
            _dirty.Value = false;
        }
        catch (Exception ex)
        {
            ShowFeedback("Failed to save", ex.Message);
        }
        finally
        {
            _saving.Value = false;
        }
    }

    string? Validate()
    {
        if (!_bagId.Value.HasValue)
            return "Please select a bag.";
        if (string.IsNullOrWhiteSpace(_drinkType.Value))
            return "Please select a drink type.";
        var dose = _services.RangeService.Resolve(DrinkValueMetric.DoseIn, _method.Value);
        var yield = _services.RangeService.Resolve(DrinkValueMetric.Yield, _method.Value);
        var time = _services.RangeService.Resolve(DrinkValueMetric.Time, _method.Value);
        if (!dose.HardRange.Contains(_dose.Value))
            return $"Dose must be {dose.HardRange.Minimum:0.#}–{dose.HardRange.Maximum:0.#} g.";
        if (!yield.HardRange.Contains(_expectedYield.Value))
            return $"Yield must be {yield.HardRange.Minimum:0.#}–{yield.HardRange.Maximum:0.#} g.";
        var maximumTime = TimeWheelMaximumSeconds(time);
        if (_expectedTime.Value < 0 || _expectedTime.Value > maximumTime)
            return $"Expected time must be between 0 and {FormatTime(maximumTime)}.";
        if (_actualTime.Value.HasValue
            && (_actualTime.Value.Value < 0 || _actualTime.Value.Value > maximumTime))
            return $"Actual time must be between 0 and {FormatTime(maximumTime)}.";
        return null;
    }

    void PersistLastSelections()
    {
        _services.Preferences.SetLastDrinkType(_drinkType.Value);
        _services.Preferences.SetLastBagId(_bagId.Value);
        _services.Preferences.SetLastMachineId(_machineId.Value);
        _services.Preferences.SetLastGrinderId(_grinderId.Value);
        _services.Preferences.SetLastAccessoryIds(_accessoryIds);
        _services.Preferences.SetLastMadeById(_madeById.Value);
        _services.Preferences.SetLastMadeForId(_madeForId.Value);
        _services.Preferences.SetLastDoseIn(_dose.Value);
        _services.Preferences.SetLastGrindMicrons(_grind.Value);
        _services.Preferences.SetLastExpectedTime(_expectedTime.Value);
        _services.Preferences.SetLastExpectedOutput(_expectedYield.Value);
    }

    void OpenRecipeForSelectedBag()
    {
        var bag = _services.Store.Bags.FirstOrDefault(item => item.Id == _bagId.Value && !item.IsDeleted);
        if (bag is null)
        {
            ShowFeedback("Select a bag", "Select a bag first to view its recipe.");
            return;
        }
        var recipe = _services.Store.Recipes.FirstOrDefault(item =>
            item.BeanId == bag.BeanId && item.BrewMethod == _method.Value && !item.IsDeleted);
        if (recipe is null)
        {
            ShowFeedback("No recipe yet",
                $"No {_method.Value.DisplayName()} recipe exists for {bag.Bean?.Name ?? "this coffee"}.");
            return;
        }
        _navigation.OpenBeanRecipe(bag.BeanId);
    }

    void HandleSystemBack()
    {
        if (_picker.Value != DrinkPickerKind.None)
        {
            ClosePicker();
            return;
        }

        _navigation.RequestShotEditorBack(this);
    }

    async void Delete()
    {
        if (!_shotId.HasValue)
            return;
        try
        {
            await _services.ShotService.DeleteShotAsync(_shotId.Value);
            _deleteDialogOpen.Value = false;
            _dirty.Value = false;
            _navigation.CloseShotEditor(this);
        }
        catch (Exception ex)
        {
            _deleteDialogOpen.Value = false;
            ShowFeedback("Delete failed", ex.Message);
        }
    }

    View DiscardDialog()
    {
        var generation = _leaveRequestGeneration.Value;
        return new AlertDialog(
            _discardDialogOpen,
            text: new Text("Your changes have not been saved.").SecondaryText(),
            title: new Text("Discard changes?").SubHeadline(),
            confirmButton: new Button("DISCARD", () =>
            {
                if (!generation.HasValue)
                    return;
                _leaveGuard.Confirm(generation.Value, () =>
                {
                    if (_leaveRequestGeneration.Value != generation)
                        return;
                    _leaveRequestGeneration.Value = null;
                    _discardDialogOpen.Value = false;
                    _dirty.Value = false;
                });
            }).TextButton().Color(CoffeeTheme.Error).CornerRadius(0),
            dismissButton: new Button("KEEP EDITING", () =>
            {
                if (!generation.HasValue || !_leaveGuard.KeepEditing(generation.Value))
                    return;
                if (_leaveRequestGeneration.Value != generation)
                    return;
                _leaveRequestGeneration.Value = null;
                _discardDialogOpen.Value = false;
            })
                .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0));
    }

    View DeleteDialog() => new AlertDialog(
        _deleteDialogOpen,
        text: new Text("This drink will be removed from Activity.").SecondaryText(),
        title: new Text("Delete drink?").SubHeadline(),
        confirmButton: new Button("DELETE", Delete).TextButton().Color(CoffeeTheme.Error).CornerRadius(0),
        dismissButton: new Button("CANCEL", () => _deleteDialogOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0));

    View FeedbackDialog() => new AlertDialog(
        _feedbackOpen,
        text: new Text(() => _feedbackMessage.Value).SecondaryText(),
        title: new Text(() => _feedbackTitle.Value).SubHeadline(),
        confirmButton: new Button("OK", () => _feedbackOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0));

    void ShowFeedback(string title, string message)
    {
        _feedbackTitle.Value = title;
        _feedbackMessage.Value = message;
        _feedbackOpen.Value = true;
    }

    async System.Threading.Tasks.Task LoadAsync(bool showLoading = true)
    {
        var loadVersion = Volatile.Read(ref _loadVersion);
        try
        {
            if (showLoading)
                _loading.Value = true;
            _loadError.Value = null;
            _hydrating = true;
            _temperatureUnit.Value = _services.Preferences.GetTemperatureUnit();
            var shot = _shotId.HasValue
                ? await _services.ShotService.GetShotByIdAsync(_shotId.Value)
                : await _services.ShotService.GetMostRecentShotAsync();

            if (loadVersion != Volatile.Read(ref _loadVersion))
                return;

            if (_shotId.HasValue && shot is null)
                throw new InvalidOperationException("The selected drink no longer exists.");

            if (shot is not null)
            {
                if (_shotId.HasValue)
                    HydrateForEdit(shot);
                else
                    HydrateDefaultsFromLastShot(shot);
            }
            else
            {
                _bagId.Value = ActiveBags().FirstOrDefault()?.Id;
            }

            if (!_shotId.HasValue)
                ApplyPersistedSelections();
            _dirty.Value = false;
        }
        catch (Exception ex)
        {
            if (loadVersion == Volatile.Read(ref _loadVersion))
                _loadError.Value = ex.Message;
        }
        finally
        {
            if (loadVersion == Volatile.Read(ref _loadVersion))
            {
                _hydrating = false;
                _loading.Value = false;
            }
        }
    }

    void HydrateForEdit(ShotRecordDto shot)
    {
        _method.Value = shot.BrewMethod;
        _drinkType.Value = NormalizeDrinkType(shot.BrewMethod, shot.DrinkType);
        _dose.Value = shot.DoseIn;
        _expectedYield.Value = shot.ExpectedOutput;
        _expectedTime.Value = shot.ExpectedTime;
        _actualYield.Value = shot.ActualOutput;
        _actualTime.Value = shot.ActualTime;
        _grind.Value = shot.GrindMicrons;
        _temperatureC.Value = shot.WaterTempC;
        _rating.Value = shot.Rating ?? 2;
        _notes.Value = shot.TastingNotes ?? string.Empty;
        _bagId.Value = shot.Bag?.Id;
        _madeById.Value = shot.MadeBy?.Id;
        _madeForId.Value = shot.MadeFor?.Id;
        _machineId.Value = shot.Machine?.Id;
        _grinderId.Value = shot.Grinder?.Id;
        _accessoryIds = shot.Accessories.Select(item => item.Id).ToList();
    }

    void HydrateDefaultsFromLastShot(ShotRecordDto shot)
    {
        _method.Value = shot.BrewMethod;
        _drinkType.Value = NormalizeDrinkType(shot.BrewMethod, shot.DrinkType);
        _dose.Value = shot.DoseIn;
        _expectedYield.Value = shot.ExpectedOutput;
        _expectedTime.Value = shot.ExpectedTime;
        _actualYield.Value = null;
        _actualTime.Value = null;
        _grind.Value = shot.GrindMicrons;
        _temperatureC.Value = shot.WaterTempC;
        _rating.Value = shot.Rating ?? 2;
        _bagId.Value = shot.Bag?.Id;
    }

    void ApplyPersistedSelections()
    {
        var lastDrinkType = _services.Preferences.GetLastDrinkType();
        if (lastDrinkType is not null && _method.Value.DrinkTypesFor().Contains(lastDrinkType))
            _drinkType.Value = lastDrinkType;
        _bagId.Value = _services.Preferences.GetLastBagId() ?? _bagId.Value;
        _madeById.Value = _services.Preferences.GetLastMadeById() ?? _madeById.Value;
        _madeForId.Value = _services.Preferences.GetLastMadeForId() ?? _madeForId.Value;
        _machineId.Value = _services.Preferences.GetLastMachineId() ?? _machineId.Value;
        _grinderId.Value = _services.Preferences.GetLastGrinderId() ?? _grinderId.Value;
        var accessories = _services.Preferences.GetLastAccessoryIds();
        if (accessories.Count > 0)
            _accessoryIds = accessories;
    }

    void MarkDirty()
    {
        if (!_hydrating)
            _dirty.Value = true;
    }

    List<Bag> ActiveBags() => _services.Store.Bags
        .Where(item => item.IsActive && !item.IsComplete && !item.IsDeleted)
        .OrderByDescending(item => item.RoastDate)
        .ToList();

    List<Bean> ActiveBeans() => _services.Store.Beans
        .Where(item => item.IsActive && !item.IsDeleted)
        .OrderBy(item => item.Name)
        .ToList();

    List<UserProfile> ActiveProfiles() => _services.Store.Profiles
        .Where(item => !item.IsDeleted)
        .OrderBy(item => item.Name)
        .ToList();

    List<Equipment> ActiveEquipment() => _services.Store.Equipment
        .Where(item => item.IsActive && !item.IsDeleted)
        .OrderBy(item => item.Name)
        .ToList();

    string BagName() =>
        _services.Store.Bags.FirstOrDefault(item => item.Id == _bagId.Value)?.Bean?.Name ?? "—";

    string MachineName() =>
        _services.Store.Equipment.FirstOrDefault(item => item.Id == _machineId.Value)?.Name ?? "—";

    string GrinderName() =>
        _services.Store.Equipment.FirstOrDefault(item => item.Id == _grinderId.Value)?.Name ?? "—";

    string RatingStars() => new string('★', _rating.Value + 1) + new string('☆', 4 - _rating.Value);

    bool PrefersFahrenheit => _temperatureUnit.Value == TemperatureUnit.Fahrenheit;

    string TemperatureValue()
    {
        if (!_temperatureC.Value.HasValue)
            return "—";
        return PrefersFahrenheit
            ? $"{TemperaturePickerConversion.ToFahrenheitDisplay(_temperatureC.Value.Value):0}"
            : $"{_temperatureC.Value.Value:0.#}";
    }

    string? TemperatureUnitLabel() =>
        _temperatureC.Value.HasValue ? PrefersFahrenheit ? "°F" : "°C" : null;

    static string FormatTime(decimal seconds)
    {
        var rounded = (int)Math.Round(seconds);
        if (rounded < 60) return rounded.ToString();
        if (rounded < 3600)
        {
            var minutes = rounded / 60;
            var remainder = rounded % 60;
            return remainder == 0 ? $"{minutes}:00" : $"{minutes}:{remainder:00}";
        }
        var hours = rounded / 3600;
        var minutesPart = (rounded % 3600) / 60;
        return minutesPart == 0 ? $"{hours}h" : $"{hours}h {minutesPart}m";
    }

    static string? TimeUnitFor(decimal seconds) => seconds < 60 ? "s" : null;

    static bool RequiresDone(DrinkPickerKind kind) => kind is
        DrinkPickerKind.People or
        DrinkPickerKind.Dose or
        DrinkPickerKind.Yield or
        DrinkPickerKind.Time or
        DrinkPickerKind.Grind;

    static string PickerTitle(DrinkPickerKind kind) => kind switch
    {
        DrinkPickerKind.BrewMethod => "Brew Method",
        DrinkPickerKind.DrinkType => "Drink Type",
        DrinkPickerKind.Dose => "Dose In",
        DrinkPickerKind.Yield => "Yield",
        DrinkPickerKind.Time => "Time",
        DrinkPickerKind.Grind => "Grind",
        DrinkPickerKind.Temperature => "Water Temp",
        DrinkPickerKind.Machine => "Machine",
        DrinkPickerKind.Grinder => "Grinder",
        DrinkPickerKind.People => "MADE BY / FOR",
        DrinkPickerKind.AddCoffee => "Add Coffee",
        DrinkPickerKind.CreateBag => "Create Bag",
        _ => kind.ToString(),
    };

    sealed record PickerChoice(
        string Key,
        string Display,
        Func<bool> Selected,
        Action Select,
        string? Icon = null);


    static string NormalizeDrinkType(BrewMethod method, string drinkType)
    {
        var validTypes = method.DrinkTypesFor();
        return validTypes.Contains(drinkType) ? drinkType : validTypes[0];
    }

    sealed class ActionCommand(Action action, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => action();
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
