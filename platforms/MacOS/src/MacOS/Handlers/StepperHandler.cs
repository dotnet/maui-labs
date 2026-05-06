using Microsoft.Maui.Handlers;
using AppKit;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Handlers;

public partial class StepperHandler : MacOSViewHandler<IStepper, NSStepper>
{
	public static readonly IPropertyMapper<IStepper, StepperHandler> Mapper =
		new PropertyMapper<IStepper, StepperHandler>(ViewMapper)
		{
			[nameof(IStepper.Minimum)] = MapMinimum,
			[nameof(IStepper.Maximum)] = MapMaximum,
			[nameof(IStepper.Value)] = MapValue,
			[nameof(IStepper.Interval)] = MapInterval,
		};

	bool _updating;

	public StepperHandler() : base(Mapper)
	{
	}

	protected override NSStepper CreatePlatformView()
	{
		return new NSStepper
		{
			MinValue = 0,
			MaxValue = 100,
			DoubleValue = 0,
			Increment = 1,
		};
	}

	protected override void ConnectHandler(NSStepper platformView)
	{
		base.ConnectHandler(platformView);
		platformView.Activated += OnActivated;
	}

	protected override void DisconnectHandler(NSStepper platformView)
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
			VirtualView.Value = PlatformView.DoubleValue;
		}
		finally
		{
			_updating = false;
		}
	}

	public static void MapMinimum(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView.MinValue = stepper.Minimum;
	}

	public static void MapMaximum(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView.MaxValue = stepper.Maximum;
	}

	public static void MapValue(StepperHandler handler, IStepper stepper)
	{
		if (handler._updating)
			return;

		handler.PlatformView.DoubleValue = stepper.Value;
	}

	public static void MapInterval(StepperHandler handler, IStepper stepper)
	{
		handler.PlatformView.Increment = stepper.Interval;
	}
}
