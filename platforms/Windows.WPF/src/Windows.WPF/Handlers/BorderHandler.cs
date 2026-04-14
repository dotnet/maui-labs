namespace Microsoft.Maui.Handlers.WPF
{
	public partial class BorderHandler
	{
		public static IPropertyMapper<IBorderView, BorderHandler> Mapper = new PropertyMapper<IBorderView, BorderHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IContentView.Content)] = MapContent,
			[nameof(IBorderView.Stroke)] = MapStroke,
			[nameof(IBorderView.StrokeThickness)] = MapStrokeThickness,
			[nameof(IBorderView.Shape)] = MapStrokeShape,
			[nameof(IBorderView.StrokeDashPattern)] = MapStrokeDashPattern,
			[nameof(IBorderView.StrokeLineCap)] = MapStrokeLineCap,
			[nameof(IBorderView.StrokeLineJoin)] = MapStrokeLineJoin,
			[nameof(IView.Background)] = MapBackground,
			[nameof(IBorderView.Padding)] = MapPadding,
		};

		public static CommandMapper<IBorderView, BorderHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public BorderHandler() : base(Mapper, CommandMapper)
		{
		}

		public BorderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
