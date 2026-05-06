namespace Microsoft.Maui.Handlers.WPF
{
	public partial class TimePickerHandler
	{
		public static IPropertyMapper<ITimePicker, TimePickerHandler> Mapper = new PropertyMapper<ITimePicker, TimePickerHandler>(ViewHandler.ViewMapper)
		{
			[nameof(ITimePicker.Time)] = MapTime,
			[nameof(ITimePicker.Format)] = MapFormat,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
		};

		public static CommandMapper<ITimePicker, TimePickerHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public TimePickerHandler() : base(Mapper, CommandMapper)
		{
		}

		public TimePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
