#nullable enable
using System;
using System.IO;
using System.Linq;
using Comet.Backend;
using Comet.Tests.Backend;
using CometBaristaNotes.Services;
using CometSamples.BaristaNotes.Components;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class DrinkPickerFeedbackContractTests
{
    static DrinkPickerFeedbackContractTests() =>
        ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

    [Theory]
    [InlineData("BrewMethod", false)]
    [InlineData("Bag", false)]
    [InlineData("DrinkType", false)]
    [InlineData("Rating", false)]
    [InlineData("Machine", false)]
    [InlineData("Grinder", false)]
    [InlineData("Temperature", false)]
    [InlineData("People", true)]
    [InlineData("Dose", true)]
    [InlineData("Yield", true)]
    [InlineData("Time", true)]
    [InlineData("Grind", true)]
    public void DonePolicy_OnlyCompositePickersRequireConfirmation(string kind, bool expected)
    {
        var policy = Slice(Page(), "static bool RequiresDone(", "static string PickerTitle(");
        var kinds = System.Text.RegularExpressions.Regex.Matches(policy, @"DrinkPickerKind\.(\w+)")
            .Select(match => match.Groups[1].Value);
        Assert.Equal(expected, kinds.Contains(kind));
    }

    [Fact]
    public void CategoricalPickers_UseNativeListCenteringRatherThanTopAlignedScrollContent()
    {
        var picker = Slice(Page(), "View CategoricalRows(", "static View CategoricalPickerRow(");
        Assert.Contains("items.FindIndex(choice => choice.Selected())", picker);
        Assert.Contains("new ListView<PickerChoice>", picker);
        Assert.Contains(".InitialScrollTo(selectedIndex, ListScrollPosition.Center)", picker);
        Assert.DoesNotContain("new ScrollView", picker);
    }

    [Fact]
    public void Temperature_TapCommitsButGrindKeepsThePendingValueAndCentersItsRow()
    {
        var picker = Slice(Page(), "View NumericPicker()", "View TimePicker(");
        var temperature = Slice(picker, "if (_picker.Value == DrinkPickerKind.Temperature)", "if (list is null)");
        Assert.Contains("_pickerValue.Value = value;", temperature);
        Assert.Contains("_pickerValueChanged.Value = true;", temperature);
        Assert.Contains("CommitPicker();", temperature);
        Assert.Contains("list.InitialScrollIndex = index;", picker);
        Assert.Contains("list.ScrollTo(index, ListScrollPosition.Center, animate: true);", picker);
        Assert.Contains("ScrollToAfterSelectionAsync(list, index)", picker);
    }

    [Fact]
    public void FahrenheitValues_RoundTripThroughCelsiusWithoutDuplicateDisplayRows()
    {
        for (var fahrenheit = 150m; fahrenheit <= 212m; fahrenheit += .1m)
        {
            var stored = TemperaturePickerConversion.ToCelsiusStorage(fahrenheit);
            Assert.Equal(fahrenheit, TemperaturePickerConversion.ToFahrenheitDisplay(stored));
        }

        var page = Page();
        Assert.Contains("TemperaturePickerConversion.ToCelsiusStorage(display)", page);
        Assert.Contains("TemperaturePickerConversion.ToFahrenheitDisplay(_temperatureC.Value.Value)", page);
    }

    [Fact]
    public void GrindRangeToggle_RebuildsAroundThePendingValueRatherThanTheEditorValue()
    {
        var page = Page();
        var rangeBar = Slice(page, "View RangeScopeBar(", "View GrindTranslationEntry()");
        Assert.Contains("using var hold = ReactiveScheduler.HoldFlushes();", rangeBar);
        Assert.Contains("NumericValues(_picker.Value, _pickerValue.Value)", rangeBar);
        var values = Slice(page, "List<decimal> NumericValues(", "string NumericValue(");
        Assert.Contains("var current = pendingValue ?? NumericOriginal(kind);", values);
        Assert.Contains(".Key($\"numeric_picker_{_picker.Value}_{_pickerShowsFullRange.Value}\")", page);
        Assert.Contains(".Key($\"{_picker.Value}_{automationPrefix}_{_pickerShowsFullRange.Value}\")", page);
    }

    [Fact]
    public void NestedGrinder_SelectionAndClearStageTheIdAndCloseReturnsToGrind()
    {
        var page = Page();
        var equipment = Slice(page, "View EquipmentSinglePicker(", "void OpenFullEquipmentCreator(");
        var select = Slice(page, "void SelectEquipment(", "void ClosePicker()");
        var nestedSelect = Slice(select, "if (kind == DrinkPickerKind.Grinder && _returnToGrind.Value)", "if (kind == DrinkPickerKind.Machine)");
        var close = Slice(page, "void ClosePicker()", "void DismissPicker()");
        var commit = Slice(page, "case DrinkPickerKind.Grind:", "case DrinkPickerKind.Temperature:");

        Assert.Contains("selectedId == item.Id ? null : item.Id", equipment);
        Assert.Contains("SelectEquipment(_picker.Value, null);", page);
        Assert.Contains("_pendingGrinderId.Value = equipmentId;", nestedSelect);
        Assert.DoesNotContain("_grinderId.Value =", nestedSelect);
        Assert.DoesNotContain("MarkDirty()", nestedSelect);
        Assert.Contains("_picker.Value = DrinkPickerKind.Grind;", close);
        Assert.DoesNotContain("_numericValues =", close);
        Assert.DoesNotContain("_pickerValueChanged.Value = false", close);
        Assert.Contains("_grinderId.Value = _pendingGrinderId.Value;", commit);
        Assert.Contains("_grind.Value = (int)CommittedNumericValue();", commit);
    }

    [Fact]
    public void Grind_ExistingGrinderCanBeChangedWithoutResettingTheMicronDraft()
    {
        var page = Page();
        var footer = Slice(page, "View GrindTranslationEntry()", "View AddCoffeeForm()");
        var open = Slice(footer, "void OpenGrinderFromGrind()", "}\n");
        Assert.Contains("item.Id == _pendingGrinderId.Value", footer);
        Assert.Contains("new Button(text, OpenGrinderFromGrind)", footer);
        Assert.Contains("_returnToGrind.Value = true;", open);
        Assert.Contains("_picker.Value = DrinkPickerKind.Grinder;", open);
        Assert.DoesNotContain("_pickerValue.Value =", open);
        Assert.DoesNotContain("_pickerShowsFullRange.Value =", open);
        Assert.Contains("\"grind_scale_help\"", footer);
    }

    [Theory]
    [InlineData("CommitVoiceAsync(", "return new VoiceToolResultDto(true, \"Drink updated.\"")]
    [InlineData("async void Save()", "ShowFeedback(\"Drink updated\"")]
    public void EditSave_ExplicitlyClearsEquipmentRemovedFromTheEditor(string start, string end)
    {
        var update = Slice(Page(), start, end);
        Assert.Contains("ClearMachine = !_machineId.Value.HasValue", update);
        Assert.Contains("ClearGrinder = !_grinderId.Value.HasValue", update);
    }

    [Fact]
    public void EmptyNestedGrinder_CreatorReturnsToTheSameLiveDraftOnlyAfterNormalPop()
    {
        var page = Page();
        var creator = Slice(page, "void OpenFullEquipmentCreator(", "View NumericPicker()");
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var pop = Slice(app, "void PopTracked()", "public void Reset()");
        var reset = Slice(app, "public void Reset()", "void SyncStacks()");

        Assert.Contains("afterReturn: returnToGrind ? () =>", creator);
        Assert.Contains("if (loadVersion != _loadVersion)", creator);
        Assert.Contains("if (actualType == EquipmentType.Grinder)", creator);
        Assert.Contains("_picker.Value = DrinkPickerKind.Grind;", creator);
        Assert.Contains("_navigation.BeginTransientSurface(DismissPicker);", creator);
        Assert.Contains("_afterReturn = null;", pop);
        Assert.Contains("afterReturn?.Invoke();", pop);
        Assert.Contains("_afterReturn = null;", reset);
        Assert.DoesNotContain("Invoke()", reset);
    }

    [Theory]
    [InlineData(402, 62)]
    [InlineData(427, 52)]
    public void TopInsetDivider_FillsOnlyTheInsetAndKeepsTheExactColumnCenter(double width, double height)
    {
        var strip = BaristaEdgeFrame.BuildTopInset(showDivider: true);
        CometBackendBridge.Materialize(
            strip,
            view => new FakeBackendNode(view.GetType().Name),
            new BackendContext(new EmptyServices()));
        CometBackendLayoutEngine.Layout(strip, new Size(width, height));
        var divider = Assert.Single(((IContainerView)strip).GetChildren());

        Assert.Equal(1, divider.Frame.Width, 3);
        Assert.Equal(width / 2, divider.Frame.X + divider.Frame.Width / 2, 3);
        Assert.Equal(0, divider.Frame.Y);
        Assert.Equal(height, divider.Frame.Height, 3);
    }

    [Fact]
    public void TopInsetDivider_IsAbsentOnPickerAndOtherSectionSurfaces()
    {
        var strip = BaristaEdgeFrame.BuildTopInset(showDivider: false);
        Assert.Empty(((IContainerView)strip).GetChildren());
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        Assert.Contains("showDivider: section == BaristaSection.NewDrink && !immersive", app);
    }

    static string Page() => Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

    static string Slice(string text, string start, string end)
    {
        var first = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Missing start: {start}");
        var last = text.IndexOf(end, first + start.Length, StringComparison.Ordinal);
        Assert.True(last > first, $"Missing end: {end}");
        return text[first..last];
    }

    static string Read(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(IOPath.Combine(directory.FullName, "global.json")) &&
                Directory.Exists(IOPath.Combine(directory.FullName, "sample")))
                return File.ReadAllText(IOPath.Combine(directory.FullName, relativePath));
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The Comet source root was not found.");
    }

    sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
