#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Comet;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Pages;
using CometSamples.BaristaNotes.Styles;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services;
using CometBaristaNotes.Services.DTOs;
using CometBaristaNotes.Services.Voice;

namespace CometSamples.BaristaNotes;

public enum BaristaModalKind
{
    None,
    PhotoIntent,
    AiSuggestions,
    ActivityFilter,
}

public sealed class BaristaNavigationCoordinator
{
    readonly BaristaServices _services;
    NavigationView? _newDrinkNavigation;
    NavigationView? _activityNavigation;
    NavigationView? _settingsNavigation;
    ShotLoggingPage? _activeShotEditor;
    ShotLoggingPage? _rootShotEditor;
    BaristaSection? _shotEditorReturnSection;
    Action? _clearTransientSurface;
    Action? _resetSettingsNavigation;
    ActivityFeedPage? _activityPage;
    int _sectionActivationVersion;

    public Signal<BaristaSection> Section { get; } = new(BaristaSection.NewDrink);
    public Signal<int> SectionIndex { get; } = new((int)BaristaSection.NewDrink);
    public Signal<bool> IsImmersive { get; } = new(false);
    public Signal<bool> IsEditingShot { get; } = new(false);
    public Signal<bool> ModalOpen { get; } = new(false);
    public Signal<BaristaModalKind> ModalKind { get; } = new(BaristaModalKind.None);
    public Signal<bool> VoiceVisible { get; } = new(false);
    public Signal<bool> VoiceCollapsed { get; } = new(false);
    public Action<string>? ExternalLinkRequested { get; set; }

    public int? EditingShotId => _activeShotEditor?.EditingShotId
        ?? _rootShotEditor?.EditingShotId;

    public ShotLoggingPage? ActiveShotEditor => _activeShotEditor ?? _rootShotEditor;

    public void SetRootShotEditor(ShotLoggingPage page) => _rootShotEditor = page;

    public BaristaNavigationCoordinator(BaristaServices services) => _services = services;

    public void SetActivityPage(ActivityFeedPage page) => _activityPage = page;

    void SetSection(BaristaSection section)
        => BaristaSectionTransition.Set(Section, SectionIndex, section);

    public bool ApplyActivityPeriodFilter(string? period, string? filter)
    {
        return _activityPage?.ApplyProgrammaticFilter(period, filter) ?? true;
    }

    public void Attach(
        NavigationView newDrinkNavigation,
        NavigationView activityNavigation,
        NavigationView settingsNavigation)
    {
        _newDrinkNavigation = newDrinkNavigation;
        _activityNavigation = activityNavigation;
        _settingsNavigation = settingsNavigation;
    }

    public void SwitchTo(BaristaSection section, bool activateVoice = false)
    {
        using var hold = ReactiveScheduler.HoldFlushes();
        var activationVersion = Interlocked.Increment(ref _sectionActivationVersion);
        var previousSection = Section.Value;
        var leavingNewDrink = previousSection == BaristaSection.NewDrink;
        var reusingRootShotEditor = IsEditingShot.Value
            && _activeShotEditor is null
            && _rootShotEditor is not null;
        _shotEditorReturnSection = null;
        ClearTransientSurface();
        if (reusingRootShotEditor && section != BaristaSection.NewDrink)
            _rootShotEditor!.ResetForNewDrink();
        if (leavingNewDrink || section == BaristaSection.NewDrink)
            ResetNewDrinkNavigation();
        IsEditingShot.Value = false;
        if ((previousSection == BaristaSection.Settings || section == BaristaSection.Settings)
            && _resetSettingsNavigation is not null)
            _resetSettingsNavigation();
        else if (section != BaristaSection.NewDrink)
            NavigationFor(section)?.PopToRoot();
        SetSection(section);
        if (section == BaristaSection.NewDrink)
        {
            ActiveShotEditor?.RefreshForActivation();
            if (activateVoice || BaristaVoiceIntegration.HasPendingNewDrinkActivation)
                _ = ActivateNewDrinkAfterTransitionAsync(activationVersion, activateVoice);
        }
        // Deactivate voice when leaving New Drink or hiding overlay
        if (leavingNewDrink && section != BaristaSection.NewDrink)
        {
            BaristaVoiceIntegration.CancelPendingNewDrinkActivation();
            VoiceVisible.Value = false;
            _ = BaristaVoiceIntegration.OnPageDeactivatedAsync();
        }
    }

    public void RequestSectionSwitch(BaristaSection section, bool activateVoice = false)
        => RequestNavigation(() => SwitchTo(section, activateVoice));

    public void RequestNavigation(Action confirmedNavigation)
    {
        if (_activeShotEditor is { } editor && Section.Value == BaristaSection.NewDrink)
        {
            editor.RequestLeave(confirmedNavigation);
            return;
        }

        if (IsEditingShot.Value
            && _activeShotEditor is null
            && _rootShotEditor is { } rootShotEditor
            && Section.Value == BaristaSection.NewDrink)
        {
            rootShotEditor.RequestLeave(confirmedNavigation);
            return;
        }

        confirmedNavigation();
    }

