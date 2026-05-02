namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ShapeViewHandler
	{
		public static IPropertyMapper<IShapeView, ShapeViewHandler> Mapper = new PropertyMapper<IShapeView, ShapeViewHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IShapeView.Shape)] = MapShape,
			[nameof(IShapeView.Fill)] = MapFill,
			[nameof(IShapeView.Stroke)] = MapStroke,
			[nameof(IShapeView.StrokeThickness)] = MapStrokeThickness,
		};

		public static CommandMapper<IShapeView, ShapeViewHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public ShapeViewHandler() : base(Mapper, CommandMapper)
		{
		}

		public ShapeViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
