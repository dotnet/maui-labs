using System.IO;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using PackagedAssetsApp;

namespace Microsoft.Maui.Platforms.Windows.WPF.Tests;

public class FileSystemTests
{
    [Theory]
    [MemberData(nameof(AssetCases.All), MemberType = typeof(AssetCases))]
    public async Task PackagedAsset_LogicalName_ExistsAndOpensExpectedContent(string name, string expected)
    {
        var fileSystem = new WPFFileSystem();

        Assert.True(await fileSystem.AppPackageFileExistsAsync(name));
        using var stream = await fileSystem.OpenAppPackageFileAsync(name);
        using var reader = new StreamReader(stream);
        Assert.Equal(expected, (await reader.ReadToEndAsync()).Trim());
    }

    [Fact]
    public async Task PackagedAsset_Missing_DoesNotExistAndThrows()
    {
        var fileSystem = new WPFFileSystem();

        Assert.False(await fileSystem.AppPackageFileExistsAsync("Data/missing.txt"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => fileSystem.OpenAppPackageFileAsync("Data/missing.txt"));
    }
}
