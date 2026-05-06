namespace Microsoft.Maui.Handlers.WPF
{
	public partial class PickerHandler
	{
		public static IPropertyMapper<IPicker, PickerHandler> Mapper = new PropertyMapper<IPicker, PickerHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IPicker.Title)] = MapTitle,
			[nameof(IPicker.TitleColor)] = MapTitleColor,
			[nameof(IPicker.SelectedIndex)] = MapSelectedIndex,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IView.Background)] = MapBackground,
			[nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(IItemDelegate<string>.GetCount)] = MapItems,
		};

		public static CommandMapper<IPicker, PickerHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public PickerHandler() : base(Mapper, CommandMapper)
		{
		}

		public PickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
