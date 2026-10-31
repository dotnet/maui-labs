using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers DevFlow plugins in an app's service collection. When the DevFlow agent starts, it creates
/// every plugin registered here, along with those added through the agent's own options.
/// </summary>
/// <remarks>
/// This lets a library ship one registration call that installs whatever its plugin needs before the
/// container is built (decorators, middleware, recorders) and registers the plugin itself. If the app
/// does not run the agent, nothing is created.
/// </remarks>
public static class DevFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="T"/> as a DevFlow plugin. The agent creates it from the app's
    /// service provider when it starts, so its constructor can take app services. Registering the same
    /// type twice has no effect.
    /// </summary>
    public static IServiceCollection AddDevFlowExtension<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services)
        where T : DevFlowExtension
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddDevFlowExtension(typeof(T), sp => ActivatorUtilities.CreateInstance<T>(sp));
    }

    /// <summary>
    /// Registers a DevFlow plugin created by <paramref name="factory"/> when the agent starts.
    /// Registering a second factory for the same <paramref name="extensionType"/> has no effect.
    /// </summary>
    public static IServiceCollection AddDevFlowExtension(
        this IServiceCollection services,
        Type extensionType,
        Func<IServiceProvider, DevFlowExtension> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(extensionType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!typeof(DevFlowExtension).IsAssignableFrom(extensionType))
            throw new ArgumentException($"{extensionType} does not derive from {nameof(DevFlowExtension)}.", nameof(extensionType));

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(DevFlowExtensionRegistration)
                && descriptor.ImplementationInstance is DevFlowExtensionRegistration existing
                && existing.ExtensionType == extensionType)
                return services;
        }

        services.Add(ServiceDescriptor.Singleton(new DevFlowExtensionRegistration(extensionType, factory)));
        return services;
    }
}

/// <summary>
/// A DevFlow plugin registered in an app's service collection with
/// <see cref="DevFlowServiceCollectionExtensions.AddDevFlowExtension{T}(IServiceCollection)"/>.
/// The agent resolves these and creates each plugin separately, so one that fails to construct is
/// reported without affecting the others.
/// </summary>
/// <param name="ExtensionType">The plugin type, used in error reports and to skip duplicates.</param>
/// <param name="Factory">Creates the plugin from the app's service provider.</param>
public sealed record DevFlowExtensionRegistration(Type ExtensionType, Func<IServiceProvider, DevFlowExtension> Factory);
