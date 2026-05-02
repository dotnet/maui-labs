using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler : ViewHandler<RadioButton, object>
	{
		protected override object CreatePlatformView() => throw new NotSupportedException();

		public static partial void MapIsChecked(RadioButtonHandler handler, RadioButton virtualView) { }
		public static partial void MapLabel(RadioButtonHandler handler, RadioButton virtualView) { }
		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView) { }
	}
}
