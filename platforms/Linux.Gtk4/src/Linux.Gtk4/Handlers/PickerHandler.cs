using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class PickerHandler : GtkViewHandler<IPicker, Gtk.DropDown>
{
	bool _updatingSelection;

	public static IPropertyMapper<IPicker, PickerHandler> Mapper =
		new PropertyMapper<IPicker, PickerHandler>(ViewMapper)
		{
			[nameof(IPicker.Title)] = MapTitle,
			[nameof(IPicker.SelectedIndex)] = MapSelectedIndex,
			[nameof(IPicker.Items)] = MapItems,
			[nameof(ITextStyle.Font)] = MapFont,
			[nameof(IPicker.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IPicker.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
			[nameof(IPicker.TextColor)] = MapTextColor,
			[nameof(IPicker.TitleColor)] = MapTitleColor,
			[nameof(ITextAlignment.VerticalTextAlignment)] = MapVerticalTextAlignment,
		};

	public PickerHandler() : base(Mapper)
	{
	}

	protected override Gtk.DropDown CreatePlatformView()
	{
		var stringList = Gtk.StringList.New(Array.Empty<string>());
		return Gtk.DropDown.New(stringList, null);
	}

	protected override void ConnectHandler(Gtk.DropDown platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnNotify += OnSelectedChanged;
	}

	protected override void DisconnectHandler(Gtk.DropDown platformView)
	{
		platformView.OnNotify -= OnSelectedChanged;
		base.DisconnectHandler(platformView);
	}

	void OnSelectedChanged(GObject.Object sender, GObject.Object.NotifySignalArgs args)
	{
		if (_updatingSelection || args.Pspec.GetName() != "selected" || VirtualView == null)
			return;

		var selected = PlatformView.GetSelected();
		VirtualView.SelectedIndex = selected == uint.MaxValue ? -1 : (int)selected;
	}

	public static void MapTitle(PickerHandler handler, IPicker picker)
	{
		// GTK DropDown doesn't have a direct title; could use a label
	}

	public static void MapSelectedIndex(PickerHandler handler, IPicker picker)
	{
		var wasUpdatingSelection = handler._updatingSelection;
		handler._updatingSelection = true;
		try
		{
			handler.PlatformView?.SetSelected(
				picker.SelectedIndex >= 0
					? (uint)picker.SelectedIndex
					: uint.MaxValue);
		}
		finally
		{
			handler._updatingSelection = wasUpdatingSelection;
		}
	}

	public static void MapItems(PickerHandler handler, IPicker picker)
	{
		var items = picker.Items?.ToArray() ?? Array.Empty<string>();
		var stringList = Gtk.StringList.New(items);
		var wasUpdatingSelection = handler._updatingSelection;
		handler._updatingSelection = true;
		try
		{
			// GTK selects the first item of a new model; MAUI remains the selection authority.
			handler.PlatformView?.SetModel(stringList);
			MapSelectedIndex(handler, picker);
		}
		finally
		{
			handler._updatingSelection = wasUpdatingSelection;
		}
	}

	public static void MapFont(PickerHandler handler, IPicker picker)
	{
		if (picker is not ITextStyle textStyle)
			return;

		var css = handler.BuildFontCss(textStyle.Font);
		handler.UpdateCss(handler.PlatformView, css);
	}

	public static void MapCharacterSpacing(PickerHandler handler, IPicker picker)
	{
		handler.UpdateCss(handler.PlatformView, $"letter-spacing: {picker.CharacterSpacing}px;");
	}

	public static void MapHorizontalTextAlignment(PickerHandler handler, IPicker picker)
	{
		var align = picker.HorizontalTextAlignment switch
		{
			TextAlignment.Start => "left",
			TextAlignment.Center => "center",
			TextAlignment.End => "right",
			_ => "left"
		};
		handler.UpdateCss(handler.PlatformView, $"text-align: {align};");
	}

	public static void MapTextColor(PickerHandler handler, IPicker picker)
	{
		handler.UpdateCss(handler.PlatformView,
			picker.TextColor != null ? $"color: {ToGtkColor(picker.TextColor)};" : null);
	}

	public static void MapTitleColor(PickerHandler handler, IPicker picker)
	{
		// GTK DropDown doesn't expose a separate title; no-op.
	}

	public static void MapVerticalTextAlignment(PickerHandler handler, IPicker picker)
	{
		if (handler.PlatformView == null || picker is not ITextAlignment ta) return;
		handler.PlatformView.SetValign(ta.VerticalTextAlignment switch
		{
			TextAlignment.Start => Gtk.Align.Start,
			TextAlignment.Center => Gtk.Align.Center,
			TextAlignment.End => Gtk.Align.End,
			_ => Gtk.Align.Center
		});
	}
}
