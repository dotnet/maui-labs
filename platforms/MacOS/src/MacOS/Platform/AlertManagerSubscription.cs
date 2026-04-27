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

        var response = alert.RunModal();
        // First button added (Accept) = NSAlertFirstButtonReturn (1000)
        // Second button (Cancel) = NSAlertSecondButtonReturn (1001)
        var accepted = arguments.Accept != null && response == (nint)1000;
        arguments.SetResult(accepted);
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

        var response = alert.RunModal();
        if (response == (nint)1000) // Accept
            arguments.SetResult(input.StringValue);
        else
            arguments.SetResult(null);
    }

    static void OnActionSheetRequested(Page? sender, ActionSheetArguments? arguments)
    {
        if (arguments == null)
            return;

        var alert = new NSAlert();
        alert.MessageText = arguments.Title ?? string.Empty;

        foreach (var button in arguments.Buttons)
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

        var response = alert.RunModal();
        var buttonIndex = (int)(response - 1000);

        var allButtons = arguments.Buttons.Where(b => b != null).ToList();
        if (arguments.Destruction != null) allButtons.Add(arguments.Destruction);
        if (arguments.Cancel != null) allButtons.Add(arguments.Cancel);

        if (buttonIndex >= 0 && buttonIndex < allButtons.Count)
            arguments.SetResult(allButtons[buttonIndex]);
        else
            arguments.SetResult(arguments.Cancel);
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
