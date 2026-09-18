#nullable enable
using System;
using System.Linq;
using System.Windows.Input;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Styles;
using CometSamples.BaristaNotes.Components;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;

namespace CometSamples.BaristaNotes.Pages;

public class ValueRangeEditorPage : View
{
    readonly DrinkValueMetric _metric;
    readonly BrewMethod _method;
    readonly ValueRangeSettingsPage? _parent;
    readonly Signal<string> _minimumText = new(string.Empty);
    readonly Signal<string> _maximumText = new(string.Empty);
    readonly Signal<bool> _isSaving = new(false);
    readonly Signal<string?> _error = new(null);
    readonly Signal<bool> _discardDialogOpen = new(false);
    readonly Signal<bool> _recommendedDialogOpen = new(false);
    readonly ICommand _backCommand;

    string _originalMinimumText = string.Empty;
    string _originalMaximumText = string.Empty;
    decimal _originalMinimumCanonical;
    decimal _originalMaximumCanonical;
    bool _hasOverride;
    bool _allowNavigation;

    IDrinkValueRangeService Svc => BaristaServiceLocator.RangeService;
    DrinkValueRangeDefinition Definition =>
        BrewMethodValueRangeCatalog.GetDefinition(_method, _metric);
    RangeEditorUnit EditorUnit =>
        DrinkValueRangeFormatting.GetEditorUnit(_metric, _method);
    bool IsDirty =>
        _minimumText.Value != _originalMinimumText
        || _maximumText.Value != _originalMaximumText;