    public async Task<VoiceNavigationOutcome> RequestNavigationAsync(
        Action confirmedNavigation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EditorLeaveOutcome outcome;
            if (_activeShotEditor is { } editor && Section.Value == BaristaSection.NewDrink)
            {
                outcome = await editor.RequestLeaveAsync(
                    confirmedNavigation,
                    cancellationToken);
            }
            else if (IsEditingShot.Value
                && _activeShotEditor is null
                && _rootShotEditor is { } rootShotEditor
                && Section.Value == BaristaSection.NewDrink)
            {
                outcome = await rootShotEditor.RequestLeaveAsync(
                    confirmedNavigation,
                    cancellationToken);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                confirmedNavigation();
                outcome = EditorLeaveOutcome.Left;
            }

            return outcome switch
            {
                EditorLeaveOutcome.Left => VoiceNavigationOutcome.Navigated,
                EditorLeaveOutcome.KeptEditing => VoiceNavigationOutcome.KeptEditing,
                _ => VoiceNavigationOutcome.Cancelled,
            };
        }
        catch (OperationCanceledException)
        {
            return VoiceNavigationOutcome.Cancelled;
        }
    }

    public bool IsRootShotEditorActive(ShotLoggingPage page)
        => ReferenceEquals(page, _rootShotEditor)
            && _activeShotEditor is null
            && IsEditingShot.Value
            && Section.Value == BaristaSection.NewDrink;

    public void RequestShotEditorBack(ShotLoggingPage page)
    {
        if (!IsRootShotEditorActive(page))
            return;

        page.RequestLeave(() => CloseShotEditor(page));
    }

    async Task ActivateNewDrinkAfterTransitionAsync(
        int activationVersion,
        bool activateVoice)
    {
        await Task.Delay(1).ConfigureAwait(false);
        ThreadHelper.RunOnMainThread(() =>
        {
            if (activationVersion != Volatile.Read(ref _sectionActivationVersion)
                || Section.Value != BaristaSection.NewDrink)
            {
                return;
            }

            var deferredActivation = BaristaVoiceIntegration.NotifyNewDrinkMounted();
            if (!activateVoice && !deferredActivation)
                return;

            VoiceVisible.Value = true;
            VoiceCollapsed.Value = false;
        });
    }

    public void OpenShotEditor(int shotId)
    {
        OpenShotEditorCore(shotId, null);
    }

    public void OpenShotEditor(ShotRecordDto shot)
    {
        OpenShotEditorCore(shot.Id, shot);
    }

    void OpenShotEditorCore(int shotId, ShotRecordDto? initialShot)
    {
        using var hold = ReactiveScheduler.HoldFlushes();
        Interlocked.Increment(ref _sectionActivationVersion);
        _shotEditorReturnSection = Section.Value == BaristaSection.Activity
            ? BaristaSection.Activity
            : null;
        ClearTransientSurface();
        ResetNewDrinkNavigation();
        SetSection(BaristaSection.NewDrink);
        IsEditingShot.Value = true;
        if (initialShot is not null && _rootShotEditor is { } rootShotEditor)
        {
            rootShotEditor.BeginEdit(initialShot);
            return;
        }

        _activeShotEditor = new ShotLoggingPage(_services, shotId, this, initialShot);
        _newDrinkNavigation?.Navigate(_activeShotEditor);
    }

    public void OpenEquipmentCreator(
        EquipmentType presetType,
        Action<int, EquipmentType> selectCreated,
        Action? afterReturn = null)
    {
        if (_newDrinkNavigation is null)
            return;

        var existingIds = _services.Store.Equipment.Select(item => item.Id).ToHashSet();
        var page = new EquipmentDetailPage(
            equipmentId: null,
            afterMutation: () =>
            {
                var created = _services.Store.Equipment
                    .Where(item => !existingIds.Contains(item.Id) && !item.IsDeleted)
                    .OrderByDescending(item => item.Id)
                    .FirstOrDefault();
                if (created is not null)
                    selectCreated(created.Id, created.Type);
                return Task.CompletedTask;
            },
            presetType: presetType);
        var proxy = new TransientNavigationProxy(_newDrinkNavigation, this, afterReturn);
        var host = new TransientNavigationHost(page, proxy);

        BeginTransientSurface(proxy.Reset);
        _newDrinkNavigation.Navigate(host);
        proxy.TrackHost(host);
    }

    public void CloseShotEditor(View view)
    {
        if (ReferenceEquals(view, _rootShotEditor))
        {
            var returnSection = _shotEditorReturnSection;
            _shotEditorReturnSection = null;
            ClearTransientSurface();
            _rootShotEditor!.ResetForNewDrink();
            IsEditingShot.Value = false;
            if (returnSection.HasValue)
                SetSection(returnSection.Value);
            return;
        }

        NavigationView.Pop(view);
        if (_newDrinkNavigation?.Content is { } root)
            ((IStackNavigation)_newDrinkNavigation).NavigationFinished(new IView[] { root });
    }

    public void NotifyShotEditorDisposed(ShotLoggingPage page)
    {
        if (!ReferenceEquals(page, _activeShotEditor))
            return;

        var returnSection = _shotEditorReturnSection;
        _shotEditorReturnSection = null;
        _activeShotEditor = null;
        ClearTransientSurface();
        IsEditingShot.Value = false;
        if (returnSection.HasValue)
            SetSection(returnSection.Value);
    }

    public void BeginTransientSurface(Action clear)
    {
        _clearTransientSurface = clear;
        IsImmersive.Value = true;
    }

    public void EndTransientSurface()
    {
        _clearTransientSurface = null;
        IsImmersive.Value = false;
    }

    public void ShowModal(BaristaModalKind kind)
    {
        ModalKind.Value = kind;
        ModalOpen.Value = true;
    }

    public void HideModal()
    {
        ModalOpen.Value = false;
        ModalKind.Value = BaristaModalKind.None;
    }

    public void ToggleVoice()
    {
        var wasVisible = VoiceVisible.Value;
        VoiceVisible.Value = !wasVisible;
        VoiceCollapsed.Value = false;
        if (wasVisible)
            _ = BaristaVoiceIntegration.OnPageDeactivatedAsync();
    }

    public void OpenVoiceFromActivity()
    {
        BaristaVoiceIntegration.RequestNewDrinkActivation();
        SwitchTo(BaristaSection.NewDrink, activateVoice: true);
    }

    public async void OpenExternalLink(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri) ||
            !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return;
        }

        if (ExternalLinkRequested is not null)
        {
            ExternalLinkRequested(uri);
            return;
        }

        try
        {
            await Microsoft.Maui.ApplicationModel.Browser.Default.OpenAsync(
                parsed,
                new Microsoft.Maui.ApplicationModel.BrowserLaunchOptions
                {
                    LaunchMode = Microsoft.Maui.ApplicationModel.BrowserLaunchMode.SystemPreferred,
                });
        }
        catch
        {
        }
    }

    public View PrepareSettingsDestination(View destination)
    {
        if (destination is BeanManagementPage)
        {
            destination.Dispose();
            _resetSettingsNavigation?.Invoke();
            BeginTransientSurface(() => _resetSettingsNavigation?.Invoke());
            return new BeanManagementPage(
                _services,
                () => SwitchTo(BaristaSection.NewDrink),
                () => SwitchTo(BaristaSection.Activity),
                OpenShotEditor,
                OpenExternalLink,
                EndTransientSurface);
        }

        if (destination is EquipmentManagementPage)
        {
            destination.Dispose();
            return new EquipmentManagementPage(
                () => SwitchTo(BaristaSection.NewDrink),
                () => SwitchTo(BaristaSection.Activity),
                () => _resetSettingsNavigation?.Invoke());
        }

        return destination;
    }

    public void OpenBeanRecipe(int beanId)
    {
        if (_newDrinkNavigation is null)
            return;

        IsEditingShot.Value = false;
        ResetNewDrinkNavigation();
        var page = new BeanDetailPage(
            beanId,
            _services,
            OpenShotEditor,
            OpenExternalLink,
            EndTransientSurface);
        var proxy = new TransientNavigationProxy(_newDrinkNavigation, this);
        var host = new TransientNavigationHost(page, proxy);

        BeginTransientSurface(proxy.Reset);
        _newDrinkNavigation.Navigate(host);
        proxy.TrackHost(host);
    }

    public void SetSettingsNavigationDepth(int depth)
    {
        IsImmersive.Value = Section.Value == BaristaSection.Settings && depth > 1;
    }

    // --- Photo workflow routing ---

    public Signal<bool> RoomResultOpen { get; } = new(false);
    public Signal<string> RoomResultTitle { get; } = new(string.Empty);
    public Signal<string> RoomResultMessage { get; } = new(string.Empty);

    /// <summary>Opens a new ProfileDetailPage with staged avatar bytes.</summary>
    public void OpenProfileFromPhoto(byte[] avatarBytes)
    {
        SwitchTo(BaristaSection.Settings);
        if (_settingsNavigation is null)
            return;
        var page = new ProfileDetailPage(null, stagedAvatarBytes: avatarBytes);
        _settingsNavigation.Navigate(page);
        SetSettingsNavigationDepth(2);
    }

    /// <summary>Shows an "Analysis Complete" dialog with the room analysis result.</summary>
    public void ShowRoomAnalysisResult(string title, string message)
    {
        RoomResultTitle.Value = title;
        RoomResultMessage.Value = message;
        RoomResultOpen.Value = true;
    }

    /// <summary>Navigate Settings stack to BeanManagement, optionally opening a specific bean.</summary>
    public void OpenBeanManagement(int? beanId = null)
    {
        SwitchTo(BaristaSection.Settings);
        if (_settingsNavigation is null)
            return;
        var page = new BeanManagementPage(
            _services,
            () => SwitchTo(BaristaSection.NewDrink),
            () => SwitchTo(BaristaSection.Activity),
            OpenShotEditor,
            OpenExternalLink,
            () => _resetSettingsNavigation?.Invoke());
        _settingsNavigation.Navigate(page);
        SetSettingsNavigationDepth(2);
        if (beanId.HasValue)
        {
            var detail = new BeanDetailPage(
                beanId.Value,
                _services,
                OpenShotEditor,
                OpenExternalLink,
                () => _resetSettingsNavigation?.Invoke());
            _settingsNavigation.Navigate(detail);
            SetSettingsNavigationDepth(3);
        }
    }

    /// <summary>Navigate Settings through the owning bean to a specific bag.</summary>
    public void OpenBagManagement(int bagId, int beanId, string beanName)
    {
        SwitchTo(BaristaSection.Settings);
        if (_settingsNavigation is null)
            return;

        var management = new BeanManagementPage(
            _services,
            () => SwitchTo(BaristaSection.NewDrink),
            () => SwitchTo(BaristaSection.Activity),
            OpenShotEditor,
            OpenExternalLink,
            () => _resetSettingsNavigation?.Invoke());
        var bean = new BeanDetailPage(
            beanId,
            _services,
            OpenShotEditor,
            OpenExternalLink,
            () => _resetSettingsNavigation?.Invoke());
        var bag = new BagDetailPage(
            beanId,
            beanName,
            bagId,
            _services);

        _settingsNavigation.Navigate(management);
        _settingsNavigation.Navigate(bean);
        _settingsNavigation.Navigate(bag);
        SetSettingsNavigationDepth(4);
    }

    /// <summary>Navigate Settings stack to EquipmentManagement, optionally opening a specific item.</summary>
    public void OpenEquipmentManagement(int? equipmentId = null)
    {
        SwitchTo(BaristaSection.Settings);
        if (_settingsNavigation is null)
            return;
        var page = new EquipmentManagementPage(
            () => SwitchTo(BaristaSection.NewDrink),
            () => SwitchTo(BaristaSection.Activity),
            () => _resetSettingsNavigation?.Invoke());
        _settingsNavigation.Navigate(page);
        SetSettingsNavigationDepth(2);
        if (equipmentId.HasValue)
        {
            var detail = new EquipmentDetailPage(equipmentId.Value);
            _settingsNavigation.Navigate(detail);
            SetSettingsNavigationDepth(3);
        }
    }

    /// <summary>Navigate Settings stack to ProfileManagement, optionally opening a specific profile.</summary>
    public void OpenProfileManagement(int? profileId = null)
    {
        SwitchTo(BaristaSection.Settings);
        if (_settingsNavigation is null)
            return;
        var page = new ProfileManagementPage(
            () => SwitchTo(BaristaSection.NewDrink),
            () => SwitchTo(BaristaSection.Activity),
            () => _resetSettingsNavigation?.Invoke());
        _settingsNavigation.Navigate(page);
        SetSettingsNavigationDepth(2);
        if (profileId.HasValue)
        {
            var detail = new ProfileDetailPage(profileId.Value);
            _settingsNavigation.Navigate(detail);
            SetSettingsNavigationDepth(3);
        }
    }

    public void SetSettingsNavigationReset(Action reset) => _resetSettingsNavigation = reset;

    void ResetNewDrinkNavigation()
    {
        _newDrinkNavigation?.PopToRoot();
        _activeShotEditor?.Dispose();
        _activeShotEditor = null;
        if (_newDrinkNavigation?.Content is { } root)
            ((IStackNavigation)_newDrinkNavigation).NavigationFinished(new IView[] { root });
    }

    void ClearTransientSurface()
    {
        var clear = _clearTransientSurface;
        _clearTransientSurface = null;
        clear?.Invoke();
        IsImmersive.Value = false;
        HideModal();
    }

    NavigationView? NavigationFor(BaristaSection section) => section switch
    {
        BaristaSection.NewDrink => _newDrinkNavigation,
        BaristaSection.Activity => _activityNavigation,
        _ => _settingsNavigation,
    };

    sealed class TransientNavigationHost : ContentView
    {
        readonly View _page;
        readonly NavigationView _pageNavigation;

        public TransientNavigationHost(View page, NavigationView pageNavigation)
        {
            _page = page;
            _pageNavigation = pageNavigation;
            Add(page);
            _page.Navigation = _pageNavigation;
            this.BackButtonBehavior(new BackButtonBehavior
            {
                IsVisible = false,
                Command = new NavigationCommand(_pageNavigation.Pop),
            });
        }

        protected override void OnParentChange(View parent)
        {
            base.OnParentChange(parent);
            _page.Navigation = _pageNavigation;
        }
    }

    sealed class TransientNavigationProxy : NavigationView
    {
        readonly NavigationView _navigation;
        readonly BaristaNavigationCoordinator _coordinator;
        readonly List<View> _ownedViews = new();
        readonly List<IView> _proxyStack = new();
        readonly List<IView> _backingStack = new();
        Action? _afterReturn;

        public TransientNavigationProxy(
            NavigationView navigation,
            BaristaNavigationCoordinator coordinator,
            Action? afterReturn = null)
        {
            _navigation = navigation;
            _coordinator = coordinator;
            _afterReturn = afterReturn;
            Content = new Grid();
            _proxyStack.Add(Content);
            if (_navigation.Content is not null)
                _backingStack.Add(_navigation.Content);
            SetPerformNavigate(Push);
            SetPerformPop(PopTracked);
        }

        public void TrackHost(View host)
        {
            host.Navigation = this;
            _ownedViews.Add(host);
            _proxyStack.Add(host);
            _backingStack.Add(host);
            SyncStacks();
        }

        void Push(View view)
        {
            var backBehavior = view.GetBackButtonBehavior();
            if (backBehavior is null)
            {
                view.BackButtonBehavior(new BackButtonBehavior
                {
                    IsVisible = false,
                    Command = new NavigationCommand(Pop),
                });
            }
            else if (backBehavior.Command is null)
            {
                backBehavior.Command = new NavigationCommand(Pop);
            }

            _navigation.Navigate(view);
            view.Navigation = this;
            _ownedViews.Add(view);
            _proxyStack.Add(view);
            _backingStack.Add(view);
            SyncStacks();
        }

        void PopTracked()
        {
            var removed = _ownedViews.Count > 0 ? _ownedViews[^1] : null;
            _navigation.Pop();
            if (_ownedViews.Count > 0)
                _ownedViews.RemoveAt(_ownedViews.Count - 1);
            if (_proxyStack.Count > 1)
                _proxyStack.RemoveAt(_proxyStack.Count - 1);
            if (_backingStack.Count > 1)
                _backingStack.RemoveAt(_backingStack.Count - 1);
            SyncStacks();
            removed?.Dispose();
            if (_ownedViews.Count == 0)
            {
                _coordinator.EndTransientSurface();
                var afterReturn = _afterReturn;
                _afterReturn = null;
                afterReturn?.Invoke();
            }
        }

        public void Reset()
        {
            _afterReturn = null;
            _navigation.PopToRoot();
            var removed = _ownedViews.ToList();
            _ownedViews.Clear();
            _proxyStack.RemoveRange(1, Math.Max(0, _proxyStack.Count - 1));
            _backingStack.RemoveRange(1, Math.Max(0, _backingStack.Count - 1));
            SyncStacks();
            removed.ForEach(view => view.Dispose());
            _coordinator.EndTransientSurface();
        }

        void SyncStacks()
        {
            ((IStackNavigation)this).NavigationFinished(_proxyStack);
            ((IStackNavigation)_navigation).NavigationFinished(_backingStack);
        }
    }

    sealed class NavigationCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}

