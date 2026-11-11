#if NET11_0_OR_GREATER
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Handles MAUI alerts, action sheets, and prompts using GTK4 modal windows.
/// </summary>
public sealed class GtkAlertManager : IAlertManagerSubscription
{
	public static void Register(IServiceCollection services)
	{
		services.AddSingleton<IAlertManagerSubscription, GtkAlertManager>();
	}

	public void OnAlertRequested(Page sender, AlertArguments arguments)
	{
		GLib.Functions.IdleAdd(0, () =>
		{
			var gtkWindow = GetGtkWindow(sender);
			if (gtkWindow is null)
			{
				arguments.SetResult(false);
				return false;
			}

			var (dialog, box) = CreateDialogWindow(
				gtkWindow,
				arguments.Title ?? "Alert",
				arguments.Message ?? string.Empty);
			var responded = false;

			var buttonBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
			buttonBox.SetHalign(Gtk.Align.End);

			if (!string.IsNullOrEmpty(arguments.Accept))
			{
				var acceptButton = Gtk.Button.NewWithLabel(arguments.Accept);
				acceptButton.OnClicked += (_, _) =>
				{
					if (responded)
						return;

					responded = true;
					arguments.SetResult(true);
					dialog.Close();
				};
				buttonBox.Append(acceptButton);
			}

			if (!string.IsNullOrEmpty(arguments.Cancel))
			{
				var cancelButton = Gtk.Button.NewWithLabel(arguments.Cancel);
				cancelButton.OnClicked += (_, _) =>
				{
					if (responded)
						return;

					responded = true;
					arguments.SetResult(false);
					dialog.Close();
				};
				buttonBox.Append(cancelButton);
			}

			if (string.IsNullOrEmpty(arguments.Accept) && string.IsNullOrEmpty(arguments.Cancel))
			{
				var okButton = Gtk.Button.NewWithLabel("OK");
				okButton.OnClicked += (_, _) =>
				{
					if (responded)
						return;

					responded = true;
					arguments.SetResult(false);
					dialog.Close();
				};
				buttonBox.Append(okButton);
			}

			dialog.OnCloseRequest += (_, _) =>
			{
				if (!responded)
				{
					responded = true;
					arguments.SetResult(false);
				}

				return false;
			};

			box.Append(buttonBox);
			dialog.Present();
			return false;
		});
	}

	public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments)
	{
		GLib.Functions.IdleAdd(0, () =>
		{
			var cancel = arguments.Cancel ?? "Cancel";
			var gtkWindow = GetGtkWindow(sender);
			if (gtkWindow is null)
			{
				arguments.SetResult(cancel);
				return false;
			}

			var (dialog, box) = CreateDialogWindow(
				gtkWindow,
				arguments.Title ?? string.Empty,
				message: null);
			var responded = false;

			foreach (var buttonText in arguments.Buttons ?? [])
			{
				var button = Gtk.Button.NewWithLabel(buttonText);
				button.OnClicked += (_, _) =>
				{
					if (responded)
						return;

					responded = true;
					arguments.SetResult(buttonText);
					dialog.Close();
				};
				box.Append(button);
			}

			if (!string.IsNullOrEmpty(arguments.Destruction))
			{
				var destructionButton = Gtk.Button.NewWithLabel(arguments.Destruction);
				destructionButton.AddCssClass("destructive-action");
				destructionButton.OnClicked += (_, _) =>
				{
					if (responded)
						return;

					responded = true;
					arguments.SetResult(arguments.Destruction);
					dialog.Close();
				};
				box.Append(destructionButton);
			}

			var cancelButton = Gtk.Button.NewWithLabel(cancel);
			cancelButton.OnClicked += (_, _) =>
			{
				if (responded)
					return;

				responded = true;
				arguments.SetResult(cancel);
				dialog.Close();
			};
			box.Append(cancelButton);

			dialog.OnCloseRequest += (_, _) =>
			{
				if (!responded)
				{
					responded = true;
					arguments.SetResult(cancel);
				}

				return false;
			};

			dialog.Present();
			return false;
		});
	}

	public void OnPromptRequested(Page sender, PromptArguments arguments)
	{
		GLib.Functions.IdleAdd(0, () =>
		{
			var gtkWindow = GetGtkWindow(sender);
			if (gtkWindow is null)
			{
				arguments.SetResult(null!);
				return false;
			}

			var (dialog, box) = CreateDialogWindow(
				gtkWindow,
				arguments.Title ?? "Prompt",
				arguments.Message ?? string.Empty);
			var responded = false;

			var entry = Gtk.Entry.New();
			if (!string.IsNullOrEmpty(arguments.Placeholder))
				entry.SetPlaceholderText(arguments.Placeholder);
			if (!string.IsNullOrEmpty(arguments.InitialValue))
				entry.GetBuffer().SetText(arguments.InitialValue, arguments.InitialValue.Length);
			if (arguments.MaxLength >= 0)
				entry.SetMaxLength(arguments.MaxLength);
			box.Append(entry);

			var buttonBox = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
			buttonBox.SetHalign(Gtk.Align.End);

			var cancelButton = Gtk.Button.NewWithLabel(arguments.Cancel ?? "Cancel");
			var acceptButton = Gtk.Button.NewWithLabel(arguments.Accept ?? "OK");

			cancelButton.OnClicked += (_, _) =>
			{
				if (responded)
					return;

				responded = true;
				arguments.SetResult(null!);
				dialog.Close();
			};

			void Accept()
			{
				if (responded)
					return;

				responded = true;
				arguments.SetResult(entry.GetBuffer().GetText());
				dialog.Close();
			}

			acceptButton.OnClicked += (_, _) => Accept();
			entry.OnActivate += (_, _) => Accept();

			dialog.OnCloseRequest += (_, _) =>
			{
				if (!responded)
				{
					responded = true;
					arguments.SetResult(null!);
				}

				return false;
			};

			buttonBox.Append(cancelButton);
			buttonBox.Append(acceptButton);
			box.Append(buttonBox);
			dialog.Present();
			entry.GrabFocus();
			return false;
		});
	}

	static Gtk.Window? GetGtkWindow(Page page)
	{
		var window = page.GetParentWindow();
		if (window?.Handler?.PlatformView is Gtk.Window gtkWindow)
			return gtkWindow;

		return (Gtk.Application.GetDefault() as Gtk.Application)?.GetActiveWindow();
	}

	static (Gtk.Window Dialog, Gtk.Box Content) CreateDialogWindow(
		Gtk.Window parent,
		string title,
		string? message,
		int width = 400)
	{
		var dialog = new Gtk.Window();
		dialog.SetTitle(title);
		dialog.SetModal(true);
		dialog.SetTransientFor(parent);
		dialog.SetDefaultSize(width, -1);
		dialog.SetResizable(false);

		var app = parent.GetApplication();
		if (app is not null)
			dialog.SetApplication(app);

		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
		box.SetMarginTop(20);
		box.SetMarginBottom(20);
		box.SetMarginStart(20);
		box.SetMarginEnd(20);

		if (!string.IsNullOrEmpty(message))
		{
			var messageLabel = Gtk.Label.New(message);
			messageLabel.SetWrap(true);
			messageLabel.SetXalign(0);
			box.Append(messageLabel);
		}

		dialog.SetChild(box);
		return (dialog, box);
	}
}
#endif
