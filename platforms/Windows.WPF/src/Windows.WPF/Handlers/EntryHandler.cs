namespace Microsoft.Maui.Handlers.WPF
{
	public partial class EntryHandler
	{
		public static IPropertyMapper<IEntry, EntryHandler> Mapper = new PropertyMapper<IEntry, EntryHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IEntry.Text)] = MapText,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(IEntry.Placeholder)] = MapPlaceholder,
			[nameof(IEntry.PlaceholderColor)] = MapPlaceholderColor,
			[nameof(IEntry.IsPassword)] = MapIsPassword,
			[nameof(IEntry.IsReadOnly)] = MapIsReadOnly,
			[nameof(IEntry.MaxLength)] = MapMaxLength,
			[nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(IEntry.ReturnType)] = MapReturnType,
			[nameof(IEntry.ClearButtonVisibility)] = MapClearButtonVisibility,
			[nameof(IEntry.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
			[nameof(IEntry.Keyboard)] = MapKeyboard,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IView.Background)] = MapBackground,
			[nameof(ITextInput.CursorPosition)] = MapCursorPosition,
			[nameof(ITextInput.SelectionLength)] = MapSelectionLength,
		};

		public static CommandMapper<IEntry, EntryHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public EntryHandler() : base(Mapper, CommandMapper)
		{
		}

		public EntryHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
