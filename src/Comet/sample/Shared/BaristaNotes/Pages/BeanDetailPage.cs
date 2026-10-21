#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using CometBaristaNotes.Services.Grind;

namespace CometSamples.BaristaNotes.Pages;

public class BeanDetailPage : View
{
    const int ShotPageSize = 20;

    readonly int? _beanId;
    readonly IBeanService _beanService;
    readonly IBagService _bagService;
    readonly IDataChangeNotifier _notifier;
    readonly IShotService? _shotService;
    readonly IRecipeService? _recipeService;
    readonly BaristaServices? _services;
    readonly Action<int>? _openShot;
    readonly Action<string>? _openExternal;
    readonly Action? _onExit;

    readonly Signal<string> _name = new(string.Empty);
    readonly Signal<string> _roaster = new(string.Empty);
    readonly Signal<string> _origin = new(string.Empty);
    readonly Signal<string> _notes = new(string.Empty);
    readonly Signal<string> _roasterUrl = new(string.Empty);
    readonly Signal<List<BagSummaryDto>> _bags = new(new());
    readonly Signal<List<ShotRecordDto>> _shots = new(new());
    readonly Signal<List<RecipeDto>> _recipes = new(new());
    readonly Signal<Dictionary<int, GrindTranslationResult>> _grindTranslations = new(new());
    readonly Signal<RatingAggregateDto?> _rating = new(null);

    readonly Signal<bool> _isLoading = new(false);
    readonly Signal<bool> _isSaving = new(false);
    readonly Signal<bool> _isLoadingBags = new(false);
    readonly Signal<bool> _isLoadingShots = new(false);
    readonly Signal<bool> _isLoadingRecipes = new(false);
    readonly Signal<bool> _isRefreshingRecipes = new(false);
    readonly Signal<bool> _isTranslatingGrinds = new(false);
    readonly Signal<bool> _hasMoreShots = new(false);
    readonly Signal<string?> _error = new(null);
    readonly Signal<string?> _shotError = new(null);
    readonly Signal<string?> _recipeError = new(null);

    readonly Signal<bool> _deleteDialogOpen = new(false);
    readonly Signal<bool> _recipeResultOpen = new(false);
    readonly Signal<string> _recipeResultTitle = new(string.Empty);
    readonly Signal<string> _recipeResultMessage = new(string.Empty);
    readonly Signal<bool> _sourceDialogOpen = new(false);
    readonly Signal<string> _sourceTitle = new(string.Empty);
    readonly Signal<string> _sourceUrl = new(string.Empty);

    int _shotPageIndex;
    CancellationTokenSource? _grindTranslationCts;
    bool _subscribed;
    bool _exitNotified;

    bool IsEdit => _beanId is > 0;

    public BeanDetailPage(int? beanId)
        : this(
            beanId,
            BaristaServiceLocator.BeanService,
            BaristaServiceLocator.BagService,
            BaristaServiceLocator.DataChangeNotifier,
            null,
            null,
            null,
            null,
            null,
            null)
    {
    }

    public BeanDetailPage(
        int? beanId,
        BaristaServices services,
        Action<int>? openShot = null,
        Action<string>? openExternal = null,
        Action? onExit = null)
        : this(
            beanId,
            services.BeanService,
            services.BagService,
            services.DataChangeNotifier,
            services.ShotService,
            new InMemoryRecipeService(services.Store),
            services,
            openShot,
            openExternal,
            onExit)
    {
    }

