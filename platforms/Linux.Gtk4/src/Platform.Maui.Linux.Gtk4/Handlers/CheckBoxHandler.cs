using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class CheckBoxHandler : GtkViewHandler<ICheckBox, Gtk.CheckButton>
{
	public static new IPropertyMapper<ICheckBox, CheckBoxHandler> Mapper =
		new PropertyMapper<ICheckBox, CheckBoxHandler>(ViewMapper)
		{
			[nameof(ICheckBox.IsChecked)] = MapIsChecked,
			[nameof(ICheckBox.Foreground)] = MapForeground,
		};

	public CheckBoxHandler() : base(Mapper)
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
	}

	protected override void DisconnectHandler(Gtk.CheckButton platformView)
	{
		platformView.OnToggled -= OnToggled;
		base.DisconnectHandler(platformView);
	}

	void OnToggled(Gtk.CheckButton sender, EventArgs args)
	{
		VirtualView?.IsChecked = sender.GetActive();
	}

	public static void MapIsChecked(CheckBoxHandler handler, ICheckBox checkBox)
	{
		if (handler.PlatformView?.GetActive() != checkBox.IsChecked)
			handler.PlatformView?.SetActive(checkBox.IsChecked);
	}

	public static void MapForeground(CheckBoxHandler handler, ICheckBox checkBox)
	{
		if (checkBox.Foreground is Microsoft.Maui.Graphics.SolidPaint solidPaint && solidPaint.Color != null)
			handler.ApplyCss(handler.PlatformView, $"color: {ToGtkColor(solidPaint.Color)};");
	}
}
