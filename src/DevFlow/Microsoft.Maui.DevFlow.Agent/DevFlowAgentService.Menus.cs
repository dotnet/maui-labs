using Microsoft.Maui.DevFlow.Agent.Core;
#if MACOS
using AppKit;
using Foundation;
using ObjCRuntime;
#endif
#if MACCATALYST
using UIKit;
using Foundation;
using ObjCRuntime;
#endif

namespace Microsoft.Maui.DevFlow.Agent;

public partial class PlatformAgentService
{
#if MACOS
    // macOS AppKit exposes the application menu bar as a global NSMenu tree, which is not
    // part of the MAUI visual tree. Walk and invoke it directly via AppKit.
    protected override bool IsNativeMenusSupported => true;

    protected override Task<object?> GetNativeMenusAsync()
        => DispatchAsync(() => BuildNativeMainMenu());

    protected override Task<MenuInvokeResult?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => DispatchMenuActionAsync(() => InvokeNativeMenuItem(request), "appkit");

    private object? BuildNativeMainMenu()
    {
        var mainMenu = NSApplication.SharedApplication.MainMenu;
        if (mainMenu == null) return null;

        return new Dictionary<string, object?>
        {
            ["source"] = "appkit",
            ["title"] = mainMenu.Title ?? string.Empty,
            ["items"] = BuildNativeMenuItems(mainMenu, "native:", string.Empty),
        };
    }

    private List<object> BuildNativeMenuItems(NSMenu menu, string idPrefix, string pathPrefix, bool parentEnabled = true)
    {
        menu.Update();
        var items = new List<object>();
        for (nint i = 0; i < menu.Count; i++)
        {
            var item = menu.ItemAt(i);
            if (item == null) continue;

            var id = $"{idPrefix}{i}";
            if (item.IsSeparatorItem)
            {
                items.Add(new Dictionary<string, object?> { ["id"] = id, ["separator"] = true });
                continue;
            }

            var title = item.Title ?? string.Empty;
            var path = CombineMenuPath(pathPrefix, title);
            var node = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["title"] = title,
                ["path"] = path,
                ["enabled"] = parentEnabled && item.Enabled,
                ["hidden"] = item.Hidden,
                ["separator"] = false,
                ["state"] = NativeStateToString(item.State),
                ["action"] = item.Action?.Name,
            };

            var key = item.KeyEquivalent;
            if (!string.IsNullOrEmpty(key))
            {
                node["key"] = key;
                node["modifiers"] = NativeModifiersToList(item.KeyEquivalentModifierMask);
            }

            if (item.Submenu is NSMenu submenu)
            {
                node["hasSubmenu"] = true;
                node["items"] = BuildNativeMenuItems(submenu, $"{id}/", path, parentEnabled && item.Enabled && !item.Hidden);
            }
            else
            {
                node["hasSubmenu"] = false;
            }

            items.Add(node);
        }

