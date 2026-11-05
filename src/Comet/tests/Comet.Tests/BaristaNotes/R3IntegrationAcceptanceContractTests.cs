#nullable enable
using System;
using System.IO;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class R3IntegrationAcceptanceContractTests
{
    [Fact]
    public void ActivityRefresh_PullToRefreshAndShotMutationsReloadCountsAndFirstPage()
    {
        var activity = ReadSampleFile("Pages/ActivityFeedPage.cs");

        Assert.Contains("_services.DataChangeNotifier.DataChanged += OnDataChanged;", activity);
        Assert.Contains(".OnRefresh(() => ReloadAsync(isRefresh: true))", activity);
        Assert.Contains(
            "DataChangeType.ShotCreated or\n            DataChangeType.ShotUpdated or\n            DataChangeType.ShotDeleted",
            Normalize(activity));
        Assert.Contains(
            "ThreadHelper.RunOnMainThread(() => _ = ReloadAsync(isRefresh: true));",
            activity);
        Assert.Contains("var totalTask = _services.ShotService.GetShotHistoryAsync(0, 1);", activity);
        Assert.Contains("_pageIndex = 0;", activity);
        Assert.Contains("_filteredShotCount.Value = page.TotalCount;", activity);
        Assert.Contains("_totalShotCount.Value = total.TotalCount;", activity);
        Assert.Contains("_services.DataChangeNotifier.DataChanged -= OnDataChanged;", activity);
    }

    [Fact]
    public void RecipeRefresh_RendersEveryOutcomeAndPreservesPersistedRecipesOnFailure()
    {
        var bean = ReadSampleFile("Pages/BeanDetailPage.cs");

        Assert.Contains("var result = await _beanService.RefreshRecipesAsync(_beanId!.Value);", bean);
        Assert.Contains("case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Success:", bean);
        Assert.Contains("_recipes.Value = result.ToList();", bean);
        Assert.Contains("\"Recipes refreshed\"", bean);
        Assert.Contains("case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.NoMatch:", bean);
        Assert.Contains("\"No recipe sources matched\"", bean);
        Assert.Contains("case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Cancelled:", bean);
        Assert.Contains("\"Recipe refresh was cancelled. Existing recipes preserved.\"", bean);
        Assert.Contains("case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Unavailable:", bean);
        Assert.Contains("\"Recipe sourcing unavailable\"", bean);
        Assert.Contains("case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Failed:", bean);
        Assert.True(
            bean.Split("await LoadRecipesAsync();", StringSplitOptions.None).Length - 1 >= 4,
            "Every non-success recipe-refresh outcome must reload persisted recipes.");
        Assert.Contains("RecipeResultDialog(),", bean);
        Assert.Contains(".AutomationId(\"recipe_result_dialog\")", bean);
    }

    [Fact]
    public void AIAdvicePanel_UsesPersistedShotAndRendersAllTerminalStates()
    {
        var panel = ReadSampleFile("Components/AIAdvicePanel.cs");
        var app = ReadSampleFile("BaristaNotesApp.cs");

        Assert.Contains("BaristaServiceLocator.AIAdviceService", panel);
        Assert.Contains("AIAdviceRequestStatus.Loading => LoadingState()", panel);
        Assert.Contains("AIAdviceRequestStatus.Unavailable =>", panel);
        Assert.Contains("AIAdviceRequestStatus.Cancelled =>", panel);
        Assert.Contains("AIAdviceRequestStatus.Failed =>", panel);
        Assert.Contains("AIAdviceRequestStatus.Success => SuccessState()", panel);
        Assert.Contains(".AutomationId(\"ai_loading\")", panel);
        Assert.Contains("\"ai_unavailable\"", panel);
        Assert.Contains("\"ai_cancelled\"", panel);
        Assert.Contains("\"ai_failed\"", panel);
        Assert.Contains(".AutomationId(\"ai_success\")", panel);
        Assert.Contains("new Text(\"ADJUSTMENTS\").SectionLabel()", panel);
        Assert.Contains("new Text(\"REASONING\").SectionLabel()", panel);
        Assert.Contains("new Text(\"SOURCE\").SectionLabel()", panel);
        Assert.Contains("() => _navigation.EditingShotId", app);
        Assert.Contains("_aiAdvicePanel?.Dispose();", app);
        Assert.Contains("_aiAdvicePanel = null;", app);
    }

    [Fact]
    public void RoastDateTiles_UseBoundedNativeDatePickerForEveryCreationAndEditRoute()
    {
        var dateTile = ReadSampleFile("Components/BeanBagSourceComponents.cs");
        var shot = ReadSampleFile("Pages/ShotLoggingPage.cs");
        var bag = ReadSampleFile("Pages/BagDetailPage.cs");

        Assert.Contains(".OnTap(_ => _isOpen.Value = true)", dateTile);
        Assert.Contains("new DatePicker(_date, maximumDate: DateTime.Today)", dateTile);
        Assert.Contains("IsOpen = _isOpen", dateTile);
        Assert.Contains(".AutomationId($\"{_automationId}_native\")", dateTile);
        Assert.Contains("new BeanBagDateTile(_createRoastDate, \"create_coffee_roast_date\")", shot);
        Assert.Contains("new BeanBagDateTile(_createRoastDate, \"create_bag_roast_date\")", shot);
        Assert.Contains("new BeanBagDateTile(_roastDate, \"bag_roast_date\")", bag);
    }

    [Fact]
    public void ExternalLinks_RequireAbsoluteUrisAndRecipeSourcesUseCoordinator()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var bean = ReadSampleFile("Pages/BeanDetailPage.cs");

        Assert.Contains("!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)", app);
        Assert.Contains("if (ExternalLinkRequested is not null)", app);
        Assert.Contains("ExternalLinkRequested(uri);", app);
        Assert.Contains("Browser.Default.OpenAsync(", app);
        Assert.Contains("BrowserLaunchMode.SystemPreferred", app);
        Assert.Contains("_openExternal?.Invoke(url);", bean);
        Assert.Contains(".AutomationId(\"recipe_source_open\")", bean);
        Assert.Contains(".AutomationId(\"recipe_source_cancel\")", bean);
        Assert.Contains(".AutomationId(\"recipe_source_dialog\")", bean);
    }

    [Fact]
    public void NativeDialogs_AreOwnedByTheirPagesAndExposeRequiredActions()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var bean = ReadSampleFile("Pages/BeanDetailPage.cs");
        var bag = ReadSampleFile("Pages/BagDetailPage.cs");
        var range = ReadSampleFile("Pages/ValueRangeEditorPage.cs");

        Assert.Contains("RoomResultDialog(),", app);
        Assert.Contains("_navigation.RoomResultOpen.Value = false", app);

        Assert.Contains("DeleteDialog(),", bean);
        Assert.Contains("RecipeResultDialog(),", bean);
        Assert.Contains("SourceDialog(),", bean);
        Assert.Contains(".AutomationId(\"bean_delete_confirm\")", bean);
        Assert.Contains(".AutomationId(\"bean_delete_cancel\")", bean);

        Assert.Contains("DeleteDialog(),", bag);
        Assert.Contains("associated shot record(s) will remain in history", bag);
        Assert.Contains(".AutomationId(\"bag_delete_confirm\")", bag);
        Assert.Contains(".AutomationId(\"bag_delete_cancel\")", bag);

        Assert.Contains("DiscardDialog(),", range);
        Assert.Contains("RecommendedDialog(),", range);
        Assert.Contains(".AutomationId(\"RangeDiscardConfirm\")", range);
        Assert.Contains(".AutomationId(\"RangeDiscardKeepEditing\")", range);
        Assert.Contains(".AutomationId(\"RangeRecommendedConfirm\")", range);
        Assert.Contains(".AutomationId(\"RangeRecommendedCancel\")", range);
    }

    [Fact]
    public void VoiceBagRoute_ResolvesParentAndConstructsCompleteSettingsStack()
    {
        var parser = ReadSampleFile("Services/Voice/VoiceCommandParser.cs");
        var service = ReadSampleFile("Services/Voice/BaristaVoiceCommandService.cs");
        var callbacks = ReadSampleFile("Services/Voice/AppVoiceCallbacks.cs");
        var app = ReadSampleFile("BaristaNotesApp.cs");

        Assert.Contains("new VoiceNavigationRequest(\"bags\", EntityId: bagId)", parser);
        Assert.Contains("var bag = await _bags.GetBagByIdAsync(nav.EntityId.Value);", service);
        Assert.Contains("ParentEntityId = bag.BeanId", service);
        Assert.Contains("case \"bags\" when request.EntityId.HasValue && request.ParentEntityId.HasValue:", callbacks);
        Assert.Contains("_navigation.OpenBagManagement(", callbacks);
        Assert.Contains("public void OpenBagManagement(int bagId, int beanId, string beanName)", app);
        Assert.Contains("var management = new BeanManagementPage(", app);
        Assert.Contains("var bean = new BeanDetailPage(", app);
        Assert.Contains("var bag = new BagDetailPage(", app);
        Assert.Contains("SetSettingsNavigationDepth(4);", app);
    }

    [Fact]
    public void VoiceNavigation_UsesSharedDirtyEditorLeaveGuardBeforeConstructingRoutes()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var callbacks = ReadSampleFile("Services/Voice/AppVoiceCallbacks.cs");

        Assert.Contains(
            "public void RequestNavigation(Action confirmedNavigation)",
            app);
        Assert.Contains(
            "public async Task<VoiceNavigationResult> NavigateAsync(",
            callbacks);
        Assert.Contains("await _navigation.RequestNavigationAsync(", callbacks);
    }

    [Fact]
    public void AppTeardown_UnbindsVoiceBeforeDisposingAndClearingOwnedStore()
    {
        var app = ReadSampleFile("BaristaNotesApp.cs");
        var services = ReadSampleFile("Services/BaristaServices.cs");
        var integration = ReadSampleFile("Services/Voice/BaristaVoiceIntegration.cs");
        var locator = ReadSampleFile("Services/BaristaServiceLocator.cs");

        Assert.True(
            app.IndexOf("base.Dispose(disposing);", StringComparison.Ordinal)
            < app.LastIndexOf("DisposeOwnedServicesAsync(ownedServices, voiceCallbacks)", StringComparison.Ordinal));
        Assert.Contains("await BaristaVoiceIntegration.UnbindAsync(voiceCallbacks);", app);
        Assert.Contains("services.Dispose();", app);
        Assert.Contains("BaristaServiceLocator.Reset(Store);", services);
        Assert.Contains("Store.Dispose();", services);
        Assert.Contains("if (!ReferenceEquals(_store, expectedStore))", locator);
        Assert.Contains("_callbacks = null;", integration);
        Assert.Contains("Session = null;", integration);
    }

    static string ReadSampleFile(string relativePath)
    {
        var root = FindCometRoot();
        Assert.NotNull(root);
        return File.ReadAllText(IOPath.Combine(
            root!,
            "sample/Shared/BaristaNotes",
            relativePath));
    }

    static string Normalize(string value) => value.Replace("\r\n", "\n");

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 10 && directory is not null; index++)
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
