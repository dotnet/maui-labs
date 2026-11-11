using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF;
using Microsoft.Maui.WPF;
using Handler = Microsoft.AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler;

namespace HandlerTests;

[Collection("Layout handlers")]
public class BlazorRootComponentTests
{
	[Fact]
	public void RootComponents_AddReplaceMoveResetAndRebind_ReconcileNativeCollection()
	{
		WpfApplicationHost.Run(_ =>
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppWPF<Microsoft.Maui.Controls.Application>()
				.Build();
			var handler = new Handler();
			handler.SetMauiContext(new WPFMauiContext(app.Services));
			var first = Component("#first");
			var second = Component("#second");
			var view = new BlazorWebView { StartPath = "/initial" };
			view.RootComponents.Add(first);
			handler.SetVirtualView(view);
			try
			{
				var nativeFirst = Assert.Single(handler.PlatformView.RootComponents);
				Assert.Equal("/initial", handler.PlatformView.StartPath);
				view.RootComponents.Add(second);
				Assert.Equal(2, handler.PlatformView.RootComponents.Count);
				Assert.Same(nativeFirst, handler.PlatformView.RootComponents[0]);
				view.RootComponents.Move(0, 1);
				Assert.Contains(nativeFirst, handler.PlatformView.RootComponents);

				view.RootComponents[0] = Component("#replacement");
				Assert.Equal(2, handler.PlatformView.RootComponents.Count);
				Assert.DoesNotContain(handler.PlatformView.RootComponents, item => item.Selector == "#second");
				view.RootComponents.Clear();
				Assert.Empty(handler.PlatformView.RootComponents);
				view.RootComponents.Add(first);

				var replacement = new BlazorWebView { StartPath = "/replacement" };
				replacement.RootComponents.Add(second);
				handler.SetVirtualView(replacement);
				Assert.Equal("#second", Assert.Single(handler.PlatformView.RootComponents).Selector);
				Assert.Equal("/replacement", handler.PlatformView.StartPath);
				view.RootComponents.Add(Component("#retired"));
				Assert.Single(handler.PlatformView.RootComponents);

				var native = handler.PlatformView;
				((IElementHandler)handler).DisconnectHandler();
				Assert.Empty(native.RootComponents);
				replacement.RootComponents.Add(Component("#disconnected"));
				Assert.Empty(native.RootComponents);
				handler.SetVirtualView(replacement);
				Assert.NotSame(native, handler.PlatformView);
				Assert.Equal(["#second", "#disconnected"],
					handler.PlatformView.RootComponents.Select(component => component.Selector));
				replacement.RootComponents.Clear();
				Assert.Empty(handler.PlatformView.RootComponents);
			}
			finally
			{
				((IElementHandler)handler).DisconnectHandler();
			}
		});
	}

	static RootComponent Component(string selector) => new()
	{
		ComponentType = typeof(TestComponent),
		Selector = selector,
	};

	sealed class TestComponent : ComponentBase;
}
