#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;

namespace CometSamples.BaristaNotes.Pages;

public class BeanManagementPage : BaristaManagementPage
{
    readonly IBeanService _beanService;
    readonly IBagService _bagService;
    readonly IDataChangeNotifier _notifier;
    readonly IShotService? _shotService;
    readonly IRecipeService? _recipeService;
    readonly BaristaServices? _services;
    readonly Action? _openNewDrink;
    readonly Action? _openActivity;
    readonly Action<int>? _openShot;
    readonly Action<string>? _openExternal;
    readonly Action? _onExit;

    readonly Signal<List<BeanDto>> _beans = new(new());
    readonly Signal<bool> _isLoading = new(true);
    readonly Signal<string?> _error = new(null);
    ListView<BeanDto>? _beanList;
    bool _subscribed;
    bool _exitNotified;

    public BeanManagementPage()
        : this(
            BaristaServiceLocator.BeanService,
            BaristaServiceLocator.BagService,
            BaristaServiceLocator.DataChangeNotifier,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null)
    {
    }

    public BeanManagementPage(
        BaristaServices services,
        Action? openNewDrink = null,
        Action? openActivity = null,
        Action<int>? openShot = null,
        Action<string>? openExternal = null,
        Action? onExit = null)
        : this(
            services.BeanService,
            services.BagService,
            services.DataChangeNotifier,
            services.ShotService,
            new InMemoryRecipeService(services.Store),
            services,
            openNewDrink,
            openActivity,
            openShot,
            openExternal,
            onExit)
    {
    }

    BeanManagementPage(
        IBeanService beanService,
        IBagService bagService,
        IDataChangeNotifier notifier,
        IShotService? shotService,
        IRecipeService? recipeService,
        BaristaServices? services,
        Action? openNewDrink,
        Action? openActivity,
        Action<int>? openShot,
        Action<string>? openExternal,
        Action? onExit)
    {
        _beanService = beanService;
        _bagService = bagService;
        _notifier = notifier;
        _shotService = shotService;
        _recipeService = recipeService;
        _services = services;
        _openNewDrink = openNewDrink;
        _openActivity = openActivity;
        _openShot = openShot;
        _openExternal = openExternal;
        _onExit = onExit;
        _notifier.DataChanged += OnDataChanged;
        _subscribed = true;
        _ = LoadAsync();
    }

    void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.ChangeType is DataChangeType.BeanCreated or DataChangeType.BeanUpdated)
            _ = LoadAsync();
    }

    async Task LoadAsync()
    {
        try
        {
            _isLoading.Value = true;
            _error.Value = null;
            _beans.Value = await _beanService.GetAllActiveBeansAsync();
            _beanList?.ReloadData();
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

    void OpenBean(int? beanId) =>
        NavigationView.Navigate(this, new BeanDetailPage(
            beanId,
            _beanService,
            _bagService,
            _notifier,
            _shotService,
            _recipeService,
            _services,
            _openShot,
            _openExternal,
            null));

    [Body]
    View body()
    {
        var safeArea = BaristaSafeAreaLayout.GetInsets(this);
        var count = _beans.Value.Count;
        var root = BaristaEdgeFrame.Build(
            BaristaPageHeader.Build(
                "BEANS", count == 1 ? "1 bean" : $"{count} beans", safeArea, "beans_header"),
            RenderBody(),
            BottomActions(),
            "bean_management_page");

        return root
            .Background(CoffeeTheme.OutlineColor);
    }

    View RenderBody()
    {
        if (_isLoading.Value)
            return new ContentStateView(ContentStateKind.Loading, "Loading beans");

        if (_error.Value is { } error)
            return new ContentStateView(
                ContentStateKind.Error,
                "Beans unavailable",
                error,
                () => _ = LoadAsync());

        if (_beans.Value.Count == 0)
            return new ContentStateView(
                ContentStateKind.Empty,
                "No beans",
                "Add your favorite coffee beans");

        var list = new ListView<BeanDto>(() => _beans.Value)
        {
            ViewFor = BeanRow,
        };
        _beanList = list;
        return list.Background(CoffeeTheme.OutlineColor);
    }

    View BeanRow(BeanDto bean)
    {
        var subtitle = !string.IsNullOrWhiteSpace(bean.Roaster)
            ? bean.Roaster!
            : !string.IsNullOrWhiteSpace(bean.Origin) ? bean.Origin! : "Bean";

        return ManagementListRow.Build(
            subtitle, bean.Name, $"bean_row_{bean.Id}", () => OpenBean(bean.Id));
    }

    View BottomActions() => new FixedBottomActionRow(
        new BottomAction("beans_new_drink", CoffeeIcons.Coffee, () => _openNewDrink?.Invoke()),
        new BottomAction("beans_activity", CoffeeIcons.Feed, () => _openActivity?.Invoke()),
        new BottomAction("beans_back", CoffeeIcons.Settings, Close),
        new BottomAction("beans_add", CoffeeIcons.Add, () => OpenBean(null), Inverted: true));

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

    protected override void Dispose(bool disposing)
    {
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