public sealed class BaristaNotesApp : View, IAsyncDisposable
{
    readonly Signal<string?> _initializationError = new(null);
    readonly Signal<bool> _photoBusy = new(false);
    readonly Signal<string?> _photoStatus = new(null);
    readonly Signal<string?> _photoError = new(null);
    BaristaServices? _services;
    BaristaNavigationCoordinator _navigation = null!;
    NavigationView _newDrinkNavigation = null!;
    NavigationView _activityNavigation = null!;
    NavigationView _settingsNavigation = null!;
    ActivityFeedPage _activityPage = null!;
    SettingsPage _settingsPage = null!;
    VoiceOverlayView _voiceOverlay = null!;
    AppVoiceCallbacks? _voiceCallbacks;
    AppPhotoWorkflowCallbacks? _photoCallbacks;
    PhotoWorkflowCoordinator? _photoCoordinator;
    CancellationTokenSource? _photoWorkflowCancellation;
    Task<PhotoWorkflowOutcome>? _photoWorkflowTask;
    AIAdvicePanel? _aiAdvicePanel;
    bool _disposed;
    Task _serviceDisposal = Task.CompletedTask;

    public BaristaServices Services => _services ??
        throw new InvalidOperationException("Barista services are not available.");
    internal bool HasActiveActivityFilters => _activityPage.HasActiveFilters;

