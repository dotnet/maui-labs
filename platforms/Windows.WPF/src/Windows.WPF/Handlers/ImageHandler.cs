namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ImageHandler
	{
		public static IPropertyMapper<IImage, ImageHandler> Mapper = new PropertyMapper<IImage, ImageHandler>(ViewHandler.ViewMapper)
		{
			[nameof(IImage.Source)] = MapSource,
			[nameof(IImage.Aspect)] = MapAspect,
		};

		public static CommandMapper<IImage, ImageHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public ImageHandler() : base(Mapper, CommandMapper)
		{
		}

		public ImageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
