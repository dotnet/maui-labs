using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class TimePickerHandler : GtkViewHandler<ITimePicker, Gtk.Box>
{
	Gtk.Window? _dialog;

	public static new IPropertyMapper<ITimePicker, TimePickerHandler> Mapper =
		new PropertyMapper<ITimePicker, TimePickerHandler>(ViewMapper)
		{
			[nameof(ITimePicker.Time)] = MapTime,
			[nameof(ITimePicker.Format)] = MapFormat,
			[nameof(ITimePicker.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(ITimePicker.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
		};

	public TimePickerHandler() : base(Mapper)
	{
	}

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		var button = Gtk.Button.NewWithLabel(DateTime.Now.ToShortTimeString());
		box.Append(button);
		return box;
	}

	protected override void ConnectHandler(Gtk.Box platformView)
	{
		base.ConnectHandler(platformView);
		if (platformView.GetFirstChild() is Gtk.Button button)
			button.OnClicked += OnClicked;
	}

	protected override void DisconnectHandler(Gtk.Box platformView)
	{
		if (platformView.GetFirstChild() is Gtk.Button button)
			button.OnClicked -= OnClicked;

		CloseDialog();
		base.DisconnectHandler(platformView);
	}

	void OnClicked(Gtk.Button sender, EventArgs args)
	{
		OpenDialog();
	}

	void OpenDialog()
	{
		if (VirtualView == null || _dialog != null)
			return;

		if (Gtk.Application.GetDefault() is not Gtk.Application app)
			return;

		var parent = app.GetActiveWindow();
		if (parent == null)
			return;

		var dialog = new Gtk.Window();
		dialog.SetTitle("Select Time");
		dialog.SetModal(true);
		dialog.SetTransientFor(parent);
		dialog.SetResizable(false);

		var gtkApp = parent.GetApplication();
		if (gtkApp != null)
			dialog.SetApplication(gtkApp);

		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
		box.SetMarginTop(16);
		box.SetMarginBottom(16);
		box.SetMarginStart(16);
		box.SetMarginEnd(16);

		var selectedTime = VirtualView.Time ?? DateTime.Now.TimeOfDay;

		var timeRow = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
		var hours = Gtk.SpinButton.NewWithRange(0, 23, 1);
		hours.SetNumeric(true);
		hours.SetValue(selectedTime.Hours);

		var minutes = Gtk.SpinButton.NewWithRange(0, 59, 1);
		minutes.SetNumeric(true);
		minutes.SetValue(selectedTime.Minutes);

		timeRow.Append(Gtk.Label.New("Hour"));
		timeRow.Append(hours);
		timeRow.Append(Gtk.Label.New("Minute"));
		timeRow.Append(minutes);
		box.Append(timeRow);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
		buttons.SetHalign(Gtk.Align.End);

		var cancel = Gtk.Button.NewWithLabel("Cancel");
		cancel.OnClicked += (_, _) => dialog.Close();

		var ok = Gtk.Button.NewWithLabel("OK");
		ok.OnClicked += (_, _) =>
		{
			if (VirtualView != null)
				VirtualView.Time = new TimeSpan(hours.GetValueAsInt(), minutes.GetValueAsInt(), 0);
			dialog.Close();
		};

		buttons.Append(cancel);
		buttons.Append(ok);
		box.Append(buttons);

		dialog.SetChild(box);
		dialog.OnCloseRequest += (_, _) =>
		{
			_dialog = null;
			return false;
		};

		_dialog = dialog;
		dialog.Present();
	}

	void CloseDialog()
	{
		if (_dialog == null)
			return;

		var dialog = _dialog;
		_dialog = null;
		dialog.Close();
	}

	public static void MapTime(TimePickerHandler handler, ITimePicker timePicker)
	{
		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		var dt = DateTime.Today.Add(timePicker.Time ?? TimeSpan.Zero);
		button?.SetLabel(dt.ToString(timePicker.Format ?? "t"));
	}

	public static void MapFormat(TimePickerHandler handler, ITimePicker timePicker)
	{
		MapTime(handler, timePicker);
	}

	public static void MapFont(TimePickerHandler handler, ITimePicker timePicker)
	{
		if (timePicker is not ITextStyle textStyle)
			return;

		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		var css = handler.BuildFontCss(textStyle.Font);
		if (!string.IsNullOrEmpty(css))
			handler.ApplyCss(button, css);
	}

	public static void MapCharacterSpacing(TimePickerHandler handler, ITimePicker timePicker)
	{
		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		handler.ApplyCss(button, $"letter-spacing: {timePicker.CharacterSpacing}px;");
	}

	public static void MapTextColor(TimePickerHandler handler, ITimePicker timePicker)
	{
		if (timePicker.TextColor != null)
		{
			var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
			handler.ApplyCss(button, $"color: {ToGtkColor(timePicker.TextColor)};");
		}
	}
}
