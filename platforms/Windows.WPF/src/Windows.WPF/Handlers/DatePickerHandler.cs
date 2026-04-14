namespace Microsoft.Maui.Handlers.WPF
{
	public partial class DatePickerHandler
	{
		public static IPropertyMapper<IDatePicker, DatePickerHandler> Mapper = new PropertyMapper<IDatePicker, DatePickerHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IDatePicker.Date)] = MapDate,
			[nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
			[nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
			[nameof(IDatePicker.Format)] = MapFormat,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
		};

		public static CommandMapper<IDatePicker, DatePickerHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public DatePickerHandler() : base(Mapper, CommandMapper)
		{
		}

		public DatePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
