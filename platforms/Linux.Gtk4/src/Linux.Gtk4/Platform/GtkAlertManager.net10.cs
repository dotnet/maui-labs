#if NET10_0
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// MAUI 10 compatibility registration for the alert subscription contract that
/// was internal in that release line.
/// </summary>
public static class GtkAlertManager
{
	public static void Register(IServiceCollection services)
	{
		var alertManagerType = typeof(Window).Assembly
			.GetType("Microsoft.Maui.Controls.Platform.AlertManager");
		var subscriptionType = typeof(Window).Assembly
			.GetType("Microsoft.Maui.Controls.Platform.IAlertManagerSubscription")
			?? alertManagerType?.GetNestedType(
				"IAlertManagerSubscription",
				BindingFlags.Public | BindingFlags.NonPublic)
			?? throw new NotSupportedException(
				$"Cannot register GTK dialogs: IAlertManagerSubscription was not found in {typeof(Window).Assembly.FullName}.");

		var proxyType = typeof(AlertSubscriptionProxy<>).MakeGenericType(subscriptionType);
		var createMethod = typeof(DispatchProxy)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.First(method => method.Name == "Create" && method.GetGenericArguments().Length == 2)
			.MakeGenericMethod(subscriptionType, proxyType);

		services.AddSingleton(subscriptionType, createMethod.Invoke(null, null)!);
	}

	public class AlertSubscriptionProxy<T> : DispatchProxy
	{
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
		{
			if (targetMethod is null || arguments is null)
				return null;

			switch (targetMethod.Name)
			{
				case "OnAlertRequested":
					HandleAlert(arguments);
					break;
				case "OnActionSheetRequested":
					HandleActionSheet(arguments);
					break;
				case "OnPromptRequested":
					HandlePrompt(arguments);
					break;
			}

			return null;
		}
	}

	static void HandleAlert(object?[] arguments)
	{
		if (arguments.Length < 2 || arguments[1] is null)
			return;

		var request = arguments[1]!;
		var title = GetProperty<string>(request, "Title") ?? "Alert";
		var message = GetProperty<string>(request, "Message") ?? string.Empty;
		var accept = GetProperty<string>(request, "Accept");
		var cancel = GetProperty<string>(request, "Cancel");
		var result = GetProperty<object>(request, "Result");

		GLib.Functions.IdleAdd(0, () =>
		{
			var parent = GetGtkWindow(arguments[0]);
			if (parent is null)
			{
				SetResult(result, false);
				return false;
			}

			var (dialog, content) = CreateDialogWindow(parent, title, message);
			var responded = false;
			var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
			buttons.SetHalign(Gtk.Align.End);

			void AddButton(string text, bool value)
			{
				var button = Gtk.Button.NewWithLabel(text);
				button.OnClicked += (_, _) =>
				{
					if (responded)
						return;
					responded = true;
					SetResult(result, value);
					dialog.Close();
				};
				buttons.Append(button);
			}

			if (!string.IsNullOrEmpty(accept))
				AddButton(accept, true);
			if (!string.IsNullOrEmpty(cancel))
				AddButton(cancel, false);
			if (string.IsNullOrEmpty(accept) && string.IsNullOrEmpty(cancel))
				AddButton("OK", false);

			dialog.OnCloseRequest += (_, _) =>
			{
				if (!responded)
				{
					responded = true;
					SetResult(result, false);
				}
				return false;
			};

			content.Append(buttons);
			dialog.Present();
			return false;
		});
	}

	static void HandleActionSheet(object?[] arguments)
	{
		if (arguments.Length < 2 || arguments[1] is null)
			return;

		var request = arguments[1]!;
		var title = GetProperty<string>(request, "Title") ?? string.Empty;
		var cancel = GetProperty<string>(request, "Cancel") ?? "Cancel";
		var destruction = GetProperty<string>(request, "Destruction");
		var options = GetProperty<IEnumerable<string>>(request, "Buttons") ?? [];
		var result = GetProperty<object>(request, "Result");

		GLib.Functions.IdleAdd(0, () =>
		{
			var parent = GetGtkWindow(arguments[0]);
			if (parent is null)
			{
				SetResult(result, cancel);
				return false;
			}

			var (dialog, content) = CreateDialogWindow(parent, title, null);
			var responded = false;

			void AddButton(string text, bool destructive = false)
			{
				var button = Gtk.Button.NewWithLabel(text);
				if (destructive)
					button.AddCssClass("destructive-action");
				button.OnClicked += (_, _) =>
				{
					if (responded)
						return;
					responded = true;
					SetResult(result, text);
					dialog.Close();
				};
				content.Append(button);
			}

			foreach (var option in options)
				AddButton(option);
			if (!string.IsNullOrEmpty(destruction))
				AddButton(destruction, destructive: true);
			AddButton(cancel);

			dialog.OnCloseRequest += (_, _) =>
			{
				if (!responded)
				{
					responded = true;
					SetResult(result, cancel);
				}
				return false;
			};

			dialog.Present();
			return false;
		});
	}

