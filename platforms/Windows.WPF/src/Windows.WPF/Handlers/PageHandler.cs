#nullable enable
namespace Microsoft.Maui.Handlers.WPF
{
	public partial class PageHandler : ContentViewHandler
	{
		public static new IPropertyMapper<IContentView, PageHandler> Mapper =
			new PropertyMapper<IContentView, PageHandler>(ContentViewHandler.Mapper)
			{
#if TIZEN
				[nameof(IContentView.Background)] = MapBackground,
#endif
				[nameof(ITitledElement.Title)] = MapTitle,
			};

		public static new CommandMapper<IContentView, PageHandler> CommandMapper =
			new(ContentViewHandler.CommandMapper);

		public PageHandler() : base(Mapper, CommandMapper)
		{
		}

		public PageHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public PageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
