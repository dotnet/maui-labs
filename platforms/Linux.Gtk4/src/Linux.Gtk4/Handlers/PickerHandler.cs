using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

public class PickerHandler : GtkViewHandler<IPicker, Gtk.DropDown>
{
	bool _updatingSelection;
	bool _hasPlaceholder;
	Gtk.StringList? _items;

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
		_items = Gtk.StringList.New(Array.Empty<string>());
		_hasPlaceholder = false;
		return Gtk.DropDown.New(_items, null);
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
		var index = selected == uint.MaxValue || (_hasPlaceholder && selected == 0)
			? -1
			: (int)selected - (_hasPlaceholder ? 1 : 0);
		if (VirtualView.SelectedIndex != index)
			VirtualView.SelectedIndex = index;

		// Picking a real item removes the placeholder row.
		MapSelectedIndex(this, VirtualView);
	}

	public static void MapTitle(PickerHandler handler, IPicker picker)
	{
		if (handler._hasPlaceholder)
			handler.UpdateSelection(picker, refreshPlaceholder: true);
	}

	public static void MapSelectedIndex(PickerHandler handler, IPicker picker) =>
		handler.UpdateSelection(picker, refreshPlaceholder: false);

	public static void MapItems(PickerHandler handler, IPicker picker)
	{
		if (handler.PlatformView is not { } dropDown)
			return;

		var wasUpdatingSelection = handler._updatingSelection;
		handler._updatingSelection = true;
		try
		{
			// GTK selects the first item of a new model; MAUI remains the selection authority.
			handler._items = Gtk.StringList.New(picker.Items?.ToArray() ?? Array.Empty<string>());
			handler._hasPlaceholder = false;
			dropDown.SetModel(handler._items);
			handler.UpdateSelection(picker, refreshPlaceholder: false);
		}
		finally
		{
			handler._updatingSelection = wasUpdatingSelection;
		}
	}

	// GtkDropDown has no "nothing selected" state for a non-empty model on GTK 4.16+: its private
	// GtkSingleSelection autoselects and ignores GTK_INVALID_LIST_POSITION. While MAUI's
	// SelectedIndex is -1, a placeholder row showing the Title is kept at position 0 instead.
	void UpdateSelection(IPicker picker, bool refreshPlaceholder)
	{
		if (PlatformView is not { } dropDown || _items is null)
			return;

		var wasUpdatingSelection = _updatingSelection;
		_updatingSelection = true;
		try
		{
			var itemCount = _items.GetNItems() - (_hasPlaceholder ? 1u : 0u);
			var wantsPlaceholder = picker.SelectedIndex < 0 && itemCount > 0;
			if (_hasPlaceholder && (!wantsPlaceholder || refreshPlaceholder))
			{
				_items.Remove(0);
				_hasPlaceholder = false;
			}
			if (wantsPlaceholder && !_hasPlaceholder)
			{
				_items.Splice(0, 0, [picker.Title ?? string.Empty]);
				_hasPlaceholder = true;
			}

			dropDown.SetSelected(
				_hasPlaceholder ? 0
				: picker.SelectedIndex >= 0 ? (uint)picker.SelectedIndex
				: uint.MaxValue);
		}
		finally
		{
			_updatingSelection = wasUpdatingSelection;
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
