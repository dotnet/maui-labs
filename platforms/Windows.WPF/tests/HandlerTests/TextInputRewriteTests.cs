using System.Runtime.ExceptionServices;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using WpfHandlers = Microsoft.Maui.Handlers.WPF;

namespace HandlerTests;

[CollectionDefinition("Text input rewrite", DisableParallelization = true)]
public class TextInputRewriteCollection;

[Collection("Text input rewrite")]
public class TextInputRewriteTests
{
	// A TextChanged handler that rewrites Text (e.g. a numeric-only filter) used to ping-pong
	// forever with the handler's platform echo of the stale value.
	[Theory]
	[InlineData("entry")]
	[InlineData("password")]
	[InlineData("editor")]
	[InlineData("searchbar")]
	public void TextChangedHandlerRewritingText_SettlesOnFilteredValue(string kind)
	{
		Exception? error = null;
		var thread = new Thread(() =>
		{
			try
			{
				_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
				DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
				using var app = MauiApp.CreateBuilder().UseMauiAppWPF<Application>().Build();
				var context = new WPFMauiContext(app.Services);

				InputView view;
				Func<string?> platformText;
				switch (kind)
				{
					case "entry":
					case "password":
						var entry = new Entry { IsPassword = kind == "password" };
						var entryHandler = new WpfHandlers.EntryHandler();
						entryHandler.SetMauiContext(context);
						entryHandler.SetVirtualView(entry);
						view = entry;
						platformText = kind == "password"
							? () => FindPasswordBox(entryHandler)?.Password
							: () => entryHandler.PlatformView.Text;
						break;
					case "editor":
						var editor = new Editor();
						var editorHandler = new WpfHandlers.EditorHandler();
						editorHandler.SetMauiContext(context);
						editorHandler.SetVirtualView(editor);
						view = editor;
						platformText = () => editorHandler.PlatformView.Text;
						break;
					default:
						var searchBar = new SearchBar();
						var searchHandler = new WpfHandlers.SearchBarHandler();
						searchHandler.SetMauiContext(context);
						searchHandler.SetVirtualView(searchBar);
						view = searchBar;
						platformText = () => FindTextBox(searchHandler.PlatformView)?.Text;
						break;
				}

				view.TextChanged += (_, e) =>
				{
					if (string.IsNullOrEmpty(e.NewTextValue))
						return;
					var filtered = new string(e.NewTextValue.Where(char.IsDigit).ToArray());
					if (filtered != e.NewTextValue)
						view.Text = filtered;
				};

				view.Text = "abc123";

				Assert.Equal("123", view.Text);
				Assert.Equal("123", platformText());
			}
			catch (Exception ex) { error = ex; }
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Setting Text did not settle (text sync loop).");
		if (error != null)
			ExceptionDispatchInfo.Capture(error).Throw();
	}

	static System.Windows.Controls.PasswordBox? FindPasswordBox(WpfHandlers.EntryHandler handler) =>
		typeof(WpfHandlers.EntryHandler)
			.GetField("_passwordBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
			?.GetValue(handler) as System.Windows.Controls.PasswordBox;

	static System.Windows.Controls.TextBox? FindTextBox(object root)
	{
		if (root is System.Windows.Controls.TextBox textBox)
			return textBox;
		if (root is System.Windows.DependencyObject dependencyObject)
		{
			foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(dependencyObject))
			{
				if (FindTextBox(child) is { } found)
					return found;
			}
		}
		return null;
	}
}
