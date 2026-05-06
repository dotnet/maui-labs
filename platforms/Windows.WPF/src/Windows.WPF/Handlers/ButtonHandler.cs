namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ButtonHandler
	{
		public static IPropertyMapper<IButton, ButtonHandler> Mapper = new PropertyMapper<IButton, ButtonHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IText.Text)] = MapText,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IButton.Padding)] = MapPadding,
			[nameof(IView.Background)] = MapBackground,
			[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
			[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
			[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			[nameof(IImageSourcePart.Source)] = MapImageSource,
		};

		public static CommandMapper<IButton, ButtonHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public ButtonHandler() : base(Mapper, CommandMapper)
		{
		}

		public ButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
