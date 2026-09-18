#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using Xunit;

namespace Comet.Tests.BaristaNotes;

public sealed class BaristaNotesUiStateRegressionTests
{
    [Fact]
    public void SourceForms_ExplicitlyOwnBorderlessNativeInputChrome()
    {
        var modifiers = ReadSampleFile("Styles/CoffeeModifiers.cs");
        var bean = ReadSampleFile("Pages/BeanDetailPage.cs");
        var equipment = ReadSampleFile("Pages/EquipmentDetailPage.cs");

        Assert.Contains("SourceInputChrome(this TextField field)", modifiers);
        Assert.Contains("SourceInputChrome(this TextEditor editor)", modifiers);
        Assert.Contains("field.Borderless()", modifiers);
        Assert.Contains("editor.Borderless()", modifiers);
        Assert.Contains("new BeanTextField(value, placeholder, onChanged))", bean);
        Assert.Equal(2, bean.Split(".SourceInputChrome()", StringSplitOptions.None).Length - 1);
        Assert.Equal(2, equipment.Split(".SourceInputChrome()", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [InlineData("Pages/BeanDetailPage.cs")]
    [InlineData("Pages/BagDetailPage.cs")]
    [InlineData("Pages/EquipmentDetailPage.cs")]
    [InlineData("Pages/ProfileDetailPage.cs")]
    public void SourceDetailPages_OwnHiddenNativeNavigationChrome(string relativePath)
    {
        var page = ReadSampleFile(relativePath);

        Assert.Contains("this.BackButtonBehavior(new BackButtonBehavior", page);
        Assert.Contains("IsVisible = false", page);
    }

    [Fact]
    public void ActivityVoice_FirstSectionTransitionConsumesDeferredActivation()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var callbacks = ReadSampleFile("Services/Voice/AppVoiceCallbacks.cs");

        Assert.Contains("BaristaVoiceIntegration.RequestNewDrinkActivation();", app);
        Assert.Contains(
            "SwitchTo(BaristaSection.NewDrink, activateVoice: true);",
            app);
        Assert.Contains(
            "_ = ActivateNewDrinkAfterTransitionAsync(activationVersion, activateVoice);",
            app);
        Assert.Contains("await Task.Delay(1).ConfigureAwait(false);", app);
        Assert.Contains(
            "var deferredActivation = BaristaVoiceIntegration.NotifyNewDrinkMounted();",
            app);
        Assert.Contains("if (!activateVoice && !deferredActivation)", app);
        Assert.Contains("VoiceVisible.Value = true;", app);
        Assert.Contains("VoiceCollapsed.Value = false;", app);
        Assert.Contains(
            "if (_navigation.Section.Value != BaristaSection.NewDrink)",
            callbacks);
        Assert.DoesNotContain("session.StartAsync()", callbacks);
    }

    [Fact]
    public void VoiceOverlay_ReexpandRematerializesFullWidthActionableSurface()
    {
        var components = ReadSampleFile("Components/SourceComponents.cs");

        Assert.Contains("if (!isVisible)", components);
        Assert.Contains(
            "return new Grid().Frame(width: 0, height: 0);",
            components);
        Assert.Contains("var collapsedSurface = new Grid", components);
        Assert.Contains(".Opacity(isVisible && isCollapsed ? 1 : 0)", components);
        Assert.Contains(".IsEnabled(isVisible && isCollapsed)", components);
        Assert.Contains(".Opacity(isVisible && !isCollapsed ? 1 : 0)", components);
        Assert.Contains(".IsEnabled(isVisible && !isCollapsed)", components);
        Assert.Contains("96 + CoffeeSpacing.VoiceControlBottomClearance", components);
        Assert.Contains("CoffeeSpacing.VoiceControlBottomClearance", components);
        Assert.Contains(".Cell(row: 1)", components);
        Assert.Contains(
            ".Frame(height: BaristaSourceVisualContract.VoicePanelHeight + CoffeeSpacing.VoiceOverlayBottomSafeArea)",
            components);
        Assert.Contains(
            "#if IOS || __IOS__",
            ReadSampleFile("Styles/CoffeeSpacing.cs"));
        Assert.Contains(".Bottom()", components);
        Assert.Contains(".FillHorizontal()", components);
        Assert.Contains(
            ".Alignment(Comet.Alignment.BottomTrailing)",
            components);
        Assert.Contains(".AutomationId(\"voice_close\")", components);
        Assert.Contains(".AutomationId(\"voice_expand\")", components);
        Assert.Contains(".AutomationId(\"voice_push_to_talk\")", components);
        Assert.Contains("GestureStatus.Started", components);
        Assert.Contains("GestureStatus.Completed", components);
        Assert.Contains("GestureStatus.Canceled", components);
    }

    [Fact]
    public void DirtyShotEditor_BottomSectionNavigationRequiresConfirmedLeave()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var page = ReadSampleFile("Pages/ShotLoggingPage.cs");

        Assert.Contains(
            "editor.RequestLeave(confirmedNavigation);",
            app);
        Assert.Contains(
            "_navigation.RequestSectionSwitch(BaristaSection.Activity)",
            app);
        Assert.Contains(
            "_navigation.RequestSectionSwitch(BaristaSection.Settings)",
            app);
        Assert.Contains("public void RequestLeave(Action confirmedLeave)", page);
        Assert.Contains("_leaveGuard.Begin(_dirty.Value, confirmedLeave)", page);
    }

    [Fact]
    public async Task EditorLeaveGuard_DirtyRequestWaitsForExplicitDiscard()
    {
        var guard = new EditorLeaveGuard();
        var confirmed = 0;

        var request = guard.Begin(true, () => confirmed++);

        Assert.True(request.RequiresConfirmation);
        Assert.False(request.Completion.IsCompleted);
        Assert.Equal(0, confirmed);

        Assert.True(guard.Confirm(request.Generation, () => { }));

        Assert.Equal(1, confirmed);
        Assert.Equal(EditorLeaveOutcome.Left, await request.Completion);
    }

    [Fact]
    public async Task EditorLeaveGuard_KeepEditingCancelsPendingSectionSwitch()
    {
        var guard = new EditorLeaveGuard();
        var confirmed = 0;

        var request = guard.Begin(true, () => confirmed++);

        Assert.True(guard.KeepEditing(request.Generation));

        Assert.Equal(0, confirmed);
        Assert.Equal(EditorLeaveOutcome.KeptEditing, await request.Completion);
        Assert.False(guard.Confirm(request.Generation, () => { }));
    }

    [Fact]
    public async Task EditorLeaveGuard_CleanRequestProceedsImmediately()
    {
        var guard = new EditorLeaveGuard();
        var confirmed = 0;

        var request = guard.Begin(false, () => confirmed++);

        Assert.False(request.RequiresConfirmation);
        Assert.Equal(1, confirmed);
        Assert.Equal(EditorLeaveOutcome.Left, await request.Completion);
    }

    [Fact]
    public async Task EditorLeaveGuard_CancelBeforeDiscard_MakesStaleDiscardNoOp()
    {
        var guard = new EditorLeaveGuard();
        using var cancellation = new CancellationTokenSource();
        var dirty = true;
        var navigated = 0;
        var request = guard.Begin(
            true,
            () => navigated++,
            cancellation.Token);

        cancellation.Cancel();

        Assert.Equal(EditorLeaveOutcome.Cancelled, await request.Completion);
        Assert.False(guard.Confirm(
            request.Generation,
            () => dirty = false));
        Assert.True(dirty);
        Assert.Equal(0, navigated);
    }

    [Fact]
    public async Task EditorLeaveGuard_StaleActionsCannotResolveReplacementRequest()
    {
        var guard = new EditorLeaveGuard();
        var firstNavigation = 0;
        var secondNavigation = 0;
        var first = guard.Begin(true, () => firstNavigation++);
        var second = guard.Begin(true, () => secondNavigation++);

        Assert.Equal(EditorLeaveOutcome.KeptEditing, await first.Completion);
        Assert.False(guard.Confirm(first.Generation, () => { }));
        Assert.False(guard.KeepEditing(first.Generation));
        Assert.False(second.Completion.IsCompleted);

        Assert.True(guard.Confirm(second.Generation, () => { }));
        Assert.Equal(EditorLeaveOutcome.Left, await second.Completion);
        Assert.Equal(0, firstNavigation);
        Assert.Equal(1, secondNavigation);
    }

    [Fact]
    public void NewDrink_RefreshesTemperaturePreferenceOnSectionActivation()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var services = ReadSampleFile("Services/BaristaServices.cs");
        var page = ReadSampleFile("Pages/ShotLoggingPage.cs");

        Assert.Contains("ActiveShotEditor?.RefreshForActivation();", app);
        Assert.Contains(
            "TemperatureUnit = new Signal<TemperatureUnit>(Preferences.GetTemperatureUnit());",
            services);
        Assert.Contains("public void RefreshForActivation()", page);
        Assert.Contains("_temperatureUnit = services.TemperatureUnit;", page);
        Assert.Contains("_services.Preferences.GetTemperatureUnit()", page);
        Assert.Contains("_ = _temperatureUnit.Value;", page);
    }

