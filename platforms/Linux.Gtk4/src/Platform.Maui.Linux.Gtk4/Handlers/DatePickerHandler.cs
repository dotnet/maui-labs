using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class DatePickerHandler : GtkViewHandler<IDatePicker, Gtk.Box>
{
	Gtk.Window? _dialog;

	public static new IPropertyMapper<IDatePicker, DatePickerHandler> Mapper =
		new PropertyMapper<IDatePicker, DatePickerHandler>(ViewMapper)
		{
			[nameof(IDatePicker.Date)] = MapDate,
			[nameof(IDatePicker.Format)] = MapFormat,
			[nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
			[nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
			[nameof(IDatePicker.CharacterSpacing)] = MapCharacterSpacing,
			[nameof(IDatePicker.TextColor)] = MapTextColor,
			[nameof(ITextStyle.Font)] = MapFont,
		};

	public DatePickerHandler() : base(Mapper)
	{
	}

	protected override Gtk.Box CreatePlatformView()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
		var button = Gtk.Button.NewWithLabel(DateTime.Today.ToShortDateString());
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
		dialog.SetTitle("Select Date");
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

		var calendar = Gtk.Calendar.New();
		var selectedDate = VirtualView.Date ?? DateTime.Today;
		calendar.SetYear(selectedDate.Year);
		calendar.SetMonth(selectedDate.Month - 1);
		calendar.SetDay(selectedDate.Day);
		box.Append(calendar);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
		buttons.SetHalign(Gtk.Align.End);

		var cancel = Gtk.Button.NewWithLabel("Cancel");
		cancel.OnClicked += (_, _) => dialog.Close();

		var ok = Gtk.Button.NewWithLabel("OK");
		ok.OnClicked += (_, _) =>
		{
			if (VirtualView != null)
			{
				var year = calendar.GetYear();
				var month = calendar.GetMonth();
				if (month < 1 || month > 12)
					month += 1;
				month = Math.Clamp(month, 1, 12);

				year = Math.Clamp(year, 1, 9999);
				var day = Math.Clamp(calendar.GetDay(), 1, DateTime.DaysInMonth(year, month));
				VirtualView.Date = new DateTime(year, month, day);
			}
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

	public static void MapDate(DatePickerHandler handler, IDatePicker datePicker)
	{
		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		var date = datePicker.Date ?? DateTime.Today;
		button?.SetLabel(date.ToString(datePicker.Format ?? "d"));
	}

	public static void MapFormat(DatePickerHandler handler, IDatePicker datePicker)
	{
		MapDate(handler, datePicker);
	}

	public static void MapFont(DatePickerHandler handler, IDatePicker datePicker)
	{
		if (datePicker is not ITextStyle textStyle)
			return;

		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		var css = handler.BuildFontCss(textStyle.Font);
		if (!string.IsNullOrEmpty(css))
			handler.ApplyCss(button, css);
	}

	public static void MapCharacterSpacing(DatePickerHandler handler, IDatePicker datePicker)
	{
		var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
		handler.ApplyCss(button, $"letter-spacing: {datePicker.CharacterSpacing}px;");
	}

	public static void MapTextColor(DatePickerHandler handler, IDatePicker datePicker)
	{
		if (datePicker.TextColor != null)
		{
			var button = handler.PlatformView?.GetFirstChild() as Gtk.Button;
			handler.ApplyCss(button, $"color: {ToGtkColor(datePicker.TextColor)};");
		}
	}

	public static void MapMinimumDate(DatePickerHandler handler, IDatePicker datePicker)
	{
		// Clamp current date if it falls below the new minimum
		if (datePicker.Date < datePicker.MinimumDate)
			datePicker.Date = datePicker.MinimumDate;
	}

	public static void MapMaximumDate(DatePickerHandler handler, IDatePicker datePicker)
	{
		// Clamp current date if it exceeds the new maximum
		if (datePicker.Date > datePicker.MaximumDate)
			datePicker.Date = datePicker.MaximumDate;
	}
}