    public BaristaNotesApp()
        => InitializeShell();

    void InitializeShell()
    {
        try
        {
            if (!_serviceDisposal.IsCompletedSuccessfully)
                throw new InvalidOperationException("Previous Barista service cleanup has not completed.", _serviceDisposal.Exception);
            var services = new BaristaServices();
            _services = services;
            _navigation = new BaristaNavigationCoordinator(services);
            var rootShotPage = new ShotLoggingPage(services, null, _navigation);
            _navigation.SetRootShotEditor(rootShotPage);
            _newDrinkNavigation = new NavigationView { rootShotPage };
            _activityPage = new ActivityFeedPage(
                services,
                shot => _navigation.OpenShotEditor(shot),
                () => _navigation.ShowModal(BaristaModalKind.ActivityFilter));
            _navigation.SetActivityPage(_activityPage);
            _activityNavigation = new NavigationView
            {
                _activityPage,
            };
            _settingsPage = new SettingsPage(
                services,
                () => _navigation.SwitchTo(BaristaSection.NewDrink),
                () => _navigation.SwitchTo(BaristaSection.Activity),
                () => { EnsureVoiceBound(); _navigation.OpenVoiceFromActivity(); },
                () => _navigation.OpenEquipmentManagement(),
                shotId => _navigation.OpenShotEditor(shotId),
                _navigation.OpenExternalLink,
                () => { });
            _settingsNavigation = new NavigationView();
            var settingsNavigationProxy = new SettingsNavigationProxy(_settingsNavigation, _navigation);
            var settingsRoot = new SettingsSectionRoot(
                _settingsPage,
                settingsNavigationProxy);
            _settingsNavigation.Add(settingsRoot);
            settingsNavigationProxy.SetBackingRoot(settingsRoot);
            _navigation.Attach(_newDrinkNavigation, _activityNavigation, _settingsNavigation);

            // Wire voice callbacks and overlay
            _voiceOverlay = new VoiceOverlayView(_navigation.VoiceVisible, _navigation.VoiceCollapsed);
            _voiceCallbacks = new AppVoiceCallbacks(_navigation, state =>
                ThreadHelper.RunOnMainThread(() => _voiceOverlay.ApplyState(state)));
            InitializeVoiceSession(services);

            // Wire photo workflow callbacks
            _photoCallbacks = new AppPhotoWorkflowCallbacks(
                _navigation, services, _photoBusy, _photoStatus, _photoError);
            if (BaristaPlatformServices.TryGet(out var photos, out var vision)
                && photos is not null
                && vision is not null)
            {
                _photoCoordinator = new PhotoWorkflowCoordinator(
                    photos,
                    vision,
                    _photoCallbacks);
            }

            _initializationError.Value = null;
        }
        catch (Exception ex)
        {
            var failedServices = _services;
            var failedVoiceCallbacks = _voiceCallbacks;
            _services = null;
            _voiceCallbacks = null;
            if (failedServices is not null)
            {
                if (failedVoiceCallbacks is null)
                    failedServices.Dispose();
                else
                    _serviceDisposal = DisposeOwnedServicesAsync(failedServices, failedVoiceCallbacks);
            }
            _navigation = null!;
            _initializationError.Value = ex.GetBaseException().Message;
        }
    }

