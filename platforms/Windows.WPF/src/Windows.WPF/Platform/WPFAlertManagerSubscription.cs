#nullable enable
using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
#pragma warning disable IL2026, IL2060, IL2080, IL2111 // Reflection required for internal IAlertManagerSubscription

	/// <summary>
	/// Uses DispatchProxy to implement MAUI's internal IAlertManagerSubscription interface,
	/// routing alerts/prompts/action sheets to WPF dialogs.
	/// </summary>
	public class WPFAlertManagerSubscription : DispatchProxy
	{
		static readonly Type? AlertManagerType = typeof(Window).Assembly
			.GetType("Microsoft.Maui.Controls.Platform.AlertManager");

		static readonly Type? IAlertManagerSubscriptionType = AlertManagerType?
			.GetNestedType("IAlertManagerSubscription", BindingFlags.Public | BindingFlags.NonPublic);

		public static void Register(IServiceCollection services)
		{
			if (IAlertManagerSubscriptionType == null)
				return;

			var proxyType = typeof(WPFAlertManagerSubscriptionProxy<>).MakeGenericType(IAlertManagerSubscriptionType);
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
			if (arguments == null) return;

			if (arguments.Cancel == null)
			{
				System.Windows.MessageBox.Show(
					arguments.Message ?? string.Empty,
					arguments.Title ?? string.Empty,
					System.Windows.MessageBoxButton.OK,
					System.Windows.MessageBoxImage.Information);
				arguments.SetResult(true);
			}
			else
			{
				var result = System.Windows.MessageBox.Show(
					arguments.Message ?? string.Empty,
					arguments.Title ?? string.Empty,
					System.Windows.MessageBoxButton.YesNo,
					System.Windows.MessageBoxImage.Question,
					System.Windows.MessageBoxResult.No);

				arguments.SetResult(result == System.Windows.MessageBoxResult.Yes);
			}
		}

		static void OnPromptRequested(Page? sender, PromptArguments? arguments)
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

		static void OnActionSheetRequested(Page? sender, ActionSheetArguments? arguments)
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

			foreach (var button in arguments.Buttons)
			{
				if (button == null) continue;
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
}
