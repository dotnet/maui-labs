using System;
using System.Windows.Input;

namespace Comet
{
	/// <summary>
	/// Customizes the back button appearance and behavior in Shell navigation.
	/// Usage: page.BackButtonBehavior(new BackButtonBehavior {
	///     IsVisible = false,
	///     Title = "Cancel",
	///     Command = new Command(() => GoBack())
	/// })
	/// </summary>
	public class BackButtonBehavior
	{
		public bool IsVisible { get; set; } = true;
		public bool IsEnabled { get; set; } = true;
		public string Title { get; set; }
		public ICommand Command { get; set; }
		public object CommandParameter { get; set; }
		public string IconOverride { get; set; }
	}
}
