using System;
using System.Collections.Generic;
using Microsoft.Maui;

namespace Comet
{
	// TitleBar is a Windows-specific custom title bar control
	// Simplified implementation - handlers will need platform-specific implementation
	public class TitleBar : ContentView
	{
		public new string Title { get; set; }
		public string Subtitle { get; set; }
		public View LeadingContent { get; set; }
		public View TrailingContent { get; set; }
		public IList<View> PassthroughElements { get; } = new List<View>();
	}
}