    internal BeanDetailPage(
        int? beanId,
        IBeanService beanService,
        IBagService bagService,
        IDataChangeNotifier notifier,
        IShotService? shotService,
        IRecipeService? recipeService,
        BaristaServices? services,
        Action<int>? openShot,
        Action<string>? openExternal,
        Action? onExit)
    {
        _beanId = beanId;
        _beanService = beanService;
        _bagService = bagService;
        _notifier = notifier;
        _shotService = shotService;
        _recipeService = recipeService;
        _services = services;
        _openShot = openShot;
        _openExternal = openExternal;
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
        if (!IsEdit)
            return;

        if (e.ChangeType is DataChangeType.BagCreated or DataChangeType.BagUpdated)
            _ = LoadBagsAsync();
        else if (e.ChangeType is DataChangeType.ShotCreated
            or DataChangeType.ShotUpdated
            or DataChangeType.ShotDeleted)
        {
            _ = LoadShotsAsync();
            _ = LoadRatingAsync();
        }
    }

    async Task LoadAsync()
    {
        try
        {
            var bean = await _beanService.GetBeanWithRatingsAsync(_beanId!.Value);
            if (bean is null)
            {
                _error.Value = "Bean not found";
                return;
            }

            _name.Value = bean.Name;
            _roaster.Value = bean.Roaster ?? string.Empty;
            _origin.Value = bean.Origin ?? string.Empty;
            _notes.Value = bean.Notes ?? string.Empty;
            _roasterUrl.Value = bean.RoasterUrl ?? string.Empty;
            _rating.Value = bean.RatingAggregate;

            await Task.WhenAll(LoadBagsAsync(), LoadShotsAsync(), LoadRecipesAsync());
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to load bean: {ex.Message}";
        }
        finally
        {
            _isLoading.Value = false;
        }
    }

    async Task LoadBagsAsync()
    {
        if (!IsEdit || _isLoadingBags.Value)
            return;

        try
        {
            _isLoadingBags.Value = true;
            _bags.Value = await _bagService.GetBagSummariesForBeanAsync(_beanId!.Value, includeCompleted: true);
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to load bags: {ex.Message}";
        }
        finally
        {
            _isLoadingBags.Value = false;
        }
    }

    async Task LoadShotsAsync()
    {
        if (!IsEdit || _isLoadingShots.Value)
            return;

        if (_shotService is null)
        {
            _shotError.Value = "Shot history is unavailable on this route.";
            return;
        }

        try
        {
            _isLoadingShots.Value = true;
            _shotError.Value = null;
            var page = await _shotService.GetShotHistoryByBeanAsync(_beanId!.Value, 0, ShotPageSize);
            _shots.Value = page.Items;
            _hasMoreShots.Value = page.HasNextPage;
            _shotPageIndex = 1;
        }
        catch (Exception ex)
        {
            _shotError.Value = $"Failed to load shots: {ex.Message}";
        }
        finally
        {
            _isLoadingShots.Value = false;
        }
    }

    async Task LoadMoreShotsAsync()
    {
        if (_shotService is null || _isLoadingShots.Value || !_hasMoreShots.Value)
            return;

        try
        {
            _isLoadingShots.Value = true;
            var page = await _shotService.GetShotHistoryByBeanAsync(_beanId!.Value, _shotPageIndex, ShotPageSize);
            _shots.Value = new List<ShotRecordDto>(_shots.Value.Concat(page.Items));
            _hasMoreShots.Value = page.HasNextPage;
            _shotPageIndex++;
        }
        catch (Exception ex)
        {
            _shotError.Value = $"Failed to load more shots: {ex.Message}";
        }
        finally
        {
            _isLoadingShots.Value = false;
        }
    }

    async Task LoadRatingAsync()
    {
        if (!IsEdit)
            return;

        try
        {
            var bean = await _beanService.GetBeanWithRatingsAsync(_beanId!.Value);
            if (bean is not null)
                _rating.Value = bean.RatingAggregate;
        }
        catch
        {
        }
    }

    async Task LoadRecipesAsync()
    {
        if (!IsEdit || _isLoadingRecipes.Value)
            return;

        if (_recipeService is null)
        {
            _recipeError.Value = "Saved recipes are unavailable on this route.";
            return;
        }

        try
        {
            _isLoadingRecipes.Value = true;
            _recipeError.Value = null;
            _recipes.Value = (await _recipeService.GetRecipesForBeanAsync(_beanId!.Value)).ToList();
            _ = TranslateRecipeGrindsAsync();
        }
        catch (Exception ex)
        {
            _recipeError.Value = $"Failed to load recipes: {ex.Message}";
        }
        finally
        {
            _isLoadingRecipes.Value = false;
        }
    }

