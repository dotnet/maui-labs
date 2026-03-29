using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class RadioButtonHandler : GtkViewHandler<IRadioButton, Gtk.CheckButton>
{
	static readonly Dictionary<string, WeakReference<Gtk.CheckButton>> _groupLeaders = new();

	public static new IPropertyMapper<IRadioButton, RadioButtonHandler> Mapper =
		new PropertyMapper<IRadioButton, RadioButtonHandler>(ViewMapper)
		{
			[nameof(IRadioButton.IsChecked)] = MapIsChecked,
			[nameof(IRadioButton.Content)] = MapContent,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(ITextStyle.TextColor)] = MapTextColor,
			[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
			[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
		};

	public RadioButtonHandler() : base(Mapper)
	{
	}

	protected override Gtk.CheckButton CreatePlatformView()
	{
		return Gtk.CheckButton.New();
	}

	protected override void ConnectHandler(Gtk.CheckButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnToggled += OnToggled;
		UpdateGroup();
	}

	protected override void DisconnectHandler(Gtk.CheckButton platformView)
	{
		platformView.OnToggled -= OnToggled;
		base.DisconnectHandler(platformView);
	}

	void UpdateGroup()
	{
		if (VirtualView is not Microsoft.Maui.Controls.RadioButton rb || string.IsNullOrEmpty(rb.GroupName))
			return;

		if (_groupLeaders.TryGetValue(rb.GroupName, out var leaderRef) &&
			leaderRef.TryGetTarget(out var leader) && leader != PlatformView)
		{
			PlatformView.SetGroup(leader);
		}
		else
		{
			_groupLeaders[rb.GroupName] = new WeakReference<Gtk.CheckButton>(PlatformView);
		}
	}

	void OnToggled(Gtk.CheckButton sender, EventArgs args)
	{
		if (VirtualView != null)
			VirtualView.IsChecked = sender.GetActive();
	}

	public static void MapIsChecked(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (handler.PlatformView?.GetActive() != radioButton.IsChecked)
			handler.PlatformView?.SetActive(radioButton.IsChecked);
	}

	public static void MapContent(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton.Content is string text)
			handler.PlatformView?.SetLabel(text);
	}

	public static void MapFont(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is not ITextStyle textStyle)
			return;

		var css = handler.BuildFontCss(textStyle.Font);
		if (!string.IsNullOrEmpty(css))
			handler.ApplyCss(handler.PlatformView, css);
	}

	public static void MapCharacterSpacing(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is ITextStyle textStyle)
			handler.ApplyCss(handler.PlatformView, $"letter-spacing: {textStyle.CharacterSpacing}px;");
	}

	public static void MapTextColor(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is ITextStyle textStyle && textStyle.TextColor != null)
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(textStyle.TextColor)};");
	}

	public static void MapCornerRadius(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is IButtonStroke stroke && stroke.CornerRadius >= 0)
			handler.ApplyCss(handler.PlatformView, $"border-radius: {stroke.CornerRadius}px;");
	}

	public static void MapStrokeColor(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is IButtonStroke stroke && stroke.StrokeColor != null)
			handler.ApplyCss(handler.PlatformView, $"border-color: {ToGtkColor(stroke.StrokeColor)};");
	}

	public static void MapStrokeThickness(RadioButtonHandler handler, IRadioButton radioButton)
	{
		if (radioButton is IButtonStroke stroke && stroke.StrokeThickness >= 0)
			handler.ApplyCss(handler.PlatformView,
				$"border-width: {stroke.StrokeThickness}px; border-style: solid;");
	}
}
