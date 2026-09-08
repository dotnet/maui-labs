using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

#pragma warning disable IL2026, IL2060, IL2080, IL2111 // Reflection required for internal IAlertManagerSubscription

public class AlertManagerSubscription : DispatchProxy
{
    static readonly Type? AlertManagerType = typeof(Window).Assembly
        .GetType("Microsoft.Maui.Controls.Platform.AlertManager");

    static readonly Type? IAlertManagerSubscriptionType = AlertManagerType?
        .GetNestedType("IAlertManagerSubscription", BindingFlags.Public | BindingFlags.NonPublic);

    public static void Register(IServiceCollection services)
    {
        if (IAlertManagerSubscriptionType == null)
            return;

        var proxyType = typeof(AlertManagerSubscription<>).MakeGenericType(IAlertManagerSubscriptionType);
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(IAlertManagerSubscriptionType, proxyType);

        var proxy = createMethod.Invoke(null, null)!;
        services.AddSingleton(IAlertManagerSubscriptionType, proxy);
    }

    internal static void HandleInvoke(MethodInfo? method, object?[]? args)
    {
        if (method == null || args == null)
            return;

        switch (method.Name)
        {
            case "OnAlertRequested":
                OnAlertRequested(args[0] as Page, args[1] as AlertArguments);
                break;
            case "OnPromptRequested":
                OnPromptRequested(args[0] as Page, args[1] as PromptArguments);
                break;
            case "OnActionSheetRequested":
                OnActionSheetRequested(args[0] as Page, args[1] as ActionSheetArguments);
                break;
        }
    }

    static void OnAlertRequested(Page? sender, AlertArguments? arguments)
    {
        if (arguments == null)
            return;

        var alert = new NSAlert();
        alert.MessageText = arguments.Title ?? string.Empty;
        alert.InformativeText = arguments.Message ?? string.Empty;

        if (arguments.Accept != null)
            alert.AddButton(arguments.Accept);

        if (arguments.Cancel != null)
            alert.AddButton(arguments.Cancel);

        PresentDialog(
            sender,
            alert,
            input: null,
            onResult: response =>
            {
                // First button added (Accept) = NSAlertFirstButtonReturn (1000)
                // Second button (Cancel) = NSAlertSecondButtonReturn (1001)
                var accepted = arguments.Accept != null && response == (nint)1000;
                arguments.SetResult(accepted);
            },
            onUnhandledException: ex =>
            {
                LogDialogCompletionFailure(nameof(OnAlertRequested), ex);
                arguments.SetResult(false);
            });
    }

    static void OnPromptRequested(Page? sender, PromptArguments? arguments)
    {
        if (arguments == null)
            return;

        var alert = new NSAlert();
        alert.MessageText = arguments.Title ?? string.Empty;
        alert.InformativeText = arguments.Message ?? string.Empty;
        alert.AddButton(arguments.Accept);
        alert.AddButton(arguments.Cancel);

        var input = new NSTextField(new CoreGraphics.CGRect(0, 0, 300, 24));
        input.PlaceholderString = arguments.Placeholder ?? string.Empty;
        input.StringValue = arguments.InitialValue ?? string.Empty;
        alert.AccessoryView = input;
        alert.Window.InitialFirstResponder = input;

        PresentDialog(
            sender,
            alert,
            input,
            onResult: response =>
            {
                if (response == (nint)1000) // Accept
                    arguments.SetResult(input.StringValue);
                else
                    arguments.SetResult(null);
            },
            onUnhandledException: ex =>
            {
                LogDialogCompletionFailure(nameof(OnPromptRequested), ex);
                arguments.SetResult(null);
            });
    }

    static void OnActionSheetRequested(Page? sender, ActionSheetArguments? arguments)
    {
        if (arguments == null)
            return;

        var alert = new NSAlert();
        alert.MessageText = arguments.Title ?? string.Empty;

        // Buttons is documented as nullable (a null params array from the caller flows
        // straight through), so it must be treated as optional here and below.
        foreach (var button in arguments.Buttons ?? Enumerable.Empty<string>())
        {
            if (button != null)
                alert.AddButton(button);
        }

        if (arguments.Destruction != null)
        {
            var destructBtn = alert.AddButton(arguments.Destruction);
            destructBtn.HasDestructiveAction = true;
        }

        if (arguments.Cancel != null)
            alert.AddButton(arguments.Cancel);

        PresentDialog(
            sender,
            alert,
            input: null,
            onResult: response =>
            {
                var buttonIndex = (int)(response - 1000);
                var result = ActionSheetResultResolver.Resolve(
                    arguments.Buttons, arguments.Destruction, arguments.Cancel, buttonIndex);
                arguments.SetResult(result);
            },
            onUnhandledException: ex =>
            {
                LogDialogCompletionFailure(nameof(OnActionSheetRequested), ex);
                arguments.SetResult(arguments.Cancel);
            });
    }

