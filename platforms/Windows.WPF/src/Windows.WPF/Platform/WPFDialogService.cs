#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Platforms.Windows.WPF;

/// <summary>
/// WPF implementation of MAUI's public keyed dialog delegates.
/// </summary>
public static class WPFDialogService
{
	public const string DisplayAlertKey = "Microsoft.Maui.Controls.DisplayAlert";
	public const string DisplayActionSheetKey = "Microsoft.Maui.Controls.DisplayActionSheet";
	public const string DisplayPromptKey = "Microsoft.Maui.Controls.DisplayPrompt";

	public static void Register(IServiceCollection services)
	{
#if !NET10_0
		services.AddKeyedSingleton<Func<Page, AlertArguments, Task<bool>>>(
			DisplayAlertKey, static (_, arguments) => ShowAlertAsync(_, arguments));
		services.AddKeyedSingleton<Func<Page, ActionSheetArguments, Task<string>>>(
			DisplayActionSheetKey, static (_, arguments) => ShowActionSheetAsync(_, arguments));
		services.AddKeyedSingleton<Func<Page, PromptArguments, Task<string>>>(
			DisplayPromptKey, static (_, arguments) => ShowPromptAsync(_, arguments));
#endif
	}

	public static Task<bool> ShowAlertAsync(Page? sender, AlertArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		var owner = GetOwner(sender);
		var dialog = CreateDialog(owner, arguments.Title, 300, 500);
		dialog.FlowDirection = arguments.FlowDirection == FlowDirection.RightToLeft
			? System.Windows.FlowDirection.RightToLeft
			: System.Windows.FlowDirection.LeftToRight;

		var panel = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(16) };
		panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
		panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
		panel.Children.Add(new System.Windows.Controls.ScrollViewer
		{
			MaxHeight = System.Windows.SystemParameters.WorkArea.Height / 2,
			VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
			Margin = new System.Windows.Thickness(0, 0, 0, 12),
			Content = new System.Windows.Controls.TextBlock
			{
				Text = arguments.Message ?? string.Empty,
				TextWrapping = System.Windows.TextWrapping.Wrap,
				FontSize = 14,
			},
		});

		var buttons = new System.Windows.Controls.WrapPanel
		{
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
		};
		if (arguments.Accept != null)
		{
			var accept = CreateAlertButton(arguments.Accept);
			accept.IsDefault = arguments.Cancel == null;
			accept.Click += (_, _) => dialog.DialogResult = true;
			buttons.Children.Add(accept);
		}
		if (arguments.Cancel != null)
		{
			var cancel = CreateAlertButton(arguments.Cancel);
			cancel.IsCancel = true;
			cancel.IsDefault = true;
			cancel.Click += (_, _) => dialog.DialogResult = false;
			buttons.Children.Add(cancel);
		}

