using System.Runtime.CompilerServices;
using AppKit;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace MacOS.RuntimeTests.Scenarios.WindowPageReset;

static class Registration
{
    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(
        new("window-page-reset", ExpectedCases: 1,
            CreateDelegate: context => new WindowPageResetScenario().CreateDelegate(context),
            ExpectedAssertions: 11));
}

sealed class WindowPageResetScenario : MauiRuntimeScenario
{
    Button _openModal = null!;
    Button _openNestedModal = null!;
    Button _resetPage = null!;
    ContentPage _firstModal = null!;
    ContentPage _nestedModal = null!;
    Shell? _replacement;
    Task? _pendingPush;
    int _resetClicks;

    public override Window CreateWindow(IActivationState? activationState)
    {
        _openModal = new Button { Text = "Open modal" };
        _openNestedModal = new Button { Text = "Open nested modal" };
        _resetPage = new Button { Text = "Reset Window.Page" };

        var root = new ContentPage { Title = "Original root", Content = _openModal };
        _firstModal = new ContentPage { Title = "First modal", Content = _openNestedModal };
        _nestedModal = new ContentPage { Title = "Nested modal", Content = _resetPage };

        _openModal.Clicked += (_, _) =>
            _pendingPush = root.Navigation.PushModalAsync(_firstModal, animated: false);
        _openNestedModal.Clicked += (_, _) =>
            _pendingPush = _firstModal.Navigation.PushModalAsync(_nestedModal, animated: false);
        _resetPage.Clicked += (_, _) =>
        {
            var window = _nestedModal.Window
                ?? throw new InvalidOperationException("The modal has no owning MAUI window.");
            _replacement = new Shell { FlyoutBehavior = FlyoutBehavior.Locked };
            MacOSShell.SetUseNativeSidebar(_replacement, true);
            _replacement.Items.Add(new ShellContent
            {
                Title = "Reset complete",
                Content = new ContentPage
                {
                    Content = new Label { Text = "Window.Page was reset from the nested modal." }
                }
            });
            window.Page = _replacement;
            _resetClicks++;
        };

        return new(root) { Title = "Window page reset regression", Width = 700, Height = 500 };
    }

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        var nativeWindow = window.Handler?.PlatformView as NSWindow
            ?? throw new InvalidOperationException("The MAUI window has no AppKit window.");
        context.Assert(NativeButton(_openModal).Window == nativeWindow && nativeWindow.Sheets.Length == 0,
            "The original page's button is attached to a window with no sheets.");

        Click(_openModal);
        await (_pendingPush ?? throw new InvalidOperationException("The open button did not push a modal."));
        await RuntimeTestContext.FlushMainQueueAsync();
        var firstSheet = NativeButton(_openNestedModal).Window
            ?? throw new InvalidOperationException("The first modal button is not attached to a window.");
        context.Assert(firstSheet.IsVisible && firstSheet.SheetParent == nativeWindow,
            "Clicking the page's button presents the first modal as an AppKit sheet.");
        context.Assert(window.Navigation.ModalStack.Count == 1,
            "The first modal is present in MAUI's navigation stack.");

        _pendingPush = null;
        Click(_openNestedModal);
        await (_pendingPush ?? throw new InvalidOperationException("The nested button did not push a modal."));
        await RuntimeTestContext.FlushMainQueueAsync();
        var nestedSheet = NativeButton(_resetPage).Window
            ?? throw new InvalidOperationException("The reset button is not attached to a modal window.");
        context.Assert(nestedSheet.IsVisible && nestedSheet.SheetParent == firstSheet &&
            nativeWindow.Sheets.Length == 1 && firstSheet.Sheets.Length == 1,
            "The nested modal is a second sheet presented from the first sheet.");
        context.Assert(window.Navigation.ModalStack.Count == 2,
            "Both modals are present before resetting Window.Page.");

        var modalWindows = new[] { firstSheet, nestedSheet };
        RecordState(context, "before-reset", window, nativeWindow, modalWindows);

        // Exercise the real native button callback while two modal sheets are presented.
        // The fixture must not pop or dismiss either modal itself.
        Click(_resetPage);
        await RuntimeTestContext.FlushMainQueueAsync();
        RecordState(context, "after-reset", window, nativeWindow, modalWindows);

        context.Assert(_resetClicks == 1 && ReferenceEquals(window.Page, _replacement) &&
            ReferenceEquals(window.Handler?.PlatformView, nativeWindow),
            "The modal's reset button replaces Window.Page in the existing native window.");
        context.Assert(nativeWindow.ContentViewController is NSSplitViewController &&
            _replacement?.Handler?.PlatformView is NSView replacementView && replacementView.Window == nativeWindow,
            "The replacement Shell is attached as the window's native content controller.");
        context.Assert(nativeWindow.AttachedSheet is null && nativeWindow.Sheets.Length == 0,
            "Resetting Window.Page closes all sheets attached to the original window.",
            "window-page-reset.attached-sheets");
        context.Assert(modalWindows.All(sheet => sheet.Handle == IntPtr.Zero ||
            (!sheet.IsVisible && sheet.SheetParent is null && sheet.Sheets.Length == 0)),
            "Neither of the old modal windows remains visible or attached, including the nested sheet.");
        context.Assert(_firstModal.Handler is null && _nestedModal.Handler is null,
            "Resetting the root disconnects both modal page handlers.");
        context.Assert(window.Navigation.ModalStack.Count == 0,
            "The replacement root has no stale MAUI modal navigation entries.");
        context.Pass("reset-window-page-closes-all-modals");
    }

    static NSButton NativeButton(Button button) => button.Handler?.PlatformView as NSButton
        ?? throw new InvalidOperationException($"'{button.Text}' has no native AppKit button.");

    static void Click(Button button)
    {
        var native = NativeButton(button);
        native.PerformClick(native);
    }

    static void RecordState(RuntimeTestContext context, string stage, Window window,
        NSWindow nativeWindow, NSWindow[] modalWindows) => context.AppendJson("states.jsonl", new
        {
            stage,
            page = window.Page?.GetType().Name,
            modalStackCount = window.Navigation.ModalStack.Count,
            sheetCount = nativeWindow.Sheets.Length,
            modalWindows = modalWindows.Select(sheet => new
                {
                    disposed = sheet.Handle == IntPtr.Zero,
                    visible = sheet.Handle != IntPtr.Zero && sheet.IsVisible,
                    hasSheetParent = sheet.Handle != IntPtr.Zero && sheet.SheetParent is not null,
                    sheetCount = sheet.Handle == IntPtr.Zero ? 0 : sheet.Sheets.Length
                }).ToArray()
        });
}
