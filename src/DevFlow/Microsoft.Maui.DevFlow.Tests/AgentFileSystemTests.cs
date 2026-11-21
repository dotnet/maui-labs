using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Storage;
using Xunit;

namespace Microsoft.Maui.DevFlow.Tests;

public class AgentFileSystemTests
{
    [Fact]
    public void GetCacheDirectory_UsesRegisteredInstance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new StubFileSystem("/instance/cache"));

        Assert.Equal("/instance/cache", AgentFileSystem.GetCacheDirectory(services));
    }

    [Fact]
    public void GetCacheDirectory_ActivatesRegisteredTypeBeforeAppIsBuilt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem, TypeRegisteredFileSystem>();

        Assert.Equal(TypeRegisteredFileSystem.Directory, AgentFileSystem.GetCacheDirectory(services));
    }

    [Fact]
    public void GetCacheDirectory_PrefersLastRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(new StubFileSystem("/first"));
        services.AddSingleton<IFileSystem>(new StubFileSystem("/last"));

        Assert.Equal("/last", AgentFileSystem.GetCacheDirectory(services));
    }

    [Fact]
    public void GetCacheDirectory_WithoutRegistrationOnPortableAssembly_FallsBackToTemp()
    {
        var directory = AgentFileSystem.GetCacheDirectory(new ServiceCollection());

        Assert.False(string.IsNullOrWhiteSpace(directory));
    }

    sealed class TypeRegisteredFileSystem : StubFileSystem
    {
        public const string Directory = "/type/cache";

        public TypeRegisteredFileSystem() : base(Directory)
        {
        }
    }

    class StubFileSystem(string cacheDirectory) : IFileSystem
    {
        public string CacheDirectory => cacheDirectory;
        public string AppDataDirectory => cacheDirectory;
        public Task<Stream> OpenAppPackageFileAsync(string filename) => throw new NotSupportedException();
        public Task<bool> AppPackageFileExistsAsync(string filename) => Task.FromResult(false);
    }
}
