namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ScrollViewHandler
	{
		public static IPropertyMapper<IScrollView, ScrollViewHandler> Mapper = new PropertyMapper<IScrollView, ScrollViewHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IScrollView.Content)] = MapContent,
			[nameof(IScrollView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
			[nameof(IScrollView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
			[nameof(IScrollView.Orientation)] = MapOrientation,
		};

		public static CommandMapper<IScrollView, ScrollViewHandler> CommandMapper = new(ViewCommandMapper)
		{
			[nameof(IScrollView.RequestScrollTo)] = MapRequestScrollTo,
		};

		public ScrollViewHandler() : base(Mapper, CommandMapper)
		{
		}

		public ScrollViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
