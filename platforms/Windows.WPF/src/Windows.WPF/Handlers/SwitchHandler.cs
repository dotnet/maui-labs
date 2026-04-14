namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SwitchHandler
	{
		public static IPropertyMapper<ISwitch, SwitchHandler> Mapper = new PropertyMapper<ISwitch, SwitchHandler>(ViewHandler.ViewMapper)
		{
			[nameof(ISwitch.IsOn)] = MapIsOn,
			[nameof(ISwitch.TrackColor)] = MapTrackColor,
			[nameof(ISwitch.ThumbColor)] = MapThumbColor,
		};

		public static CommandMapper<ISwitch, SwitchHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public SwitchHandler() : base(Mapper, CommandMapper)
		{
		}

		public SwitchHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