    async Task RefreshRecipesAsync()
    {
        if (!IsEdit || _isRefreshingRecipes.Value)
            return;

        try
        {
            _isRefreshingRecipes.Value = true;
            _recipeError.Value = null;
            var result = await _beanService.RefreshRecipesAsync(_beanId!.Value);

            switch (result.Status)
            {
                case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Success:
                    _recipes.Value = result.ToList();
                    _ = TranslateRecipeGrindsAsync();
                    ShowRecipeResult(
                        result.Count == 0 ? "No recipes found" : "Recipes refreshed",
                        result.Count == 0
                            ? "No recipes were found for this bean yet."
                            : $"Refreshed {result.Count} recipe(s).");
                    break;

                case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.NoMatch:
                    // No adapters matched — keep existing persisted recipes
                    await LoadRecipesAsync();
                    ShowRecipeResult(
                        "No recipe sources matched",
                        "No roaster adapters matched this bean. Existing recipes preserved.");
                    break;

                case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Cancelled:
                    await LoadRecipesAsync();
                    ShowRecipeResult("Cancelled", "Recipe refresh was cancelled. Existing recipes preserved.");
                    break;

                case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Unavailable:
                    await LoadRecipesAsync();
                    ShowRecipeResult(
                        "Recipe sourcing unavailable",
                        result.ErrorMessage ?? "Recipe sourcing is not configured. Existing recipes preserved.");
                    break;

                case CometBaristaNotes.Services.Recipes.RecipeSourcingStatus.Failed:
                default:
                    await LoadRecipesAsync();
                    ShowRecipeResult(
                        "Recipe refresh failed",
                        result.ErrorMessage ?? "Could not refresh recipes. Existing recipes preserved.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _recipeError.Value = $"Failed to refresh recipes: {ex.Message}";
            ShowRecipeResult("Recipe refresh failed", ex.Message);
        }
        finally
        {
            _isRefreshingRecipes.Value = false;
        }
    }

    async Task TranslateRecipeGrindsAsync()
    {
        _grindTranslationCts?.Cancel();
        _grindTranslationCts?.Dispose();
        var cts = new CancellationTokenSource();
        _grindTranslationCts = cts;
        var cancellationToken = cts.Token;

        if (_services is null)
        {
            _grindTranslations.Value = new();
            return;
        }

        try
        {
            _isTranslatingGrinds.Value = true;
            var grinders = await _services.EquipmentService.GetEquipmentByTypeAsync(EquipmentType.Grinder);
            cancellationToken.ThrowIfCancellationRequested();
            var grinder = grinders.FirstOrDefault(item => item.IsActive);
            if (grinder is null)
            {
                _grindTranslations.Value = new();
                return;
            }

            var translations = new Dictionary<int, GrindTranslationResult>();
            foreach (var recipe in _recipes.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(recipe.GrindHint))
                    continue;
                var translation = await BaristaServiceLocator.GrindTranslationService.TranslateAsync(
                    new GrindTranslationRequest(
                        grinder.Id,
                        grinder.Name,
                        recipe.GrindHint!,
                        recipe.BrewMethod,
                        _beanId),
                    cancellationToken);
                translations[recipe.Id] = translation;
            }
            cancellationToken.ThrowIfCancellationRequested();
            _grindTranslations.Value = translations;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                _recipeError.Value = $"Failed to translate recipe grinds: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_grindTranslationCts, cts))
                _isTranslatingGrinds.Value = false;
        }
    }

    bool Validate()
    {
        var validation = BeanFormValidation.Validate(
            _name.Value,
            _roaster.Value,
            _origin.Value,
            _notes.Value,
            _roasterUrl.Value);
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
            if (IsEdit)
            {
                await _beanService.UpdateBeanAsync(_beanId!.Value, new UpdateBeanDto
                {
                    Name = _name.Value.Trim(),
                    Roaster = _roaster.Value.Trim(),
                    Origin = _origin.Value.Trim(),
                    Notes = _notes.Value.Trim(),
                    RoasterUrl = _roasterUrl.Value.Trim(),
                });
            }
            else
            {
                var created = await _beanService.CreateBeanAsync(new CreateBeanDto
                {
                    Name = _name.Value.Trim(),
                    Roaster = Optional(_roaster.Value),
                    Origin = Optional(_origin.Value),
                    Notes = Optional(_notes.Value),
                    RoasterUrl = Optional(_roasterUrl.Value),
                });
                if (!created.Success || created.Data is null)
                {
                    _error.Value = created.ErrorMessage ?? "Failed to create bean";
                    return;
                }

                await _bagService.CreateNewBagForBeanAsync(created.Data.Id, DateTime.Today);
            }

            Close();
        }
        catch (Exception ex)
        {
            _error.Value = $"Failed to save: {ex.Message}";
        }
        finally
        {
            _isSaving.Value = false;
        }
    }

