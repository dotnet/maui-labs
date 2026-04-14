namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SearchBarHandler
	{
		public static IPropertyMapper<ISearchBar, SearchBarHandler> Mapper = new PropertyMapper<ISearchBar, SearchBarHandler>(ViewHandler.ViewMapper)
		{
			[nameof(ISearchBar.Text)] = MapText,
			[nameof(ISearchBar.Placeholder)] = MapPlaceholder,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(ISearchBar.PlaceholderColor)] = MapPlaceholderColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ISearchBar.CancelButtonColor)] = MapCancelButtonColor,
			[nameof(ISearchBar.IsTextPredictionEnabled)] = MapIsTextPredictionEnabled,
			[nameof(ISearchBar.MaxLength)] = MapMaxLength,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
		};

		public static CommandMapper<ISearchBar, SearchBarHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public SearchBarHandler() : base(Mapper, CommandMapper)
		{
		}

		public SearchBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
