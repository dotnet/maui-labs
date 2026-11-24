#if NET10_0
#nullable enable
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Platforms.Windows.WPF;

#pragma warning disable IL2026, IL2060, IL2080, IL2111

/// <summary>
/// MAUI 10 compatibility bridge for the internal dialog subscription contract.
/// .NET 11 uses <see cref="WPFDialogService"/> and public keyed delegates instead.
/// </summary>
public class WPFAlertManagerSubscription : DispatchProxy
{
	static readonly Type? AlertManagerType = typeof(Window).Assembly
		.GetType("Microsoft.Maui.Controls.Platform.AlertManager");

	static readonly Type? IAlertManagerSubscriptionType = typeof(Window).Assembly
		.GetType("Microsoft.Maui.Controls.Platform.IAlertManagerSubscription")
		?? AlertManagerType?.GetNestedType("IAlertManagerSubscription", BindingFlags.Public | BindingFlags.NonPublic);

	public static void Register(IServiceCollection services)
	{
		if (IAlertManagerSubscriptionType == null)
			throw new NotSupportedException($"Cannot register WPF dialogs: IAlertManagerSubscription was not found in {typeof(Window).Assembly.FullName}.");

		var proxyType = typeof(WPFAlertManagerSubscriptionProxy<>).MakeGenericType(IAlertManagerSubscriptionType);
		var createMethod = typeof(DispatchProxy)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.First(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
			.MakeGenericMethod(IAlertManagerSubscriptionType, proxyType);

		services.AddSingleton(IAlertManagerSubscriptionType, createMethod.Invoke(null, null)!);
	}

	internal static void HandleInvoke(MethodInfo? method, object?[]? args)
	{
		if (method == null || args == null)
			return;

		try
		{
			switch (method.Name)
			{
				case "OnAlertRequested" when args[1] is AlertArguments alert:
					alert.SetResult(WPFDialogService.ShowAlertAsync(args[0] as Page, alert).GetAwaiter().GetResult());
					break;
				case "OnPromptRequested" when args[1] is PromptArguments prompt:
					prompt.SetResult(WPFDialogService.ShowPromptAsync(args[0] as Page, prompt).GetAwaiter().GetResult());
					break;
				case "OnActionSheetRequested" when args[1] is ActionSheetArguments sheet:
					sheet.SetResult(WPFDialogService.ShowActionSheetAsync(args[0] as Page, sheet).GetAwaiter().GetResult());
					break;
			}
		}
		catch (Exception exception)
		{
			switch (args.ElementAtOrDefault(1))
			{
				case AlertArguments alert:
					alert.Result.TrySetException(exception);
					break;
				case PromptArguments prompt:
					prompt.Result.TrySetException(exception);
					break;
				case ActionSheetArguments sheet:
					sheet.Result.TrySetException(exception);
					break;
			}
		}
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
}

public class WPFAlertManagerSubscriptionProxy<T> : DispatchProxy
{
	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		WPFAlertManagerSubscription.HandleInvoke(targetMethod, args);
		return null;
	}
}
#endif
