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

    protected override Task<object?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => DispatchAsync(() => InvokeNativeMenuItem(request));

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

    private List<object> BuildNativeMenuItems(NSMenu menu, string idPrefix, string pathPrefix)
    {
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
            var path = CombineNativePath(pathPrefix, title);
            var node = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["title"] = title,
                ["path"] = path,
                ["enabled"] = item.Enabled,
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
                node["items"] = BuildNativeMenuItems(submenu, $"{id}/", path);
            }
            else
            {
                node["hasSubmenu"] = false;
            }

            items.Add(node);
        }

        return items;
    }

    private object? InvokeNativeMenuItem(MenuInvokeRequest request)
    {
        var mainMenu = NSApplication.SharedApplication.MainMenu;
        if (mainMenu == null) return null;

        return FindAndInvokeNative(mainMenu, "native:", string.Empty, request);
    }

    private object? FindAndInvokeNative(NSMenu menu, string idPrefix, string pathPrefix, MenuInvokeRequest request)
    {
        for (nint i = 0; i < menu.Count; i++)
        {
            var item = menu.ItemAt(i);
            if (item == null || item.IsSeparatorItem) continue;

            var id = $"{idPrefix}{i}";
            var path = CombineNativePath(pathPrefix, item.Title ?? string.Empty);

            if (NativeItemMatches(item, id, path, request))
            {
                if (!item.Enabled)
                    return new
                    {
                        success = false,
                        source = "appkit",
                        title = item.Title,
                        path,
                        error = "disabled",
                    };

                var invoked = PerformNativeMenuItem(menu, item, i);
                return new
                {
                    success = invoked,
                    source = "appkit",
                    title = item.Title,
                    path,
                    action = item.Action?.Name,
                };
            }

            if (item.Submenu is NSMenu submenu)
            {
                var nested = FindAndInvokeNative(submenu, $"{id}/", path, request);
                if (nested != null) return nested;
            }
        }

        return null;
    }

    private static bool PerformNativeMenuItem(NSMenu menu, NSMenuItem item, nint index)
    {
        try
        {
            menu.PerformActionForItem(index);
            return true;
        }
        catch { }

        try
        {
            if (item.Action is Selector action)
                return NSApplication.SharedApplication.SendAction(action, item.Target, item);
        }
        catch { }

        return false;
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

            var requested = ParseNativeModifierSet(request.Modifiers);
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

    protected override Task<object?> InvokeNativeMenuAsync(MenuInvokeRequest request)
        => DispatchAsync(() => InvokeCatalystKeyCommand(request));

    private object? BuildCatalystKeyCommands()
    {
        var commands = CollectKeyCommands();
        var items = new List<object>();
        var index = 0;
        foreach (var command in commands)
        {
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = $"native:{index}",
                ["title"] = command.Title,
                ["path"] = command.Title,
                ["separator"] = false,
                ["hasSubmenu"] = false,
                ["key"] = string.IsNullOrEmpty(command.Input) ? null : command.Input,
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

    private object? InvokeCatalystKeyCommand(MenuInvokeRequest request)
    {
        var commands = CollectKeyCommands();
        var requestedModifiers = ParseNativeModifierSet(request.Modifiers);

        var index = 0;
        foreach (var command in commands)
        {
            var id = $"native:{index}";
            index++;

            var matches = false;
            if (!string.IsNullOrWhiteSpace(request.Id))
                matches = string.Equals(request.Id.Trim(), id, StringComparison.OrdinalIgnoreCase);
            else if (!string.IsNullOrWhiteSpace(request.Title) || !string.IsNullOrWhiteSpace(request.Path))
            {
                var wanted = (request.Title ?? request.Path)!.Trim();
                // On Catalyst the responder-chain key commands are flat (path == title), so a
                // hierarchical path like "File/Save" is matched by its last segment.
                if (string.IsNullOrWhiteSpace(request.Title) && wanted.Contains('/'))
                    wanted = wanted[(wanted.LastIndexOf('/') + 1)..].Trim();
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

            if (!matches || command.Action == null) continue;

            var invoked = UIApplication.SharedApplication.SendAction(command.Action, null, null, null);
            return new
            {
                success = invoked,
                source = "uikit",
                title = command.Title,
                action = command.Action.Name,
            };
        }

        return null;
    }

    private static List<UIKeyCommand> CollectKeyCommands()
    {
        var collected = new List<UIKeyCommand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(UIResponder? responder)
        {
            try
            {
                if (responder?.KeyCommands is { } keyCommands)
                {
                    foreach (var command in keyCommands)
                    {
                        var key = $"{command.Title}|{command.Input}|{(long)command.ModifierFlags}|{command.Action?.Name}";
                        if (seen.Add(key)) collected.Add(command);
                    }
                }
            }
            catch { }
        }

        try
        {
            Add(UIApplication.SharedApplication);

            foreach (var window in UIApplication.SharedApplication.Windows ?? Array.Empty<UIWindow>())
            {
                Add(window);
                var controller = window.RootViewController;
                while (controller != null)
                {
                    Add(controller);
                    foreach (var child in controller.ChildViewControllers ?? Array.Empty<UIViewController>())
                        Add(child);
                    if (controller.View != null) Add(controller.View);
                    controller = controller.PresentedViewController;
                }
            }
        }
        catch { }

        return collected;
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
    private static HashSet<string> ParseNativeModifierSet(string? modifiers)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(modifiers)) return set;

        foreach (var raw in modifiers.Split(new[] { ',', '+', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = raw.ToLowerInvariant() switch
            {
                "cmd" or "command" or "meta" or "super" => "cmd",
                "ctrl" or "control" => "ctrl",
                "alt" or "option" or "opt" => "alt",
                "shift" => "shift",
                _ => null,
            };
            if (normalized != null) set.Add(normalized);
        }

        return set;
    }

    private static string CombineNativePath(string prefix, string title)
        => string.IsNullOrEmpty(prefix) ? title : $"{prefix}/{title}";

    private static string NormalizeNativePath(string path)
        => path.Replace('\\', '/').Trim().Trim('/');
#endif
}
