namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ProgressBarHandler
	{
		public static IPropertyMapper<IProgress, ProgressBarHandler> Mapper = new PropertyMapper<IProgress, ProgressBarHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IProgress.Progress)] = MapProgress,
			[nameof(IProgress.ProgressColor)] = MapProgressColor,
		};

		public static CommandMapper<IProgress, ProgressBarHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public ProgressBarHandler() : base(Mapper, CommandMapper)
		{
		}

		public ProgressBarHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
