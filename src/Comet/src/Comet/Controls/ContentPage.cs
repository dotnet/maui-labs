using System;

namespace Comet
{
	// ContentPage is just a ContentView in Comet's MVU model
	// This provides API compatibility for code that references ContentPage
	public class ContentPage : ContentView
	{
		public new string Title { get; set; }
		public View ToolbarItems { get; set; }
	}
}
