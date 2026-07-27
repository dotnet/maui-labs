using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Controls;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.DevFlow.Tests;

public class NativeElementRegistrationRegistryTests
{
    [Fact]
    public void Register_SameNativeObject_PreservesStableIdAndUpdatesMetadata()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new object();
        var nativeElement = new object();

        var firstId = registry.Register(owner, nativeElement, "toolbar-item", "primary");
        var secondId = registry.Register(owner, nativeElement, "toolbar-item", "secondary");

        Assert.Equal(firstId, secondId);
        Assert.True(registry.TryGet(firstId, out var registration));
        Assert.Same(owner, registration.Owner);
        Assert.Same(nativeElement, registration.NativeElement);
        Assert.Equal("toolbar-item", registration.Role);
        Assert.Equal("secondary", registration.Discriminator);
    }

    [Fact]
    public void Register_DifferentNativeObjects_UsesDifferentIds()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new object();

        var firstId = registry.Register(owner, new object(), "tab");
        var secondId = registry.Register(owner, new object(), "tab");

        Assert.NotEqual(firstId, secondId);
        Assert.StartsWith("native:registered:", firstId, StringComparison.Ordinal);
        Assert.StartsWith("native:registered:", secondId, StringComparison.Ordinal);
    }

    [Fact]
    public void Register_SameNativeObjectWithNewOwner_ReplacesIdentity()
    {
        var registry = new NativeElementRegistrationRegistry();
        var firstOwner = new object();
        var secondOwner = new object();
        var nativeElement = new object();
        var firstId = registry.Register(firstOwner, nativeElement, "tab");

        var secondId = registry.Register(secondOwner, nativeElement, "tab");

        Assert.NotEqual(firstId, secondId);
        Assert.False(registry.TryGet(firstId, out _));
        Assert.True(registry.TryGet(secondId, out var registration));
        Assert.Same(secondOwner, registration.Owner);
    }

    [Fact]
    public void Generation_ChangesOnlyWhenRegistryStateChanges()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new object();
        var nativeElement = new object();

        var id = registry.Register(owner, nativeElement, "toolbar-item", "primary");
        var afterRegister = registry.Generation;

        Assert.Equal(id, registry.Register(owner, nativeElement, "toolbar-item", "primary"));
        Assert.Equal(afterRegister, registry.Generation);

        Assert.Equal(id, registry.Register(owner, nativeElement, "toolbar-item", "secondary"));
        Assert.True(registry.Generation > afterRegister);
        var afterUpdate = registry.Generation;

        Assert.True(registry.Unregister(nativeElement));
        Assert.True(registry.Generation > afterUpdate);
    }

    [Fact]
    public void Unregister_RemovesRegistration()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new object();
        var nativeElement = new object();
        var id = registry.Register(owner, nativeElement, "search-handler");

        Assert.True(registry.Unregister(nativeElement));
        Assert.False(registry.TryGet(id, out _));
        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void Unregister_ReRegisteringRecycledObject_UsesNewId()
    {
        var registry = new NativeElementRegistrationRegistry();
        var firstOwner = new object();
        var secondOwner = new object();
        var nativeElement = new object();
        var firstId = registry.Register(firstOwner, nativeElement, "tab");

        registry.Unregister(nativeElement);
        var secondId = registry.Register(secondOwner, nativeElement, "tab");

        Assert.NotEqual(firstId, secondId);
        Assert.False(registry.TryGet(firstId, out _));
        Assert.True(registry.TryGet(secondId, out var registration));
        Assert.Same(secondOwner, registration.Owner);
    }

    [Fact]
    public void DiagnosticSubscriber_ForwardsRegistrationLifecycle()
    {
        var registry = new NativeElementRegistrationRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        using var listener = new DiagnosticListener(MauiNativeElementDiagnosticSubscriber.ListenerName);
        var owner = new object();
        var nativeElement = new object();

        listener.Write(
            MauiNativeElementDiagnosticSubscriber.RegisteredEventName,
            new object?[]
            {
                MauiNativeElementDiagnosticSubscriber.ContractVersion,
                owner,
                nativeElement,
                "ToolbarItem",
                "RealizedView"
            });

        var registration = Assert.Single(registry.GetSnapshot());
        Assert.Same(owner, registration.Owner);
        Assert.Same(nativeElement, registration.NativeElement);
        Assert.Equal("ToolbarItem", registration.Role);
        Assert.Equal("RealizedView", registration.Discriminator);

        listener.Write(
            MauiNativeElementDiagnosticSubscriber.UnregisteredEventName,
            new object?[]
            {
                MauiNativeElementDiagnosticSubscriber.ContractVersion,
                nativeElement
            });

        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void DiagnosticSubscriber_AcceptsLegacyPayloads()
    {
        var registry = new NativeElementRegistrationRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        using var listener = new DiagnosticListener(MauiNativeElementDiagnosticSubscriber.ListenerName);
        var owner = new object();
        var nativeElement = new object();

        listener.Write(
            MauiNativeElementDiagnosticSubscriber.LegacyRegisteredEventName,
            new object?[] { owner, nativeElement, "ToolbarItem", "Legacy" });

        var registration = Assert.Single(registry.GetSnapshot());
        Assert.Equal("Legacy", registration.Discriminator);

        listener.Write(
            MauiNativeElementDiagnosticSubscriber.LegacyUnregisteredEventName,
            new object?[] { nativeElement });

        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void DiagnosticSubscriber_IgnoresMalformedRegistration()
    {
        var registry = new NativeElementRegistrationRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        using var listener = new DiagnosticListener(MauiNativeElementDiagnosticSubscriber.ListenerName);

        var exception = Record.Exception(() => listener.Write(
            MauiNativeElementDiagnosticSubscriber.RegisteredEventName,
            new object?[]
            {
                MauiNativeElementDiagnosticSubscriber.ContractVersion,
                new object(),
                new object(),
                " "
            }));

        Assert.Null(exception);
        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void DiagnosticSubscriber_IgnoresOtherListeners()
    {
        var registry = new NativeElementRegistrationRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        using var listener = new DiagnosticListener("Other.Listener");

        listener.Write(
            MauiNativeElementDiagnosticSubscriber.RegisteredEventName,
            new object?[] { new object(), new object(), "ToolbarItem", null });

        Assert.Empty(registry.GetSnapshot());
    }

    [Fact]
    public void GetSnapshot_ReturnsLiveRegistrations()
    {
        var registry = new NativeElementRegistrationRegistry();
        var firstOwner = new object();
        var secondOwner = new object();
        var firstNative = new object();
        var secondNative = new object();
        registry.Register(firstOwner, firstNative, "toolbar-item", "primary");
        registry.Register(secondOwner, secondNative, "shell-section", "tab");

        var registrations = registry.GetSnapshot();

        Assert.Collection(
            registrations.OrderBy(registration => registration.Role, StringComparer.Ordinal),
            registration =>
            {
                Assert.Equal("shell-section", registration.Role);
                Assert.Same(secondOwner, registration.Owner);
                Assert.Same(secondNative, registration.NativeElement);
            },
            registration =>
            {
                Assert.Equal("toolbar-item", registration.Role);
                Assert.Same(firstOwner, registration.Owner);
                Assert.Same(firstNative, registration.NativeElement);
            });
    }

    [Fact]
    public void WalkTree_RegisteredNativeElement_AddsSelectableNativeNode()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new ToolbarItem { Text = "Settings" };
        var nativeElement = new object();
        var id = registry.Register(owner, nativeElement, "ToolbarItem", "primary");
        var walker = new BoundsVisualTreeWalker(registry);
        var app = new Application();
        var page = new ContentPage();
        page.ToolbarItems.Add(owner);
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        var tree = walker.WalkTree(app);

        var nativeInfo = Assert.Single(
            VisualTreeWalker.FlattenElementInfos(tree),
            info => info.Id == id);
        Assert.Equal(id, nativeInfo.Id);
        Assert.NotNull(nativeInfo.OwnerId);
        Assert.Equal(nativeInfo.OwnerId, nativeInfo.ParentId);
        Assert.Equal("native", nativeInfo.Origin);
        Assert.Equal("toolbar-item", nativeInfo.Role);
        Assert.Equal("primary", nativeInfo.Discriminator);
        Assert.Equal("exact", nativeInfo.BoundsQuality);
        Assert.Equal(["select", "invoke"], nativeInfo.Capabilities);
        Assert.Same(nativeElement, walker.GetElementById(id, app));
        Assert.Same(nativeElement, walker.GetNativeElementById(id));
        Assert.Equal("toolbar-item", walker.GetNativeElementInfoById(id)?.Role);
        var simpleQueryInfo = Assert.Single(walker.Query(app, type: nameof(Object)), info => info.Id == id);
        Assert.Null(simpleQueryInfo.Children);
        Assert.Equal("native", simpleQueryInfo.Origin);
        Assert.Equal(nativeInfo.OwnerId, simpleQueryInfo.OwnerId);
        Assert.Equal("exact", simpleQueryInfo.BoundsQuality);
        var cssQueryInfo = Assert.Single(walker.QueryCss(app, nameof(Object)), info => info.Id == id);
        Assert.Null(cssQueryInfo.Children);
        Assert.Equal("native", cssQueryInfo.Origin);
        Assert.Equal(nativeInfo.OwnerId, cssQueryInfo.OwnerId);
        Assert.Equal("exact", cssQueryInfo.BoundsQuality);
        Assert.Equal(id, Assert.Single(walker.HitTestRegisteredNativeElements(20, 30)).Id);
        Assert.Empty(walker.HitTestRegisteredNativeElements(200, 300));

        registry.Register(owner, nativeElement, "UpdatedToolbarItem", "overflow");

        var updatedInfo = walker.GetNativeElementInfoById(id);
        Assert.Equal("updated-toolbar-item", updatedInfo?.Role);
        Assert.Equal("overflow", updatedInfo?.Discriminator);

        registry.Unregister(nativeElement);

        Assert.Null(walker.GetNativeElementInfoById(id));
    }

    [Fact]
    public void HitTestRegisteredNativeElements_FiltersOverlappingControlsByWindow()
    {
        var registry = new NativeElementRegistrationRegistry();
        var firstOwner = new ToolbarItem { Text = "First" };
        var secondOwner = new ToolbarItem { Text = "Second" };
        var firstId = registry.Register(firstOwner, new object(), "ToolbarItem");
        var secondId = registry.Register(secondOwner, new object(), "ToolbarItem");
        var firstPage = new ContentPage();
        firstPage.ToolbarItems.Add(firstOwner);
        var secondPage = new ContentPage();
        secondPage.ToolbarItems.Add(secondOwner);
        var app = new Application();
        var addWindow = typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        addWindow.Invoke(app, [new Window(firstPage)]);
        addWindow.Invoke(app, [new Window(secondPage)]);
        var walker = new BoundsVisualTreeWalker(registry);

        walker.WalkTree(app);

        Assert.Equal(firstId, Assert.Single(walker.HitTestRegisteredNativeElements(20, 30, 0)).Id);
        Assert.Equal(secondId, Assert.Single(walker.HitTestRegisteredNativeElements(20, 30, 1)).Id);
    }

    [Fact]
    public void HitTestRegisteredNativeElements_EqualBoundsPreferRicherCapabilities()
    {
        var registry = new NativeElementRegistrationRegistry();
        var toolbarOwner = new ToolbarItem { Text = "Toolbar" };
        var searchOwner = new SearchHandler();
        registry.Register(toolbarOwner, new object(), "ToolbarItem");
        var searchId = registry.Register(searchOwner, new object(), "SearchHandler");
        var page = new ContentPage();
        page.ToolbarItems.Add(toolbarOwner);
        Shell.SetSearchHandler(page, searchOwner);
        var app = new Application();
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [new Window(page)]);
        var walker = new BoundsVisualTreeWalker(registry);

        walker.WalkTree(app);

        Assert.Equal(searchId, walker.HitTestRegisteredNativeElements(20, 30)[0].Id);
    }

    [Fact]
    public void WalkTree_SearchHandlerOwner_AttachesToSyntheticNode()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new SearchHandler { Placeholder = "Search" };
        var nativeElement = new object();
        var id = registry.Register(owner, nativeElement, "SearchHandler");
        var walker = new BoundsVisualTreeWalker(registry);
        var app = new Application();
        var page = new ContentPage();
        Shell.SetSearchHandler(page, owner);
        var window = new Window(page);
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        var tree = walker.WalkTree(app);

        var nativeInfo = Assert.Single(
            VisualTreeWalker.FlattenElementInfos(tree),
            info => info.Id == id);
        Assert.NotNull(nativeInfo.OwnerId);
        var ownerInfo = Assert.Single(
            VisualTreeWalker.FlattenElementInfos(tree),
            info => info.Id == nativeInfo.OwnerId);
        Assert.Equal("SearchHandler", ownerInfo.Type);
        Assert.Equal(ownerInfo.Id, nativeInfo.ParentId);
        Assert.Equal("search-handler", nativeInfo.Role);
        Assert.Equal(["select", "invoke", "focus", "set-value"], nativeInfo.Capabilities);
    }

    [Fact]
    public void TryNativeElementSetValue_SearchHandler_UpdatesQuery()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new SearchHandler();
        var nativeElement = new object();
        var id = registry.Register(owner, nativeElement, "SearchHandler");
        var walker = new SetValueVisualTreeWalker(registry);

        var result = walker.TryNativeElementSetValue(id, "renewal");

        Assert.Equal("ok", result);
        Assert.Equal("renewal", owner.Query);
        Assert.Same(nativeElement, walker.NativeElement);
        Assert.Equal("renewal", walker.Value);
    }

    [Fact]
    public void TryNativeElementSetValue_SearchHandler_PreservesSuccessWhenNativeSyncFails()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new SearchHandler();
        var nativeElement = new object();
        var id = registry.Register(owner, nativeElement, "SearchHandler");
        var walker = new SetValueVisualTreeWalker(registry)
        {
            NativeResult = "Native search control is not enabled"
        };

        var result = walker.TryNativeElementSetValue(id, "renewal");

        Assert.Equal("ok", result);
        Assert.Equal("renewal", owner.Query);
        Assert.Same(nativeElement, walker.NativeElement);
        Assert.Equal("renewal", walker.Value);
    }

    [Fact]
    public void RegisteredShellTabOverflowItem_AdvertisesInvoke()
    {
        var registry = new NativeElementRegistrationRegistry();
        var owner = new ShellItem();
        var id = registry.Register(owner, new object(), "ShellTabOverflow", "TabBarItem");
        var walker = new VisualTreeWalker(registry);

        var info = walker.GetNativeElementInfoById(id);

        Assert.Equal(["select", "invoke"], info?.Capabilities);
    }

    [Fact]
    public void WalkTree_PasswordEntry_RedactsText()
    {
        var walker = new VisualTreeWalker();
        var app = new Application();
        var entry = new Entry
        {
            AutomationId = "Password",
            IsPassword = true,
            Text = "super-secret"
        };
        var window = new Window(new ContentPage { Content = entry });
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(app, [window]);

        var tree = walker.WalkTree(app);

        var info = Assert.Single(
            VisualTreeWalker.FlattenElementInfos(tree),
            element => element.AutomationId == "Password");
        Assert.Equal(SensitiveValueRedactor.RedactedValue, info.Text);
        Assert.DoesNotContain(
            "super-secret",
            System.Text.Json.JsonSerializer.Serialize(tree),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveValueRedactor_PreservesNonSensitiveValues()
    {
        Assert.Equal("visible", SensitiveValueRedactor.Redact("visible", isSensitive: false));
        Assert.Equal(
            SensitiveValueRedactor.RedactedValue,
            SensitiveValueRedactor.Redact("hidden", isSensitive: true));
    }

    [Fact]
    public void TryNativeElementTap_ToolbarItem_ExecutesCommand()
    {
        var registry = new NativeElementRegistrationRegistry();
        var invoked = false;
        var owner = new ToolbarItem
        {
            Text = "Add",
            Command = new Command(() => invoked = true)
        };
        var id = registry.Register(owner, new object(), "ToolbarItem");
        var walker = new VisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.True(invoked);
    }

    [Fact]
    public void TryNativeElementTap_UnsupportedOwner_UsesNativeFallback()
    {
        var registry = new NativeElementRegistrationRegistry();
        var nativeElement = new object();
        var id = registry.Register(new object(), nativeElement, "DialogAction");
        var walker = new FallbackVisualTreeWalker(registry);

        var info = walker.GetNativeElementInfoById(id);
        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.True(walker.FallbackInvoked);
        Assert.Contains("invoke", info!.Capabilities!);
    }

    [Fact]
    public void RegisteredLogicalDialogAction_AdvertisesAndExecutesInvoke()
    {
        var registry = new NativeElementRegistrationRegistry();
        var invoked = false;
        var logicalAction = new MenuItem
        {
            Text = "Accept",
            Command = new Command(() => invoked = true)
        };
        var id = registry.Register(
            new ContentPage(),
            logicalAction,
            "DialogAction",
            "LogicalModel");
        var walker = new VisualTreeWalker(registry);

        var info = walker.GetNativeElementInfoById(id);
        var result = walker.TryNativeElementTap(id);

        Assert.Equal("DialogAction", info?.Type);
        Assert.Equal("Accept", info?.Text);
        Assert.Equal(["select", "invoke"], info?.Capabilities);
        Assert.Equal("ok", result);
        Assert.True(invoked);
    }

    [Fact]
    public void TryNativeElementTap_DialogOwnedByTabbedPage_UsesNativeFallback()
    {
        var registry = new NativeElementRegistrationRegistry();
        var page = new ContentPage();
        var tabbedPage = (TabbedPage)RuntimeHelpers.GetUninitializedObject(typeof(TabbedPage));
        typeof(Element)
            .GetField("_realParent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new WeakReference<Element>(tabbedPage));
        var id = registry.Register(page, new object(), "DialogAction");
        var walker = new FallbackVisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.True(walker.FallbackInvoked);
    }

    [Fact]
    public void TryNativeElementTap_ShellOverflowRow_UsesNativeFallback()
    {
        var registry = new NativeElementRegistrationRegistry();
        var id = registry.Register(new ShellSection(), new object(), "ShellTabOverflow");
        var walker = new FallbackVisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.True(walker.FallbackInvoked);
    }

    [Fact]
    public void TryNativeElementTap_RealizedShellOverflowRow_ActivatesSection()
    {
        var shell = new Shell();
        var firstItem = CreateShellItem("First");
        var secondItem = CreateShellItem("Second");
        shell.Items.Add(firstItem);
        shell.Items.Add(secondItem);
        shell.CurrentItem = firstItem;
        var targetSection = secondItem.Items[0];
        var registry = new NativeElementRegistrationRegistry();
        var id = registry.Register(
            targetSection,
            new object(),
            "ShellTabOverflow",
            "OverflowRow");
        var walker = new VisualTreeWalker(registry);

        var info = walker.GetNativeElementInfoById(id);
        var result = walker.TryNativeElementTap(id);

        Assert.Equal(["select", "invoke"], info?.Capabilities);
        Assert.Equal("ok", result);
        Assert.Same(secondItem, shell.CurrentItem);
        Assert.Same(targetSection, secondItem.CurrentItem);
    }

    [Fact]
    public void TryNativeElementTap_ShellFlyoutItem_SelectsItemAndClosesFlyout()
    {
        var registry = new NativeElementRegistrationRegistry();
        var shell = new Shell();
        var home = CreateShellItem("Home");
        var settings = CreateShellItem("Settings");
        shell.Items.Add(home);
        shell.Items.Add(settings);
        shell.CurrentItem = home;
        shell.FlyoutIsPresented = true;
        var id = registry.Register(settings, new object(), "ShellFlyout");
        var walker = new VisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.Same(settings, shell.CurrentItem);
        Assert.False(shell.FlyoutIsPresented);
    }

    [Fact]
    public void TryNativeElementTap_LockedShellFlyout_DoesNotToggleOrClose()
    {
        var registry = new NativeElementRegistrationRegistry();
        var shell = new Shell { FlyoutBehavior = FlyoutBehavior.Locked };
        var home = CreateShellItem("Home");
        var settings = CreateShellItem("Settings");
        shell.Items.Add(home);
        shell.Items.Add(settings);
        shell.CurrentItem = home;
        shell.FlyoutIsPresented = true;
        var toggleId = registry.Register(shell, new object(), "ShellFlyoutToggle");
        var itemId = registry.Register(settings, new object(), "ShellFlyout");
        var walker = new FallbackVisualTreeWalker(registry);

        var toggleResult = walker.TryNativeElementTap(toggleId);
        var itemResult = walker.TryNativeElementTap(itemId);

        Assert.Contains("locked", toggleResult, StringComparison.OrdinalIgnoreCase);
        Assert.Same(settings, shell.CurrentItem);
        Assert.True(shell.FlyoutIsPresented);
        Assert.Equal("ok", itemResult);
        Assert.False(walker.FallbackInvoked);
    }

    [Fact]
    public void TryNativeElementTap_ShellSection_SelectsSectionAndParentItem()
    {
        var registry = new NativeElementRegistrationRegistry();
        var shell = new Shell();
        var shellItem = new ShellItem { Title = "Root" };
        var first = new ShellSection { Title = "First" };
        var second = new ShellSection { Title = "Second" };
        first.Items.Add(new ShellContent { Title = "First content" });
        second.Items.Add(new ShellContent { Title = "Second content" });
        shellItem.Items.Add(first);
        shellItem.Items.Add(second);
        shell.Items.Add(shellItem);
        shell.CurrentItem = shellItem;
        shellItem.CurrentItem = first;
        var id = registry.Register(second, new object(), "ShellTab");
        var walker = new VisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.Same(shellItem, shell.CurrentItem);
        Assert.Same(second, shellItem.CurrentItem);
    }

    [Fact]
    public void TryNativeElementTap_ShellContent_SelectsContentAndAncestors()
    {
        var registry = new NativeElementRegistrationRegistry();
        var shell = new Shell();
        var shellItem = new ShellItem { Title = "Root" };
        var shellSection = new ShellSection { Title = "Section" };
        var first = new ShellContent { Title = "First" };
        var second = new ShellContent { Title = "Second" };
        shellSection.Items.Add(first);
        shellSection.Items.Add(second);
        shellItem.Items.Add(shellSection);
        shell.Items.Add(shellItem);
        shell.CurrentItem = shellItem;
        shellItem.CurrentItem = shellSection;
        shellSection.CurrentItem = first;
        var id = registry.Register(second, new object(), "ShellTab");
        var walker = new VisualTreeWalker(registry);

        var result = walker.TryNativeElementTap(id);

        Assert.Equal("ok", result);
        Assert.Same(shellItem, shell.CurrentItem);
        Assert.Same(shellSection, shellItem.CurrentItem);
        Assert.Same(second, shellSection.CurrentItem);
    }

    [Fact]
    public async Task TryNativeElementTap_BackButton_ExecutesConfiguredCommand()
    {
        var registry = new NativeElementRegistrationRegistry();
        var invoked = false;
        var page = new ContentPage();
        Shell.SetBackButtonBehavior(page, new BackButtonBehavior
        {
            Command = new Command(() => invoked = true)
        });
        var id = registry.Register(page, new object(), "BackButton");
        var walker = new VisualTreeWalker(registry);

        var result = await walker.TryRegisteredNativeElementTapAsync(id);

        Assert.Equal("ok", result);
        Assert.True(invoked);
    }

    [Fact]
    public async Task TryNativeElementTap_BackButton_RejectsDisabledBehavior()
    {
        var registry = new NativeElementRegistrationRegistry();
        var page = new ContentPage();
        Shell.SetBackButtonBehavior(page, new BackButtonBehavior { IsEnabled = false });
        var id = registry.Register(page, new object(), "BackButton");
        var walker = new VisualTreeWalker(registry);

        var result = await walker.TryRegisteredNativeElementTapAsync(id);

        Assert.Contains("disabled", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TryNativeElementTap_BackButton_UsesShellLevelBehavior()
    {
        var registry = new NativeElementRegistrationRegistry();
        var invoked = false;
        var page = new ContentPage();
        var shellContent = new ShellContent { Content = page };
        var shellSection = new ShellSection();
        shellSection.Items.Add(shellContent);
        var shellItem = new ShellItem();
        shellItem.Items.Add(shellSection);
        var shell = new Shell();
        shell.Items.Add(shellItem);
        Shell.SetBackButtonBehavior(shell, new BackButtonBehavior
        {
            Command = new Command(() => invoked = true)
        });
        var id = registry.Register(page, new object(), "BackButton");
        var walker = new VisualTreeWalker(registry);

        var result = await walker.TryRegisteredNativeElementTapAsync(id);

        Assert.Equal("ok", result);
        Assert.True(invoked);
    }

    [Fact]
    public async Task ActivateBackButtonMarker_UsesConfiguredBehavior()
    {
        var invoked = false;
        var page = new ContentPage();
        var navigationPage = new NavigationPage(new ContentPage());
        await navigationPage.Navigation.PushAsync(page);
        var marker = new VisualTreeWalker.BackButtonMarker
        {
            Navigation = page.Navigation,
            Behavior = new BackButtonBehavior
            {
                Command = new Command(() => invoked = true)
            }
        };

        var result = await VisualTreeWalker.ActivateBackButtonMarkerAsync(marker);

        Assert.Equal("ok", result);
        Assert.True(invoked);
        Assert.Same(page, navigationPage.CurrentPage);
    }

    [Fact]
    public void BuildSyntheticElementInfo_BackButton_ReflectsBehaviorState()
    {
        var walker = new VisualTreeWalker();
        var marker = new VisualTreeWalker.BackButtonMarker
        {
            Navigation = new NavigationPage().Navigation,
            Behavior = new BackButtonBehavior
            {
                IsVisible = false,
                IsEnabled = false
            }
        };

        var info = walker.BuildSyntheticElementInfo("BackButton", marker);

        Assert.NotNull(info);
        Assert.False(info.IsVisible);
        Assert.False(info.IsEnabled);
    }

    [Fact]
    public async Task ActivateBackButtonMarker_RejectsHiddenNavigationBar()
    {
        var first = new ContentPage();
        var page = new ContentPage();
        var navigationPage = new NavigationPage(first);
        await navigationPage.Navigation.PushAsync(page);
        NavigationPage.SetHasNavigationBar(page, false);
        var marker = new VisualTreeWalker.BackButtonMarker
        {
            Navigation = page.Navigation,
            Page = page
        };

        var result = await VisualTreeWalker.ActivateBackButtonMarkerAsync(marker);

        Assert.Contains("hidden", result, StringComparison.OrdinalIgnoreCase);
        Assert.Same(page, navigationPage.CurrentPage);
    }

    private static ShellItem CreateShellItem(string title)
    {
        var shellItem = new ShellItem { Title = title };
        var shellSection = new ShellSection { Title = $"{title} section" };
        shellSection.Items.Add(new ShellContent { Title = $"{title} content" });
        shellItem.Items.Add(shellSection);
        return shellItem;
    }

    private sealed class BoundsVisualTreeWalker : VisualTreeWalker
    {
        public BoundsVisualTreeWalker(NativeElementRegistrationRegistry registry)
            : base(registry)
        {
        }

        internal override ElementInfo CreateRegisteredNativeElementInfo(
            NativeElementRegistrationSnapshot registration,
            string? ownerId)
        {
            var info = base.CreateRegisteredNativeElementInfo(registration, ownerId);
            info.WindowBounds = new BoundsInfo
            {
                X = 10,
                Y = 20,
                Width = 30,
                Height = 40
            };
            info.BoundsQuality = "exact";
            return info;
        }
    }

    private sealed class FallbackVisualTreeWalker : VisualTreeWalker
    {
        public FallbackVisualTreeWalker(NativeElementRegistrationRegistry registry)
            : base(registry)
        {
        }

        public bool FallbackInvoked { get; private set; }

        protected override bool CanInvokeRegisteredNativeElement(object nativeElement)
            => true;

        protected override string? TryInvokeRegisteredNativeElement(
            string elementId,
            object nativeElement)
        {
            FallbackInvoked = true;
            return "ok";
        }
    }

    private sealed class SetValueVisualTreeWalker : VisualTreeWalker
    {
        public SetValueVisualTreeWalker(NativeElementRegistrationRegistry registry)
            : base(registry)
        {
        }

        public object? NativeElement { get; private set; }
        public string? Value { get; private set; }
        public string? NativeResult { get; set; } = "ok";

        protected override string? TrySetValueRegisteredNativeElement(
            string elementId,
            object nativeElement,
            string value)
        {
            NativeElement = nativeElement;
            Value = value;
            return NativeResult;
        }
    }
}