    void InitializeVoiceSession(BaristaServices services)
    {
        if (_voiceCallbacks is null)
            return;
        if (BaristaVoiceIntegration.Session is not null)
        {
            // Already bound — just refresh overlay state
            _voiceOverlay?.ApplyState(BaristaVoiceIntegration.State);
            return;
        }
        try
        {
            var session = BaristaVoiceIntegration.Bind(
                _voiceCallbacks,
                services.ShotService,
                services.BeanService,
                services.BagService,
                services.EquipmentService,
                services.ProfileService);
            _ = session.InitializeAsync();
        }
        catch (InvalidOperationException)
        {
            // Platform not configured — update overlay with Unconfigured state
            _voiceOverlay?.ApplyState(PushToTalkVoiceService.UnconfiguredState);
        }
        catch (Exception ex)
        {
            _voiceOverlay?.ApplyState(new VoiceOverlayState(
                VoiceSessionStatus.Error,
                CometBaristaNotes.Models.Enums.SpeechRecognitionState.Error,
                CometBaristaNotes.Models.Enums.CommandStatus.Failed,
                "Voice error",
                ErrorMessage: ex.Message));
        }
    }

    void EnsureVoiceBound()
    {
        if (_voiceCallbacks is null || _services is null)
            return;
        if (BaristaVoiceIntegration.Session is not null)
            return;
        // Retry binding if it failed at startup (e.g. platform configured after app init)
        InitializeVoiceSession(_services);
    }

