using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Comet.Handlers
{
	public partial class RadioButtonHandler
	{
		public static IPropertyMapper<RadioButton, RadioButtonHandler> Mapper =
			new PropertyMapper<RadioButton, RadioButtonHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IRadioButton.IsChecked)] = MapIsChecked,
				[nameof(RadioButton.Label)] = MapLabel,
				[nameof(ITextStyle.TextColor)] = MapTextColor,
			};

		public RadioButtonHandler() : base(Mapper)
		{
		}

		public static partial void MapIsChecked(RadioButtonHandler handler, RadioButton virtualView);
		public static partial void MapLabel(RadioButtonHandler handler, RadioButton virtualView);
		public static partial void MapTextColor(RadioButtonHandler handler, RadioButton virtualView);
	}
}
