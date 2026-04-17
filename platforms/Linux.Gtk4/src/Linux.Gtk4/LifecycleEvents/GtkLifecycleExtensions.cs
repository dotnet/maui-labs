using System;

namespace Microsoft.Maui.LifecycleEvents;

public delegate void GtkApplicationActivated(Gio.Application application);
public delegate void GtkApplicationShutdown(Gio.Application application);
public delegate void GtkMauiApplicationCreated(Microsoft.Maui.IApplication application);
public delegate void GtkWindowCreated(Gtk.Window window);

public interface IGtkLifecycleBuilder : ILifecycleBuilder
{
}

public static class GtkLifecycleExtensions
{
	public static ILifecycleBuilder AddGtk(this ILifecycleBuilder builder, Action<IGtkLifecycleBuilder> configureDelegate)
	{
		var lifecycle = new LifecycleBuilder(builder);
		configureDelegate?.Invoke(lifecycle);
		return builder;
	}

	class LifecycleBuilder : IGtkLifecycleBuilder
	{
		readonly ILifecycleBuilder _builder;

		public LifecycleBuilder(ILifecycleBuilder builder)
		{
			_builder = builder;
		}

		public void AddEvent<TDelegate>(string eventName, TDelegate action)
			where TDelegate : Delegate
		{
			_builder.AddEvent(eventName, action);
		}
	}
}

public static class GtkLifecycleBuilderExtensions
{
	public static IGtkLifecycleBuilder OnApplicationActivated(this IGtkLifecycleBuilder lifecycle, GtkApplicationActivated action)
	{
		lifecycle.AddEvent(nameof(GtkApplicationActivated), action);
		return lifecycle;
	}

	public static IGtkLifecycleBuilder OnApplicationShutdown(this IGtkLifecycleBuilder lifecycle, GtkApplicationShutdown action)
	{
		lifecycle.AddEvent(nameof(GtkApplicationShutdown), action);
		return lifecycle;
	}

	public static IGtkLifecycleBuilder OnMauiApplicationCreated(this IGtkLifecycleBuilder lifecycle, GtkMauiApplicationCreated action)
	{
		lifecycle.AddEvent(nameof(GtkMauiApplicationCreated), action);
		return lifecycle;
	}

	public static IGtkLifecycleBuilder OnWindowCreated(this IGtkLifecycleBuilder lifecycle, GtkWindowCreated action)
	{
		lifecycle.AddEvent(nameof(GtkWindowCreated), action);
		return lifecycle;
	}
}
