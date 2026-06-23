using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core;

// Application menu inspection & invocation.
//
// Surfaces two layers of menus:
//   1. Cross-platform MAUI menus defined via Page.MenuBarItems (MenuBarItem /
//      MenuFlyoutItem / MenuFlyoutSubItem / MenuFlyoutSeparator). Inspectable and
//      invokable on every platform straight from Agent.Core.
//   2. The platform's native application menu (macOS NSMenu, Mac Catalyst UIKit menus)
//      via the IsNativeMenusSupported / GetNativeMenusAsync / InvokeNativeMenuAsync hooks,
//      which platform agents override.
public partial class DevFlowAgentService
{
    // ── Platform extensibility hooks ──

    /// <summary>
    /// Whether the platform exposes a native application menu (macOS AppKit NSMenu,
    /// Mac Catalyst UIKit menus). Override in platform-specific agents.
    /// </summary>
    protected virtual bool IsNativeMenusSupported => false;

    // The cross-platform MAUI MenuBarItems backbone works on every platform (returning a
    // valid, possibly-empty tree), so the menus capability is advertised everywhere. Per-layer
    // fidelity (native vs. MAUI) is reported by the ui.menus capability's maui/native flags.
    protected virtual bool IsMenusSupported => true;

    /// <summary>
    /// Returns the native application-menu payload, or <c>null</c> when the platform has
    /// no inspectable native menu. Override in platform agents (macOS AppKit / Mac Catalyst).
    /// </summary>
    protected virtual Task<object?> GetNativeMenusAsync() => Task.FromResult<object?>(null);

    /// <summary>
    /// Invokes a native menu item identified by the request. Returns a result object on
    /// success, or <c>null</c> when no matching native item was found (or the platform
    /// does not support native menu invocation). Override in platform agents.
    /// </summary>
    protected virtual Task<object?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => Task.FromResult<object?>(null);

    // ── HTTP handlers ──

    private async Task<HttpResponse> HandleMenusList(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var windowIndex = ParseWindowIndex(request);

        var menuBar = await DispatchAsync(() => BuildMauiMenuBar(windowIndex));
        var native = IsNativeMenusSupported ? await GetNativeMenusAsync() : null;

        return HttpResponse.Json(new
        {
            platform = PlatformName,
            mauiSupported = true,
            nativeSupported = IsNativeMenusSupported,
            menuBar,
            native,
        });
    }

    private async Task<HttpResponse> HandleMenuInvoke(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<MenuInvokeRequest>();
        if (body == null ||
            (string.IsNullOrWhiteSpace(body.Id) &&
             string.IsNullOrWhiteSpace(body.Path) &&
             string.IsNullOrWhiteSpace(body.Title) &&
             string.IsNullOrWhiteSpace(body.Key)))
        {
            return HttpResponse.Error("One of id, path, title, or key is required");
        }

        var target = (body.Target ?? "auto").Trim().ToLowerInvariant();
        if (target is not ("auto" or "maui" or "native"))
            return HttpResponse.Error("target must be 'auto', 'maui', or 'native'");

        var startedAtUtc = DateTime.UtcNow;
        var label = body.Id ?? body.Path ?? body.Title ?? body.Key;

        // Layer 1: cross-platform MAUI MenuBarItems.
        if (target is "auto" or "maui")
        {
            var mauiResult = await DispatchAsync(() => TryInvokeMauiMenu(body));
            if (mauiResult.Success)
            {
                PublishUiOperationSpan("action.menu", startedAtUtc, true, null, label, new { source = "maui" });
                return HttpResponse.Json(new { success = true, source = "maui", title = mauiResult.Title, path = mauiResult.Path });
            }

            if (target == "maui")
            {
                PublishUiOperationSpan("action.menu", startedAtUtc, false, mauiResult.Error, label, new { source = "maui" });
                return HttpResponse.Error(mauiResult.Error ?? "Menu item not found", 404, "not-found");
            }
        }

        // Layer 2: native platform menu.
        if (target is "auto" or "native")
        {
            if (IsNativeMenusSupported)
            {
                var nativeResult = await InvokeNativeMenuAsync(body);
                if (nativeResult != null)
                {
                    PublishUiOperationSpan("action.menu", startedAtUtc, true, null, label, new { source = "native" });
                    return HttpResponse.Json(nativeResult);
                }
            }
            else if (target == "native")
            {
                return HttpResponse.Error($"Native menu invocation is not supported on {PlatformName}", 501, "unsupported-capability");
            }
        }

        PublishUiOperationSpan("action.menu", startedAtUtc, false, "not found", label, new { target });
        return HttpResponse.Error("Menu item not found", 404, "not-found");
    }

    // ── MAUI menu walk ──

    private object BuildMauiMenuBar(int? windowIndex)
    {
        var groups = new List<object>();