	static void HandlePrompt(object?[] arguments)
	{
		if (arguments.Length < 2 || arguments[1] is null)
			return;

		var request = arguments[1]!;
		var title = GetProperty<string>(request, "Title") ?? "Prompt";
		var message = GetProperty<string>(request, "Message") ?? string.Empty;
		var accept = GetProperty<string>(request, "Accept") ?? "OK";
		var cancel = GetProperty<string>(request, "Cancel") ?? "Cancel";
		var placeholder = GetProperty<string>(request, "Placeholder") ?? string.Empty;
		var initialValue = GetProperty<string>(request, "InitialValue") ?? string.Empty;
		var result = GetProperty<object>(request, "Result");

		GLib.Functions.IdleAdd(0, () =>
		{
			var parent = GetGtkWindow(arguments[0]);
			if (parent is null)
			{
				SetResult(result, null);
				return false;
			}

			var (dialog, content) = CreateDialogWindow(parent, title, message);
			var responded = false;
			var entry = Gtk.Entry.New();
			if (!string.IsNullOrEmpty(placeholder))
				entry.SetPlaceholderText(placeholder);
			if (!string.IsNullOrEmpty(initialValue))
				entry.GetBuffer().SetText(initialValue, initialValue.Length);
			content.Append(entry);

			var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
			buttons.SetHalign(Gtk.Align.End);
			var cancelButton = Gtk.Button.NewWithLabel(cancel);
			var acceptButton = Gtk.Button.NewWithLabel(accept);

			void Complete(string? value)
			{
				if (responded)
					return;
				responded = true;
				SetResult(result, value);
				dialog.Close();
			}

			cancelButton.OnClicked += (_, _) => Complete(null);
			acceptButton.OnClicked += (_, _) => Complete(entry.GetBuffer().GetText());
			entry.OnActivate += (_, _) => Complete(entry.GetBuffer().GetText());
			dialog.OnCloseRequest += (_, _) =>
			{
				Complete(null);
				return false;
			};

			buttons.Append(cancelButton);
			buttons.Append(acceptButton);
			content.Append(buttons);
			dialog.Present();
			entry.GrabFocus();
			return false;
		});
	}

	static void SetResult(object? result, object? value) =>
		result?.GetType().GetMethod("TrySetResult")?.Invoke(result, [value]);

	static T? GetProperty<T>(object source, string name) =>
		source.GetType().GetProperty(name)?.GetValue(source) is T value ? value : default;

	static Gtk.Window? GetGtkWindow(object? page)
	{
		if (page is Page mauiPage &&
			mauiPage.GetParentWindow()?.Handler?.PlatformView is Gtk.Window gtkWindow)
			return gtkWindow;

		return (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow();
	}

	static (Gtk.Window Dialog, Gtk.Box Content) CreateDialogWindow(
		Gtk.Window parent,
		string title,
		string? message)
	{
		var dialog = new Gtk.Window();
		dialog.SetTitle(title);
		dialog.SetModal(true);
		dialog.SetTransientFor(parent);
		dialog.SetDefaultSize(400, -1);
		dialog.SetResizable(false);
		if (parent.GetApplication() is { } application)
			dialog.SetApplication(application);

		var content = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
		content.SetMarginTop(20);
		content.SetMarginBottom(20);
		content.SetMarginStart(20);
		content.SetMarginEnd(20);
		if (!string.IsNullOrEmpty(message))
		{
			var label = Gtk.Label.New(message);
			label.SetWrap(true);
			label.SetXalign(0);
			content.Append(label);
		}

		dialog.SetChild(content);
		return (dialog, content);
	}
}
#endif
