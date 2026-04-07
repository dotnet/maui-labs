#if DEBUG
using System;
using Comet;
using MauiDevFlow.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;

namespace Microsoft.Maui.Hosting;

public static class SampleRuntimeDebugExtensions
{
	public static MauiAppBuilder EnableSampleRuntimeDebugging(this MauiAppBuilder builder)
	{
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent();
		return builder;
	}

	public static MauiAppBuilder UseCometSampleDebugHost<TView>(this MauiAppBuilder builder)
		where TView : Comet.View, new()
	{
		return builder.UseCometSampleDebugHost(static () => new TView(), typeof(TView));
	}

	public static MauiAppBuilder UseCometSampleDebugHost(this MauiAppBuilder builder, Func<Comet.View> rootViewFactory)
	{
		return builder.UseCometSampleDebugHost(rootViewFactory, null);
	}

	static MauiAppBuilder UseCometSampleDebugHost(this MauiAppBuilder builder, Func<Comet.View> rootViewFactory, Type? rootType)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(rootViewFactory);

		if (rootType != null && typeof(CometApp).IsAssignableFrom(rootType))
			throw new InvalidOperationException(
				$"DEBUG sample host requires a real root Comet.View factory, not '{rootType.FullName}'. " +
				"Use a root-view factory such as UseCometSampleDebugHost(() => new MainPage()).");

		builder.Services.AddSingleton<ICometSampleDebugRootViewFactory>(new CometSampleDebugRootViewFactory(rootViewFactory));
#pragma warning disable MCT001 // .UseMauiCommunityToolkit() is chained by the caller after UseCometSampleDebugHost() returns
		builder.UseMauiApp<CometSampleDebugHostApplication>();
#pragma warning restore MCT001
		builder.UseCometHandlers();
		return builder;
	}
}

interface ICometSampleDebugRootViewFactory
{
	Comet.View Create();
}

sealed class CometSampleDebugRootViewFactory : ICometSampleDebugRootViewFactory
{
	readonly Func<Comet.View> rootViewFactory;

	public CometSampleDebugRootViewFactory(Func<Comet.View> rootViewFactory)
	{
		ArgumentNullException.ThrowIfNull(rootViewFactory);
		this.rootViewFactory = rootViewFactory;
	}

	public Comet.View Create()
	{
		var rootView = rootViewFactory();
		if (rootView is null)
			throw new InvalidOperationException("DEBUG sample host root-view factory returned null.");
		if (rootView is CometApp)
			throw new InvalidOperationException(
				$"DEBUG sample host cannot wrap '{rootView.GetType().FullName}' because it inherits CometApp. " +
				"Return the real root Comet.View instead.");
		return rootView;
	}
}

sealed class CometSampleDebugHostApplication : Application
{
	readonly ICometSampleDebugRootViewFactory rootViewFactory;

	public CometSampleDebugHostApplication(ICometSampleDebugRootViewFactory rootViewFactory)
	{
		ArgumentNullException.ThrowIfNull(rootViewFactory);
		this.rootViewFactory = rootViewFactory;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var rootView = rootViewFactory.Create();
		var page = new Microsoft.Maui.Controls.ContentPage
		{
			Content = new CometHost(rootView)
		};

		// Disable the navigation bar so MAUI's NavigationLayout overlay
		// doesn't block touches from reaching Comet controls (e.g., TextField).
		Microsoft.Maui.Controls.NavigationPage.SetHasNavigationBar(page, false);

#if IOS || MACCATALYST
		// Respect safe area insets on iOS/Mac so content isn't hidden behind notch/status bar.
		Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(
			page, true);
#elif ANDROID
		// On Android, content renders edge-to-edge under the status bar.
		// Apply top padding so controls aren't hidden behind the status bar
		// (which intercepts touch events in its zone).
		page.Loaded += (s, e) =>
		{
			if (page.Handler?.PlatformView is global::Android.Views.View pv)
			{
				var insets = pv.RootWindowInsets;
				if (insets != null)
				{
					var statusBarInsets = insets.GetInsets(global::Android.Views.WindowInsets.Type.StatusBars());
					var navBarInsets = insets.GetInsets(global::Android.Views.WindowInsets.Type.NavigationBars());
					var density = pv.Context?.Resources?.DisplayMetrics?.Density ?? 1;
					page.Padding = new Microsoft.Maui.Thickness(
						0,
						statusBarInsets.Top / density,
						0,
						navBarInsets.Bottom / density);
				}
			}
		};
#endif

		return new Window(page);
	}
}
#endif
