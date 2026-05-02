namespace Microsoft.Maui.Handlers.WPF
{
	public partial class BoxViewHandler
	{
		public static IPropertyMapper<IShapeView, BoxViewHandler> Mapper = new PropertyMapper<IShapeView, BoxViewHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IShapeView.Fill)] = MapColor,
			[nameof(IShapeView.Shape)] = MapCornerRadius,
			[nameof(IView.Background)] = MapBackground,
		};

		public static CommandMapper<IShapeView, BoxViewHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public BoxViewHandler() : base(Mapper, CommandMapper)
		{
		}

		public BoxViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
