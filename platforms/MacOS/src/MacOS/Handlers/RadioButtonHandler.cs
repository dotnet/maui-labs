using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class RadioButtonHandler : MacOSViewHandler<IRadioButton, NSButton>
{
	public static readonly IPropertyMapper<IRadioButton, RadioButtonHandler> Mapper =
		new PropertyMapper<IRadioButton, RadioButtonHandler>(ViewMapper)
		{
			[nameof(IRadioButton.IsChecked)] = MapIsChecked,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(IText.Text)] = MapContent,
			[nameof(IContentView.Content)] = MapContent,
		};

	bool _updating;

	public RadioButtonHandler() : base(Mapper)
	{
	}

	protected override NSButton CreatePlatformView()
	{
		var button = new NSButton
		{
			Title = string.Empty,
		};
		button.SetButtonType(NSButtonType.Radio);
		return button;
	}

	protected override void ConnectHandler(NSButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.Activated += OnActivated;
	}

	protected override void DisconnectHandler(NSButton platformView)
	{
		platformView.Activated -= OnActivated;
		base.DisconnectHandler(platformView);
	}

	void OnActivated(object? sender, EventArgs e)
	{
		if (_updating || VirtualView == null)
			return;

		_updating = true;
		try
		{
			VirtualView.IsChecked = PlatformView.State == NSCellStateValue.On;
		}
		finally
		{
			_updating = false;
		}
	}

	public static void MapIsChecked(RadioButtonHandler handler, IRadioButton view)
	{
		if (handler._updating)
			return;

		handler.PlatformView.State = view.IsChecked ? NSCellStateValue.On : NSCellStateValue.Off;
	}

	public static void MapTextColor(RadioButtonHandler handler, IRadioButton view)
	{
		if (view is ITextStyle textStyle && textStyle.TextColor != null)
			handler.PlatformView.ContentTintColor = textStyle.TextColor.ToPlatformColor();
	}

	public static void MapContent(RadioButtonHandler handler, IRadioButton view)
	{
		var text = ExtractContentText(view) ?? string.Empty;

		// Apply TextTransform if available
		if (view is Microsoft.Maui.Controls.RadioButton rb)
		{
			text = rb.TextTransform switch
			{
				TextTransform.Uppercase => text.ToUpperInvariant(),
				TextTransform.Lowercase => text.ToLowerInvariant(),
				_ => text,
			};
		}

		handler.PlatformView.Title = text;
		handler.PlatformView.InvalidateIntrinsicContentSize();
	}

	static string? ExtractContentText(IRadioButton view)
	{
		var content = view.Content;
		if (content == null)
			return null;

		if (content is string s)
			return s;

		// For View content, try to extract meaningful text
		if (content is IText textView && !string.IsNullOrEmpty(textView.Text))
			return textView.Text;

		if (content is ILabel label && !string.IsNullOrEmpty(label.Text))
			return label.Text;

		// Recursively search child views for text content
		if (content is IView viewContent)
		{
			var extracted = ExtractTextFromViewTree(viewContent);
			if (!string.IsNullOrEmpty(extracted))
				return extracted;
		}

		return content.ToString();
	}

	static string? ExtractTextFromViewTree(IView view)
	{
		if (view is IText tv && !string.IsNullOrEmpty(tv.Text))
			return tv.Text;
		if (view is ILabel lbl && !string.IsNullOrEmpty(lbl.Text))
			return lbl.Text;

		if (view is Microsoft.Maui.ILayout layout)
		{
			foreach (var child in layout)
			{
				var text = ExtractTextFromViewTree(child);
				if (!string.IsNullOrEmpty(text))
					return text;
			}
		}

		return null;
	}
}
