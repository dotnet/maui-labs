using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;

namespace HandlerTests;

[CollectionDefinition("Shell handlers", DisableParallelization = true)]
public sealed class ShellHandlersCollection;

[Collection("Shell handlers")]
public class ShellHandlerTests
{
	[Fact]
	public void TemplatePage_IsAttachedAndInspectable_WithoutCreatingInactivePages()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var context = new WPFMauiContext(app.Services);
			var created = 0;
			var inactiveCreated = 0;
			var label = new Label { Text = "Shell page label", AutomationId = "ShellLabel" };
			var button = new Button { Text = "Click me", AutomationId = "CounterBtn" };
			var page = new ContentPage { Content = new VerticalStackLayout { Children = { label, button } } };
			var content = new ShellContent
			{
				ContentTemplate = new DataTemplate(() => { created++; return page; }),
			};
			var inactive = new ShellContent
			{
				ContentTemplate = new DataTemplate(() => { inactiveCreated++; return new ContentPage(); }),
			};
			var shell = new Shell { Items = { content, inactive } };
			var handler = new ShellHandler();
			handler.SetMauiContext(context);
			try
			{
				handler.SetVirtualView(shell);

				Assert.Equal(1, created);
				Assert.Same(page, ((IShellContentController)content).Page);
				Assert.Same(content, page.Parent);
				Assert.Same(page, shell.CurrentPage);
				Assert.Contains(page, ((IVisualTreeElement)content).GetVisualChildren());
				Assert.NotNull(page.Handler?.PlatformView);

				var walker = new VisualTreeWalker();
				var tree = walker.WalkElement(content, null, 0, 20);
				var pageInfo = Assert.Single(tree!.Children!);
				var layoutInfo = Assert.Single(pageInfo.Children!, child => child.Type == nameof(VerticalStackLayout));
				Assert.Contains(layoutInfo.Children!, child =>
					child.Type == nameof(Label) && child.Text == label.Text && child.AutomationId == label.AutomationId);
				Assert.Contains(layoutInfo.Children!, child =>
					child.Type == nameof(Button) && child.Text == button.Text && child.AutomationId == button.AutomationId);

				handler.UpdateValue(nameof(Shell.CurrentItem));
				Assert.Equal(1, created);
				Assert.Equal(0, inactiveCreated);
				Assert.Null(((IShellContentController)inactive).Page);

				shell.CurrentItem = shell.Items[1];
				handler.UpdateValue(nameof(Shell.CurrentItem));
				Assert.Equal(1, inactiveCreated);
				shell.CurrentItem = shell.Items[0];
				handler.UpdateValue(nameof(Shell.CurrentItem));
				Assert.Same(page, shell.CurrentPage);
				Assert.Equal(1, created);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void ExplicitContent_UsesExistingPage()
	{
		StaHelper.RunOnSta(() =>
		{
			using var app = CreateApp();
			var page = new ContentPage { Content = new Label { Text = "Existing page" } };
			var content = new ShellContent { Content = page };
			var shell = new Shell { Items = { content } };
			var handler = new ShellHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(shell);
				handler.UpdateValue(nameof(Shell.CurrentItem));
				Assert.Same(page, shell.CurrentPage);
				Assert.Same(content, page.Parent);
				Assert.NotNull(page.Handler?.PlatformView);
				Assert.Single(((IVisualTreeElement)content).GetVisualChildren());
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void TypedTemplate_UsesShellDependencyInjectionAndCachesPage()
	{
		StaHelper.RunOnSta(() =>
		{
			var page = new ContentPage { Content = new Label { Text = "Resolved from DI" } };
			using var app = CreateApp(builder => builder.Services.AddSingleton(page));
			var content = new ShellContent { ContentTemplate = new DataTemplate(typeof(ContentPage)) };
			var shell = new Shell { Items = { content } };
			var handler = new ShellHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(shell);
				handler.UpdateValue(nameof(Shell.CurrentItem));
				Assert.Same(page, shell.CurrentPage);
				Assert.Same(page, ((IShellContentController)content).Page);
				Assert.Same(content, page.Parent);
				Assert.NotNull(page.Handler?.PlatformView);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	[Fact]
	public void TemplatePage_WhenCreationFails_LogsException()
	{
		StaHelper.RunOnSta(() =>
		{
			var logger = new ShellTestLogger();
			using var app = CreateApp(builder => builder.Services.AddSingleton<ILogger<ShellHandler>>(logger));
			var exception = new InvalidOperationException("Page creation failed");
			var content = new ShellContent
			{
				ContentTemplate = new DataTemplate(() => throw exception),
			};
			var shell = new Shell { Items = { content } };
			var handler = new ShellHandler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			try
			{
				handler.SetVirtualView(shell);
				Assert.Contains(logger.Entries, entry =>
					entry.Level == LogLevel.Error &&
					ReferenceEquals(entry.Exception, exception) &&
					entry.Message == "Showing current Shell page failed.");
				Assert.Null(((IShellContentController)content).Page);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	sealed class ShellTestLogger : ILogger<ShellHandler>
	{
		public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
			Exception? exception, Func<TState, Exception?, string> formatter)
			=> Entries.Add((logLevel, exception, formatter(state, exception)));
	}

	static MauiApp CreateApp(Action<MauiAppBuilder>? configure = null)
	{
		_ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
		DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
		var builder = MauiApp.CreateBuilder().UseMauiAppWPF<Application>();
		configure?.Invoke(builder);
		return builder.Build();
	}

	[Fact]
	public void ToggleFlyout_WhenStateChanges_NotifiesShellPropertyBridge()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;

			container.ToggleFlyout(true);
			container.ToggleFlyout(true);
			container.ToggleFlyout(false);

			Assert.Equal([true, false], changes);
		});
	}

	[Fact]
	public void ToggleFlyout_WhenLocked_ReportsForcedOpenAndDoesNotClose()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;
			container.SetFlyoutBehavior(FlyoutBehavior.Locked);

			container.ToggleFlyout(false);

			Assert.Equal([true], changes);
		});
	}

	[Fact]
	public void ToggleFlyout_WhenDisabled_DoesNotOpen()
	{
		StaHelper.RunOnSta(() =>
		{
			var container = new ShellContainerView();
			var changes = new List<bool>();
			container.OnFlyoutOpenChanged = changes.Add;
			container.SetFlyoutBehavior(FlyoutBehavior.Disabled);

			container.ToggleFlyout(true);

			Assert.Empty(changes);
		});
	}
}
