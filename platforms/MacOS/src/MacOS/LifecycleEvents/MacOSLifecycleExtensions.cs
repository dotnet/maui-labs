using System;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.LifecycleEvents;

public static class MacOSLifecycleExtensions
{
    public static ILifecycleBuilder AddMacOS(this ILifecycleBuilder builder, Action<IMacOSLifecycleBuilder> configureDelegate)
    {
        var lifecycle = new LifecycleBuilder(builder);
        configureDelegate?.Invoke(lifecycle);
        return builder;
    }

    class LifecycleBuilder : IMacOSLifecycleBuilder
    {
        readonly ILifecycleBuilder _builder;

        public LifecycleBuilder(ILifecycleBuilder builder) => _builder = builder;

        public void AddEvent<TDelegate>(string eventName, TDelegate action) where TDelegate : Delegate
            => _builder.AddEvent(eventName, action);
    }
}
