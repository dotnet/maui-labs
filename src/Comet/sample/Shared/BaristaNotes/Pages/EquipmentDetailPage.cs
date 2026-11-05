#nullable enable
using System;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

/// <summary>
/// Creates, edits, and archives equipment using the source app's flat form.
/// </summary>
public class EquipmentDetailPage : View
{
    static readonly (EquipmentType Type, string Label)[] TypeChoices =
    {
        (EquipmentType.Machine, "MACHINE"),
        (EquipmentType.Grinder, "GRINDER"),
        (EquipmentType.Tamper, "TAMPER"),
        (EquipmentType.PuckScreen, "PUCK SCREEN"),
        (EquipmentType.Other, "OTHER"),
    };

    readonly int? _equipmentId;
    readonly Func<Task>? _afterMutation;
    readonly Signal<string> _name = new(string.Empty);
    readonly Signal<EquipmentType> _type = new(EquipmentType.Machine);
    readonly Signal<string> _notes = new(string.Empty);
    readonly Signal<bool> _isLoading = new(false);
    readonly Signal<bool> _isSaving = new(false);
    readonly Signal<string?> _error = new(null);
    readonly Signal<bool> _archiveDialogOpen = new(false);

    IEquipmentService Service => BaristaServiceLocator.EquipmentService;
    bool IsEdit => _equipmentId is > 0;

    public EquipmentDetailPage(
        int? equipmentId,
        Func<Task>? afterMutation = null,
        EquipmentType? presetType = null)
    {
        _equipmentId = equipmentId;
        _afterMutation = afterMutation;
        this.BackButtonBehavior(new BackButtonBehavior
        {
            IsVisible = false,
        });

        if (IsEdit)
        {
            _isLoading.Value = true;
            _ = LoadAsync();
        }
        else if (presetType.HasValue)
        {
            _type.Value = presetType.Value;
        }
    }

    async Task LoadAsync()
    {
        _error.Value = null;

        try
        {
            var equipment = await Service.GetEquipmentByIdAsync(_equipmentId!.Value);
            if (equipment is null)
            {
                _error.Value = "Equipment not found";
                return;
            }

            _name.Value = equipment.Name;
            _type.Value = equipment.Type;
            _notes.Value = equipment.Notes ?? string.Empty;
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to load equipment: {ex.Message}";
        }
        finally
        {
            _isLoading.Value = false;
        }
    }

