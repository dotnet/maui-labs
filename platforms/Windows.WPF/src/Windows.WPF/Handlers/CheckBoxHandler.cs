namespace Microsoft.Maui.Handlers.WPF
{
	public partial class CheckBoxHandler
	{
		public static IPropertyMapper<ICheckBox, CheckBoxHandler> Mapper = new PropertyMapper<ICheckBox, CheckBoxHandler>(ViewHandler.ViewMapper)
		{
			[nameof(ICheckBox.IsChecked)] = MapIsChecked,
			[nameof(ICheckBox.Foreground)] = MapForeground,
		};

		public static CommandMapper<ICheckBox, CheckBoxHandler> CommandMapper = new(ViewCommandMapper)
		{
		};

		public CheckBoxHandler() : base(Mapper, CommandMapper)
		{
		}

		public CheckBoxHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}
	}
}