        return items;
    }

    private MenuInvokeResult? InvokeNativeMenuItem(MenuInvokeRequest request)
    {
        var mainMenu = NSApplication.SharedApplication.MainMenu;
        if (mainMenu == null) return null;

        return FindAndInvokeNative(mainMenu, "native:", string.Empty, request);
    }

    private MenuInvokeResult? FindAndInvokeNative(NSMenu menu, string idPrefix, string pathPrefix, MenuInvokeRequest request, bool parentEnabled = true)
    {
        menu.Update();
        for (nint i = 0; i < menu.Count; i++)
        {
            var item = menu.ItemAt(i);
            if (item == null) continue;

            var id = $"{idPrefix}{i}";
            if (item.IsSeparatorItem)
            {
                if (string.Equals(request.Id?.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return new MenuInvokeResult { Source = "appkit", Error = "Menu separators have no invokable action" };
                continue;
            }
            var path = CombineMenuPath(pathPrefix, item.Title ?? string.Empty);

            var exactSelector = !string.IsNullOrWhiteSpace(request.Id) || !string.IsNullOrWhiteSpace(request.Path);
            if ((exactSelector || (!item.Hidden && item.Submenu == null)) && NativeItemMatches(item, id, path, request))
            {
                if (item.Hidden)
                    return new MenuInvokeResult { Source = "appkit", Error = $"Menu item '{path}' is hidden" };
                if (!parentEnabled || !item.Enabled)
                    return new MenuInvokeResult
                    {
                        Success = false,
                        Source = "appkit",
                        Title = item.Title,
                        Path = path,
                        Error = $"Menu item '{path}' is disabled",
                    };

                if (item.Submenu != null || item.Action is not Selector action)
                    return new MenuInvokeResult
                    {
                        Source = "appkit",
                        Error = $"Menu item '{path}' has no invokable action",
                    };

                var invoked = NSApplication.SharedApplication.SendAction(action, item.Target, item);
                return new MenuInvokeResult
                {
                    Success = invoked,
                    Source = "appkit",
                    Title = item.Title,
                    Path = path,
                    Action = action.Name,
                    Error = invoked ? null : $"Menu action '{path}' was not handled",
                };
            }

            if (item.Submenu is NSMenu submenu)
            {
                var nested = FindAndInvokeNative(submenu, $"{id}/", path, request, parentEnabled && item.Enabled && !item.Hidden);
                if (nested != null) return nested;
            }
        }

        return null;
    }

    private static bool NativeItemMatches(NSMenuItem item, string id, string path, MenuInvokeRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Id))
            return string.Equals(request.Id.Trim(), id, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Path))
            return string.Equals(NormalizeNativePath(request.Path), path, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Title))
            return string.Equals(request.Title.Trim(), item.Title?.Trim(), StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(request.Key))
        {
            var key = item.KeyEquivalent;
            if (string.IsNullOrEmpty(key) || !string.Equals(key, request.Key.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;

            var requested = ParseModifierList(request.Modifiers);
            var actual = new HashSet<string>(NativeModifiersToList(item.KeyEquivalentModifierMask), StringComparer.OrdinalIgnoreCase);
            return requested.SetEquals(actual);
        }

        return false;
    }

    private static List<string> NativeModifiersToList(NSEventModifierMask mask)
    {
        var list = new List<string>();
        if (mask.HasFlag(NSEventModifierMask.CommandKeyMask)) list.Add("cmd");
        if (mask.HasFlag(NSEventModifierMask.ControlKeyMask)) list.Add("ctrl");
        if (mask.HasFlag(NSEventModifierMask.AlternateKeyMask)) list.Add("alt");
        if (mask.HasFlag(NSEventModifierMask.ShiftKeyMask)) list.Add("shift");
        return list;
    }

    private static string? NativeStateToString(NSCellStateValue state) => state switch
    {
        NSCellStateValue.On => "on",
        NSCellStateValue.Mixed => "mixed",
        _ => null,
    };
#endif

#if MACCATALYST
    // Mac Catalyst exposes menus through UIKit. UIKit has no public runtime API to read the
    // built main menu tree (UIMenuBuilder is build-time only), so we surface the responder
    // chain's UIKeyCommands as a best-effort inspection surface and invoke through the
    // responder chain via SendAction. MAUI-defined menus are covered by the cross-platform
    // MAUI backbone in Agent.Core.
    protected override bool IsNativeMenusSupported => true;

    protected override Task<object?> GetNativeMenusAsync()
        => DispatchAsync(() => BuildCatalystKeyCommands());

    protected override Task<MenuInvokeResult?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => DispatchMenuActionAsync(() => InvokeCatalystKeyCommand(request), "uikit");

    private object? BuildCatalystKeyCommands()
    {
        var commands = CollectKeyCommands();
        var items = new List<object>();
        var index = 0;
        foreach (var (command, owner) in commands)
        {
            // Input is an NSString; the wire payload must contain a managed string.
            string? input = command.Input;
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = $"native:{index}",
                ["title"] = command.Title,
                ["path"] = command.Title,
                ["enabled"] = !command.Attributes.HasFlag(UIMenuElementAttributes.Disabled) &&
                    command.Action is Selector action && owner.CanPerform(action, command),
                ["separator"] = false,
                ["hasSubmenu"] = false,
                ["key"] = string.IsNullOrEmpty(input) ? null : input,
                ["modifiers"] = CatalystModifiersToList(command.ModifierFlags),
                ["action"] = command.Action?.Name,
            });
            index++;
        }

        return new Dictionary<string, object?>
        {
            ["source"] = "uikit",
            ["note"] = "UIKit exposes only responder-chain key commands at runtime; use the MAUI menuBar for the full menu definition.",
            ["items"] = items,
        };
    }

    private MenuInvokeResult? InvokeCatalystKeyCommand(MenuInvokeRequest request)
    {
        var commands = CollectKeyCommands();
        var requestedModifiers = ParseModifierList(request.Modifiers);

        var index = 0;
        foreach (var (command, owner) in commands)
        {
            var id = $"native:{index}";
            index++;

            var matches = false;
            if (!string.IsNullOrWhiteSpace(request.Id))
                matches = string.Equals(request.Id.Trim(), id, StringComparison.OrdinalIgnoreCase);
            else if (!string.IsNullOrWhiteSpace(request.Title) || !string.IsNullOrWhiteSpace(request.Path))
            {
                var wanted = (!string.IsNullOrWhiteSpace(request.Title) ? request.Title : request.Path)!.Trim();
                matches = string.Equals(wanted, command.Title?.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            else if (!string.IsNullOrWhiteSpace(request.Key))
            {
                if (string.Equals(command.Input, request.Key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    var actual = new HashSet<string>(CatalystModifiersToList(command.ModifierFlags), StringComparer.OrdinalIgnoreCase);
                    matches = requestedModifiers.SetEquals(actual);
                }
            }

            if (!matches) continue;
            if (command.Action is not Selector action)
                return new MenuInvokeResult { Source = "uikit", Error = $"Menu item '{command.Title}' has no invokable action" };

            if (command.Attributes.HasFlag(UIMenuElementAttributes.Disabled) || !owner.CanPerform(action, command))
                return new MenuInvokeResult { Source = "uikit", Error = $"Menu item '{command.Title}' is disabled" };

            var invoked = UIApplication.SharedApplication.SendAction(action, owner, command, null);
            return new MenuInvokeResult
            {
                Success = invoked,
                Source = "uikit",
                Title = command.Title,
                Path = command.Title,
                Action = action.Name,
                Error = invoked ? null : $"Menu action '{command.Title}' was not handled",
            };
        }

        return null;
    }

    private static List<(UIKeyCommand Command, UIResponder Owner)> CollectKeyCommands()
    {
        var collected = new List<(UIKeyCommand, UIResponder)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(UIResponder? responder)
        {
            if (responder?.KeyCommands is { } keyCommands)
            {
                foreach (var command in keyCommands)
                {
                    var key = $"{command.Title}|{command.Input}|{(long)command.ModifierFlags}|{command.Action?.Name}";
                    if (seen.Add(key)) collected.Add((command, responder));
                }
            }
        }

        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene ||
                windowScene.ActivationState != UISceneActivationState.ForegroundActive)
                continue;

            foreach (var window in windowScene.Windows.Where(window => window.IsKeyWindow))
            {
                UIResponder? responder = FindFirstResponder(window);
                var controller = window.RootViewController;
                while (controller?.PresentedViewController is { } presented)
                    controller = presented;
                responder ??= (UIResponder?)controller ?? window;
                var visited = new HashSet<UIResponder>();
                while (responder != null && visited.Add(responder))
                {
                    Add(responder);
                    responder = responder.NextResponder;
                }
            }
        }
        Add(UIApplication.SharedApplication);

        return collected;
    }

    private static UIResponder? FindFirstResponder(UIView view)
    {
        if (view.IsFirstResponder) return view;
        foreach (var child in view.Subviews)
            if (FindFirstResponder(child) is { } responder)
                return responder;
        return null;
    }

    private static List<string> CatalystModifiersToList(UIKeyModifierFlags flags)
    {
        var list = new List<string>();
        if (flags.HasFlag(UIKeyModifierFlags.Command)) list.Add("cmd");
        if (flags.HasFlag(UIKeyModifierFlags.Control)) list.Add("ctrl");
        if (flags.HasFlag(UIKeyModifierFlags.Alternate)) list.Add("alt");
        if (flags.HasFlag(UIKeyModifierFlags.Shift)) list.Add("shift");
        return list;
    }
#endif

#if MACOS || MACCATALYST
    private static string NormalizeNativePath(string path)
        => path.Replace('\\', '/').Trim().Trim('/');
#endif
}
