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
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

/// <summary>
/// Lists active equipment in the same flat, name-sorted order as the source app.
/// </summary>
public class EquipmentManagementPage : BaristaManagementPage
{
    readonly Action? _openNewDrink;
    readonly Action? _openActivity;
    readonly Action? _openSettings;
    readonly Signal<List<EquipmentDto>> _equipment = new(new());
    readonly Signal<bool> _isLoading = new(true);
    readonly Signal<string?> _error = new(null);

    IEquipmentService Service => BaristaServiceLocator.EquipmentService;

    public EquipmentManagementPage(
        Action? openNewDrink = null,
        Action? openActivity = null,
        Action? openSettings = null)
    {
        _openNewDrink = openNewDrink;
        _openActivity = openActivity;
        _openSettings = openSettings;
        _ = LoadAsync(showLoading: true);
    }

    async Task LoadAsync(bool showLoading)
    {
        if (showLoading)
            _isLoading.Value = true;
        _error.Value = null;

        try
        {
            var equipment = await Service.GetAllActiveEquipmentAsync();
            _equipment.Value = equipment
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id)
                .ToList();
        }
        catch (Exception ex)
        {
            _error.Value = ex.Message;
        }
        finally
        {
            _isLoading.Value = false;
        }
    }

    Task RefreshAfterMutationAsync() => LoadAsync(showLoading: false);

    void OpenDetail(int? equipmentId) =>
        NavigationView.Navigate(this, new EquipmentDetailPage(equipmentId, RefreshAfterMutationAsync));

    [Body]
    View body()
    {
        _ = CoffeeTheme.IsLight;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return BaristaEdgeFrame.Build(
            HeaderView(safeArea),
            BodyView(),
            BottomActionRow(),
            "equipment_management_page");
    }

    View HeaderView(Thickness safeArea)
    {
        var count = _equipment.Value.Count;
        return BaristaPageHeader.Build(
            "EQUIPMENT",
            count == 1 ? "1 item" : $"{count} items",
            safeArea,
            "equipment_header",
            titleAutomationId: "equipment_count");
    }

    View BodyView()
    {
        if (_isLoading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading equipment")
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("equipment_loading");

        if (_error.Value is { } error)
            return new ContentStateView(
                    ContentStateKind.Error,
                    "Equipment unavailable",
                    error,
                    () => _ = LoadAsync(showLoading: true))
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("equipment_error");

        if (_equipment.Value.Count == 0)
            return new ContentStateView(
                    ContentStateKind.Empty,
                    "NO EQUIPMENT",
                    "Add machines, grinders, and accessories")
                .Background(CoffeeTheme.SurfaceColor)
                .AutomationId("equipment_empty");

        return new ListView<EquipmentDto>(() => _equipment.Value)
        {
            ViewFor = EquipmentRow,
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId("equipment_list");
    }

    View EquipmentRow(EquipmentDto equipment) => ManagementListRow.Build(
        equipment.Type.ToString(),
        equipment.Name,
        $"equipment_row_{equipment.Id}",
        () => OpenDetail(equipment.Id));

    View BottomActionRow()
    {
        var padding = BaristaSafeAreaLayout.ActionPadding(this, CoffeeSpacing.M);
        return new Grid(
            columns: new object[] { "*", "*", "*", "*" },
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.Divider)
        {
            ManagementAction(
                    CoffeeIcons.Coffee,
                    "equipment_new_drink",
                    () => _openNewDrink?.Invoke(),
                    padding)
                .Cell(column: 0),
            ManagementAction(
                    CoffeeIcons.Feed,
                    "equipment_activity",
                    () => _openActivity?.Invoke(),
                    padding)
                .Cell(column: 1),
            ManagementAction(
                    CoffeeIcons.Settings,
                    "equipment_settings",
                    OpenSettings,
                    padding)
                .Cell(column: 2),
            ManagementAction(
                    CoffeeIcons.Add,
                    "equipment_add",
                    () => OpenDetail(null),
                    padding,
                    inverted: true)
                .Cell(column: 3),
        }
        .Background(CoffeeTheme.OutlineColor)
        .AutomationId("equipment_management_actions");
    }

    void OpenSettings()
    {
        if (_openSettings is not null)
            _openSettings();
        else
            NavigationView.Pop(this);
    }

    static View ManagementAction(
        string icon,
        string automationId,
        Action action,
        Thickness padding,
        bool inverted = false)
    {
        var background = inverted ? CoffeeTheme.TextPrimary : CoffeeTheme.SurfaceColor;
        var foreground = inverted ? CoffeeTheme.SurfaceColor : CoffeeTheme.TextPrimary;

        return new Grid
        {
            new Image(CoffeeIcons.Source(icon, foreground, 32))
                .Center(),
        }
        .Padding(padding)
        .MinimumHeight(CoffeeSpacing.ActionRowHeight)
        .Background(background)
        .AutomationId(automationId)
        .OnTap(_ => action());
    }
}
