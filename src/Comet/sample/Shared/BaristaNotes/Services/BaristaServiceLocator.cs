#nullable enable
using System;
using System.Net.Http;
using CometBaristaNotes.Data;
using CometBaristaNotes.Services.Grind;
using CometBaristaNotes.Services.Recipes;

namespace CometBaristaNotes.Services;

/// <summary>
/// Static service locator for BaristaNotes pages that cannot receive
/// constructor-injected services (e.g. pages pushed via NavigationView
/// from SettingsPage which has no BaristaServices reference).
/// Initialized once by <see cref="Initialize"/>.
/// </summary>
public static class BaristaServiceLocator
{
    static IBaristaDataStore? _store;
    static IDataChangeNotifier? _notifier;
    static IPreferencesService? _preferences;
    static IImageProcessingService? _imageProcessingService;

    static IBeanService? _beanService;
    static IBagService? _bagService;
    static IEquipmentService? _equipmentService;
    static IUserProfileService? _profileService;
    static IDrinkValueRangeService? _rangeService;
    static IGrindTranslationService? _grindTranslationService;
    static IRecipeService? _recipeService;
    static IRecipeSourcingService? _recipeSourcingService;
    static IAIAdviceService? _aiAdviceService;
    static AzureOpenAIAdviceConfiguration? _aiAdviceConfiguration;
    static HttpClient? _recipeHttpClient;
    static HttpClient? _aiAdviceHttpClient;

    public static bool IsInitialized => _store is not null;

    public static void ConfigureAIAdvice(
        AzureOpenAIAdviceConfiguration configuration,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _aiAdviceConfiguration = configuration;
        _aiAdviceHttpClient = httpClient;
        _aiAdviceService = null;
    }

    public static void Initialize(
        IBaristaDataStore store,
        IDataChangeNotifier notifier,
        IPreferencesService? preferences = null,
        IImageProcessingService? imageProcessingService = null,
        IRecipeSourcingService? recipeSourcingService = null,
        IAIAdviceService? aiAdviceService = null,
        AzureOpenAIAdviceConfiguration? aiAdviceConfiguration = null,
        HttpClient? aiAdviceHttpClient = null)
    {
        _store = store;
        _notifier = notifier;
        _preferences = preferences;
        _imageProcessingService = imageProcessingService ?? new LocalImageProcessingService();
        _beanService = null;
        _bagService = null;
        _equipmentService = null;
        _profileService = null;
        _rangeService = null;
        _grindTranslationService = null;
        _recipeService = null;
        _recipeSourcingService = recipeSourcingService;
        _aiAdviceService = aiAdviceService;
        _aiAdviceConfiguration = aiAdviceConfiguration;
        _aiAdviceHttpClient = aiAdviceHttpClient;
    }

    public static bool Reset(IBaristaDataStore expectedStore)
    {
        ArgumentNullException.ThrowIfNull(expectedStore);
        if (!ReferenceEquals(_store, expectedStore))
            return false;

        _store = null;
        _notifier = null;
        _preferences = null;
        _imageProcessingService = null;
        _beanService = null;
        _bagService = null;
        _equipmentService = null;
        _profileService = null;
        _rangeService = null;
        _grindTranslationService = null;
        _recipeService = null;
        _recipeSourcingService = null;
        _aiAdviceService = null;
        _aiAdviceConfiguration = null;
        _recipeHttpClient = null;
        _aiAdviceHttpClient = null;
        return true;
    }

    /// <summary>Lazy-init from the singleton <see cref="InMemoryDataStore"/>
    /// created in BaristaNotesApp. If not explicitly initialized,
    /// creates a fresh store (for standalone page testing).</summary>
    static void EnsureInitialized()
    {
        if (_store is not null) return;
        var store = new SqliteDataStore(SqliteDataStore.GetDefaultPath());
        _store = store;
        _preferences = new PreferencesService(store.CreatePreferencesStore());
        _notifier = new DataChangeNotifier();
        _imageProcessingService = new LocalImageProcessingService();
    }

    public static IBeanService BeanService
    {
        get { EnsureInitialized(); return _beanService ??= new InMemoryBeanService(_store!, _notifier!, new InMemoryRatingService(_store!), RecipeSourcingService); }
    }

    public static IBagService BagService
    {
        get { EnsureInitialized(); return _bagService ??= new InMemoryBagService(_store!, _notifier!); }
    }

    public static IEquipmentService EquipmentService
    {
        get { EnsureInitialized(); return _equipmentService ??= new InMemoryEquipmentService(_store!, _notifier!); }
    }

    public static IUserProfileService ProfileService
    {
        get { EnsureInitialized(); return _profileService ??= new InMemoryUserProfileService(_store!, _notifier!, _imageProcessingService!); }
    }

    public static IDrinkValueRangeService RangeService
    {
        get { EnsureInitialized(); return _rangeService ??= new InMemoryDrinkValueRangeService(_preferences); }
    }

    public static IRecipeService RecipeService
    {
        get { EnsureInitialized(); return _recipeService ??= new InMemoryRecipeService(_store!); }
    }

    public static IRecipeSourcingService RecipeSourcingService
    {
        get
        {
            EnsureInitialized();
            return _recipeSourcingService ??= new Recipes.RecipeSourcingService(
                _store!,
                RecipeService,
                RoasterRecipeAdapterRegistry.CreateDefault(
                    _recipeHttpClient ??= new HttpClient()),
                new NullAIRecipeGenerator());
        }
    }

    public static IAIAdviceService AIAdviceService
    {
        get
        {
            EnsureInitialized();
            var configuration =
                _aiAdviceConfiguration ??
                AzureOpenAIAdviceConfiguration.FromEnvironment();
            IAIAdviceAdapter adapter = configuration.IsConfigured
                ? new AzureOpenAIAdviceAdapter(
                    _aiAdviceHttpClient ??= new HttpClient(),
                    configuration)
                : new UnavailableAIAdviceAdapter();
            return _aiAdviceService ??= new AIAdviceService(
                new InMemoryShotService(_store!, _notifier!, _preferences),
                adapter);
        }
    }

    public static IDataChangeNotifier DataChangeNotifier
    {
        get { EnsureInitialized(); return _notifier!; }
    }

    public static IGrindTranslationService GrindTranslationService
    {
        get { EnsureInitialized(); return _grindTranslationService ??= new InMemoryGrindTranslationService(_store!); }
    }
}
