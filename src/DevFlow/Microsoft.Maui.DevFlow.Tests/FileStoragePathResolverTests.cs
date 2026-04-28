using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class FileStoragePathResolverTests
{
    [Fact]
    public void Resolve_AllowsFileNamesContainingDotDot()
    {
        var basePath = CreateTempDirectory();

        var resolved = FileStoragePathResolver.Resolve(basePath, "logs/my..log");

        Assert.Equal("logs/my..log", resolved.RelativePath);
        Assert.True(
            resolved.FullPath.StartsWith(Path.GetFullPath(basePath), StringComparison.Ordinal),
            resolved.FullPath);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("logs/../outside.txt")]
    [InlineData("logs/%2e%2e/outside.txt")]
    public void Resolve_RejectsTraversalSegments(string path)
    {
        var basePath = CreateTempDirectory();
        var decodedPath = Uri.UnescapeDataString(path);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FileStoragePathResolver.Resolve(basePath, decodedPath));

        Assert.Equal("Directory traversal is not allowed", ex.Message);
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:Windows\\win.ini")]
    public void Resolve_RejectsRootedOrAbsolutePaths(string path)
    {
        var basePath = CreateTempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FileStoragePathResolver.Resolve(basePath, path));

        Assert.Equal("Rooted or absolute paths are not allowed", ex.Message);
    }

    [Fact]
    public void Resolve_AllowsRootWhenRequested()
    {
        var basePath = CreateTempDirectory();

        var resolved = FileStoragePathResolver.Resolve(basePath, null, allowRoot: true);

        Assert.Equal(string.Empty, resolved.RelativePath);
        Assert.Equal(Path.GetFullPath(basePath), resolved.FullPath);
    }

    [Fact]
    public void Resolve_RejectsEmptyFilePathByDefault()
    {
        var basePath = CreateTempDirectory();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FileStoragePathResolver.Resolve(basePath, ""));

        Assert.Equal("file path is required", ex.Message);
    }

    [Fact]
    public void EnsureNoReparsePointTraversal_RejectsSymlinkedDirectory()
    {
        var basePath = CreateTempDirectory();
        var outsidePath = CreateTempDirectory();
        var linkPath = Path.Combine(basePath, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outsidePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var targetPath = Path.Combine(linkPath, "outside.txt");

        var error = Assert.Throws<InvalidOperationException>(() =>
            FileStoragePathResolver.EnsureNoReparsePointTraversal(basePath, targetPath, includeTarget: true));

        Assert.Equal("Symbolic links are not allowed in file storage paths", error.Message);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mauidevflow-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