    async Task DeleteAsync()
    {
        if (!IsEdit)
            return;

        try
        {
            await _beanService.DeleteBeanAsync(_beanId!.Value);
            _notifier.NotifyDataChanged(DataChangeType.BeanUpdated, _beanId.Value);
            _deleteDialogOpen.Value = false;
            Close();
        }
        catch (Exception ex)
        {
            _deleteDialogOpen.Value = false;
            _error.Value = $"Failed to delete bean: {ex.Message}";
        }
    }

    static string? Optional(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [Body]
    View body()
    {
        _ = _error.Value;
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);

        return new Grid
        {
            BaristaEdgeFrame.Build(
                new BeanBagHeaderTile(
                    IsEdit ? "EDIT BEAN" : "NEW BEAN",
                    () => IsEdit
                        ? (string.IsNullOrWhiteSpace(_name.Value) ? "Loading…" : _name.Value)
                        : "Add bean",
                    safeArea),
                RenderBody(),
                BottomActions(),
                "bean_detail_frame"),
            DeleteDialog(),
            RecipeResultDialog(),
            SourceDialog(),
        }.AutomationId(IsEdit ? "bean_detail_page" : "bean_create_page");
    }

    View RenderBody()
    {
        if (_isLoading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading bean")
                .Background(CoffeeTheme.SurfaceColor);

        var sections = BaristaSections.Create(
            EntryTile("NAME", _name, "Bean name (required)", "bean_name", 100, UpdateName),
            EntryTile("ROASTER", _roaster, "Roaster name", "bean_roaster"),
            EntryTile("ORIGIN", _origin, "Country or region", "bean_origin"),
            EntryTile("ROASTER URL", _roasterUrl, "https://… (where to reorder these beans)", "bean_url"),
            NotesTile());

        if (IsEdit)
        {
            sections.Add(RatingsSection());
            sections.Add(RecipesSection());
            sections.Add(BagsSection());
            sections.Add(ShotHistorySection());
        }
        if (_error.Value is { } error)
            sections.Add(ErrorTile(error));
        sections.Add(new Grid().Background(CoffeeTheme.SurfaceColor).MinimumHeight(CoffeeSpacing.L));

        return BaristaSections.Scroll(sections);
    }

    View EntryTile(
        string label,
        Signal<string> value,
        string placeholder,
        string automationId,
        float height = 90,
        Action<string>? onChanged = null) =>
        new VStack(spacing: CoffeeSpacing.XS)
        {
            new Text(label).SectionLabel(),
            (onChanged is null
                ? SignalExtensions.TextField(value, placeholder)
                : new BeanTextField(value, placeholder, onChanged))
                .SourceInputChrome()
                .SourceText()
                .AutomationId(automationId),
        }
        .Padding(new Thickness(CoffeeSpacing.M, 14))
        .Background(CoffeeTheme.SurfaceColor)
        .MinimumHeight(height);

    void UpdateName(string value)
    {
        _name.Value = value;
        if (_error.Value == BeanFormValidation.NameRequired
            && !string.IsNullOrWhiteSpace(value))
        {
            _error.Value = null;
        }
    }

    View NotesTile() => new VStack(spacing: CoffeeSpacing.XS)
    {
        new Text("NOTES").SectionLabel(),
        _notes.Value.Length == 0
            ? new Text("Tasting notes, processing method…").MutedText()
            : new Grid().Frame(height: 0),
        SignalExtensions.TextEditor(_notes)
            .SourceInputChrome()
            .SourceText()
            .Frame(height: 100)
            .AutomationId("bean_notes"),
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor)
    .MinimumHeight(150);

    View RatingsSection() => Section(
        "RATINGS",
        new BeanBagRatingSummary(() => _rating.Value, "bean_rating_summary"));

    View RecipesSection()
    {
        var content = new VStack(spacing: CoffeeSpacing.S)
        {
            new Grid(columns: new object[] { "*", "Auto" })
            {
                new Text("RECIPES").SectionLabel().Center().Cell(column: 0),
                new Button(
                    _isRefreshingRecipes.Value
                        ? "…"
                        : _recipes.Value.Count == 0 ? "FIND" : "REFRESH",
                    () => _ = RefreshRecipesAsync())
                    .TextButton()
                    .Color(CoffeeTheme.PrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("recipes_refresh")
                    .Cell(column: 1),
            },
        };

        if (_recipeError.Value is { } error)
        {
            content.Add(new VStack(spacing: CoffeeSpacing.XS)
            {
                new Text(error).Color(CoffeeTheme.Error).FontSize(13),
                _recipeService is null
                    ? new Grid().Frame(height: 0)
                    : new Button("TRY AGAIN", () => _ = LoadRecipesAsync())
                        .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0),
            });
        }
        else if (_isLoadingRecipes.Value && _recipes.Value.Count == 0)
        {
            content.Add(new ContentStateView(ContentStateKind.Loading, "Loading recipes"));
        }
        else if (_recipes.Value.Count == 0)
        {
            content.Add(new VStack(spacing: CoffeeSpacing.XS)
            {
                new Text("No recipes yet.").SecondaryText(),
                new Text("Tap FIND to look up brew guides.").MutedText(),
            });
        }
        else
        {
            foreach (var recipe in _recipes.Value)
                content.Add(RecipeRow(recipe));
        }

        return content
            .Padding(new Thickness(CoffeeSpacing.M, 14))
            .Background(CoffeeTheme.SurfaceColor);
    }

    View RecipeRow(RecipeDto recipe)
    {
        var source = recipe.Source switch
        {
            RecipeSource.RoasterSite => "ROASTER",
            RecipeSource.AIGenerated => "AI",
            RecipeSource.Manual => "CUSTOM",
            _ => recipe.Source.ToString().ToUpperInvariant(),
        };
        var row = new VStack(spacing: CoffeeSpacing.XS)
        {
            new Grid(columns: new object[] { "*", "Auto", "Auto" }, columnSpacing: CoffeeSpacing.S)
            {
                new Text(recipe.BrewMethod.DisplayName())
                    .FontFamily("ManropeSemibold").FontSize(16).Color(CoffeeTheme.TextPrimary)
                    .Cell(column: 0),
                new Text(source).SectionLabel().Cell(column: 1),
                recipe.IsEditedByUser
                    ? new Text("EDITED").SectionLabel().Cell(column: 2)
                    : new Grid().Frame(width: 0).Cell(column: 2),
            },
        };
        if (!string.IsNullOrWhiteSpace(recipe.Title))
            row.Add(new Text(recipe.Title!).SecondaryText());
        row.Add(new Text(FormatRecipeParameters(recipe))
            .FontFamily("ManropeSemibold").FontSize(13).Color(CoffeeTheme.TextPrimary));
        if (!string.IsNullOrWhiteSpace(recipe.GrindHint))
            row.Add(GrindTranslationRow(recipe));
        if (!string.IsNullOrWhiteSpace(recipe.Notes))
            row.Add(new Text(recipe.Notes!).SecondaryText().MaxLines(4));
        if (!string.IsNullOrWhiteSpace(recipe.SourceUrl))
        {
            row.Add(new Button("VIEW SOURCE →", () => ShowSource(recipe))
                .TextButton()
                .Color(CoffeeTheme.PrimaryColor)
                .CornerRadius(0)
                .AutomationId($"recipe_source_{recipe.Id}"));
        }

        return row
            .Padding(new Thickness(12))
            .Background(CoffeeTheme.SurfaceVariant)
            .AutomationId($"recipe_{recipe.Id}");
    }

    View GrindTranslationRow(RecipeDto recipe)
    {
        _grindTranslations.Value.TryGetValue(recipe.Id, out var translation);
        var translated = translation is not null
            ? (View)new VStack(spacing: 2)
            {
                new Text(FormatGrindSetting(translation))
                    .FontFamily("ManropeSemibold")
                    .FontSize(12)
                    .Color(CoffeeTheme.TextPrimary),
                new Text(GrindSourceLabel(translation.Source)).SectionLabel(),
            }
            : _isTranslatingGrinds.Value && _services is not null
                ? new Text("Translating grind…").MutedText()
                : new Grid().Frame(width: 0);

        return new Grid(
            columns: new object[] { "*", "Auto" },
            rows: new object[] { "Auto" },
            columnSpacing: CoffeeSpacing.S)
        {
            new Text($"Grind: {recipe.GrindHint}").SecondaryText().Cell(column: 0),
            translated.Cell(column: 1),
        }.AutomationId($"recipe_grind_{recipe.Id}");
    }

    static string FormatGrindSetting(GrindTranslationResult result)
    {
        if (result.SuggestedSetting.HasValue
            && result.MinSetting.HasValue
            && result.MaxSetting.HasValue
            && result.MinSetting != result.MaxSetting)
        {
            return $"On {result.GrinderModel}: {result.MinSetting:0.#}–{result.MaxSetting:0.#} (try {result.SuggestedSetting:0.#})";
        }
        if (result.SuggestedSetting.HasValue)
            return $"On {result.GrinderModel}: {result.SuggestedSetting:0.#}";
        if (result.MinSetting.HasValue && result.MaxSetting.HasValue)
            return $"On {result.GrinderModel}: {result.MinSetting:0.#}–{result.MaxSetting:0.#}";
        return result.Explanation ?? "No translated grinder setting";
    }

    static string GrindSourceLabel(GrindTranslationSource source) => source switch
    {
        GrindTranslationSource.UserHistory => "FROM YOUR HISTORY",
        GrindTranslationSource.Deterministic => "CALCULATED",
        GrindTranslationSource.Cache => "KNOWN MATCH",
        GrindTranslationSource.AI => "AI",
        _ => "ESTIMATE",
    };

    static string FormatRecipeParameters(RecipeDto recipe)
    {
        var parts = new List<string>();
        if (recipe.DoseIn.HasValue) parts.Add($"{recipe.DoseIn:0.#}g in");
        if (recipe.OutputAmount.HasValue) parts.Add($"{recipe.OutputAmount:0.#}g out");
        if (recipe.TotalTimeSeconds.HasValue)
        {
            var seconds = recipe.TotalTimeSeconds.Value;
            parts.Add(seconds >= 60
                ? $"{(int)(seconds / 60)}:{(int)(seconds % 60):D2}"
                : $"{seconds:0}s");
        }
        if (recipe.BrewTempC.HasValue) parts.Add($"{recipe.BrewTempC:0.#}°C");
        return parts.Count == 0 ? "No parameters captured." : string.Join(" · ", parts);
    }

    View BagsSection()
    {
        var content = new VStack(spacing: CoffeeSpacing.S)
        {
            new Grid(columns: new object[] { "*", "Auto" })
            {
                new Text("BAGS").SectionLabel().Center().Cell(column: 0),
                new Button("+ BAG", OpenNewBag)
                    .TextButton()
                    .Color(CoffeeTheme.PrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("bean_add_bag")
                    .Cell(column: 1),
            },
        };

        if (_isLoadingBags.Value)
            content.Add(new ContentStateView(ContentStateKind.Loading, "Loading bags"));
        else if (_bags.Value.Count == 0)
            content.Add(new Text("No bags added yet").SecondaryText());
        else
        {
            foreach (var bag in _bags.Value)
                content.Add(BagRow(bag));
        }

        return content
            .Padding(new Thickness(CoffeeSpacing.M, 14))
            .Background(CoffeeTheme.SurfaceColor);
    }

    View BagRow(BagSummaryDto bag)
    {
        var row = new VStack(spacing: CoffeeSpacing.XS)
        {
            new Grid(columns: new object[] { "*", "Auto" })
            {
                new Text($"Roasted {bag.FormattedRoastDate}")
                    .FontFamily("ManropeSemibold").FontSize(16).Color(CoffeeTheme.TextPrimary)
                    .Cell(column: 0),
                new Text(bag.StatusBadge.ToUpperInvariant()).SectionLabel().Cell(column: 1),
            },
        };
        if (!string.IsNullOrWhiteSpace(bag.Notes))
            row.Add(new Text(bag.Notes!).SecondaryText().MaxLines(3));
        row.Add(new HStack(spacing: CoffeeSpacing.M)
        {
            new Text($"{bag.ShotCount} shots").MutedText(),
            new Text(bag.AverageRating.HasValue ? bag.FormattedRating : "no ratings").MutedText(),
        });

        return row
            .Padding(new Thickness(12))
            .Background(CoffeeTheme.SurfaceVariant)
            .AutomationId($"bag_row_{bag.Id}")
            .OnTap(_ => OpenBag(bag.Id));
    }

    View ShotHistorySection()
    {
        var content = new VStack(spacing: CoffeeSpacing.S)
        {
            new Text("SHOT HISTORY").SectionLabel(),
        };
        if (_shotError.Value is { } error)
        {
            content.Add(new VStack(spacing: CoffeeSpacing.XS)
            {
                new Text(error).Color(CoffeeTheme.Error).FontSize(13),
                _shotService is null
                    ? new Grid().Frame(height: 0)
                    : new Button("RETRY", () => _ = LoadShotsAsync())
                        .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0),
            });
        }
        else if (_isLoadingShots.Value && _shots.Value.Count == 0)
        {
            content.Add(new ContentStateView(ContentStateKind.Loading, "Loading shots"));
        }
        else if (_shots.Value.Count == 0)
        {
            content.Add(new Text("No shots recorded with this bean yet").SecondaryText());
        }
        else
        {
            foreach (var shot in _shots.Value)
                content.Add(new BeanBagShotRow(shot, _openShot));
            if (_hasMoreShots.Value)
            {
                content.Add(new Button(_isLoadingShots.Value ? "LOADING…" : "LOAD MORE", () => _ = LoadMoreShotsAsync())
                    .TextButton()
                    .Color(CoffeeTheme.PrimaryColor)
                    .CornerRadius(0)
                    .AutomationId("bean_shots_load_more"));
            }
        }

        return content
            .Padding(new Thickness(CoffeeSpacing.M, 14))
            .Background(CoffeeTheme.SurfaceColor);
    }

    static View Section(string label, View content) => new VStack(spacing: CoffeeSpacing.S)
    {
        new Text(label).SectionLabel(),
        content,
    }
    .Padding(new Thickness(CoffeeSpacing.M, 14))
    .Background(CoffeeTheme.SurfaceColor);

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
    .AutomationId("bean_error");

    View BottomActions() => IsEdit
        ? new BeanBagActionRow(
            new BeanBagAction("CANCEL", "bean_cancel", Close),
            new BeanBagAction("DELETE", "bean_delete", () => _deleteDialogOpen.Value = true, Danger: true),
            new BeanBagAction(
                _isSaving.Value ? "SAVING…" : "SAVE",
                "bean_save",
                () => _ = SaveAsync(),
                Primary: true))
        : new BeanBagActionRow(
            new BeanBagAction("CANCEL", "bean_cancel", Close),
            new BeanBagAction(
                _isSaving.Value ? "SAVING…" : "ADD",
                "bean_save",
                () => _ = SaveAsync(),
                Primary: true));

    void OpenNewBag() => OpenBagCore(null);

    void OpenBag(int bagId) => OpenBagCore(bagId);

    void OpenBagCore(int? bagId)
    {
        var page = _services is not null
            ? new BagDetailPage(_beanId!.Value, _name.Value, bagId, _services)
            : new BagDetailPage(
                _beanId!.Value,
                _name.Value,
                bagId,
                _bagService,
                _notifier,
                null,
                null);
        NavigationView.Navigate(this, page);
    }

    View DeleteDialog() => new AlertDialog(
        _deleteDialogOpen,
        text: new Text($"Are you sure you want to delete '{_name.Value}'? This action cannot be undone.").SecondaryText(),
        title: new Text("Delete Bean?").SubHeadline(),
        confirmButton: new Button("DELETE", () => _ = DeleteAsync())
            .TextButton().Color(CoffeeTheme.Error).CornerRadius(0)
            .AutomationId("bean_delete_confirm"),
        dismissButton: new Button("CANCEL", () => _deleteDialogOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0)
            .AutomationId("bean_delete_cancel"))
        .AutomationId("bean_delete_dialog");

    View RecipeResultDialog() => new AlertDialog(
        _recipeResultOpen,
        text: new Text(() => _recipeResultMessage.Value).SecondaryText(),
        title: new Text(() => _recipeResultTitle.Value).SubHeadline(),
        confirmButton: new Button("OK", () => _recipeResultOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0))
        .AutomationId("recipe_result_dialog");

    View SourceDialog() => new AlertDialog(
        _sourceDialogOpen,
        text: new Text(() => _sourceUrl.Value).SecondaryText(),
        title: new Text(() => _sourceTitle.Value).SubHeadline(),
        confirmButton: new Button("OPEN", () =>
        {
            var url = _sourceUrl.Value;
            _sourceDialogOpen.Value = false;
            _openExternal?.Invoke(url);
        }).TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0)
          .AutomationId("recipe_source_open"),
        dismissButton: new Button("CANCEL", () => _sourceDialogOpen.Value = false)
            .TextButton().Color(CoffeeTheme.TextSecondary).CornerRadius(0)
            .AutomationId("recipe_source_cancel"))
        .AutomationId("recipe_source_dialog");

    void ShowRecipeResult(string title, string message)
    {
        _recipeResultTitle.Value = title;
        _recipeResultMessage.Value = message;
        _recipeResultOpen.Value = true;
    }

    void ShowSource(RecipeDto recipe)
    {
        _sourceTitle.Value = recipe.Title ?? recipe.BrewMethod.DisplayName();
        _sourceUrl.Value = recipe.SourceUrl ?? string.Empty;
        _sourceDialogOpen.Value = true;
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

    sealed class BeanTextField : TextField
    {
        readonly Signal<string> _value;
        readonly Action<string> _onChanged;

        public BeanTextField(
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _grindTranslationCts?.Cancel();
            _grindTranslationCts?.Dispose();
            _grindTranslationCts = null;
        }
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