    void LaunchPhotoWorkflow(PhotoSource source)
    {
        if (_photoCallbacks is null || _disposed)
            return;

        _photoCallbacks.ResetPresentation();
        if (_photoCoordinator is null
            || !BaristaPlatformServices.TryGet(out var photos, out _)
            || photos is null)
        {
            _photoCallbacks.PublishAcquisitionResult(
                source,
                PhotoOperationResult.Unavailable(
                    source == PhotoSource.Camera
                        ? "camera_not_connected"
                        : "gallery_not_connected",
                    source == PhotoSource.Camera
                        ? "Camera and vision services are not configured by the host."
                        : "Gallery and vision services are not configured by the host."));
            return;
        }

        if (source == PhotoSource.Camera && !photos.IsCameraAvailable)
        {
            _photoCallbacks.PublishAcquisitionResult(
                source,
                PhotoOperationResult.Unavailable(
                    "camera_unavailable",
                    "No camera application is available on this device."));
            return;
        }
        if (source == PhotoSource.Gallery && !photos.IsGalleryAvailable)
        {
            _photoCallbacks.PublishAcquisitionResult(
                source,
                PhotoOperationResult.Unavailable(
                    "gallery_unavailable",
                    "No photo gallery is available on this device."));
            return;
        }
        if (_photoWorkflowTask is { IsCompleted: false })
        {
            _photoCallbacks.PublishAcquisitionResult(
                source,
                PhotoOperationResult.Error(
                    "photo_operation_busy",
                    "Another photo operation is already active."));
            return;
        }

        _photoWorkflowCancellation?.Dispose();
        _photoWorkflowCancellation = new CancellationTokenSource();
        _photoWorkflowTask = source == PhotoSource.Camera
            ? _photoCoordinator.RunCameraAsync(_photoWorkflowCancellation.Token)
            : _photoCoordinator.RunGalleryAsync(_photoWorkflowCancellation.Token);
    }

    [Body]
    View body()
    {
        _ = CoffeeTheme.IsLight;
        if (_initializationError.Value is { } initializationError)
        {
            return new ContentStateView(
                    ContentStateKind.Error,
                    "Database unavailable",
                    initializationError,
                    InitializeShell)
                .CoffeePageBackground()
                .AutomationId("barista_database_error");
        }
        if (_navigation is null)
            return new ContentStateView(ContentStateKind.Loading, "Preparing your data")
                .CoffeePageBackground();

        var section = _navigation.Section.Value;
        var immersive = _navigation.IsImmersive.Value;
        var showSharedActions = !immersive && section != BaristaSection.Settings;
        var modalKind = _navigation.ModalKind.Value;
        var navigation = new ContentSwitcher(
            _navigation.SectionIndex,
            new View[]
            {
                _newDrinkNavigation,
                _activityNavigation,
                _settingsNavigation,
            });

        var shell = new Grid(
            columns: new object[] { "*" },
            rows: showSharedActions
                ? new object[] { "*", "Auto" }
                : new object[] { "*" },
            rowSpacing: CoffeeSpacing.Divider)
        {
            navigation.Cell(row: 0),
        }
        .Background(CoffeeTheme.OutlineColor)
        .IgnoreSafeArea()
        .AutomationId("barista_navigation_shell");

        if (showSharedActions)
            shell.Add(BuildActionRow(section).Cell(row: 1));

        var root = new Grid
        {
            shell,
            new ActionModalOverlay(
                _navigation.ModalOpen,
                ModalTitle(modalKind),
                () => ModalContent(modalKind),
                () => DismissModal(modalKind),
                $"modal_{modalKind.ToString().ToLowerInvariant()}",
                useFixedDarkPalette: modalKind == BaristaModalKind.ActivityFilter),
            new PhotoWorkflowFeedbackView(_photoBusy, _photoStatus, _photoError),
            _voiceOverlay ?? new VoiceOverlayView(_navigation.VoiceVisible, _navigation.VoiceCollapsed),
            RoomResultDialog(),
        };
#if ANDROID
        return new Grid(
            columns: new object[] { "*" },
            rows: new object[] { BaristaSafeAreaLayout.AndroidStatusBarHeight(), "*" })
        {
            BaristaEdgeFrame.BuildTopInset(
                showDivider: section == BaristaSection.NewDrink && !immersive)
                .Cell(row: 0),
            root.Cell(row: 1),
        }
        .Background(CoffeeTheme.BackgroundColor)
        .IgnoreSafeArea();
#else
        return root.Background(CoffeeTheme.BackgroundColor);
#endif
    }

    sealed class SettingsSectionRoot : View
    {
        readonly SettingsPageNavigationHost _pageHost;

        public SettingsSectionRoot(
            SettingsPage page,
            NavigationView pageNavigation)
        {
            _pageHost = new SettingsPageNavigationHost(page, pageNavigation);
        }

        [Body]
        View body() =>
            new Grid
            {
                _pageHost,
            }
            .Background(CoffeeTheme.OutlineColor)
            .IgnoreSafeArea()
            .AutomationId("settings_section_root");
    }

