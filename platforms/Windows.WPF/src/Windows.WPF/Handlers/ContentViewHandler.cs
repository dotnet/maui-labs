#nullable enable

namespace Microsoft.Maui.Handlers.WPF
{
	public partial class ContentViewHandler
	{
		public static IPropertyMapper<IContentView, ContentViewHandler> Mapper =
			new PropertyMapper<IContentView, ContentViewHandler>(ViewMapper)
			{
				[nameof(IContentView.Content)] = MapContent
			};

		public static CommandMapper<IContentView, ContentViewHandler> CommandMapper =
			new(ViewCommandMapper);

		public ContentViewHandler() : base(Mapper, CommandMapper)
		{

		}

		public ContentViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public ContentViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
