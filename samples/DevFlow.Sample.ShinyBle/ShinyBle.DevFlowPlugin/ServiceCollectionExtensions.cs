using Microsoft.Extensions.DependencyInjection;
using Shiny.BluetoothLE;

namespace ShinyBle.DevFlowPlugin;

public static class ShinyBleDevFlowServiceCollectionExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="IBleManager"/> so the DevFlow plugin sees what the app does with it.
    /// Call after <c>AddBluetoothLE()</c>, in debug builds only.
    /// </summary>
    public static IServiceCollection AddShinyBleDevFlowMonitor(this IServiceCollection services)
    {
        var registration = services.LastOrDefault(d => d.ServiceType == typeof(IBleManager))
            ?? throw new InvalidOperationException("Call AddBluetoothLE() before AddShinyBleDevFlowMonitor().");

        services.Remove(registration);
        services.AddSingleton<BleActivityMonitor>();
        services.AddSingleton<IBleManager>(sp => new MonitoredBleManager(
            Resolve(registration, sp),
            sp.GetRequiredService<BleActivityMonitor>()));
        return services;
    }

    static IBleManager Resolve(ServiceDescriptor registration, IServiceProvider services)
    {
        if (registration.ImplementationInstance is IBleManager instance)
            return instance;
        if (registration.ImplementationFactory is { } factory)
            return (IBleManager)factory(services);
        return (IBleManager)ActivatorUtilities.CreateInstance(services, registration.ImplementationType!);
    }
}
