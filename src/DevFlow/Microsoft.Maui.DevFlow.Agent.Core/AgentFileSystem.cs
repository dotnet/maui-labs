using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Resolves Essentials file-system locations while a <c>MauiAppBuilder</c> is still being configured.
/// </summary>
internal static class AgentFileSystem
{
    /// <summary>
    /// Returns the app cache directory without requiring the static <see cref="FileSystem"/> facade.
    /// </summary>
    /// <remarks>
    /// MAUI 11 bridges the static Essentials facades to dependency injection only when the app is
    /// built, so during configuration a custom backend's registered <see cref="IFileSystem"/> is the
    /// authoritative implementation (the static facade still points at the portable assembly, which
    /// throws). Built-in platforms register nothing, so they keep using the static facade.
    /// </remarks>
    public static string GetCacheDirectory(IServiceCollection services)
    {
        var registration = services.LastOrDefault(
            descriptor => descriptor.ServiceType == typeof(IFileSystem) && !descriptor.IsKeyedService);

        if (registration?.ImplementationInstance is IFileSystem instance)
            return instance.CacheDirectory;

        if (registration?.ImplementationType is { } type &&
            Activator.CreateInstance(type) is IFileSystem created)
        {
            return created.CacheDirectory;
        }

        try
        {
            return FileSystem.CacheDirectory;
        }
        catch (NotImplementedException)
        {
            // The portable Essentials assembly throws NotImplementedInReferenceAssemblyException.
            // A custom backend that registers its file system after the agent: fall back to a
            // per-user temporary location rather than failing app startup.
            return Path.Combine(Path.GetTempPath(), "mauidevflow", AppDomain.CurrentDomain.FriendlyName);
        }
    }
}