    /// <summary>
    /// Registers the dialog surface, its buttons, and (for prompts) its input field as
    /// DevFlow-inspectable native elements, then presents the alert as a nonblocking sheet on
    /// the initiating window so this call returns immediately - unlike <c>NSAlert.RunModal()</c>,
    /// which spins a nested run loop that starves the DevFlow agent's HTTP dispatcher for as
    /// long as the alert is on screen, making every registration made above unreachable until
    /// the alert is dismissed. <paramref name="onResult"/> runs once the sheet is dismissed
    /// (by a native action or a programmatic close); if it throws (or presentation itself
    /// fails before the sheet is shown), <paramref name="onUnhandledException"/> runs instead
    /// so the corresponding MAUI result is still always set exactly once and no exception
    /// escapes into the native completion callback. Every registration is unregistered
    /// immediately after completion (success or fallback) via
    /// <see cref="DialogCompletionScope"/>, so nothing is retained past dismissal on any path.
    /// </summary>
    static void PresentDialog(
        Page? sender,
        NSAlert alert,
        NSTextField? input,
        Action<nint> onResult,
        Action<Exception> onUnhandledException)
    {
        // Prefer the initiating page as the owner so registrations follow the same
        // ownership convention as other Dialog/DialogAction registrations; fall back to the
        // alert itself when no page is available (e.g. the alert wasn't page-initiated).
        object owner = (object?)sender ?? alert;

        var registrationScope = new DialogNativeRegistrationScope(NativeElementDiagnosticsBridge.Unregister);
        var completionScope = new DialogCompletionScope(registrationScope);
        try
        {
            // Accessing the window forces AppKit to finalize its (possibly implicit) button
            // list before we enumerate alert.Buttons below.
            if (alert.Window.ContentView is NSView dialogSurface)
            {
                NativeElementDiagnosticsBridge.Register(
                    owner,
                    dialogSurface,
                    DialogNativeElementContract.DialogRole,
                    DialogNativeElementContract.RealizedViewDiscriminator);
                registrationScope.Track(dialogSurface);
            }

            foreach (var button in alert.Buttons)
            {
                NativeElementDiagnosticsBridge.Register(
                    owner,
                    button,
                    DialogNativeElementContract.DialogActionRole,
                    DialogNativeElementContract.RealizedViewDiscriminator);
                registrationScope.Track(button);
            }

            if (input is not null)
            {
                NativeElementDiagnosticsBridge.Register(
                    owner,
                    input,
                    DialogNativeElementContract.DialogRole,
                    DialogNativeElementContract.RealizedViewDiscriminator);
                registrationScope.Track(input);
            }

            var window = ResolveWindow(sender);
            if (window is not null)
            {
                alert.BeginSheetForResponse(
                    window,
                    response => completionScope.Complete(() => onResult(response), onUnhandledException));
                return;
            }

            // No live window was found (e.g. no window has been created yet). This is not
            // expected during normal operation - a Page-initiated alert implies a visible
            // window - and there is no nonblocking API for a windowless alert, so fall back to
            // the blocking presentation rather than silently dropping the alert. Inspection
            // isn't defeated by this path in practice: with no window at all, there is nothing
            // else for DevFlow to inspect while it is blocked.
            var response = alert.RunModal();
            completionScope.Complete(() => onResult(response), onUnhandledException);
        }
        catch (Exception ex)
        {
            // Setup failed synchronously, before any sheet/modal was shown, so this exception
            // never crosses the native completion boundary - Fail's fallback runs directly on
            // this call stack. Rethrowing afterward preserves the pre-existing synchronous
            // failure behavior: Page.DisplayAlertAsync (and friends) call
            // AlertManager.RequestAlert/RequestPrompt/RequestActionSheet - which is what
            // eventually calls this method - synchronously and only return arguments.Result.Task
            // to their own caller afterward, so a synchronous throw here means that Task was
            // never handed to anyone who could await/observe it; the caller simply sees this
            // exception thrown directly out of DisplayAlertAsync, as it always has. Calling
            // Fail first, defensively, guarantees the dialog's MAUI task is still always
            // completed and every registration is still always disposed exactly once, even if a
            // future caller ends up holding a reference to that Task before this point is
            // reached, or if this path is ever reached from a context where the exception
            // doesn't propagate all the way back out.
            completionScope.Fail(ex, onUnhandledException);
            throw;
        }
    }

    /// <summary>
    /// Logs an unexpected failure while computing or applying a dialog's result, matching this
    /// backend's existing "[Type.Method] {exception}" error logging convention (see e.g.
    /// <c>ShellHandler.ShowCurrentPage</c>, <c>ImageHandler</c>).
    /// </summary>
    static void LogDialogCompletionFailure(string dialogMethod, Exception ex)
        => Console.Error.WriteLine($"[AlertManagerSubscription.{dialogMethod}] Dialog result completion failed: {ex}");

    /// <summary>
    /// Resolves the NSWindow that should host the alert sheet: the initiating page's own
    /// window (matching the Page-to-PlatformView resolution convention used elsewhere in this
    /// backend, e.g. <c>ApplicationHandler.MapCloseWindow</c>), falling back to the app's key
    /// window or main window when the page has none.
    /// </summary>
    static NSWindow? ResolveWindow(Page? sender)
    {
        if (sender?.Window?.Handler?.PlatformView is NSWindow pageWindow)
            return pageWindow;

        var app = NSApplication.SharedApplication;
        return app.KeyWindow ?? app.MainWindow;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}

public class AlertManagerSubscription<T> : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        AlertManagerSubscription.HandleInvoke(targetMethod, args);
        return null;
    }
}