		System.Windows.Controls.Grid.SetRow(buttons, 1);
		panel.Children.Add(buttons);
		dialog.Content = panel;
		return Task.FromResult(dialog.ShowDialog() == true);
	}

	public static Task<string> ShowPromptAsync(Page? sender, PromptArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		var dialog = CreateDialog(GetOwner(sender), arguments.Title, 350, double.PositiveInfinity);
		var panel = new System.Windows.Controls.StackPanel
		{
			Margin = new System.Windows.Thickness(16),
			MinWidth = 320,
		};
		panel.Children.Add(new System.Windows.Controls.TextBlock
		{
			Text = arguments.Message ?? string.Empty,
			FontSize = 14,
			Margin = new System.Windows.Thickness(0, 0, 0, 12),
			TextWrapping = System.Windows.TextWrapping.Wrap,
		});

		var textBox = new System.Windows.Controls.TextBox
		{
			Text = arguments.InitialValue ?? string.Empty,
			MaxLength = arguments.MaxLength > 0 ? arguments.MaxLength : int.MaxValue,
			Padding = new System.Windows.Thickness(6, 4, 6, 4),
			FontSize = 14,
		};
		panel.Children.Add(textBox);

		var buttonPanel = new System.Windows.Controls.StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
			Margin = new System.Windows.Thickness(0, 12, 0, 0),
		};
		string? result = null;
		var cancel = CreateAlertButton(arguments.Cancel);
		cancel.IsCancel = true;
		cancel.Click += (_, _) => dialog.Close();
		var accept = CreateAlertButton(arguments.Accept);
		accept.IsDefault = true;
		accept.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
		buttonPanel.Children.Add(cancel);
		buttonPanel.Children.Add(accept);
		panel.Children.Add(buttonPanel);
		dialog.Content = panel;
		dialog.ShowDialog();
		return Task.FromResult(result!);
	}

	public static Task<string> ShowActionSheetAsync(Page? sender, ActionSheetArguments arguments)
	{
		ArgumentNullException.ThrowIfNull(arguments);
		var dialog = CreateDialog(GetOwner(sender), arguments.Title, 300, 500);
		var panel = new System.Windows.Controls.StackPanel
		{
			Margin = new System.Windows.Thickness(16),
			MinWidth = 280,
		};
		panel.Children.Add(new System.Windows.Controls.TextBlock
		{
			Text = arguments.Title ?? string.Empty,
			FontSize = 16,
			FontWeight = System.Windows.FontWeights.SemiBold,
			Margin = new System.Windows.Thickness(0, 0, 0, 12),
		});

		string? result = null;
		foreach (var option in (arguments.Buttons ?? Enumerable.Empty<string>()).Where(static option => option != null))
			AddActionButton(panel, dialog, option!, value => result = value);
		if (arguments.Destruction != null)
		{
			var destructive = AddActionButton(panel, dialog, arguments.Destruction, value => result = value);
			destructive.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
		}
		if (arguments.Cancel != null)
		{
			var cancel = AddActionButton(panel, dialog, arguments.Cancel, value => result = value);
			cancel.IsCancel = true;
			cancel.Margin = new System.Windows.Thickness(0, 8, 0, 0);
		}
		dialog.Content = panel;
		dialog.ShowDialog();
		return Task.FromResult(result ?? arguments.Cancel ?? string.Empty);
	}

	static System.Windows.Window? GetOwner(Page? sender)
	{
		var owner = sender?.Window?.Handler?.PlatformView as System.Windows.Window
			?? System.Windows.Application.Current?.MainWindow;
		if (owner != null && (!owner.CheckAccess()
			|| new System.Windows.Interop.WindowInteropHelper(owner).Handle == IntPtr.Zero))
			return null;
		return owner;
	}

	static System.Windows.Window CreateDialog(System.Windows.Window? owner, string? title, double minWidth, double maxWidth)
	{
		var dialog = new System.Windows.Window
		{
			Title = title ?? string.Empty,
			SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
			WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
			Owner = owner,
			ResizeMode = System.Windows.ResizeMode.NoResize,
			ShowInTaskbar = false,
			MinWidth = minWidth,
		};
		if (!double.IsPositiveInfinity(maxWidth))
			dialog.MaxWidth = maxWidth;
		return dialog;
	}

	static System.Windows.Controls.Button CreateAlertButton(string? text) => new()
	{
		Content = new System.Windows.Controls.TextBlock { Text = text ?? string.Empty },
		Padding = new System.Windows.Thickness(16, 6, 16, 6),
		Margin = new System.Windows.Thickness(4, 0, 0, 0),
		MinWidth = 80,
	};

	static System.Windows.Controls.Button AddActionButton(
		System.Windows.Controls.Panel panel,
		System.Windows.Window dialog,
		string text,
		Action<string> select)
	{
		var button = new System.Windows.Controls.Button
		{
			Content = text,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
			Padding = new System.Windows.Thickness(12, 8, 12, 8),
			Margin = new System.Windows.Thickness(0, 2, 0, 2),
			FontSize = 14,
		};
		button.Click += (_, _) => { select(text); dialog.Close(); };
		panel.Children.Add(button);
		return button;
	}
}