    sealed class SettingsPageNavigationHost : ContentView
    {
        readonly SettingsPage _page;
        readonly NavigationView _pageNavigation;

        public SettingsPageNavigationHost(SettingsPage page, NavigationView pageNavigation)
        {
            _page = page;
            _pageNavigation = pageNavigation;
            Add(page);
            _page.Navigation = _pageNavigation;
        }

        protected override void OnParentChange(View parent)
        {
            base.OnParentChange(parent);
            _page.Navigation = _pageNavigation;
        }
    }

    sealed class SettingsNavigationProxy : NavigationView
    {
        readonly NavigationView _navigation;
        readonly BaristaNavigationCoordinator _coordinator;
        readonly List<IView> _trackedStack = new();
        readonly List<IView> _backingStack = new();
        int _depth = 1;

        public SettingsNavigationProxy(
            NavigationView navigation,
            BaristaNavigationCoordinator coordinator)
        {
            _navigation = navigation;
            _coordinator = coordinator;
            Content = new Grid();
            _trackedStack.Add(Content);
            SetPerformNavigate(Push);
            SetPerformPop(PopTracked);
            SetPerformContentReset(_ => PopToRootTracked());
            _coordinator.SetSettingsNavigationReset(PopToRootTracked);
        }

        public void SetBackingRoot(View root)
        {
            _backingStack.Clear();
            _backingStack.Add(root);
            SyncBackingStack();
        }

        void Push(View view)
        {
            view = _coordinator.PrepareSettingsDestination(view);
            var backBehavior = view.GetBackButtonBehavior();
            if (backBehavior is null)
            {
                view.BackButtonBehavior(new BackButtonBehavior
                {
                    IsVisible = false,
                    Command = new CallbackCommand(Pop),
                });
            }
            else if (backBehavior.Command is null)
            {
                backBehavior.Command = new CallbackCommand(Pop);
            }

            _navigation.Navigate(view);
            view.Navigation = this;
            _trackedStack.Add(view);
            _backingStack.Add(view);
            SyncProxyStack();
            SyncBackingStack();
            _depth++;
            _coordinator.SetSettingsNavigationDepth(_depth);
        }

        void PopTracked()
        {
            var removed = _backingStack.Count > 1
                ? _backingStack[^1] as View
                : null;
            _navigation.Pop();
            if (_trackedStack.Count > 1)
                _trackedStack.RemoveAt(_trackedStack.Count - 1);
            if (_backingStack.Count > 1)
                _backingStack.RemoveAt(_backingStack.Count - 1);
            SyncProxyStack();
            SyncBackingStack();
            removed?.Dispose();
            _depth = Math.Max(1, _depth - 1);
            _coordinator.SetSettingsNavigationDepth(_depth);
        }

        void PopToRootTracked()
        {
            var removed = _backingStack.Skip(1).OfType<View>().ToList();
            _navigation.PopToRoot();
            _trackedStack.RemoveRange(1, Math.Max(0, _trackedStack.Count - 1));
            _backingStack.RemoveRange(1, Math.Max(0, _backingStack.Count - 1));
            SyncProxyStack();
            SyncBackingStack();
            removed.ForEach(view => view.Dispose());
            _depth = 1;
            _coordinator.SetSettingsNavigationDepth(_depth);
        }

        void SyncProxyStack() =>
            ((IStackNavigation)this).NavigationFinished(_trackedStack);

        void SyncBackingStack() =>
            ((IStackNavigation)_navigation).NavigationFinished(_backingStack);
    }

    sealed class CallbackCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    View BuildActionRow(BaristaSection section) => section switch
    {
        BaristaSection.NewDrink => new FixedBottomActionRow(
            new("nav_activity", CoffeeIcons.Feed, () => _navigation.RequestSectionSwitch(BaristaSection.Activity)),
            new("nav_settings", CoffeeIcons.Settings, () => _navigation.RequestSectionSwitch(BaristaSection.Settings)),
            new("nav_voice", CoffeeIcons.Mic, () => { EnsureVoiceBound(); _navigation.ToggleVoice(); }),
            new(
                _navigation.IsEditingShot.Value ? "nav_ai" : "nav_photo",
                _navigation.IsEditingShot.Value ? CoffeeIcons.Ai : CoffeeIcons.Camera,
                () =>
                {
                    if (_navigation.IsEditingShot.Value)
                    {
                        _navigation.ShowModal(BaristaModalKind.AiSuggestions);
                    }
                    else
                    {
                        _photoCallbacks?.ResetPresentation();
                        _navigation.ShowModal(BaristaModalKind.PhotoIntent);
                    }
                })),
        BaristaSection.Activity => _activityPage.BottomActions(
            () => _navigation.SwitchTo(BaristaSection.NewDrink),
            () => _navigation.SwitchTo(BaristaSection.Settings),
            () => { EnsureVoiceBound(); _navigation.OpenVoiceFromActivity(); }),
        _ => new FixedBottomActionRow(
            new("nav_new_drink", CoffeeIcons.Coffee, () => _navigation.SwitchTo(BaristaSection.NewDrink)),
            new("nav_activity", CoffeeIcons.Feed, () => _navigation.SwitchTo(BaristaSection.Activity)),
            new("nav_voice", CoffeeIcons.Mic, () => { EnsureVoiceBound(); _navigation.ToggleVoice(); })),
    };

    static string ModalTitle(BaristaModalKind kind) => kind switch
    {
        BaristaModalKind.PhotoIntent => "Photo",
        BaristaModalKind.AiSuggestions => "AI suggestions",
        BaristaModalKind.ActivityFilter => "Filter Shots",
        _ => "BaristaNotes",
    };

