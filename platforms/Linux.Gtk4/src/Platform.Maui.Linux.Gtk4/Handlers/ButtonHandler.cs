using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class ButtonHandler : GtkViewHandler<IButton, Gtk.Button>
{
	public static new IPropertyMapper<IButton, ButtonHandler> Mapper =
		new PropertyMapper<IButton, ButtonHandler>(ViewMapper)
		{
			[nameof(ITextButton.Text)] = MapText,
			[nameof(ITextButton.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(IButton.Padding)] = MapPadding,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
			[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
			[nameof(IImageButton.Source)] = MapImageSource,
		};

	public ButtonHandler() : base(Mapper)
	{
	}

	protected override Gtk.Button CreatePlatformView()
	{
		var button = Gtk.Button.New();
		return button;
	}

	protected override void ConnectHandler(Gtk.Button platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnClicked += OnClicked;
	}

	protected override void DisconnectHandler(Gtk.Button platformView)
	{
		platformView.OnClicked -= OnClicked;
		base.DisconnectHandler(platformView);
	}

	void OnClicked(Gtk.Button sender, EventArgs args)
	{
		try
		{
			VirtualView?.Clicked();
			VirtualView?.Released();
		}
		catch (InvalidOperationException) { }
	}

	public static void MapText(ButtonHandler handler, IButton button)
	{
		if (button is ITextButton textButton)
		{
			handler.PlatformView?.SetLabel(textButton.Text ?? string.Empty);
		}
	}

	public static void MapTextColor(ButtonHandler handler, IButton button)
	{
		if (button is ITextStyle textStyle && textStyle.TextColor != null)
		{
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(textStyle.TextColor)};");
		}
	}

	public static void MapFont(ButtonHandler handler, IButton button)
	{
		if (button is not ITextStyle textStyle)
			return;

		var css = handler.BuildFontCss(textStyle.Font);
		if (!string.IsNullOrEmpty(css))
			handler.ApplyCss(handler.PlatformView, css);
	}

	public static void MapPadding(ButtonHandler handler, IButton button)
	{
		var p = button.Padding;
		handler.ApplyCss(handler.PlatformView,
			$"padding: {(int)p.Top}px {(int)p.Right}px {(int)p.Bottom}px {(int)p.Left}px;");
	}

	public static void MapCharacterSpacing(ButtonHandler handler, IButton button)
	{
		if (button is ITextStyle textStyle)
			handler.ApplyCss(handler.PlatformView, $"letter-spacing: {textStyle.CharacterSpacing}px;");
	}

	public static void MapCornerRadius(ButtonHandler handler, IButton button)
	{
		if (button is IButtonStroke stroke && stroke.CornerRadius >= 0)
			handler.ApplyCss(handler.PlatformView, $"border-radius: {stroke.CornerRadius}px;");
	}

	public static void MapStrokeColor(ButtonHandler handler, IButton button)
	{
		if (button is IButtonStroke stroke && stroke.StrokeColor != null)
			handler.ApplyCss(handler.PlatformView, $"border-color: {ToGtkColor(stroke.StrokeColor)};");
	}

	public static void MapStrokeThickness(ButtonHandler handler, IButton button)
	{
		if (button is IButtonStroke stroke && stroke.StrokeThickness >= 0)
			handler.ApplyCss(handler.PlatformView,
				$"border-width: {stroke.StrokeThickness}px; border-style: solid;");
	}

	public static void MapImageSource(ButtonHandler handler, IButton button)
	{
		// Button image source requires async image loading infrastructure; not yet wired.
	}
}
