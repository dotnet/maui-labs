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
public partial class MauiDevFlowAgentService
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
    protected override bool IsMenusSupported => true;

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
    protected virtual Task<MenuInvokeResult?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => Task.FromResult<MenuInvokeResult?>(null);

    // ── HTTP handlers ──

    protected override async Task<HttpResponse> HandleMenusList(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var windowIndex = ParseWindowIndex(request);
        if (request.QueryParams.ContainsKey("window") &&
            (!windowIndex.HasValue || windowIndex < 0 || windowIndex >= _app.Windows.Count))
            return HttpResponse.Error("window must identify an existing window");

        var menuBar = await DispatchAsync(() => BuildMauiMenuBar(windowIndex));
        var native = IsNativeMenusSupported ? await GetNativeMenusAsync() : null;

        return HttpResponse.Json(new Dictionary<string, object?>
        {
            ["platform"] = PlatformName,
            ["mauiSupported"] = true,
            ["nativeSupported"] = IsNativeMenusSupported,
            ["menuBar"] = menuBar,
            ["native"] = native,
        });
    }

    protected override async Task<HttpResponse> HandleMenuInvoke(HttpRequest request)
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
        if (body.Window is < 0 || body.Window >= _app.Windows.Count)
            return HttpResponse.Error("window must identify an existing window");
        if (!string.IsNullOrWhiteSpace(body.Modifiers) &&
            body.Modifiers.Split(new[] { ',', '+', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(modifier => NormalizeModifier(modifier) == null))
            return HttpResponse.Error("Unknown keyboard modifier");

        var startedAtUtc = DateTime.UtcNow;
        var label = body.Id ?? body.Path ?? body.Title ?? body.Key;

        // Layer 1: cross-platform MAUI MenuBarItems.
        if (target is "auto" or "maui")
        {
            var mauiResult = await DispatchAsync(() => TryInvokeMauiMenu(body));
            if (mauiResult != null)
            {
                return CompleteMenuInvoke(mauiResult, startedAtUtc, label);
            }

            if (target == "maui")
            {
                PublishUiOperationSpan("action.menu", startedAtUtc, false, "Menu item not found", label);
                return HttpResponse.Error("Menu item not found", 404, "not-found");
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
                    return CompleteMenuInvoke(nativeResult, startedAtUtc, label);
                }
            }
            else if (target == "native")
            {
                return NotSupported("ui.menus", $"Native menu invocation is not supported on {PlatformName}");
            }
        }

        PublishUiOperationSpan("action.menu", startedAtUtc, false, "not found", label);
        return HttpResponse.Error("Menu item not found", 404, "not-found");
    }

    protected async Task<MenuInvokeResult?> DispatchMenuActionAsync(Func<MenuInvokeResult?> action, string source)
    {
        var state = 0;
        var invocation = DispatchAsync(() =>
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
                return new MenuInvokeResult { Source = source, Error = "Menu invocation timed out before dispatch" };
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] Menu invocation failed: {ex}");
                return new MenuInvokeResult { Source = source, Error = ex.Message };
            }
        });

        if (await Task.WhenAny(invocation, Task.Delay(TimeSpan.FromSeconds(5))) == invocation || invocation.IsCompleted)
            return await invocation;

        if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            return new MenuInvokeResult { Source = source, Error = "Menu invocation timed out before dispatch" };

        // A synchronous native action may run a modal event loop. Report dispatch, not
        // completion, so automation can inspect/dismiss the dialog without invoking twice.
        return new MenuInvokeResult { Success = true, Source = source, Status = "dispatched" };
    }

    private HttpResponse CompleteMenuInvoke(MenuInvokeResult result, DateTime startedAtUtc, string? label)
    {
        PublishUiOperationSpan("action.menu", startedAtUtc, result.Success, result.Error, label,
            new Dictionary<string, object?> { ["source"] = result.Source });
        if (!result.Success)
            return HttpResponse.Error(result.Error ?? "Menu action was not handled", 409, "menu-not-invokable");

        return HttpResponse.Json(new Dictionary<string, object?>
        {
            ["success"] = true,
            ["source"] = result.Source,
            ["title"] = result.Title,
            ["path"] = result.Path,
            ["action"] = result.Action,
            ["status"] = result.Status,
        }, result.Status == "dispatched" ? 202 : 200);
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
                    ["path"] = EscapeMenuTitle(title),
                    ["enabled"] = bar.IsEnabled,
                    ["separator"] = false,
                    ["hasSubmenu"] = true,
                    ["items"] = BuildMauiChildren(bar, id, EscapeMenuTitle(title), bar.IsEnabled),
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["source"] = "maui",
            ["items"] = groups,
        };
    }

    private List<object> BuildMauiChildren(IEnumerable<IMenuElement> elements, string idPrefix, string pathPrefix, bool parentEnabled)
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
                        ["enabled"] = parentEnabled && sub.IsEnabled,
                        ["separator"] = false,
                        ["hasSubmenu"] = true,
                        ["items"] = BuildMauiChildren(sub, id, path, parentEnabled && sub.IsEnabled),
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
                        ["enabled"] = parentEnabled && flyout.IsEnabled,
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
                        ["enabled"] = parentEnabled && menuItem.IsEnabled,
                        ["separator"] = false,
                        ["hasSubmenu"] = false,
                    });
                    break;
                }
            }
        }

        return items;
    }

    private MenuInvokeResult? TryInvokeMauiMenu(MenuInvokeRequest request)
    {
        foreach (var (window, wIndex) in ResolveMenuWindows(request.Window))
        {
            var barItems = GetMenuBarItemsForWindow(window);
            if (barItems == null) continue;

            for (var b = 0; b < barItems.Count; b++)
            {
                var bar = barItems[b];
                if (bar == null) continue;

                var match = FindMauiMatch(bar, $"maui:w{wIndex}/{b}", EscapeMenuTitle(bar.Text ?? string.Empty), request, bar.IsEnabled);
                if (match == null) continue;

                var (item, path, enabled) = match.Value;
                if (!enabled)
                    return new MenuInvokeResult { Success = false, Source = "maui", Error = $"Menu item '{path}' is disabled" };

                ((IMenuItemController)item).Activate();
                return new MenuInvokeResult { Success = true, Source = "maui", Title = item.Text, Path = path };
            }
        }

        return null;
    }

    private (MenuItem item, string path, bool enabled)? FindMauiMatch(IEnumerable<IMenuElement> elements, string idPrefix, string pathPrefix, MenuInvokeRequest request, bool parentEnabled)
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
                    var nested = FindMauiMatch(sub, id, CombineMenuPath(pathPrefix, sub.Text ?? string.Empty), request, parentEnabled && sub.IsEnabled);
                    if (nested != null) return nested;
                    break;
                }

                case MenuItem menuItem:
                {
                    var path = CombineMenuPath(pathPrefix, menuItem.Text ?? string.Empty);
                    if (MauiItemMatches(menuItem, id, path, request))
                        return (menuItem, path, parentEnabled && menuItem.IsEnabled);
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
            var requested = ParseModifierList(request.Modifiers);
            return flyout.KeyboardAccelerators.Any(accelerator =>
                string.Equals(accelerator.Key, request.Key.Trim(), StringComparison.OrdinalIgnoreCase) &&
                requested.SetEquals(ModifiersToList(accelerator.Modifiers)));
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
        var seen = new HashSet<Page>();
        while (page != null && seen.Add(page))
        {
            var modal = page.Navigation.ModalStack.LastOrDefault();
            if (modal != null && seen.Contains(modal))
                modal = null;
            var next = modal ?? page switch
            {
                Shell shell => shell.CurrentPage,
                NavigationPage navigation => navigation.CurrentPage,
                TabbedPage tabs => tabs.CurrentPage,
                FlyoutPage flyout => flyout.Detail,
                _ => null,
            };
            if (next == null || ReferenceEquals(next, page)) break;
            page = next;
        }
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

    protected static HashSet<string> ParseModifierList(string? modifiers)
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

    protected static string EscapeMenuTitle(string title)
        => title.Replace("%", "%25").Replace("/", "%2F").Replace("\\", "%5C");

    protected static string CombineMenuPath(string prefix, string title)
        => string.IsNullOrEmpty(prefix) ? EscapeMenuTitle(title) : $"{prefix}/{EscapeMenuTitle(title)}";

    private static string NormalizeMenuPath(string path)
        => path.Replace('\\', '/').Trim().Trim('/');

    protected sealed class MenuInvokeResult
    {
        public bool Success { get; set; }
        public string? Source { get; set; }
        public string? Title { get; set; }
        public string? Path { get; set; }
        public string? Action { get; set; }
        public string? Error { get; set; }
        public string? Status { get; set; }
    }
}

/// <summary>Request body for invoking an application menu item.</summary>
public sealed class MenuInvokeRequest
{
    /// <summary>Positional id from a fresh menu listing (e.g. <c>maui:w0/1/2</c>).</summary>
    public string? Id { get; set; }

    /// <summary>Menu path copied from the listing (e.g. <c>Account/Log Out</c>).</summary>
    public string? Path { get; set; }

    /// <summary>Menu item title (first match).</summary>
    public string? Title { get; set; }

    /// <summary>Key equivalent (e.g. <c>l</c>) used with <see cref="Modifiers"/>.</summary>
    public string? Key { get; set; }

    /// <summary>Comma/plus separated modifiers (e.g. <c>cmd,shift</c>).</summary>
    public string? Modifiers { get; set; }

    /// <summary>Which menu layer to target: <c>auto</c> (default), <c>maui</c>, or <c>native</c>.</summary>
    public string? Target { get; set; }

    /// <summary>Optional window index to scope MAUI menu invocation.</summary>
    public int? Window { get; set; }
}
