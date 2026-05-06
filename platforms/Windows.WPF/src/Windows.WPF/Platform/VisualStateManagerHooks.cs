#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Hooks WPF mouse/focus events to trigger MAUI VisualStateManager state changes.
	/// Maps: PointerOver, Pressed, Focused, Disabled visual states.
	/// </summary>
	public static class VisualStateManagerHooks
	{
		/// <summary>
		/// Wire up WPF events on a platform element to notify MAUI VSM.
		/// Should be called after the handler connects.
		/// </summary>
		public static void AttachVisualStateHooks(FrameworkElement platformView, IView virtualView)
		{
			if (platformView == null || virtualView == null) return;

			platformView.MouseEnter += (s, e) => GoToState(virtualView, "PointerOver");
			platformView.MouseLeave += (s, e) => GoToState(virtualView, "Normal");
			platformView.PreviewMouseLeftButtonDown += (s, e) => GoToState(virtualView, "Pressed");
			platformView.PreviewMouseLeftButtonUp += (s, e) =>
			{
				if (platformView.IsMouseOver)
					GoToState(virtualView, "PointerOver");
				else
					GoToState(virtualView, "Normal");
			};

			platformView.GotFocus += (s, e) => GoToState(virtualView, "Focused");
			platformView.LostFocus += (s, e) =>
			{
				if (platformView.IsMouseOver)
					GoToState(virtualView, "PointerOver");
				else
					GoToState(virtualView, "Normal");
			};

			platformView.IsEnabledChanged += (s, e) =>
			{
				if (!(bool)e.NewValue)
					GoToState(virtualView, "Disabled");
				else
					GoToState(virtualView, "Normal");
			};
		}

		static void GoToState(IView view, string stateName)
		{
			try
			{
				if (view is Microsoft.Maui.Controls.VisualElement ve)
				{
					Microsoft.Maui.Controls.VisualStateManager.GoToState(ve, stateName);
				}
			}
			catch { }
		}
	}
}
