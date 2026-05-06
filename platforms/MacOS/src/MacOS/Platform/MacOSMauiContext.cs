using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public class MacOSMauiContext : IMauiContext
{
    readonly WrappedServiceProvider _services;
    readonly Lazy<IMauiHandlersFactory> _handlers;

    public MacOSMauiContext(IServiceProvider services)
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

    class WrappedServiceProvider : IServiceProvider
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
    }
}
