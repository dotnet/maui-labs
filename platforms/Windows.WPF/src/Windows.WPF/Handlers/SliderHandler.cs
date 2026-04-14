namespace Microsoft.Maui.Handlers.WPF
{
	public partial class SliderHandler
	{
		public static IPropertyMapper<ISlider, SliderHandler> Mapper = new PropertyMapper<ISlider, SliderHandler>(ViewHandler.ViewMapper)
		{
			[nameof(ISlider.Value)] = MapValue,
			[nameof(ISlider.Minimum)] = MapMinimum,
			[nameof(ISlider.Maximum)] = MapMaximum,
			[nameof(ISlider.MinimumTrackColor)] = MapMinimumTrackColor,
			[nameof(ISlider.MaximumTrackColor)] = MapMaximumTrackColor,
			[nameof(ISlider.ThumbColor)] = MapThumbColor,
		};

		public static CommandMapper<ISlider, SliderHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public SliderHandler() : base(Mapper, CommandMapper)
		{
		}

		public SliderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