    [Fact]
    public void BeanValidation_BlankNamePersistsUntilNameIsCorrected()
    {
        Assert.Equal(
            BeanFormValidation.NameRequired,
            BeanFormValidation.Validate("", "", "", "", ""));
        Assert.Equal(
            BeanFormValidation.NameRequired,
            BeanFormValidation.Validate("   ", "", "", "", ""));
        Assert.Null(BeanFormValidation.Validate("Ethiopia", "", "", "", ""));

        var page = ReadSampleFile("Pages/BeanDetailPage.cs");
        Assert.Contains("_ = _error.Value;", page);
        Assert.Contains("new BeanTextField(value, placeholder, onChanged)", page);
        Assert.Contains("_onChanged(text);", page);
        Assert.Contains(".AutomationId(\"bean_error\")", page);
    }

    [Fact]
    public void ValueRangeErrorAndResetStateRefreshImmediately()
    {
        var editor = ReadSampleFile("Pages/ValueRangeEditorPage.cs");
        var settings = ReadSampleFile("Pages/ValueRangeSettingsPage.cs");

        Assert.Contains("_ = _error.Value;", editor);
        Assert.Contains("\"Minimum must be less than maximum.\"", ReadSampleFile(
            "Services/ValueRangeEditorValidation.cs"));
        Assert.Contains("public override void ViewDidAppear()", settings);
        Assert.Contains("Reload();", settings);
        Assert.Contains(".AutomationId(\"RangeResetAll\")", settings);
        Assert.Contains(".AutomationId(\"RangeResetConfirm\")", settings);
        Assert.Contains(".AutomationId(\"RangeResetCancel\")", settings);

        var service = new InMemoryDrinkValueRangeService(
            new InMemoryPreferencesService());
        service.SetMode(DrinkValueMetric.DoseIn, ValueRangeMode.Custom);
        service.SaveOverride(
            DrinkValueMetric.DoseIn,
            BrewMethod.Espresso,
            20,
            25);
        Assert.Single(service.GetSettings().Overrides);

        service.ResetOverrides(DrinkValueMetric.DoseIn);
        Assert.Empty(service.GetSettings().Overrides);
        Assert.Equal(
            ValueRangeSource.AutoFallback,
            service.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);

        service.SetMode(DrinkValueMetric.DoseIn, ValueRangeMode.Auto);
        Assert.Equal(
            ValueRangeSource.Auto,
            service.Resolve(DrinkValueMetric.DoseIn, BrewMethod.Espresso).Source);
    }

