using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Maui.Storage;
#if WPF
using PlatformFileSystem = Microsoft.Maui.Platforms.Windows.WPF.Essentials.WPFFileSystem;
#else
using PlatformFileSystem = Microsoft.Maui.Platforms.Linux.Gtk4.Essentials.Storage.LinuxFileSystem;
#endif

namespace Essentials.Tests;

public class FileSystemDirectoryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static IEnumerable<object[]> Directories()
    {
        foreach (var cache in new[] { false, true })
        {
            yield return new object[] { cache, false };
#if !WPF
            yield return new object[] { cache, true };
#endif
        }
    }

    [Theory]
    [MemberData(nameof(Directories))]
    public void Directory_Missing_CanImmediatelyWriteAndRead(bool cache, bool useXdg)
    {
        WithIsolatedApplication(cache, useXdg, (fileSystem, expected) =>
        {
            Assert.False(Directory.Exists(expected));
            var path = GetDirectory(fileSystem, cache);
            Assert.Equal(expected, path);

            // This is the caller's first operation after reading the property.
            var file = Path.Combine(path, "pick.jpg");
            File.WriteAllBytes(file, [1, 2, 3]);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
            Assert.True(Directory.Exists(path));
            Assert.Equal(path, GetDirectory(fileSystem, cache));
            Assert.Equal(path, GetDirectory(new PlatformFileSystem(), cache));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
        });
    }

    [Theory]
    [MemberData(nameof(Directories))]
    public void Directory_AlreadyExists_PreservesContents(bool cache, bool useXdg)
    {
        WithIsolatedApplication(cache, useXdg, (fileSystem, expected) =>
        {
            Directory.CreateDirectory(expected);
            var file = Path.Combine(expected, "existing.txt");
            File.WriteAllText(file, "keep this content");

            Assert.Equal(expected, GetDirectory(fileSystem, cache));
            Assert.Equal(expected, GetDirectory(fileSystem, cache));
            Assert.Equal("keep this content", File.ReadAllText(file));
        });
    }

    [Theory]
    [MemberData(nameof(Directories))]
    public void Directory_PathIsAFile_PropagatesIOException(bool cache, bool useXdg)
    {
        WithIsolatedApplication(cache, useXdg, (fileSystem, expected) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
            File.WriteAllText(expected, "do not replace");

            Assert.Throws<IOException>(() => GetDirectory(fileSystem, cache));
            Assert.Equal("do not replace", File.ReadAllText(expected));
        });
    }

#if !WPF
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Directory_XdgChanges_ResolvesCurrentPathOnSameInstance(bool cache)
    {
        WithIsolatedApplication(cache, useXdg: true, (fileSystem, expected) =>
        {
            Assert.Equal(expected, GetDirectory(fileSystem, cache));
            var originalFile = Path.Combine(expected, "original.txt");
            File.WriteAllText(originalFile, "keep this content");

            var variable = cache ? "XDG_CACHE_HOME" : "XDG_DATA_HOME";
            var changedRoot = Path.Combine(Path.GetDirectoryName(expected)!, "changed");
            Environment.SetEnvironmentVariable(variable, changedRoot);
            var changedPath = Path.Combine(changedRoot, AppDomain.CurrentDomain.FriendlyName);
            Assert.Equal(changedPath, GetDirectory(fileSystem, cache));
            var changedFile = Path.Combine(changedPath, "changed.txt");
            File.WriteAllText(changedFile, "new location");
            Assert.Equal("new location", File.ReadAllText(changedFile));

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var fallbackRoot = cache ? Path.Combine(home, ".cache") : Path.Combine(home, ".local", "share");
            var fallbackPath = Path.Combine(fallbackRoot, AppDomain.CurrentDomain.FriendlyName);
            foreach (var emptyValue in new string?[] { null, "" })
            {
                Environment.SetEnvironmentVariable(variable, emptyValue);
                Assert.Equal(fallbackPath, GetDirectory(fileSystem, cache));
                var fallbackFile = Path.Combine(fallbackPath, "fallback.txt");
                File.WriteAllText(fallbackFile, "fallback location");
                Assert.Equal("fallback location", File.ReadAllText(fallbackFile));
            }
            Assert.Equal("keep this content", File.ReadAllText(originalFile));
        });
    }
#endif

    static string GetDirectory(IFileSystem fileSystem, bool cache)
        => cache ? fileSystem.CacheDirectory : fileSystem.AppDataDirectory;

    void WithIsolatedApplication(bool cache, bool useXdg, Action<IFileSystem, string> action)
    {
        var originalEntry = Assembly.GetEntryAssembly();
        var name = $"essentials-filesystem-{Guid.NewGuid():N}";
        // Type.Assembly returns the runtime assembly required by SetEntryAssembly,
        // rather than the AssemblyBuilder. The shared test assembly disables parallelism.
        var entry = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect)
            .DefineDynamicModule(name).DefineType("Application").CreateType()!.Assembly;
        var ownedPaths = new List<string>();
#if !WPF
        var originalCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var originalData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var xdgRoot = Path.Combine(Path.GetTempPath(), name);
#endif
        try
        {
            Assembly.SetEntryAssembly(entry);
            Assert.Equal(name, AppDomain.CurrentDomain.FriendlyName);
            output.WriteLine($"Isolated application: {AppDomain.CurrentDomain.FriendlyName}; OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
#if WPF
            var dataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cacheRoot = Path.GetTempPath();
#else
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", useXdg ? Path.Combine(xdgRoot, "cache") : null);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", useXdg ? Path.Combine(xdgRoot, "data") : null);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var dataRoot = useXdg ? Path.Combine(xdgRoot, "data") : Path.Combine(home, ".local", "share");
            var cacheRoot = useXdg ? Path.Combine(xdgRoot, "cache") : Path.Combine(home, ".cache");
            if (useXdg)
            {
                Assert.False(Path.Exists(xdgRoot));
                ownedPaths.Add(xdgRoot);
                foreach (var root in new[] { Path.Combine(home, ".local", "share"), Path.Combine(home, ".cache") })
                {
                    var fallbackPath = Path.Combine(root, name);
                    Assert.False(Path.Exists(fallbackPath));
                    ownedPaths.Add(fallbackPath);
                }
            }
#endif
            var dataPath = Path.Combine(dataRoot, name);
            var cachePath = Path.Combine(cacheRoot, name);
            foreach (var path in new[] { dataPath, cachePath })
            {
                Assert.False(Path.Exists(path));
                ownedPaths.Add(path);
            }
            output.WriteLine($"Expected directory: {(cache ? cachePath : dataPath)}");
            action(new PlatformFileSystem(), cache ? cachePath : dataPath);
        }
        finally
        {
            Assembly.SetEntryAssembly(originalEntry);
#if !WPF
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", originalCache);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", originalData);
#endif
            // Only GUID-owned paths are removed, never the user's storage roots.
            foreach (var path in ownedPaths.AsEnumerable().Reverse())
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
