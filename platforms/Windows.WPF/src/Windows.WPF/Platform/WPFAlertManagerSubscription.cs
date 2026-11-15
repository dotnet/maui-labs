#nullable enable
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Platform;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Implements MAUI's public <see cref="IAlertManagerSubscription"/>, routing
	/// alerts/prompts/action sheets to WPF dialogs. MAUI's default AlertManager
	/// resolves it from DI when a window is created.
	/// </summary>
	public sealed class WPFAlertManagerSubscription : IAlertManagerSubscription
	{
		public static void Register(IServiceCollection services)
		{
			services.AddSingleton<IAlertManagerSubscription>(new WPFAlertManagerSubscription());
		}

		public void OnAlertRequested(Page sender, AlertArguments arguments) => HandleAlertRequested(sender, arguments);

		public void OnPromptRequested(Page sender, PromptArguments arguments) => HandlePromptRequested(sender, arguments);

		public void OnActionSheetRequested(Page sender, ActionSheetArguments arguments) => HandleActionSheetRequested(sender, arguments);

		static void HandleAlertRequested(Page? sender, AlertArguments? arguments)
		{
			if (arguments == null) return;

			try
			{
				arguments.SetResult(ShowAlert(sender, arguments));
			}
			catch (Exception exception)
			{
				arguments.Result.TrySetException(exception);
			}
		}

		static bool ShowAlert(Page? sender, AlertArguments arguments)
		{
			var owner = sender?.Window?.Handler?.PlatformView as System.Windows.Window
				?? System.Windows.Application.Current?.MainWindow;
			// A startup alert can precede the owner's native window creation.
			if (owner != null && (!owner.CheckAccess()
				|| new System.Windows.Interop.WindowInteropHelper(owner).Handle == IntPtr.Zero))
				owner = null;

			var dialog = new System.Windows.Window
			{
				Title = arguments.Title ?? string.Empty,
				SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
				WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
				Owner = owner,
				ResizeMode = System.Windows.ResizeMode.NoResize,
				ShowInTaskbar = false,
				MinWidth = 300,
				MaxWidth = 500,
				FlowDirection = arguments.FlowDirection == FlowDirection.RightToLeft
					? System.Windows.FlowDirection.RightToLeft
					: System.Windows.FlowDirection.LeftToRight,
			};

			var panel = new System.Windows.Controls.Grid
			{
				Margin = new System.Windows.Thickness(16),
			};
			panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
			{
				Height = System.Windows.GridLength.Auto,
			});
			panel.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
			{
				Height = System.Windows.GridLength.Auto,
			});
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

			// The one-button overload supplies Cancel, not Accept. Keep cancellation
			// as the default action, including for two-button destructive alerts.
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
			return dialog.ShowDialog() == true;
		}

		static System.Windows.Controls.Button CreateAlertButton(string text)
		{
			return new System.Windows.Controls.Button
			{
				// A TextBlock preserves literal underscores instead of access keys.
				Content = new System.Windows.Controls.TextBlock { Text = text },
				Padding = new System.Windows.Thickness(16, 6, 16, 6),
				Margin = new System.Windows.Thickness(4, 0, 0, 0),
				MinWidth = 80,
			};
		}

		static void HandlePromptRequested(Page? sender, PromptArguments? arguments)
		{
			if (arguments == null) return;

			var dialog = new System.Windows.Window
			{
				Title = arguments.Title ?? string.Empty,
				SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
				WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
				Owner = System.Windows.Application.Current?.MainWindow,
				ResizeMode = System.Windows.ResizeMode.NoResize,
				MinWidth = 350,
			};

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

			bool accepted = false;

			var cancelBtn = new System.Windows.Controls.Button
			{
				Content = arguments.Cancel,
				Padding = new System.Windows.Thickness(16, 6, 16, 6),
				Margin = new System.Windows.Thickness(0, 0, 8, 0),
				MinWidth = 80,
			};
			cancelBtn.Click += (s, e) => { dialog.Close(); };

			var acceptBtn = new System.Windows.Controls.Button
			{
				Content = arguments.Accept,
				Padding = new System.Windows.Thickness(16, 6, 16, 6),
				MinWidth = 80,
				IsDefault = true,
			};
			acceptBtn.Click += (s, e) => { accepted = true; dialog.Close(); };

			buttonPanel.Children.Add(cancelBtn);
			buttonPanel.Children.Add(acceptBtn);
			panel.Children.Add(buttonPanel);

			dialog.Content = panel;
			dialog.ShowDialog();

			arguments.SetResult(accepted ? textBox.Text : null);
		}

		static void HandleActionSheetRequested(Page? sender, ActionSheetArguments? arguments)
		{
			if (arguments == null) return;

			var dialog = new System.Windows.Window
			{
				Title = arguments.Title ?? string.Empty,
				SizeToContent = System.Windows.SizeToContent.WidthAndHeight,
				WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
				Owner = System.Windows.Application.Current?.MainWindow,
				ResizeMode = System.Windows.ResizeMode.NoResize,
				MinWidth = 300,
				MaxWidth = 500,
			};

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

			string? selectedResult = null;

			foreach (var button in arguments.Buttons.OfType<string>())
			{
				var btn = CreateDialogButton(button);
				var capturedButton = button;
				btn.Click += (s, e) => { selectedResult = capturedButton; dialog.Close(); };
				panel.Children.Add(btn);
			}

			if (arguments.Destruction != null)
			{
				var destructBtn = CreateDialogButton(arguments.Destruction);
				destructBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
				var destruction = arguments.Destruction;
				destructBtn.Click += (s, e) => { selectedResult = destruction; dialog.Close(); };
				panel.Children.Add(destructBtn);
			}

			if (arguments.Cancel != null)
			{
				var cancelBtn = CreateDialogButton(arguments.Cancel);
				cancelBtn.Margin = new System.Windows.Thickness(0, 8, 0, 0);
				var cancel = arguments.Cancel;
				cancelBtn.Click += (s, e) => { selectedResult = cancel; dialog.Close(); };
				panel.Children.Add(cancelBtn);
			}

			dialog.Content = panel;
			dialog.ShowDialog();

			arguments.SetResult(selectedResult ?? arguments.Cancel ?? string.Empty);
		}

		static System.Windows.Controls.Button CreateDialogButton(string text)
		{
			return new System.Windows.Controls.Button
			{
				Content = text,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
				HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
				Padding = new System.Windows.Thickness(12, 8, 12, 8),
				Margin = new System.Windows.Thickness(0, 2, 0, 2),
				FontSize = 14,
			};
		}
	}
}