    [Fact]
    public void ActionModalPanel_FillsWidthWhileRemainingBottomAligned()
    {
        var components = ReadSampleFile("Components/SourceComponents.cs");

        Assert.Contains(
            ".Bottom()\n        .FillHorizontal();",
            components.Replace("\r\n", "\n"));
    }

    [Fact]
    public void EquipmentManagement_UsesCoordinatorRouteAndReleasesSettingsStack()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var settings = ReadSampleFile("Pages/SettingsPage.cs");

        Assert.Contains("readonly Action _openEquipment;", settings);
        Assert.Contains("_openEquipment)", settings);
        Assert.Contains(
            "() => _navigation.OpenEquipmentManagement(),",
            app);
        Assert.Contains(
            "previousSection == BaristaSection.Settings || section == BaristaSection.Settings",
            app);
        Assert.Contains("_resetSettingsNavigation();", app);
    }

    static string ReadSampleFile(string relativePath)
    {
        var root = FindCometRoot();
        Assert.NotNull(root);
        return File.ReadAllText(System.IO.Path.Combine(
            root!,
            "sample/Shared/BaristaNotes",
            relativePath));
    }

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 10 && directory is not null; index++)
        {
            if (File.Exists(System.IO.Path.Combine(directory, "global.json"))
                && Directory.Exists(System.IO.Path.Combine(directory, "sample")))
            {
                return directory;
            }
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return null;
    }
}
