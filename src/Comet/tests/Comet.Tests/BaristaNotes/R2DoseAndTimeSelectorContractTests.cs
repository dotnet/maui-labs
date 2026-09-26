#nullable enable
using System;
using System.IO;
using Comet.Backend;
using CometBaristaNotes.Services;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class R2DoseAndTimeSelectorContractTests
{
    [Fact]
    public void CenteredInitialScroll_RecordsSelectedIndexAndCenterPlacement()
    {
        var list = new ListView<int>(new[] { 10, 20, 30 })
            .InitialScrollTo(1, ListScrollPosition.Center);

        Assert.Equal(1, list.InitialScrollIndex);
        Assert.Equal(ListScrollPosition.Center, list.InitialScrollPosition);
    }

    [Fact]
    public void CenteredInitialScroll_ProvidesHalfViewportEndSpace()
    {
        var compose = Read("src/Comet/Platform/Compose/ComposeListNode.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");

        Assert.Contains("ListInitialScrollState.EdgeSpacing(", compose);
        Assert.Contains("top: new Dp((float)edgeSpacing)", compose);
        Assert.Contains("bottom: new Dp((float)edgeSpacing)", compose);
        Assert.Contains("ContentPadding = contentPadding", compose);
        Assert.Contains("targetExtent = GetRow(initialIndex).MeasureExtent(rowWidth).Height;", compose);
        Assert.Contains("_ = BuildRow(initialIndex);", compose);
        Assert.Contains("ListInitialScrollState.EdgeSpacing(", swiftUi);
        Assert.Contains("_initialScroll.TrySchedule(", swiftUi);
        Assert.Contains("_initialScroll.IsCurrent(initialPlan.Generation)", compose);
        Assert.Contains("generation != _ownerGeneration", swiftUi);
        Assert.Contains("var lastAppliedScrollToken: Int = 0", swiftUiShim);
        Assert.Contains("@Published var listUsesLazyVStack: Bool = false", swiftUiShim);
        Assert.Contains("applyPendingListScroll(node, proxy)", swiftUiShim);
        Assert.Contains("let applyScroll = {", swiftUiShim);
        Assert.Contains(
            "selectorChildID(node.children[node.listScrollTargetIndex])",
            swiftUiShim);
        Assert.Contains("@Published var listContentGeneration: Int = 0", swiftUiShim);
        Assert.Contains(".onChange(of: node.listContentGeneration)", swiftUiShim);
        Assert.Contains("CometSwiftUIHost.MarkListContentReady(_native);", swiftUi);
        Assert.Contains("CometSwiftUIHost.ResetListScrollReplay(_native);", swiftUi);
        Assert.Contains(".onAppear {", swiftUiShim);
    }

    [Fact]
    public void CenteredInitialScroll_RearmsAfterRetainedOwnerReplacement()
    {
        var compose = Read("src/Comet/Platform/Compose/ComposeListNode.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var composeOwnerChanged = Slice(
            compose,
            "public override void OnOwnerViewChanged(",
            "protected override void ApplyControlProperty(");
        var swiftUiRebuild = Slice(swiftUi, "void Rebuild()", "void LayoutRow(");
        var swiftUiOwnerChanged = Slice(
            swiftUi,
            "public void OnOwnerViewChanged(",
            "public void Dispose()");

        Assert.DoesNotContain("_initialScroll.Invalidate()", composeOwnerChanged);
        Assert.DoesNotContain("ResetListState(", composeOwnerChanged);
        Assert.Contains("composer.RememberLazyListState()", compose);
        Assert.Contains("composer.LaunchedEffect(2, async ct =>", compose);
        Assert.Contains("composer.LaunchedEffect(3, async ct =>", compose);
        Assert.DoesNotContain("_anchorBottomSeeded", compose);
        Assert.DoesNotContain("_listStateGeneration", compose);
        Assert.DoesNotContain("StopNativeScrollAsync(", compose);
        Assert.DoesNotContain("_initialScroll.CancelPending()", swiftUiRebuild);
        Assert.Contains("_initialScroll.Invalidate()", swiftUiOwnerChanged);
        Assert.Contains("!list.Horizontal", swiftUi);
        Assert.Contains("list.InitialScrollPosition == ListScrollPosition.Center", swiftUi);
        Assert.Contains("_initialScroll.Dispose()", compose);
        Assert.Contains("_initialScroll.Dispose()", swiftUi);
    }

    [Fact]
    public void CenteredInitialScroll_DefersWithoutConsumingZeroViewportRequest()
    {
        var state = new ListInitialScrollState();

        Assert.False(state.TrySchedule(
            viewportExtent: 0,
            itemCount: 3,
            requestedIndex: 1,
            ListScrollPosition.Center,
            targetExtent: 96,
            out _));
        Assert.False(state.IsScheduled);

        Assert.True(state.TrySchedule(
            viewportExtent: 640,
            itemCount: 3,
            requestedIndex: 1,
            ListScrollPosition.Center,
            targetExtent: 96,
            out var plan));
        Assert.True(state.IsScheduled);
        Assert.Equal(1, plan.Index);
    }

    [Fact]
    public void CenteredInitialScroll_UsesSelectedRowGeometry()
    {
        var selected = new ListInitialScrollState();
        var ordinary = new ListInitialScrollState();

        Assert.True(selected.TrySchedule(
            640, 5, 2, ListScrollPosition.Center, 96, out var selectedPlan));
        Assert.True(ordinary.TrySchedule(
            640, 5, 2, ListScrollPosition.Center, 72, out var ordinaryPlan));

        Assert.Equal(320, selectedPlan.EdgeSpacing);
        Assert.Equal(48, selectedPlan.TargetOffset);
        Assert.Equal(36, ordinaryPlan.TargetOffset);
    }

    [Fact]
    public void CenteredInitialScroll_IsOneShotAcrossRetainedOwnerUpdates()
    {
        var state = new ListInitialScrollState();

        Assert.True(state.TrySchedule(
            640, 20, 8, ListScrollPosition.Center, 96, out _));
        Assert.False(state.TrySchedule(
            640, 20, 9, ListScrollPosition.Center, 96, out _));

        state.Invalidate();

        Assert.True(state.TrySchedule(
            640, 20, 9, ListScrollPosition.Center, 96, out var replacementPlan));
        Assert.Equal(9, replacementPlan.Index);

        state.CancelPending();

        Assert.False(state.TrySchedule(
            640, 20, 10, ListScrollPosition.Center, 96, out _));
        Assert.False(state.IsCurrent(replacementPlan.Generation));
    }

    [Fact]
    public void DoseWholeAndTenthColumns_HaveIndependentInitialScrollState()
    {
        var whole = new ListInitialScrollState();
        var tenth = new ListInitialScrollState();

        Assert.True(whole.TrySchedule(
            640, 21, 8, ListScrollPosition.Center, 96, out var wholePlan));
        Assert.True(tenth.TrySchedule(
            640, 10, 5, ListScrollPosition.Center, 96, out var tenthPlan));

        Assert.Equal(8, wholePlan.Index);
        Assert.Equal(5, tenthPlan.Index);
    }

    [Fact]
    public void DoseAndTimeSelectors_ConfigureCenteredSelectedIndexes()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains(
            ".InitialScrollTo(selectedIndex, ListScrollPosition.Center)",
            page);
        Assert.Contains(
            ".InitialScrollTo(snappedIndex, ListScrollPosition.Center)",
            page);
        Assert.Contains(
            ".Key($\"numeric_picker_{_picker.Value}_{_pickerShowsFullRange.Value}\")",
            page);
        Assert.Contains(
            ".Key($\"{_picker.Value}_{automationPrefix}_{_pickerShowsFullRange.Value}\")",
            page);
        Assert.Contains(
            "var selectedIndex = values.FindIndex(value => value == selectedValue);",
            page);
        Assert.Contains(
            "var snappedIndex = _numericValues.FindIndex(value => value == _pickerValue.Value);",
            page);
        Assert.Contains("if (_picker.Value == DrinkPickerKind.Time)", page);
        Assert.Contains("View TimePicker(EffectiveDrinkValueRange effective, decimal original)", page);
        Assert.Contains("\"time_minute\"", page);
        Assert.Contains("\"time_second\"", page);
        Assert.Contains("var minuteValues = Enumerable.Range(0, lastMinute + 1).ToList();", page);
        Assert.Contains("var lastMinute = TimeWheelMaximumMinute(effective);", page);
        Assert.Contains("var secondValues = Enumerable.Range(0, 60).ToList();", page);
        Assert.Contains(
            "value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) / 60",
            page);
        Assert.Contains(
            "value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) % 60",
            page);
        Assert.Contains(
            "var pending = (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute);",
            page);
        Assert.Contains("var candidate = value * 60 + pending % 60;", page);
        Assert.Contains("SelectTimeValue(candidate);", page);
        Assert.Contains("var candidate = (pending / 60) * 60 + value;", page);
        Assert.DoesNotContain("if (effective.HardRange.Contains(candidate))", page);
        Assert.Contains("outsidePreferredRange: isPreferred is not null ? !isPreferred(value) : false", page);
        Assert.Contains("return effective.Range.Contains(value * 60 + pending % 60);", page);
        Assert.Contains("return effective.Range.Contains((pending / 60) * 60 + value);", page);
        Assert.DoesNotContain("var pending = current;", page);
        Assert.Contains("RangeScopeBar(effective, original, allowToggle: false)", page);
        Assert.Contains("rowHeight: 96", page);
        Assert.Contains(".Background(CoffeeTheme.SurfaceColor)", page);
    }

    [Fact]
    public void TimeSelector_AllowsMinuteOneAndSecondTwentyEightAsOneCombinedPendingValue()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var timePicker = Slice(page, "View TimePicker(", "View MassPicker(");

        Assert.Contains("var minuteValues = Enumerable.Range(0, lastMinute + 1).ToList();", timePicker);
        Assert.Contains("var secondValues = Enumerable.Range(0, 60).ToList();", timePicker);
        Assert.Contains("var candidate = value * 60 + pending % 60;", timePicker);
        Assert.Contains("var candidate = (pending / 60) * 60 + value;", timePicker);
        Assert.DoesNotContain("HardRange.Contains(candidate)", timePicker);
        Assert.DoesNotContain("ClampTime(", timePicker);
        Assert.Contains("void SelectTimeValue(int seconds)", page);
        Assert.Contains("_pickerValue.Value = seconds;", page);

        var combined = 1 * 60 + 28;
        Assert.Equal(88, combined);
        Assert.Equal(
            88m,
            NumericPickerCommitContract.Resolve(
                committedValue: 28m,
                pendingValue: combined,
                pendingValueChanged: true,
                dismissal: NumericPickerDismissal.Done));
    }

    [Fact]
    public void TimeSelector_OutOfRecommendedSecondsRemainSelectableAndStyledAsOutliers()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var timePicker = Slice(page, "View TimePicker(", "View MassPicker(");
        var row = Slice(page, "static View NumericPickerRow(", "static View PickerSectionLabel");
        var openPicker = Slice(page, "void OpenPicker(", "void OpenCreation(");

        Assert.Contains("return effective.Range.Contains(value * 60 + pending % 60);", timePicker);
        Assert.Contains("return effective.Range.Contains((pending / 60) * 60 + value);", timePicker);
        Assert.DoesNotContain("var pending = current;", timePicker);
        Assert.Contains("outsidePreferredRange: isPreferred is not null ? !isPreferred(value) : false", page);
        Assert.Contains("or DrinkPickerKind.Time", openPicker);
        Assert.Contains(".Color(selected", row);
        Assert.Contains("outsidePreferredRange", row);
        Assert.Contains("CoffeeTheme.TextSecondary", row);
    }

    [Fact]
    public void TimeSelector_StylesSelectedMinuteAndSecondFromTheSameCombinedPendingValue()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var timePicker = Slice(page, "View TimePicker(", "View MassPicker(");
        var commitPicker = Slice(page, "void CommitPicker()", "void ClearSingleEquipment()");

        Assert.Contains("var selectedMinutes = current / 60;", timePicker);
        Assert.Contains("var selectedSeconds = current % 60;", timePicker);
        Assert.Contains(
            "value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) / 60",
            timePicker);
        Assert.Contains(
            "value => value == (int)ClampTimeToWheelDomain(_pickerValue.Value, lastMinute) % 60",
            timePicker);
        Assert.DoesNotContain("value == current / 60", timePicker);
        Assert.DoesNotContain("value == current % 60", timePicker);
        Assert.DoesNotContain("var pending = current;", timePicker);
        Assert.Contains("SelectTimeValue(candidate);", timePicker);
        Assert.Contains("_expectedTime.Value = time;", commitPicker);
        Assert.Contains("_actualTime.Value = time;", commitPicker);
        Assert.Contains("NumericPickerDismissal.Done", page);
    }

    [Fact]
    public void TimePicker_UsesRecommendedRangeWordingAndRepresentableDomainValidation()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var rangeBar = Slice(page, "View RangeScopeBar(", "View GrindTranslationEntry(");
        var validation = Slice(page, "string? Validate()", "void PersistLastSelections()");

        Assert.Contains("Current value is outside your recommended range.", rangeBar);
        Assert.Contains("Showing the full recommended range.", rangeBar);
        Assert.Contains("Showing your recommended range.", rangeBar);
        Assert.DoesNotContain("allowed range", rangeBar);
        Assert.Contains("var maximumTime = TimeWheelMaximumSeconds(time);", validation);
        Assert.DoesNotContain("time.HardRange.Contains(_expectedTime.Value)", validation);
        Assert.DoesNotContain("time.HardRange.Contains(_actualTime.Value.Value)", validation);
    }

    [Fact]
    public void CenteredSelectorLists_RequestNativeCenterSnapping()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var list = Read("src/Comet/Controls/ListView.cs");
        var compose = Read("src/Comet/Platform/Compose/ComposeListNode.cs");
        var lazyColumn = Read("src/vendor/Microsoft.AndroidX.Compose/LazyColumn.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");

        Assert.Contains("SnapToCenter = true", page);
        Assert.Contains("bool SnapToCenter { get; }", list);
        Assert.Contains("SnapToCenter = _list.SnapToCenter", compose);
        Assert.Contains("SnapToCenter", lazyColumn);
        Assert.Contains("RememberSnapFlingBehavior", lazyColumn);
        Assert.Contains("snapcenter", swiftUi);
        Assert.Contains(".scrollTargetLayout()", swiftUiShim);
        Assert.Contains(".scrollTargetBehavior(.viewAligned)", swiftUiShim);
    }

    [Fact]
    public void SwiftUiCenteredSelectors_UseScrollViewReaderCentering()
    {
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");

        Assert.Contains("ScrollViewReader { proxy in", swiftUiShim);
        Assert.Contains("proxy.scrollTo(", swiftUiShim);
        Assert.Contains("selectorChildID(node.children[node.listScrollTargetIndex])", swiftUiShim);
        Assert.Contains(
            "withAnimation(.easeInOut(duration: 0.35))",
            swiftUiShim);
        Assert.DoesNotContain("CenteredInitialScrollModifier", swiftUiShim);
        Assert.Contains(
            "enabled: node.listSnapsToCenter",
            swiftUiShim);
        Assert.Contains(
            "private func centeredSelectorStack(_ node: CometNode)",
            swiftUiShim);
        Assert.Contains("Color.clear", swiftUiShim);
        Assert.Contains(".frame(height: node.listEndSpacing)", swiftUiShim);
        Assert.Contains(
            "if node.listEndSpacing > 0",
            swiftUiShim);
        Assert.DoesNotContain(
            ".padding(.vertical, node.listEndSpacing)",
            swiftUiShim);
    }

    [Fact]
    public void SelectorValueTap_RequestsAnimatedCentering()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var list = Read("src/Comet/Controls/ListView.cs");
        var compose = Read("src/Comet/Platform/Compose/ComposeListNode.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");

        Assert.Contains("list.ScrollTo(index, ListScrollPosition.Center, animate: true);", page);
        Assert.Contains("void RegisterScrollTo(System.Action<int, ListScrollPosition, bool> scrollTo);", list);
        Assert.Contains("captured.AnimateScrollToItemAsync(index, scrollOffset)", compose);
        Assert.Contains("CometSwiftUIHost.ScrollAnimated", swiftUi);
        Assert.Contains("UsesRetainedSafeLazyStack", swiftUi);
        Assert.Contains("DispatchQueue.main.async", swiftUiShim);
        Assert.Contains("withAnimation(.easeInOut(duration: 0.35))", swiftUiShim);
        Assert.Contains("ScrollToAfterSelectionAsync", page);
        Assert.Contains("await Task.Yield();", page);
        Assert.Contains("ThreadHelper.RunOnMainThread", page);
        Assert.Contains(
            "_ = ScrollToAfterSelectionAsync(",
            page);
    }

    [Fact]
    public void SwiftUiList_HidesNativeScrollSurfaceOnTheListBranch()
    {
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");
        var listBranch = Slice(
            swiftUiShim,
            "case \"list\":",
            "case \"scroll\":");

        // Keep this contract scoped to the vertical List branch. A file-wide search can
        // accidentally pass on the same modifiers used by TextEditorChromeModifier while
        // the selector's native List still owns an opaque system background.
        Assert.Contains("List {", listBranch);
        Assert.Contains("if node.listUsesLazyVStack", listBranch);
        Assert.Contains("centeredSelectorStack(node)", listBranch);
        Assert.Contains("ScrollView(.vertical, showsIndicators: false)", listBranch);
        Assert.Contains(".listRowBackground(Color.clear)", listBranch);
        Assert.Contains(".scrollContentBackground(.hidden)", listBranch);
        Assert.Contains(".background(Color.clear)", listBranch);
    }

    [Fact]
    public void SwiftUiList_UsesEachRowSurfaceForTheNativeRowHost()
    {
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");
        var listBranch = Slice(
            swiftUiShim,
            "case \"list\":",
            "case \"scroll\":");
        var rowSurface = Slice(
            listBranch,
            "child.backgroundARGB == 0",
            ".id(child.id)");

        // The List row host must carry the Comet row's surface. Using Color.clear here
        // exposes SwiftUI's default white row host around the numeric selector content.
        Assert.Contains("? Color.clear", rowSurface);
        Assert.Contains(": argbColor(child.backgroundARGB)", rowSurface);
        Assert.DoesNotContain(".listRowBackground(Color.clear)", rowSurface);
    }

    [Fact]
    public void TimeSelectorSecondWheel_RebuildsAllRowsThroughNativeListChildren()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");
        var timePicker = Slice(page, "View TimePicker(", "View MassPicker(");
        var centeredColumn = Slice(
            page,
            "ListView<int> CenteredMassColumn(",
            "static decimal ClampMass");
        var rebuild = Slice(swiftUi, "void Rebuild()", "void LayoutRow(");
        var listBranch = Slice(swiftUiShim, "case \"list\":", "case \"scroll\":");

        Assert.Contains("var secondValues = Enumerable.Range(0, 60).ToList();", timePicker);
        Assert.Contains("\"time_second\"", timePicker);
        Assert.Contains("SelectTimeValue", timePicker);
        Assert.Contains("_ = ScrollToAfterSelectionAsync(", centeredColumn);
        Assert.Contains(
            "list.ScrollTo(index, ListScrollPosition.Center, animate: true)",
            centeredColumn);
        Assert.Contains("int count = _list.Sections() > 0 ? _list.Rows(0) : 0;", rebuild);
        Assert.Contains("for (int i = 0; i < count; i++)", rebuild);
        Assert.Contains("CometSwiftUIHost.InsertChild(_native, i, node.Native);", rebuild);
        Assert.Contains("ForEach(node.children) { child in", listBranch);
        Assert.Contains(".id(child.id)", listBranch);
    }

    [Fact]
    public void TimeSelectorSecondWheel_UsesTheSelectedTextStyleContract()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var timePicker = Slice(page, "View TimePicker(", "View MassPicker(");
        var centeredColumn = Slice(
            page,
            "ListView<int> CenteredMassColumn(",
            "static decimal ClampMass");
        var row = Slice(page, "static View NumericPickerRow(", "static View PickerSectionLabel");

        Assert.Contains("var candidate = (pending / 60) * 60 + value;", timePicker);
        Assert.Contains("value.ToString(\"00\")", timePicker);
        Assert.Contains("selectedSeconds", timePicker);
        Assert.Contains("selectedMinutes", timePicker);
        Assert.Contains("select(value);", centeredColumn);
        Assert.Contains("ScrollToAfterSelectionAsync", centeredColumn);
        Assert.Contains(
            ".FontFamily(selected ? \"ManropeSemibold\" : \"Manrope\")",
            row);
        Assert.Contains(".FontSize(selected ? 56 : 32)", row);
        Assert.Contains(
            ".Color(selected",
            row);
        Assert.Contains("outsidePreferredRange", row);
        Assert.Contains("? CoffeeTheme.TextSecondary.WithAlpha(.72f)", row);
        Assert.Contains(".Background(CoffeeTheme.SurfaceColor)", row);
        Assert.DoesNotContain("CoffeeTheme.Warning", row);
        Assert.DoesNotContain("CoffeeTheme.SurfaceVariant", row);
        Assert.DoesNotContain(".Opacity(", row);
    }

    [Fact]
    public void NativePickerSelection_UsesIndexedCenteredScrollOnBothBackends()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var compose = Read("src/Comet/Platform/Compose/ComposeListNode.cs");
        var swiftUi = Read("src/Comet/Platform/SwiftUI/SwiftUIListNode.cs");
        var swiftUiShim = Read(
            "src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");
        var centeredColumn = Slice(
            page,
            "ListView<int> CenteredMassColumn(",
            "static decimal ClampMass");

        Assert.Contains("values.FindIndex(candidate => candidate == value)", centeredColumn);
        Assert.Contains("ScrollToAfterSelectionAsync", centeredColumn);
        Assert.Contains(
            "list.ScrollTo(index, ListScrollPosition.Center, animate: true)",
            centeredColumn);
        Assert.Contains(
            "ListScrollPosition.Center ? scrollOffset : 0",
            compose);
        Assert.Contains(
            "captured.AnimateScrollToItemAsync(index, scrollOffset)",
            compose);
        Assert.Contains(
            "? targetExtent / 2",
            compose);
        Assert.Contains(
            "_centerContentPadding is not null",
            compose);
        Assert.Contains(
            "CometSwiftUIHost.ScrollAnimated(_native, index, (nint)position, animate)",
            swiftUi);
        Assert.Contains(
            "node.listScrollPosition == 1 ? .center : .top",
            swiftUiShim);
    }

    [Fact]
    public void ActivityEdit_PassesTheTappedShotIntoTheDetailShell()
    {
        var activity = Read("sample/Shared/BaristaNotes/Pages/ActivityFeedPage.cs");
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains("Action<ShotRecordDto>? _openShot", activity);
        Assert.Contains("_openShot?.Invoke(shot)", activity);
        Assert.Contains("shot => _navigation.OpenShotEditor(shot)", app);
        Assert.Contains("public void OpenShotEditor(ShotRecordDto shot)", app);
        Assert.Contains("ShotRecordDto? initialShot = null", page);
        Assert.Contains("if (initialShot is null)", page);
    }

    [Fact]
    public void ActivityEdit_DoesNotStartARedundantShotReloadAfterWarmNavigation()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var constructor = Slice(
            page,
            "public ShotLoggingPage(",
            "public int? EditingShotId");

        Assert.Contains("if (initialShot is not null)", constructor);
        Assert.Contains("HydrateForEdit(initialShot)", constructor);
        Assert.Contains("_loading.Value = false", constructor);
        Assert.Contains("if (initialShot is null)", constructor);
        Assert.DoesNotContain(
            "_ = LoadAsync(showLoading: initialShot is null);",
            constructor);
    }

    [Fact]
    public void ActivityEdit_ReusesTheRenderedRootEditorAndResetsItForNewDrinks()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var openEditor = Slice(
            app,
            "void OpenShotEditorCore(",
            "public void OpenEquipmentCreator(");

        Assert.Contains(
            "if (initialShot is not null && _rootShotEditor is { } rootShotEditor)",
            openEditor);
        Assert.Contains("rootShotEditor.BeginEdit(initialShot);", openEditor);
        Assert.DoesNotContain(
            "_activeShotEditor = new ShotLoggingPage(_services, shotId, this, initialShot);",
            openEditor[..openEditor.IndexOf("if (initialShot is not null", StringComparison.Ordinal)]);
        Assert.Contains("public void BeginEdit(ShotRecordDto shot)", page);
        Assert.Contains("HydrateForEdit(shot);", page);
        Assert.Contains("readonly Signal<int> _editorStateVersion", page);
        Assert.Contains("_ = _editorStateVersion.Value;", page);
        Assert.Contains("_editorStateVersion.Value++;", page);
        Assert.Contains("public void ResetForNewDrink()", page);
        Assert.Contains("_ = LoadAsync(showLoading: false);", page);
        Assert.Contains("_rootShotEditor!.ResetForNewDrink();", app);
        Assert.Contains(
            "rootShotEditor.RequestLeave(confirmedNavigation);",
            app);
    }

    [Fact]
    public void ActivityEdit_UsesTheReusedRootEditorIdentityAndSystemBackRoute()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains(
            "public int? EditingShotId => _activeShotEditor?.EditingShotId",
            app);
        Assert.Contains("?? _rootShotEditor?.EditingShotId;", app);
        Assert.Contains("public bool IsRootShotEditorActive(ShotLoggingPage page)", app);
        Assert.Contains("public void RequestShotEditorBack(ShotLoggingPage page)", app);
        Assert.Contains("page.RequestLeave(() => CloseShotEditor(page));", app);
        Assert.Contains(
            "() => _picker.Value != DrinkPickerKind.None",
            page);
        Assert.Contains("|| _navigation.IsRootShotEditorActive(this)", page);
        Assert.Contains("_backCommand.RaiseCanExecuteChanged();", page);
        Assert.Contains("if (_picker.Value != DrinkPickerKind.None)", page);
        Assert.Contains("ClosePicker();", page);
        Assert.Contains("_navigation.RequestShotEditorBack(this);", page);
    }

    [Fact]
    public void ActivityEdit_DevFlowBackPrefersTheRenderedNavigationOwner()
    {
        var navigation = Read("src/Comet/Controls/NavigationView.cs");
        var devFlow = Read("src/Comet/DevTools/CometDevAgent.DevFlow.cs");

        Assert.Contains(
            "internal bool HasActiveBackendCallbacks => CurrentViewProvider is not null;",
            navigation);
        var requestBack = Slice(
            devFlow,
            "static bool RequestBack()",
            "\n\t\t}\n\t}\n}");

        Assert.Contains("nav.HasActiveBackendCallbacks", requestBack);
        Assert.Contains("nav.RequestBack();", requestBack);
        Assert.Contains(
            "// The registry can retain navigation roots that are no longer attached",
            requestBack);
    }

    [Fact]
    public void ActivityEdit_CancelsDeferredVoiceWhenLeavingNewDrink()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var voice = Read("sample/Shared/BaristaNotes/Services/Voice/BaristaVoiceIntegration.cs");

        Assert.Contains(
            "BaristaVoiceIntegration.CancelPendingNewDrinkActivation();",
            app);
        Assert.Contains(
            "public static void CancelPendingNewDrinkActivation()",
            voice);
        Assert.Contains(
            "_activateOnNextNewDrinkMount = false;",
            voice);
    }

    [Fact]
    public void ActivityEdit_BatchesNavigationStateChangesIntoOneReactiveFlush()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var openEditor = Slice(
            app,
            "void OpenShotEditorCore(",
            "public void OpenEquipmentCreator(");

        Assert.Contains(
            "using var hold = ReactiveScheduler.HoldFlushes();",
            openEditor);
    }

    [Fact]
    public void ActivityEdit_InvalidatesPendingNewDrinkActivationBeforeChangingSection()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var openEditor = Slice(
            app,
            "void OpenShotEditorCore(",
            "public void OpenEquipmentCreator(");

        Assert.Contains(
            "Interlocked.Increment(ref _sectionActivationVersion);",
            openEditor);
    }

    [Fact]
    public void ActivityEdit_InvalidatesAnInFlightNewDrinkLoadBeforeHydratingTheTappedShot()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains("Interlocked.Increment(ref _loadVersion);", page);
        Assert.Contains("var loadVersion = Volatile.Read(ref _loadVersion);", page);
        Assert.Contains(
            "if (loadVersion != Volatile.Read(ref _loadVersion))",
            page);
        Assert.Contains("HydrateForEdit(shot);", page);
    }

    [Fact]
    public void ActivityEdit_SkipsDelayedNewDrinkActivationUnlessVoiceWasRequested()
    {
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var voice = Read("sample/Shared/BaristaNotes/Services/Voice/BaristaVoiceIntegration.cs");
        var switchTo = Slice(
            app,
            "public void SwitchTo(",
            "public void RequestSectionSwitch(");

        Assert.Contains(
            "if (activateVoice || BaristaVoiceIntegration.HasPendingNewDrinkActivation)",
            switchTo);
        Assert.Contains(
            "_ = ActivateNewDrinkAfterTransitionAsync(activationVersion, activateVoice);",
            switchTo);
        Assert.Contains("public static bool HasPendingNewDrinkActivation", voice);
    }

    public static TheoryData<NumericPickerDismissal, decimal, decimal, bool, decimal>
        PendingCommitCases => new()
        {
            { NumericPickerDismissal.Close, 18m, 19.5m, true, 18m },
            { NumericPickerDismissal.Done, 18m, 19.5m, true, 19.5m },
            { NumericPickerDismissal.Done, 18m, 19.5m, false, 18m },
        };

    [Theory]
    [MemberData(nameof(PendingCommitCases))]
    public void PendingValue_CommitsOnlyForChangedDone(
        NumericPickerDismissal dismissal,
        decimal committed,
        decimal pending,
        bool changed,
        decimal expected)
    {
        var resolved = NumericPickerCommitContract.Resolve(
            committed,
            pending,
            changed,
            dismissal);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void DoseAndTime_UsePendingValueUntilDoneWhileCloseOnlyResetsPickerState()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var openPicker = Slice(page, "void OpenPicker(", "void OpenCreation(");
        var commitPicker = Slice(page, "void CommitPicker()", "void ClearSingleEquipment()");
        var closePicker = Slice(page, "void ClosePicker()", "void ApplyMethod(");

        Assert.Contains("_pickerValue.Value = NumericOriginal(kind);", openPicker);
        Assert.Contains("_pickerValueChanged.Value = true;", page);
        Assert.Contains("case DrinkPickerKind.Dose:", commitPicker);
        Assert.Contains("_dose.Value = CommittedNumericValue();", commitPicker);
        Assert.Contains("case DrinkPickerKind.Time:", commitPicker);
        Assert.Contains("_actualTime.Value = time;", commitPicker);
        Assert.Contains("_expectedTime.Value = time;", commitPicker);
        Assert.DoesNotContain("_dose.Value =", closePicker);
        Assert.DoesNotContain("_actualTime.Value =", closePicker);
        Assert.DoesNotContain("_expectedTime.Value =", closePicker);
    }

    [Fact]
    public void UnchangedDone_ResolvesToTheOriginalCommittedValue()
    {
        var page = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains(
            "NumericPickerCommitContract.Resolve(",
            page);
        Assert.Contains(
            "NumericPickerDismissal.Done",
            page);
        Assert.Equal(
            28m,
            NumericPickerCommitContract.Resolve(
                committedValue: 28m,
                pendingValue: 32m,
                pendingValueChanged: false,
                dismissal: NumericPickerDismissal.Done));
    }

    static string Slice(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker after {start}: {end}");
        return source[startIndex..endIndex];
    }

    static string Read(string relativePath)
    {
        var root = FindCometRoot();
        Assert.NotNull(root);
        var path = IOPath.Combine(root!, relativePath);
        Assert.True(File.Exists(path), $"Missing source file: {path}");
        return File.ReadAllText(path);
    }

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var depth = 0; depth < 12 && directory is not null; depth++)
        {
            if (File.Exists(IOPath.Combine(directory, "global.json"))
                && Directory.Exists(IOPath.Combine(directory, "sample")))
            {
                return directory;
            }

            directory = IOPath.GetDirectoryName(directory);
        }

        return null;
    }
}
