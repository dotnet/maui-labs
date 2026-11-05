using System.Text;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Files")]
// Requires the MAUI sample: storage roots are resolved through Essentials FileSystem. Native runs filter this out.
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class FileStorageTests : IntegrationTestBase
{
    const string TestPathPrefix = "integration-tests/files";

    public FileStorageTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task ListStorageRoots_ReturnsAppDataAndCacheRoots()
    {
        var roots = await Client.ListStorageRootsAsync();

        var descriptors = roots.GetProperty("roots").EnumerateArray().ToArray();
        Assert.Equal(["appData", "cache"], descriptors.Select(root => root.GetProperty("id").GetString()));
        foreach (var root in descriptors)
        {
            var isAppData = root.GetProperty("id").GetString() == "appData";
            Assert.Equal(isAppData ? "appData" : "cache", root.GetProperty("kind").GetString());
            Assert.True(root.GetProperty("isWritable").GetBoolean());
            Assert.False(root.GetProperty("isReadOnly").GetBoolean());
            Assert.Equal(isAppData, root.GetProperty("isPersistent").GetBoolean());
            Assert.Equal(isAppData, root.GetProperty("isBackedUp").GetBoolean());
            Assert.Equal(!isAppData, root.GetProperty("mayBeClearedBySystem").GetBoolean());
            Assert.False(root.GetProperty("isUserVisible").GetBoolean());
            Assert.Equal(
                ["create-directory", "delete", "delete-directory", "download", "list", "move", "upload"],
                root.GetProperty("supportedOperations").EnumerateArray()
                    .Select(value => value.GetString()).OrderBy(value => value, StringComparer.Ordinal));
        }
        Assert.DoesNotContain("basePath", roots.ToString());
    }

    [Fact]
    public async Task UploadDownloadListAndDelete_RoundTripsSubdirectoryFile()
    {
        var path = TestPath("roundtrip.txt");
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from file integration test"));

        var upload = await Client.UploadFileAsync(path, contentBase64);
        Assert.True(upload.GetProperty("success").GetBoolean());
        Assert.Equal("appData", upload.GetProperty("root").GetString());
        Assert.Equal(path, upload.GetProperty("path").GetString());

        var list = await Client.ListFilesAsync(TestPathPrefix);
        Assert.Equal("appData", list.GetProperty("root").GetString());
        Assert.DoesNotContain("basePath", list.ToString());
        Assert.Contains("roundtrip.txt", list.ToString());

        var download = await Client.DownloadFileAsync(path);
        Assert.Equal("appData", download.GetProperty("root").GetString());
        Assert.Equal(path, download.GetProperty("path").GetString());
        Assert.Equal(contentBase64, download.GetProperty("contentBase64").GetString());

        var deleted = await Client.DeleteFileAsync(path);
        Assert.True(deleted);

        Assert.False(await Client.DeleteFileAsync(path));
    }

    [Theory]
    [InlineData("appData")]
    [InlineData("cache")]
    public async Task ExplicitRoot_RoundTripsFile(string root)
    {
        var path = TestPath("explicit-root.txt");
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"hello from explicit {root} root"));

        try
        {
            var upload = await Client.UploadFileAsync(path, contentBase64, root);
            Assert.True(upload.GetProperty("success").GetBoolean());
            Assert.Equal(root, upload.GetProperty("root").GetString());
            Assert.Equal(path, upload.GetProperty("path").GetString());

            var list = await Client.ListFilesAsync(TestPathPrefix, root);
            Assert.Equal(root, list.GetProperty("root").GetString());
            Assert.Contains(Path.GetFileName(path), list.ToString());

            var download = await Client.DownloadFileAsync(path, root);
            Assert.Equal(root, download.GetProperty("root").GetString());
            Assert.Equal(path, download.GetProperty("path").GetString());
            Assert.Equal(contentBase64, download.GetProperty("contentBase64").GetString());

            Assert.True(await Client.DeleteFileAsync(path, root));
            Assert.False(await Client.DeleteFileAsync(path, root));
        }
        finally
        {
            await Client.DeleteFileAsync(path, root);
        }
    }

    [Fact]
    public async Task UnsupportedRoot_ReturnsClearError()
    {
        const string root = "integration-test-unsupported";
        var result = await Client.UploadFileAsync(TestPath("unsupported-root.txt"), "aGVsbG8=", root);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains($"Storage root '{root}' is not available", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("integration-tests/../outside.txt")]
    [InlineData("integration-tests/%2e%2e/outside.txt")]
    public async Task Upload_RejectsTraversalPaths(string rawPath)
    {
        var path = Uri.UnescapeDataString(rawPath);
        var result = await Client.UploadFileAsync(path, Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Directory traversal is not allowed", result.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("/tmp/outside.txt")]
    [InlineData("\\Windows\\win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    public async Task Download_RejectsRootedOrAbsolutePaths(string path)
    {
        var result = await Client.DownloadFileAsync(path);

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Rooted or absolute paths are not allowed", result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Upload_AllowsFileNamesContainingDotDot()
    {
        var path = TestPath("my..log");
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("legitimate dot dot file name"));

        try
        {
            var upload = await Client.UploadFileAsync(path, contentBase64);

            Assert.True(upload.GetProperty("success").GetBoolean());
            Assert.Equal(path, upload.GetProperty("path").GetString());
        }
        finally
        {
            await Client.DeleteFileAsync(path);
        }
    }

    [Fact]
    public async Task Upload_RejectsInvalidBase64()
    {
        var result = await Client.UploadFileAsync(TestPath("invalid-base64.txt"), "not base64!");

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Invalid base64 content", result.GetProperty("error").GetString());
    }

    private static string TestPath(string fileName) => $"{TestPathPrefix}/{Guid.NewGuid():N}-{fileName}";
}