    bool ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(_name.Value))
        {
            _error.Value = "Equipment name is required";
            return false;
        }

        if (_name.Value.Length > 100)
        {
            _error.Value = "Equipment name must be 100 characters or less";
            return false;
        }

        if (_notes.Value.Length > 500)
        {
            _error.Value = "Notes must be 500 characters or less";
            return false;
        }

        _error.Value = null;
        return true;
    }

    async Task SaveAsync()
    {
        if (_isSaving.Value || !ValidateForm())
            return;

        _isSaving.Value = true;
        try
        {
            if (IsEdit)
            {
                await Service.UpdateEquipmentAsync(_equipmentId!.Value, new UpdateEquipmentDto
                {
                    Name = _name.Value,
                    Type = _type.Value,
                    Notes = string.IsNullOrWhiteSpace(_notes.Value) ? null : _notes.Value,
                });
            }
            else
            {
                await Service.CreateEquipmentAsync(new CreateEquipmentDto
                {
                    Name = _name.Value,
                    Type = _type.Value,
                    Notes = string.IsNullOrWhiteSpace(_notes.Value) ? null : _notes.Value,
                });
            }

            if (_afterMutation is not null)
                await _afterMutation();
            NavigationView.Pop(this);
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to save: {ex.Message}";
            _isSaving.Value = false;
        }
    }

    async Task ArchiveAsync()
    {
        if (!IsEdit || _isSaving.Value)
            return;

        _isSaving.Value = true;
        _archiveDialogOpen.Value = false;
        try
        {
            await Service.ArchiveEquipmentAsync(_equipmentId!.Value);
            if (_afterMutation is not null)
                await _afterMutation();
            NavigationView.Pop(this);
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to archive: {ex.Message}";
            _isSaving.Value = false;
        }
    }

    [Body]
    View body()
    {
        _ = CoffeeTheme.IsLight;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return new Grid
        {
            EditorGrid(safeArea),
            ArchiveDialog(),
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId(IsEdit ? "equipment_edit_page" : "equipment_create_page");
    }

    View EditorGrid(Thickness safeArea) => BaristaEdgeFrame.Build(
        HeaderView(safeArea),
        FormBody(),
        BottomActionRow(),
        "equipment_detail_frame");

    View HeaderView(Thickness safeArea)
    {
        var title = IsEdit
            ? string.IsNullOrEmpty(_name.Value) ? "Loading…" : _name.Value
            : "Add equipment";

        return BaristaPageHeader.Build(
            IsEdit ? "EDIT EQUIPMENT" : "NEW EQUIPMENT",
            title,
            safeArea,
            "equipment_detail_header",
            BaristaPageHeader.TitleFontSize(title),
            titleAutomationId: "equipment_title");
    }

    View FormBody()
    {
        if (_isLoading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading equipment")
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("equipment_detail_loading");

        return BaristaSections.Scroll(
            BaristaSections.Create(
                NameFieldTile(),
                TypeSelectorTile(),
                NotesFieldTile(),
                ErrorTile(),
                new Grid().Frame(height: CoffeeSpacing.L).Background(CoffeeTheme.SurfaceColor)),
            "equipment_form");
    }

    View NameFieldTile() => new Grid(
        rows: new object[] { "Auto", 60 },
        columns: new object[] { "*" })
    {
        new Text("NAME").SectionLabel().Cell(row: 0),
        SignalExtensions.TextField(_name, "Equipment name")
            .SourceInputChrome()
            .SourceText()
            .FontFamily("ManropeSemibold")
            .FontSize(22)
            .AutomationId("equipment_name")
            .Cell(row: 1),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(100)
    .AutomationId("equipment_name_tile");

    View TypeSelectorTile()
    {
        var grid = new Grid(
            rows: new object[] { 44, 44 },
            columns: new object[] { "*", "*", "*" },
            rowSpacing: CoffeeSpacing.S,
            columnSpacing: CoffeeSpacing.S)
        {
            TypeChoice(TypeChoices[0]).Cell(row: 0, column: 0),
            TypeChoice(TypeChoices[1]).Cell(row: 0, column: 1),
            TypeChoice(TypeChoices[2]).Cell(row: 0, column: 2),
            TypeChoice(TypeChoices[3]).Cell(row: 1, column: 0),
            TypeChoice(TypeChoices[4]).Cell(row: 1, column: 1),
        };

        return new VStack(spacing: 10)
        {
            new Text("TYPE").SectionLabel(),
            grid,
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14, CoffeeSpacing.M, CoffeeSpacing.M))
        .Background(CoffeeTheme.SurfaceColor)
        .AutomationId("equipment_type_selector");
    }

    View TypeChoice((EquipmentType Type, string Label) choice)
    {
        var selected = _type.Value == choice.Type;
        var background = selected ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceVariant;
        var foreground = selected ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;

        return new Grid
        {
            new Text(choice.Label)
                .FontFamily("ManropeSemibold")
                .FontSize(11)
                .CharacterSpacing(1.5)
                .Color(foreground)
                .Center(),
        }
        .Background(background)
        .AutomationId($"equipment_type_{choice.Type.ToString().ToLowerInvariant()}")
        .OnTap(_ =>
        {
            if (!_isSaving.Value)
                _type.Value = choice.Type;
        });
    }

    View NotesFieldTile() => new Grid(
        rows: new object[] { "Auto", 120 },
        columns: new object[] { "*" })
    {
        new Text("NOTES").SectionLabel().Cell(row: 0),
        SignalExtensions.TextEditor(_notes)
            .Placeholder("Additional details")
            .SourceInputChrome()
            .SourceText()
            .FontSize(16)
            .AutomationId("equipment_notes")
            .Cell(row: 1),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(160)
    .AutomationId("equipment_notes_tile");

    View ErrorTile()
    {
        if (_error.Value is null)
            return new Grid().Frame(height: 0).Background(CoffeeTheme.SurfaceColor);

        return new Grid(rows: new object[] { "Auto", "Auto" }, columns: new object[] { "*" })
        {
            new Text("ERROR")
                .SectionLabel()
                .Color(CoffeeTheme.SurfaceColor.WithAlpha(.8f))
                .Cell(row: 0),
            new Text(_error.Value!)
                .FontFamily("ManropeSemibold")
                .FontSize(16)
                .Color(CoffeeTheme.SurfaceColor)
                .AutomationId("equipment_error_message")
                .Cell(row: 1),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 12))
        .Background(CoffeeTheme.Error)
        .MinimumHeight(60)
        .AutomationId("equipment_validation_error");
    }

    View BottomActionRow()
    {
        var columns = IsEdit
            ? new object[] { "*", "*", "*" }
            : new object[] { "*", "*" };
        var row = new Grid(
            columns: columns,
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
            .Background(CoffeeTheme.OutlineColor)
            .AutomationId("equipment_detail_actions");

        var padding = BaristaSafeAreaLayout.ActionPadding(this);
        row.Add(ActionTile(
                "CANCEL",
                "equipment_cancel",
                () => NavigationView.Pop(this),
                padding,
                enabled: !_isSaving.Value)
            .Cell(column: 0));

        if (IsEdit)
        {
            row.Add(ActionTile(
                    "DELETE",
                    "equipment_archive",
                    () => _archiveDialogOpen.Value = true,
                    padding,
                    danger: true,
                    enabled: !_isSaving.Value)
                .Cell(column: 1));
        }

        row.Add(ActionTile(
                _isSaving.Value ? "SAVING…" : IsEdit ? "SAVE" : "ADD",
                "equipment_save",
                () => _ = SaveAsync(),
                padding,
                inverted: true,
                enabled: !_isSaving.Value)
            .Cell(column: IsEdit ? 2 : 1));

        return row;
    }

    static View ActionTile(
        string label,
        string automationId,
        Action action,
        Thickness padding,
        bool inverted = false,
        bool danger = false,
        bool enabled = true)
    {
        var background = danger
            ? CoffeeTheme.Error
            : inverted
                ? CoffeeTheme.TextPrimary
                : CoffeeTheme.SurfaceColor;
        var foreground = inverted || danger ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;
        if (!enabled)
            foreground = foreground.WithAlpha(.5f);

        View tile = new Button(label, action)
        .FontFamily("ManropeSemibold")
        .FontSize(18)
        .CharacterSpacing(1)
        .Color(foreground)
        .Padding(padding)
        .MinimumHeight(CoffeeSpacing.ActionRowHeight)
        .Background(background)
        .CornerRadius(0)
        .Enabled(enabled)
        .AutomationId(automationId);

        return tile;
    }

    View ArchiveDialog() => new AlertDialog(
            _archiveDialogOpen,
            text: new Text($"Are you sure you want to archive '{_name.Value}'? This action cannot be undone.")
                .SecondaryText()
                .AutomationId("equipment_archive_message"),
            title: new Text("Archive Equipment?")
                .SubHeadline()
                .AutomationId("equipment_archive_title"),
            confirmButton: new Button("ARCHIVE", async () => await ArchiveAsync())
                .TextButton()
                .Color(CoffeeTheme.Error)
                .CornerRadius(0)
                .AutomationId("equipment_archive_confirm"),
            dismissButton: new Button("CANCEL", () => _archiveDialogOpen.Value = false)
                .TextButton()
                .Color(CoffeeTheme.PrimaryColor)
                .CornerRadius(0)
                .AutomationId("equipment_archive_cancel"))
        .AutomationId("equipment_archive_dialog");

}
