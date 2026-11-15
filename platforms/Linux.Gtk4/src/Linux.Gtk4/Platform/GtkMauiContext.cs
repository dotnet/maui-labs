using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

public class GtkMauiContext : IMauiContext
{
	readonly WrappedServiceProvider _services;
	readonly Lazy<IMauiHandlersFactory> _handlers;

	public GtkMauiContext(IServiceProvider services)
	{
		_services = new WrappedServiceProvider(services ?? throw new ArgumentNullException(nameof(services)));
		_handlers = new Lazy<IMauiHandlersFactory>(() => _services.GetRequiredService<IMauiHandlersFactory>());
	}

	public IServiceProvider Services => _services;

	public IMauiHandlersFactory Handlers => _handlers.Value;

	internal void AddSpecific<TService>(TService instance) where TService : class
	{
		_services.AddSpecific(typeof(TService), static state => state, instance);
	}

	internal void AddWeakSpecific<TService>(TService instance) where TService : class
	{
		_services.AddSpecific(typeof(TService), static state => ((WeakReference)state).Target, new WeakReference(instance));
	}

	// Keyed lookups (e.g. the application IDispatcher MAUI keys to typeof(IApplication))
	// pass through to the app container, as MAUI's own MauiContext does.
	class WrappedServiceProvider : IServiceProvider, IKeyedServiceProvider
	{
		readonly ConcurrentDictionary<Type, (object State, Func<object, object?> Getter)> _scopeStatic = new();

		public WrappedServiceProvider(IServiceProvider serviceProvider)
		{
			Inner = serviceProvider;
		}

		public IServiceProvider Inner { get; }

		public object? GetService(Type serviceType)
		{
			if (_scopeStatic.TryGetValue(serviceType, out var scope))
			{
				var (state, getter) = scope;
				return getter.Invoke(state);
			}

			return Inner.GetService(serviceType);
		}

		public void AddSpecific(Type type, Func<object, object?> getter, object state)
		{
			_scopeStatic[type] = (state, getter);
		}

		public object? GetKeyedService(Type serviceType, object? serviceKey) =>
			Inner is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;

		public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
			Inner is IKeyedServiceProvider keyed
				? keyed.GetRequiredKeyedService(serviceType, serviceKey)
				: throw new InvalidOperationException("This service provider doesn't support keyed services.");
	}
}
