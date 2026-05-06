namespace Microsoft.Maui.Handlers.WPF
{
	public partial class StepperHandler
	{
		public static IPropertyMapper<IStepper, StepperHandler> Mapper = new PropertyMapper<IStepper, StepperHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IStepper.Value)] = MapValue,
			[nameof(IStepper.Minimum)] = MapMinimum,
			[nameof(IStepper.Maximum)] = MapMaximum,
			[nameof(IStepper.Interval)] = MapIncrement,
		};

		public static CommandMapper<IStepper, StepperHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public StepperHandler() : base(Mapper, CommandMapper)
		{
		}

		public StepperHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
