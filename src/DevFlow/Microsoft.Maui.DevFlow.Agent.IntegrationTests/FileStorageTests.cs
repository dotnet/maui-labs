using System.Text;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "Files")]
public class FileStorageTests : IntegrationTestBase
{
    const string TestPathPrefix = "integration-tests/files";

    public FileStorageTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    [Fact]
    public async Task ListStorageRoots_ReturnsAppDataRootOnly()
    {
        var roots = await Client.ListStorageRootsAsync();

        var root = Assert.Single(roots.GetProperty("roots").EnumerateArray());
        Assert.Equal("appData", root.GetProperty("id").GetString());
        Assert.Equal("appData", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("isWritable").GetBoolean());
        Assert.Contains("upload", root.GetProperty("supportedOperations").EnumerateArray().Select(value => value.GetString()));
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

    [Fact]
    public async Task ExplicitAppDataRoot_RoundTripsFile()
    {
        var path = TestPath("explicit-root.txt");
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from explicit appData root"));

        try
        {
            var upload = await Client.UploadFileAsync(path, contentBase64, "appData");
            Assert.True(upload.GetProperty("success").GetBoolean());
            Assert.Equal("appData", upload.GetProperty("root").GetString());

            var download = await Client.DownloadFileAsync(path, "appData");
            Assert.Equal("appData", download.GetProperty("root").GetString());
            Assert.Equal(contentBase64, download.GetProperty("contentBase64").GetString());
        }
        finally
        {
            await Client.DeleteFileAsync(path, "appData");
        }
    }

    [Fact]
    public async Task UnsupportedRoot_ReturnsClearError()
    {
        var result = await Client.UploadFileAsync(TestPath("unsupported-root.txt"), "aGVsbG8=", "cache");

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Storage root 'cache' is not available", result.GetProperty("error").GetString());
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
