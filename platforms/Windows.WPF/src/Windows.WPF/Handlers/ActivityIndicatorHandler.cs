namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ActivityIndicatorHandler
	{
		public static IPropertyMapper<IActivityIndicator, ActivityIndicatorHandler> Mapper = new PropertyMapper<IActivityIndicator, ActivityIndicatorHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IActivityIndicator.IsRunning)] = MapIsRunning,
			[nameof(IActivityIndicator.Color)] = MapColor,
		};

		public static CommandMapper<IActivityIndicator, ActivityIndicatorHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public ActivityIndicatorHandler() : base(Mapper, CommandMapper)
		{
		}

		public ActivityIndicatorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
