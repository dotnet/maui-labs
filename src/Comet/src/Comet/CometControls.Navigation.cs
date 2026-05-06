using System;

namespace Comet
{
	public static partial class CometControls
	{
		public static CometShell CometShell(params ShellItem[] items)
			=> new CometShell(items);

		public static ShellItem ShellItem(string title, params ShellSection[] sections)
			=> new ShellItem(title, sections);

		public static ShellSection ShellSection(string title, params ShellContent[] content)
			=> new ShellSection(title, content);

		public static ShellContent ShellContent(string title, View content)
			=> new ShellContent(title, content);

		public static ShellContent ShellContent(string title, Func<View> contentTemplate)
			=> new ShellContent(title, contentTemplate);

		public static ShellContent ShellContent<TView>(string title = null, string route = null) where TView : View, new()
			=> Comet.ShellContent.Create<TView>(title, route);
	}
}
