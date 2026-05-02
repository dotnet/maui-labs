using System;

namespace Microsoft.Maui.LifecycleEvents.WPF
{
	public interface IWPFLifecycleBuilder : ILifecycleBuilder
	{
	}

	public static class WPFLifecycleExtensions
	{
		public static ILifecycleBuilder AddWPF(this ILifecycleBuilder builder, Action<IWPFLifecycleBuilder> configureDelegate)
		{
			var lifecycle = new LifecycleBuilder(builder);
			configureDelegate?.Invoke(lifecycle);
			return builder;
		}

		class LifecycleBuilder : IWPFLifecycleBuilder
		{
			readonly ILifecycleBuilder _builder;
			public LifecycleBuilder(ILifecycleBuilder builder) => _builder = builder;
			public void AddEvent<TDelegate>(string eventName, TDelegate action) where TDelegate : Delegate
				=> _builder.AddEvent(eventName, action);
		}
	}

	public static class WPFLifecycleBuilderExtensions
	{
		public static IWPFLifecycleBuilder OnActivated(this IWPFLifecycleBuilder lifecycle, WPFLifecycle.OnActivated action)
		{
			lifecycle.AddEvent(nameof(WPFLifecycle.OnActivated), action);
			return lifecycle;
		}
	}

	public static class WPFLifecycle
	{
		public delegate void OnActivated(System.Windows.Window window, EventArgs args);
		//public delegate void OnClosed(System.Windows.Window window, System.Windows.WindowEventArgs args);
		//public delegate void OnLaunched(System.Windows.Application application, UI.Xaml.LaunchActivatedEventArgs args);
		//public delegate void OnLaunching(System.Windows.Application application, UI.Xaml.LaunchActivatedEventArgs args);
		//public delegate void OnVisibilityChanged(System.Windows.Window window, System.Windows.WindowVisibilityChangedEventArgs args);
		//public delegate void OnPlatformMessage(System.Windows.Window window, WindowsPlatformMessageEventArgs args);
		//public delegate void OnWindowCreated(System.Windows.Window window);
		//public delegate void OnResumed(System.Windows.Window window);
		//public delegate void OnPlatformWindowSubclassed(System.Windows.Window window, WindowsPlatformWindowSubclassedEventArgs args);

		// Internal events
		internal delegate void OnMauiContextCreated(IMauiContext mauiContext);
	}
}