        foreach (var (window, wIndex) in ResolveMenuWindows(windowIndex))
        {
            var barItems = GetMenuBarItemsForWindow(window);
            if (barItems == null) continue;

            for (var b = 0; b < barItems.Count; b++)
            {
                var bar = barItems[b];
                if (bar == null) continue;

                var id = $"maui:w{wIndex}/{b}";
                var title = bar.Text ?? string.Empty;
                groups.Add(new Dictionary<string, object?>
                {
                    ["id"] = id,
                    ["title"] = title,
                    ["path"] = title,
                    ["enabled"] = true,
                    ["separator"] = false,
                    ["hasSubmenu"] = true,
                    ["items"] = BuildMauiChildren(bar, id, title),
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["source"] = "maui",
            ["items"] = groups,
        };
    }

    private List<object> BuildMauiChildren(IEnumerable<IMenuElement> elements, string idPrefix, string pathPrefix)
    {
        var items = new List<object>();
        var i = 0;
        foreach (var element in elements)
        {
            var id = $"{idPrefix}/{i}";
            i++;

            switch (element)
            {
                case MenuFlyoutSeparator:
                    items.Add(new Dictionary<string, object?> { ["id"] = id, ["separator"] = true });
                    break;

                case MenuFlyoutSubItem sub:
                {
                    var title = sub.Text ?? string.Empty;
                    var path = CombineMenuPath(pathPrefix, title);
                    items.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["title"] = title,
                        ["path"] = path,
                        ["enabled"] = sub.IsEnabled,
                        ["separator"] = false,
                        ["hasSubmenu"] = true,
                        ["items"] = BuildMauiChildren(sub, id, path),
                    });
                    break;
                }

                case MenuFlyoutItem flyout:
                {
                    var title = flyout.Text ?? string.Empty;
                    var (key, mods) = GetAcceleratorInfo(flyout);
                    items.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["title"] = title,
                        ["path"] = CombineMenuPath(pathPrefix, title),
                        ["enabled"] = flyout.IsEnabled,
                        ["separator"] = false,
                        ["hasSubmenu"] = false,
                        ["key"] = key,
                        ["modifiers"] = mods,
                    });
                    break;
                }

                case MenuItem menuItem:
                {
                    var title = menuItem.Text ?? string.Empty;
                    items.Add(new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["title"] = title,
                        ["path"] = CombineMenuPath(pathPrefix, title),
                        ["enabled"] = menuItem.IsEnabled,
                        ["separator"] = false,
                        ["hasSubmenu"] = false,
                    });
                    break;
                }
            }
        }

        return items;
    }

    private MenuInvokeResult TryInvokeMauiMenu(MenuInvokeRequest request)
    {
        foreach (var (window, wIndex) in ResolveMenuWindows(null))
        {
            var barItems = GetMenuBarItemsForWindow(window);
            if (barItems == null) continue;

            for (var b = 0; b < barItems.Count; b++)
            {
                var bar = barItems[b];
                if (bar == null) continue;

                var match = FindMauiMatch(bar, $"maui:w{wIndex}/{b}", bar.Text ?? string.Empty, request);
                if (match == null) continue;

                var (item, path) = match.Value;
                if (!item.IsEnabled)
                    return new MenuInvokeResult { Success = false, Error = $"Menu item '{path}' is disabled" };

                ((IMenuItemController)item).Activate();
                return new MenuInvokeResult { Success = true, Title = item.Text, Path = path };
            }
        }

        return new MenuInvokeResult { Success = false, Error = "Menu item not found" };
    }

    private (MenuItem item, string path)? FindMauiMatch(IEnumerable<IMenuElement> elements, string idPrefix, string pathPrefix, MenuInvokeRequest request)
    {
        var i = 0;
        foreach (var element in elements)
        {
            var id = $"{idPrefix}/{i}";
            i++;

            switch (element)
            {
                case MenuFlyoutSeparator:
                    break;

                case MenuFlyoutSubItem sub:
                {
                    var nested = FindMauiMatch(sub, id, CombineMenuPath(pathPrefix, sub.Text ?? string.Empty), request);
                    if (nested != null) return nested;
                    break;
                }

                case MenuItem menuItem:
                {
                    var path = CombineMenuPath(pathPrefix, menuItem.Text ?? string.Empty);
                    if (MauiItemMatches(menuItem, id, path, request))
                        return (menuItem, path);
                    break;
                }
            }
        }

        return null;
    }

    private static bool MauiItemMatches(MenuItem item, string id, string path, MenuInvokeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Id))
            return string.Equals(request.Id.Trim(), id, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Path))
            return string.Equals(NormalizeMenuPath(request.Path), path, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Title))
            return string.Equals(request.Title.Trim(), item.Text?.Trim(), StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Key))
        {
            if (item is not MenuFlyoutItem flyout) return false;
            var (key, mods) = GetAcceleratorInfo(flyout);
            if (key == null || !string.Equals(key, request.Key.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            var requested = ParseModifierList(request.Modifiers);
            var actual = new HashSet<string>(mods ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return requested.SetEquals(actual);
        }

        return false;
    }

    // ── Helpers ──

    private IEnumerable<(Window window, int index)> ResolveMenuWindows(int? windowIndex)
    {
        if (_app == null) yield break;

        if (windowIndex.HasValue)
        {
            if (windowIndex.Value >= 0 && windowIndex.Value < _app.Windows.Count && _app.Windows[windowIndex.Value] is Window scoped)
                yield return (scoped, windowIndex.Value);
            yield break;
        }

        for (var i = 0; i < _app.Windows.Count; i++)
            if (_app.Windows[i] is Window window)
                yield return (window, i);
    }

    private static IList<MenuBarItem>? GetMenuBarItemsForWindow(Window window)
    {
        var active = ResolveActiveMenuPage(window);
        var items = active?.MenuBarItems;
        if ((items == null || items.Count == 0) && window.Page is Page root && !ReferenceEquals(root, active))
            items = root.MenuBarItems;
        return items;
    }

    private static Page? ResolveActiveMenuPage(Window window)
    {
        var page = window.Page;
        if (page == null) return null;

        try
        {
            var modalStack = page.Navigation?.ModalStack;
            if (modalStack is { Count: > 0 } && modalStack[^1] is Page modal)
                return modal;
        }
        catch { }

        if (page is Shell shell && shell.CurrentPage is Page shellPage)
            return shellPage;

        try
        {
            var navStack = page.Navigation?.NavigationStack;
            if (navStack is { Count: > 0 } && navStack[^1] is Page navPage)
                return navPage;
        }
        catch { }

        return page;
    }

    private static (string? key, List<string>? modifiers) GetAcceleratorInfo(MenuFlyoutItem item)
    {
        var accelerator = item.KeyboardAccelerators?.FirstOrDefault();
        if (accelerator == null) return (null, null);

        var mods = ModifiersToList(accelerator.Modifiers);
        return (string.IsNullOrEmpty(accelerator.Key) ? null : accelerator.Key, mods.Count > 0 ? mods : null);
    }

    private static List<string> ModifiersToList(KeyboardAcceleratorModifiers modifiers)
    {
        var list = new List<string>();
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Cmd)) list.Add("cmd");
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Ctrl)) list.Add("ctrl");
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Alt)) list.Add("alt");
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Shift)) list.Add("shift");
        if (modifiers.HasFlag(KeyboardAcceleratorModifiers.Windows)) list.Add("windows");
        return list;
    }

    private static HashSet<string> ParseModifierList(string? modifiers)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(modifiers)) return set;

        foreach (var raw in modifiers.Split(new[] { ',', '+', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = NormalizeModifier(raw);
            if (normalized != null) set.Add(normalized);
        }

        return set;
    }

    private static string? NormalizeModifier(string modifier) => modifier.ToLowerInvariant() switch
    {
        "cmd" or "command" or "meta" or "super" => "cmd",
        "ctrl" or "control" => "ctrl",
        "alt" or "option" or "opt" => "alt",
        "shift" => "shift",
        "win" or "windows" => "windows",
        _ => null,
    };

    private static string CombineMenuPath(string prefix, string title)
        => string.IsNullOrEmpty(prefix) ? title : $"{prefix}/{title}";

    private static string NormalizeMenuPath(string path)
        => path.Replace('\\', '/').Trim().Trim('/');
}

/// <summary>Request body for invoking an application menu item.</summary>
public sealed class MenuInvokeRequest
{
    /// <summary>Stable id from the menu listing (e.g. <c>maui:w0/1/2</c>).</summary>
    public string? Id { get; set; }

    /// <summary>Slash-joined title path (e.g. <c>Account/Log Out</c>).</summary>
    public string? Path { get; set; }

    /// <summary>Menu item title (first match).</summary>
    public string? Title { get; set; }

    /// <summary>Key equivalent (e.g. <c>l</c>) used with <see cref="Modifiers"/>.</summary>
    public string? Key { get; set; }

    /// <summary>Comma/plus separated modifiers (e.g. <c>cmd,shift</c>).</summary>
    public string? Modifiers { get; set; }

    /// <summary>Which menu layer to target: <c>auto</c> (default), <c>maui</c>, or <c>native</c>.</summary>
    public string? Target { get; set; }
}

internal sealed class MenuInvokeResult
{
    public bool Success { get; set; }
    public string? Title { get; set; }
    public string? Path { get; set; }
    public string? Error { get; set; }
}