    View ModalContent(BaristaModalKind kind)
    {
        return kind switch
        {
            BaristaModalKind.PhotoIntent => PhotoIntentContent(),
            BaristaModalKind.AiSuggestions => AIAdviceContent(),
            BaristaModalKind.ActivityFilter => _activityPage.FilterContent(_navigation.HideModal),
            _ => new VStack(spacing: CoffeeSpacing.Divider)
            {
                ModalRow("BARISTANOTES", "No content", "modal_empty", _navigation.HideModal),
            },
        };
    }

    View AIAdviceContent()
    {
        _aiAdvicePanel ??= new AIAdvicePanel(
            () => _navigation.EditingShotId,
            CloseAIAdvice);
        return _aiAdvicePanel;
    }

    View PhotoIntentContent()
    {
        // Post-capture: coordinator is waiting for user to choose intent
        if (_photoCallbacks?.HasPendingIntentChoice == true)
        {
            return new VStack(spacing: CoffeeSpacing.Divider)
            {
                ModalRow("COFFEE", "Identify coffee and create a bag", "photo_intent_coffee",
                    () => { _photoCallbacks.ResolveIntentChoice(PhotoIntentChoice.Coffee); _navigation.HideModal(); }),
                ModalRow("PROFILE", "Use as a profile photo", "photo_intent_profile",
                    () => { _photoCallbacks.ResolveIntentChoice(PhotoIntentChoice.Profile); _navigation.HideModal(); }),
                ModalRow("ROOM", "Count people and coffee needs", "photo_intent_room",
                    () => { _photoCallbacks.ResolveIntentChoice(PhotoIntentChoice.Room); _navigation.HideModal(); }),
                ModalRow("RETAKE", "Open the camera again", "photo_intent_retake",
                    () => { _photoCallbacks.ResolveIntentChoice(PhotoIntentChoice.Retake); _navigation.HideModal(); }),
                ModalRow("CANCEL", "Dismiss", "photo_intent_cancel",
                    () => { _photoCallbacks.ResolveIntentChoice(PhotoIntentChoice.Cancel); _navigation.HideModal(); }),
            };
        }

        // Initial entry: camera/gallery selection
        if (BaristaPlatformServices.TryGet(out _, out _))
        {
            return new VStack(spacing: CoffeeSpacing.Divider)
            {
                ModalRow("CAMERA", "Take a photo", "photo_intent_camera",
                    () => { _navigation.HideModal(); LaunchPhotoWorkflow(PhotoSource.Camera); }),
                ModalRow("GALLERY", "Choose from library", "photo_intent_gallery",
                    () => { _navigation.HideModal(); LaunchPhotoWorkflow(PhotoSource.Gallery); }),
            };
        }

        return new VStack(spacing: CoffeeSpacing.Divider)
        {
            ModalRow(
                "CAMERA",
                "Camera unavailable — tap for details",
                "photo_intent_camera",
                () => { _navigation.HideModal(); LaunchPhotoWorkflow(PhotoSource.Camera); }),
            ModalRow(
                "GALLERY",
                "Gallery unavailable — tap for details",
                "photo_intent_gallery",
                () => { _navigation.HideModal(); LaunchPhotoWorkflow(PhotoSource.Gallery); }),
        };
    }

    void DismissModal(BaristaModalKind kind)
    {
        if (kind == BaristaModalKind.ActivityFilter)
            _activityPage.CancelFilters();
        if (kind == BaristaModalKind.AiSuggestions)
            DisposeAIAdvice();
        _navigation.HideModal();
    }

    void CloseAIAdvice()
    {
        DisposeAIAdvice();
        _navigation.HideModal();
    }

    void DisposeAIAdvice()
    {
        _aiAdvicePanel?.Dispose();
        _aiAdvicePanel = null;
    }

    protected override void Dispose(bool disposing)
    {
        BaristaServices? ownedServices = null;
        AppVoiceCallbacks? voiceCallbacks = null;
        if (disposing && !_disposed)
        {
            _disposed = true;
            DisposeAIAdvice();
            _photoWorkflowCancellation?.Cancel();
            _photoWorkflowCancellation?.Dispose();
            _photoWorkflowCancellation = null;
            _photoCallbacks?.Dispose();
            _photoCallbacks = null;
            _photoCoordinator = null;
            _photoWorkflowTask = null;
            voiceCallbacks = _voiceCallbacks;
            _voiceCallbacks = null;
            ownedServices = _services;
            _services = null;
        }
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (ownedServices is not null)
            {
                if (voiceCallbacks is null)
                    ownedServices.Dispose();
                else
                    _serviceDisposal = DisposeOwnedServicesAsync(ownedServices, voiceCallbacks);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { Dispose(); }
        finally { await _serviceDisposal; }
    }

    internal static async Task DisposeOwnedServicesAsync(
        BaristaServices services,
        AppVoiceCallbacks voiceCallbacks)
    {
        try
        {
            await BaristaVoiceIntegration.UnbindAsync(voiceCallbacks);
        }
        finally
        {
            services.Dispose();
        }
    }

    View RoomResultDialog() => new AlertDialog(
        _navigation.RoomResultOpen,
        text: new Text(() => _navigation.RoomResultMessage.Value).SecondaryText(),
        title: new Text(() => _navigation.RoomResultTitle.Value).SubHeadline(),
        confirmButton: new Button("OK", () => _navigation.RoomResultOpen.Value = false)
            .TextButton().Color(CoffeeTheme.PrimaryColor).CornerRadius(0));

    static View ModalRow(string label, string value, string automationId, Action onTap) =>
        new SectionListRow(label, () => value, automationId, onTap);
}