    public ValueRangeEditorPage(
        DrinkValueMetric metric,
        BrewMethod method,
        ValueRangeSettingsPage? parent = null)
    {
        _metric = metric;
        _method = method;
        _parent = parent;
        _backCommand = new DelegateCommand(AttemptBack);
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
            Command = _backCommand,
        });
        LoadValues();
    }

    void LoadValues()
    {
        var custom = Svc.GetSettings().Overrides.LastOrDefault(
            item => item.Metric == _metric && item.Method == _method);
        var range = custom is null
            ? Definition.AutoRange
            : new DrinkValueRange(custom.Minimum, custom.Maximum);

        _minimumText.Value = DrinkValueRangeFormatting.FormatEditorValue(
            range.Minimum,
            EditorUnit);
        _maximumText.Value = DrinkValueRangeFormatting.FormatEditorValue(
            range.Maximum,
            EditorUnit);
        _originalMinimumText = _minimumText.Value;
        _originalMaximumText = _maximumText.Value;
        _originalMinimumCanonical = range.Minimum;
        _originalMaximumCanonical = range.Maximum;
        _hasOverride = custom is not null;
        _error.Value = null;
    }

    void ValidateIfComplete(string minimumText, string maximumText)
    {
        if (string.IsNullOrWhiteSpace(minimumText)
            || string.IsNullOrWhiteSpace(maximumText))
        {
            _error.Value = null;
            return;
        }

        TryGetCanonicalRange(minimumText, maximumText, out _, out var error);
        _error.Value = error;
    }

    void UpdateMinimum(string value)
    {
        _minimumText.Value = value;
        ValidateIfComplete(value, _maximumText.Value);
    }

    void UpdateMaximum(string value)
    {
        _maximumText.Value = value;
        ValidateIfComplete(_minimumText.Value, value);
    }

    async System.Threading.Tasks.Task SaveAsync()
    {
        if (!TryGetCanonicalRange(
                _minimumText.Value,
                _maximumText.Value,
                out var range,
                out var error))
        {
            _error.Value = error;
            return;
        }

        _isSaving.Value = true;
        _error.Value = null;
        try
        {
            Svc.SaveOverride(_metric, _method, range.Minimum, range.Maximum);
            _parent?.RefreshAfterEdit();
            _allowNavigation = true;
            NavigationView.Pop(this);
        }
        catch (ArgumentException ex)
        {
            _error.Value = ex.Message;
            _isSaving.Value = false;
        }
    }

    void AttemptBack()
    {
        if (_allowNavigation || !IsDirty)
        {
            _allowNavigation = true;
            NavigationView.Pop(this);
            return;
        }

        _discardDialogOpen.Value = true;
    }

    void UseRecommended()
    {
        if (_hasOverride)
        {
            _recommendedDialogOpen.Value = true;
            return;
        }

        SetRecommendedFields();
    }

    void SetRecommendedFields()
    {
        _minimumText.Value = DrinkValueRangeFormatting.FormatEditorValue(
            Definition.AutoRange.Minimum,
            EditorUnit);
        _maximumText.Value = DrinkValueRangeFormatting.FormatEditorValue(
            Definition.AutoRange.Maximum,
            EditorUnit);
        _error.Value = null;
    }

    bool TryGetCanonicalRange(
        string minimumText,
        string maximumText,
        out DrinkValueRange range,
        out string? error)
    {
        return ValueRangeEditorValidation.TryGetCanonicalRange(
            _metric,
            Definition,
            EditorUnit,
            minimumText,
            maximumText,
            _originalMinimumText,
            _originalMaximumText,
            _originalMinimumCanonical,
            _originalMaximumCanonical,
            out range,
            out error);
    }

    [Body]
    View body()
    {
        _ = CoffeeTheme.IsLight;
        _ = _error.Value;
        var metric = DrinkValueRangeFormatting.MetricTitle(_metric);
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return new Grid
        {
            BaristaEdgeFrame.Build(
                HeaderTile(metric, safeArea),
                BodyContent(),
                BottomActions(safeArea),
                "value_range_editor_frame"),
            DiscardDialog(),
            RecommendedDialog(),
        }
        .CoffeePageBackground()
        .AutomationId("ValueRangeEditorPage");
    }

    View HeaderTile(string metric, Thickness safeArea) => new Grid(rows: new object[] { "Auto", "*" })
    {
        new Text($"CUSTOM {metric.ToUpperInvariant()}").RangeCaption().Cell(row: 0),
        new Text(_method.DisplayName()).Headline()
            .Alignment(Comet.Alignment.BottomLeading).Cell(row: 1),
    }
    .Padding(BaristaSafeAreaLayout.HeaderPadding(safeArea))
    .MinimumHeight(BaristaSafeAreaLayout.HeaderMinimumHeight(safeArea))
    .Background(CoffeeTheme.SurfaceColor)
    .AutomationId("RangeEditorHeader");

    View BodyContent()
    {
        var content = new VStack(spacing: CoffeeSpacing.Divider)
        {
            GuidanceTile(),
            ValueFieldTile(
                "MINIMUM",
                _minimumText,
                "Minimum value",
                UpdateMinimum,
                "RangeMinimum"),
            ValueFieldTile(
                "MAXIMUM",
                _maximumText,
                "Maximum value",
                UpdateMaximum,
                "RangeMaximum"),
        };

        if (_error.Value is not null)
            content.Add(ErrorTile(_error.Value!));

        content.Add(RecommendedTile());

        return new ScrollView { content.Background(CoffeeTheme.OutlineColor) }
            .Background(CoffeeTheme.SurfaceColor);
    }

    View GuidanceTile() => new VStack(spacing: CoffeeSpacing.S)
    {
        new Text($"ENTER VALUES IN {EditorUnit.Label.ToUpperInvariant()}").RangeCaption(),
        new Text(
            $"Recommended: {DrinkValueRangeFormatting.FormatRange(_metric, Definition.AutoRange)}")
            .SecondaryText()
            .Color(CoffeeTheme.TextPrimary),
        new Text(
            $"Allowed: {DrinkValueRangeFormatting.FormatRange(_metric, Definition.HardRange)}")
            .SecondaryText(),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .AutomationId("RangeEditorGuidance");

    View ValueFieldTile(
        string label,
        Signal<string> value,
        string placeholder,
        Action<string> onChanged,
        string automationId) =>
        new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto", "*" },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text(label).RangeCaption().Cell(row: 0, column: 0),
            new RangeTextField(value, placeholder, onChanged)
                .Keyboard(Keyboard.Numeric)
                .Borderless()
                .FontFamily("ManropeSemibold")
                .FontSize(22)
                .Color(CoffeeTheme.TextPrimary)
                .AutomationId(automationId)
                .Cell(row: 1, column: 0),
            new Text(EditorUnit.Label)
                .SecondaryText()
                .Center()
                .Cell(row: 0, column: 1, rowSpan: 2),
        }
        .Padding(new Thickness(CoffeeSpacing.M))
        .MinimumHeight(96)
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId($"{automationId}Tile");

    static View ErrorTile(string message) =>
        new Text(message)
            .FontFamily("ManropeSemibold")
            .FontSize(13)
            .Color(CoffeeTheme.SurfaceColor)
            .Padding(new Thickness(CoffeeSpacing.M, 12))
            .MinimumHeight(56)
            .Background(CoffeeTheme.Error)
            .AutomationId("RangeValidationError");

    View RecommendedTile() => new Grid(
        columns: new object[] { "*", "Auto" },
        rows: new object[] { "Auto", "Auto" },
        columnSpacing: CoffeeSpacing.S)
    {
        new Text("USE RECOMMENDED RANGE")
            .RangeCaption()
            .CharacterSpacing(1.5)
            .Cell(row: 0, column: 0),
        new Text(DrinkValueRangeFormatting.FormatRange(_metric, Definition.AutoRange))
            .FontFamily("ManropeSemibold")
            .FontSize(18)
            .Color(CoffeeTheme.TextPrimary)
            .Cell(row: 1, column: 0),
        new Text("\uf053")
            .FontFamily(CoffeeIcons.FontFamily)
            .FontSize(28)
            .Color(CoffeeTheme.PrimaryColor)
            .Center()
            .Cell(row: 0, column: 1, rowSpan: 2),
    }
    .Padding(new Thickness(CoffeeSpacing.M))
    .MinimumHeight(80)
    .Background(CoffeeTheme.SurfaceColor)
    .AutomationId("UseRecommendedRange")
    .OnTap(_ => UseRecommended());

    View BottomActions(Thickness safeArea) => new Grid(
        columns: new object[] { "*", "*" },
        rows: new object[] { "Auto" },
        columnSpacing: CoffeeSpacing.Divider)
    {
        ActionTile(
            "CANCEL",
            false,
            "RangeEditorCancel",
            AttemptBack,
            safeArea).Cell(column: 0),
        ActionTile(
            _isSaving.Value ? "SAVING" : "SAVE",
            true,
            "RangeEditorSave",
            async () =>
            {
                if (!_isSaving.Value)
                    await SaveAsync();
            },
            safeArea).Cell(column: 1),
    }
    .Background(CoffeeTheme.OutlineColor);

    static View ActionTile(
        string label,
        bool inverted,
        string automationId,
        Action action,
        Thickness safeArea) =>
        new Button(label, action)
            .FontFamily("ManropeSemibold")
            .FontSize(18)
            .CharacterSpacing(1)
            .Color(inverted ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary)
            .Background(inverted ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor)
            .CornerRadius(0)
            .Padding(BaristaSafeAreaLayout.ActionPadding(safeArea))
            .MinimumHeight(CoffeeSpacing.ActionRowHeight)
            .AutomationId(automationId);

    View DiscardDialog() => new AlertDialog(
        _discardDialogOpen,
        text: new Text("Your range changes have not been saved.").SecondaryText(),
        title: new Text("Discard changes?").SubHeadline(),
        confirmButton: new Button("Discard", () =>
        {
            _discardDialogOpen.Value = false;
            _allowNavigation = true;
            NavigationView.Pop(this);
        })
        .TextButton()
        .Color(CoffeeTheme.Error)
        .CornerRadius(0)
        .AutomationId("RangeDiscardConfirm"),
        dismissButton: new Button("Keep Editing", () => _discardDialogOpen.Value = false)
            .TextButton()
            .Color(CoffeeTheme.PrimaryColor)
            .CornerRadius(0)
            .AutomationId("RangeDiscardKeepEditing"));

    View RecommendedDialog() => new AlertDialog(
        _recommendedDialogOpen,
        text: new Text(
            $"Remove the custom {DrinkValueRangeFormatting.MetricTitle(_metric).ToLowerInvariant()} range for {_method.DisplayName()}?")
            .SecondaryText(),
        title: new Text("Use recommended range?").SubHeadline(),
        confirmButton: new Button("USE RECOMMENDED", () =>
        {
            Svc.RemoveOverride(_metric, _method);
            _parent?.RefreshAfterEdit();
            _recommendedDialogOpen.Value = false;
            _allowNavigation = true;
            NavigationView.Pop(this);
        })
        .TextButton()
        .Color(CoffeeTheme.PrimaryColor)
        .CornerRadius(0)
        .AutomationId("RangeRecommendedConfirm"),
        dismissButton: new Button("CANCEL", () => _recommendedDialogOpen.Value = false)
            .TextButton()
            .Color(CoffeeTheme.TextPrimary)
            .CornerRadius(0)
            .AutomationId("RangeRecommendedCancel"));

    sealed class DelegateCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    sealed class RangeTextField : TextField
    {
        readonly Signal<string> _value;
        readonly Action<string> _onChanged;

        public RangeTextField(
            Signal<string> value,
            string placeholder,
            Action<string> onChanged)
            : base(value, placeholder)
        {
            _value = value;
            _onChanged = onChanged;
        }

        protected override void OnBackendEvent<T>(
            Comet.Backend.EventId id,
            T payload)
        {
            base.OnBackendEvent(id, payload);
            if (id == Comet.Backend.EventIds.TextChanged && payload is string text)
            {
                _value.Value = text;
                _onChanged(text);
            }
        }
    }
}
