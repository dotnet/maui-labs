using AppKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
#if NET11_0_OR_GREATER
using Microsoft.Maui.Controls.Platform;
#else
using System.Reflection;
#endif

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

#if NET11_0_OR_GREATER
public sealed class AlertManagerSubscription : IAlertManagerSubscription
#else
#pragma warning disable IL2026, IL2060, IL2080, IL2111
public class AlertManagerSubscription : DispatchProxy
#endif
{
#if NET11_0_OR_GREATER
    public static void Register(IServiceCollection services)
        => services.AddSingleton<IAlertManagerSubscription, AlertManagerSubscription>();
#else
    static readonly Type? AlertManagerType = typeof(Window).Assembly
        .GetType("Microsoft.Maui.Controls.Platform.AlertManager");

    static readonly Type? IAlertManagerSubscriptionType = typeof(Window).Assembly
        .GetType("Microsoft.Maui.Controls.Platform.IAlertManagerSubscription")
        ?? AlertManagerType?.GetNestedType("IAlertManagerSubscription", BindingFlags.Public | BindingFlags.NonPublic);

    public static void Register(IServiceCollection services)
    {
        if (IAlertManagerSubscriptionType == null)
            throw new NotSupportedException($"Cannot register AppKit dialogs: IAlertManagerSubscription was not found in {typeof(Window).Assembly.FullName}.");

        var proxyType = typeof(AlertManagerSubscription<>).MakeGenericType(IAlertManagerSubscriptionType);
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(IAlertManagerSubscriptionType, proxyType);

        services.AddSingleton(IAlertManagerSubscriptionType, createMethod.Invoke(null, null)!);
    }
#endif

    public void OnAlertRequested(Page sender, AlertArguments arguments)
    {
        var alert = new NSAlert
        {
            MessageText = arguments.Title ?? string.Empty,
            InformativeText = arguments.Message ?? string.Empty
        };

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
                var accepted = arguments.Accept != null && response == (nint)1000;
                arguments.SetResult(accepted);
            },
            onUnhandledException: ex =>
            {
                LogDialogCompletionFailure(nameof(OnAlertRequested), ex);
                arguments.SetResult(false);
            });
    }

    public void OnPromptRequested(Page sender, PromptArguments arguments)
    {
        var alert = new NSAlert
        {
            MessageText = arguments.Title ?? string.Empty,
            InformativeText = arguments.Message ?? string.Empty
        };
        alert.AddButton(arguments.Accept);
        alert.AddButton(arguments.Cancel);

        var input = new NSTextField(new CoreGraphics.CGRect(0, 0, 300, 24))
        {
            PlaceholderString = arguments.Placeholder ?? string.Empty,
            StringValue = arguments.InitialValue ?? string.Empty
        };
        alert.AccessoryView = input;
        alert.Window.InitialFirstResponder = input;

        PresentDialog(
            sender,
            alert,
            input,
            onResult: response => arguments.SetResult(response == (nint)1000 ? input.StringValue : null),
            onUnhandledException: ex =>
            {
                LogDialogCompletionFailure(nameof(OnPromptRequested), ex);
                arguments.SetResult(null);
            });
    }

    public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
    {
        var alert = new NSAlert { MessageText = arguments.Title ?? string.Empty };

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

    static void PresentDialog(
        Page? sender,
        NSAlert alert,
        NSTextField? input,
        Action<nint> onResult,
        Action<Exception> onUnhandledException)
    {
        object owner = (object?)sender ?? alert;

        var registrationScope = new DialogNativeRegistrationScope(NativeElementDiagnosticsBridge.Unregister);
        var completionScope = new DialogCompletionScope(registrationScope);
        try
        {
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

            var response = alert.RunModal();
            completionScope.Complete(() => onResult(response), onUnhandledException);
        }
        catch (Exception ex)
        {
            completionScope.Fail(ex, onUnhandledException);
            throw;
        }
    }

    static void LogDialogCompletionFailure(string dialogMethod, Exception ex)
        => Console.Error.WriteLine($"[AlertManagerSubscription.{dialogMethod}] Dialog result completion failed: {ex}");

    static NSWindow? ResolveWindow(Page? sender)
    {
        if (sender?.Window?.Handler?.PlatformView is NSWindow pageWindow)
            return pageWindow;

        var app = NSApplication.SharedApplication;
        return app.KeyWindow ?? app.MainWindow;
    }

#if !NET11_0_OR_GREATER
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null || args is null)
            return null;

        switch (targetMethod.Name)
        {
            case nameof(OnAlertRequested):
                OnAlertRequested((Page)args[0]!, (AlertArguments)args[1]!);
                break;
            case nameof(OnPromptRequested):
                OnPromptRequested((Page)args[0]!, (PromptArguments)args[1]!);
                break;
            case nameof(OnActionSheetRequested):
                OnActionSheetRequested((Page)args[0]!, (ActionSheetArguments)args[1]!);
                break;
        }

        return null;
    }
#endif
}

#if !NET11_0_OR_GREATER
public class AlertManagerSubscription<T> : AlertManagerSubscription where T : class
{
}
#pragma warning restore IL2026, IL2060, IL2080, IL2111
#endif
