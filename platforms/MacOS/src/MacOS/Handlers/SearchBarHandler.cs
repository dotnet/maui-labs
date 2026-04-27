using Foundation;
using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class SearchBarHandler : MacOSViewHandler<ISearchBar, NSSearchField>
{
	public static readonly IPropertyMapper<ISearchBar, SearchBarHandler> Mapper =
		new PropertyMapper<ISearchBar, SearchBarHandler>(ViewMapper)
		{
			[nameof(ITextInput.Text)] = MapText,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(IPlaceholder.Placeholder)] = MapPlaceholder,
			[nameof(ISearchBar.CancelButtonColor)] = MapCancelButtonColor,
			[nameof(ITextInput.IsReadOnly)] = MapIsReadOnly,
			[nameof(ITextInput.MaxLength)] = MapMaxLength,
		};

	bool _updating;

	public SearchBarHandler() : base(Mapper)
	{
	}

	protected override NSSearchField CreatePlatformView()
	{
		return new NSSearchField
		{
			Bordered = true,
			Bezeled = true,
			BezelStyle = NSTextFieldBezelStyle.Rounded,
		};
	}

	protected override void ConnectHandler(NSSearchField platformView)
	{
		base.ConnectHandler(platformView);
		platformView.Changed += OnTextChanged;
		platformView.EditingEnded += OnEditingEnded;
	}

	protected override void DisconnectHandler(NSSearchField platformView)
	{
		platformView.Changed -= OnTextChanged;
		platformView.EditingEnded -= OnEditingEnded;
		base.DisconnectHandler(platformView);
	}

	void OnTextChanged(object? sender, EventArgs e)
	{
		if (_updating || VirtualView == null)
			return;

		_updating = true;
		try
		{
			if (VirtualView is ITextInput textInput)
				textInput.Text = PlatformView.StringValue ?? string.Empty;
		}
		finally
		{
			_updating = false;
		}
	}

	void OnEditingEnded(object? sender, EventArgs e)
	{
		if (VirtualView is ISearchBar searchBar)
			searchBar.SearchButtonPressed();
	}

	public static void MapText(SearchBarHandler handler, ISearchBar searchBar)
	{
		if (handler._updating)
			return;

		if (searchBar is ITextInput textInput)
			handler.PlatformView.StringValue = textInput.Text ?? string.Empty;
	}

	public static void MapTextColor(SearchBarHandler handler, ISearchBar searchBar)
	{
		if (searchBar is ITextStyle textStyle && textStyle.TextColor != null)
			handler.PlatformView.TextColor = textStyle.TextColor.ToPlatformColor();
	}

	public static void MapPlaceholder(SearchBarHandler handler, ISearchBar searchBar)
	{
		if (searchBar is IPlaceholder placeholder)
			handler.PlatformView.PlaceholderString = placeholder.Placeholder ?? string.Empty;
	}

	public static void MapCancelButtonColor(SearchBarHandler handler, ISearchBar searchBar)
	{
		// NSSearchField manages its own cancel button; no direct color API
	}

	public static void MapIsReadOnly(SearchBarHandler handler, ISearchBar searchBar)
	{
		if (searchBar is ITextInput textInput)
			handler.PlatformView.Editable = !textInput.IsReadOnly;
	}

	public static void MapMaxLength(SearchBarHandler handler, ISearchBar searchBar)
	{
		if (searchBar is ITextInput textInput && textInput.MaxLength >= 0)
		{
			var currentText = handler.PlatformView.StringValue ?? string.Empty;
			if (currentText.Length > textInput.MaxLength)
				handler.PlatformView.StringValue = currentText[..textInput.MaxLength];
		}
	}
}
