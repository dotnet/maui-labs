using System.Runtime.CompilerServices;
using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;
using Microsoft.Maui.Platforms.MacOS.Platform;
using ObjCRuntime;

namespace MacOS.RuntimeTests.Scenarios.MenuLocalization;

static class Registration
{
    [ModuleInitializer]
    public static void Register() =>
        ScenarioRegistry.Register(new("menu-localization", 5,
            context => new MenuLocalizationScenario().CreateDelegate(context), ExpectedAssertions: 24));
}

sealed class MenuLocalizationScenario : MauiRuntimeScenario
{
    public override Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage { Content = new Label { Text = "Native localized menus" } })
        { Title = "Menu localization", Width = 420, Height = 240 };

    public override async Task RunAsync(RuntimeTestContext evidence, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        var app = NSApplication.SharedApplication;
        var main = app.MainMenu ?? throw new InvalidOperationException("No native main menu.");
        var language = NSBundle.MainBundle.PreferredLocalizations.First();
        var expectedLanguage = Environment.GetEnvironmentVariable("MENU_TEST_EXPECTED_LANGUAGE") ?? "de";
        var overrides = Environment.GetEnvironmentVariable("MENU_TEST_OVERRIDES") == "true";
        evidence.Assert(language == expectedLanguage,
            $"Foundation selected the requested application language: expected {expectedLanguage}, actual {language}.");
        evidence.Assert(NSBundle.MainBundle.PathForResource("MauiMenuBar", "bundle") != null,
            "The built application includes the library's embedded native localization bundle.");
        var expected = language switch
        {
            "de" => new[] { "Bearbeiten", "Fenster", "Kopieren", "Über {0}", "{0} ausblenden", "{0} beenden",
                "Andere ausblenden", "Alle einblenden", "Im Dock ablegen", "Zoomen", "Vollbildmodus", "Menüprüfung" },
            "fr" => new[] { "Édition", "Fenêtre", "Copier", "À propos de {0}", "Masquer {0}", "Quitter {0}",
                "Masquer les autres", "Tout afficher", "Placer dans le Dock", "Réduire/agrandir",
                "Activer le mode plein écran", "Vérification des menus" },
            "nl" => new[] { "Wijzig", "Venster", "Kopieer", "Over {0}", "Verberg {0}", "Stop {0}",
                "Verberg andere", "Toon alles", "Minimaliseer", "Zoom", "Schakel schermvullende weergave in", "Menutest" },
            _ => new[] { "Edit", "Window", "Copy", "About {0}", "Hide {0}", "Quit {0}", "Hide Others",
                "Show All", "Minimize", "Zoom", "Enter Full Screen", "Menu Localization Tests" }
        };
        Dump(evidence, "initial");
        var edit = main.Items.SingleOrDefault(item => item.Title == expected[0])?.Submenu;
        if (language == "de" && edit == null && main.Items.Any(item => item.Title == "Edit"))
        {
            evidence.Assert(true, "German app language still has a hard-coded English Edit menu.");
            evidence.BaselineFailure("menu-localization.english-defaults",
                "BASELINE_417: German application has English default menus.");
            return;
        }

        evidence.Assert(edit != null, $"Default Edit menu uses the app's native language ({language}).");
        var copy = edit!.Items.Single(item => item.Action?.Name == "copy:");
        evidence.Assert(copy.Title == (overrides ? "Host Copy" : expected[2]) && copy.KeyEquivalent == "c",
            "Copy uses the localized title or host override and preserves Command-C.");
        var appMenu = main.ItemAt(0).Submenu!;
        var appName = expected[11];
        evidence.Assert(appMenu.Items.Single(item => item.Action?.Name == "orderFrontStandardAboutPanel:").Title ==
            (overrides ? "Host About " + appName + " {literal" : expected[3].Replace("{0}", appName)),
            "About uses the literal expected localized display name (also accepting non-format braces in overrides).");
        evidence.Assert(appMenu.Items.Single(item => item.Action?.Name == "hide:").Title ==
            expected[4].Replace("{0}", appName) &&
            appMenu.Items.Single(item => item.Action?.Name == "terminate:").Title ==
            expected[5].Replace("{0}", appName),
            "Hide and Quit use the localized application name and language-specific word order.");
        evidence.Assert(appMenu.Items.Single(item => item.Action?.Name == "hideOtherApplications:").Title == expected[6] &&
            appMenu.Items.Single(item => item.Action?.Name == "unhideAllApplications:").Title == expected[7],
            "Hide Others and Show All are localized even when the app overrides other keys.");
        evidence.Assert(appMenu.Items.Single(item => item.Action?.Name == "terminate:").KeyEquivalent == "q" &&
            edit.Items.Single(item => item.Action?.Name == "redo:").KeyEquivalentModifierMask ==
            (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask),
            "Quit and Redo keep their native keyboard equivalents.");
        evidence.Pass("localized-defaults");

        var windows = app.WindowsMenu;
        windows?.Update();
        evidence.Assert(windows != null && windows.Title == expected[1],
            "The localized Window menu is registered with AppKit.");
        evidence.Assert(windows!.Items.Any(item => item.Action?.Name == "performMiniaturize:") &&
            windows.Items.Any(item => item.Action?.Name == "performZoom:"),
            "The registered Window menu has standard native responder commands.");
        var minimize = windows.Items.Single(item => item.Action?.Name == "performMiniaturize:");
        var zoom = windows.Items.Single(item => item.Action?.Name == "performZoom:");
        var fullScreen = windows.Items.Single(item => item.Action?.Name == "toggleFullScreen:");
        evidence.Assert(minimize.Title == expected[8] && minimize.KeyEquivalent == "m" &&
            zoom.Title == expected[9], "Window commands are localized without duplicates and preserve Command-M.");
        evidence.Assert(fullScreen.Title == expected[10] && fullScreen.KeyEquivalent == "f" &&
            fullScreen.KeyEquivalentModifierMask ==
            (NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask),
            "Full Screen has a localized native title and Control-Command-F.");
        evidence.Assert(windows.Items.All(item => item.Title != "Toggle Full Screen"),
            "The unlocalized Toggle Full Screen label is not installed.");
        evidence.Pass("appkit-window-menu");

        MenuBarManager.UpdateMenuBar([new MenuBarItem { Text = expected[0] }, new MenuBarItem { Text = expected[1] }]);
        Dump(evidence, "localized-custom");
        evidence.Assert(main.Items.Count(item => item.Title == expected[0]) == 1 &&
            main.Items.Count(item => item.Title == expected[1]) == 1,
            "Localized custom titles suppress the corresponding defaults.");
        evidence.Assert(app.WindowsMenu?.Handle != windows.Handle,
            "Removing the default Window menu clears AppKit's old reference (AppKit may lazily create a replacement).");
        MenuBarManager.UpdateMenuBar([new MenuBarItem { Text = "Edit" }, new MenuBarItem { Text = "Window" }]);
        evidence.Assert(main.Items.Count(item => item.Title == "Edit") == 1 &&
            main.Items.Count(item => item.Title == "Window") == 1 && main.Count == 3,
            "Legacy English custom titles still suppress defaults in a non-English app.");
        evidence.Pass("custom-overrides");

        var clicks = 0;
        var custom = new MenuBarItem { Text = "Custom" };
        custom.Add(new MenuFlyoutItem { Text = "Run", Command = new Command(() => clicks++) });
        MenuBarManager.UpdateMenuBar([custom]);
        var customMenu = main.Items.Single(item => item.Title == "Custom").Submenu!;
        customMenu.Update();
        var customItem = customMenu.ItemAt(0);
        evidence.Assert(app.SendAction(customItem.Action!, customItem.Target, customItem),
            "AppKit dispatches the custom item's target/action.");
        evidence.Assert(clicks > 0, $"Custom menu action executes its MAUI command (invocations: {clicks}).");
        evidence.Assert(main.Items.Count(item => item.Title == expected[0]) == 1 &&
            main.Items.Count(item => item.Title == expected[1]) == 1 && app.WindowsMenu != null,
            "Page updates restore localized defaults without duplicates.");
        var restoredEdit = main.Items.Single(item => item.Title == expected[0]).Submenu!;
        using var field = new NSTextField(new CGRect(20, 20, 300, 28))
        { StringValue = "Copy via responder chain", Editable = true, Selectable = true };
        var nativeWindow = (NSWindow)window.Handler!.PlatformView!;
        nativeWindow.ContentView!.AddSubview(field);
        nativeWindow.MakeFirstResponder(field);
        field.SelectText(null);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.Assert(field.CurrentEditor != null && nativeWindow.FirstResponder == field.CurrentEditor,
            "The native text field's field editor is the window's first responder.");
        var restoredCopy = restoredEdit.Items.Single(item => item.Action?.Name == "copy:");
        var pasteboard = NSPasteboard.GeneralPasteboard;
        var originalItems = SnapshotPasteboard(pasteboard);
        var testChangeCount = pasteboard.ChangeCount;
        try
        {
            testChangeCount = pasteboard.ClearContents();
            var dispatched = app.SendAction(restoredCopy.Action!, nativeWindow.FirstResponder, restoredCopy);
            testChangeCount = pasteboard.ChangeCount;
            evidence.Assert(restoredCopy.Target == null && dispatched,
                "Copy retains a nil target and AppKit dispatches its action to the real window first responder.");
            evidence.Assert(pasteboard.GetStringForType(NSPasteboard.NSStringType) == field.StringValue,
                "Localized Copy invokes the native text responder and writes the selection to the pasteboard.");
        }
        finally
        {
            if (pasteboard.ChangeCount == testChangeCount)
            {
                pasteboard.ClearContents();
                if (originalItems.Length > 0 && !pasteboard.WriteObjects(originalItems))
                    throw new InvalidOperationException("Could not restore the pasteboard after the native Copy test.");
            }
            foreach (var item in originalItems)
                item.Dispose();
            field.RemoveFromSuperview();
        }
        evidence.Pass("native-and-custom-actions");

        var previousWindowMenu = app.WindowsMenu!.Handle;
        MenuBarManager.SetupDefaultMenuBar(new MacOSMenuBarOptions { IncludeDefaultMenus = false });
        evidence.Assert(app.MainMenu!.Count == 1 && app.WindowsMenu?.Handle != previousWindowMenu,
            "Disabling defaults retains only the application menu and clears the Window registration.");
        MenuBarManager.SetupDefaultMenuBar();
        MenuBarManager.UpdateMenuBar(null);
        await RuntimeTestContext.FlushMainQueueAsync();
        evidence.Assert(app.MainMenu!.Count == 3 && app.WindowsMenu != null,
            "Resetting options and clearing page items restores the standard menus.");
        Dump(evidence, "final");
        evidence.Capture(nativeWindow.ContentView!, "window.png");
        evidence.Pass("options-and-reset");
    }

    static NSPasteboardItem[] SnapshotPasteboard(NSPasteboard pasteboard) =>
        (pasteboard.PasteboardItems ?? []).Select(original =>
        {
            var copy = new NSPasteboardItem();
            foreach (var type in original.Types)
            {
                using var data = original.GetDataForType(type);
                if (data == null || !copy.SetDataForType(data, type))
                    throw new InvalidOperationException("Could not preserve the pasteboard before the native Copy test.");
            }
            return copy;
        }).ToArray();

    static void Dump(RuntimeTestContext evidence, string stage) =>
        evidence.AppendJson("menus.jsonl", new
        {
            stage,
            windowMenu = NSApplication.SharedApplication.WindowsMenu?.Title,
            windowMenuHandle = NSApplication.SharedApplication.WindowsMenu?.Handle.ToString(),
            languages = NSBundle.MainBundle.PreferredLocalizations,
            menus = NSApplication.SharedApplication.MainMenu!.Items.Select(item => new
            {
                item.Title,
                submenuHandle = item.Submenu?.Handle.ToString(),
                children = item.Submenu?.Items.Select(child => new
                { child.Title, action = child.Action?.Name, child.KeyEquivalent })
            })
        });
}
