using Microsoft.Maui;

namespace Platform.Maui.Linux.Gtk4.Handlers;

public class StepperHandler : GtkViewHandler<IStepper, Gtk.SpinButton>
{
	public static new IPropertyMapper<IStepper, StepperHandler> Mapper =
		new PropertyMapper<IStepper, StepperHandler>(ViewMapper)
		{
			[nameof(IStepper.Minimum)] = MapMinimum,
			[nameof(IStepper.Maximum)] = MapMaximum,
			[nameof(IStepper.Value)] = MapValue,
			[nameof(IStepper.Interval)] = MapInterval,
		};

	public StepperHandler() : base(Mapper)
	{
	}

	protected override Gtk.SpinButton CreatePlatformView()
	{
		return Gtk.SpinButton.NewWithRange(0, 100, 1);
	}

	protected override void ConnectHandler(Gtk.SpinButton platformView)
	{
		base.ConnectHandler(platformView);
		platformView.OnValueChanged += OnValueChanged;
	}

	protected override void DisconnectHandler(Gtk.SpinButton platformView)
	{
		platformView.OnValueChanged -= OnValueChanged;
		base.DisconnectHandler(platformView);
	}

	void OnValueChanged(Gtk.SpinButton sender, EventArgs args)
	{
		if (VirtualView != null)
			VirtualView.Value = sender.GetValue();
	}

	public static void MapMinimum(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView?.GetAdjustment()?.SetLower(stepper.Minimum);
	}

	public static void MapMaximum(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView?.GetAdjustment()?.SetUpper(stepper.Maximum);
	}

	public static void MapValue(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView?.SetValue(stepper.Value);
	}

	public static void MapInterval(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView?.SetIncrements(stepper.Interval, stepper.Interval * 10);
	}
}
