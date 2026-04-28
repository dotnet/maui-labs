using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DevFlowCliIntegrationTests
{
    private static async Task<(MockAgentServer server, CliTestHarness cli)> CreateFixturesAsync()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        return (server, cli);
    }

    [Fact]
    public async Task UiStatus_UsesV1AgentStatusRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "status", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.TryGetProperty("agent", out _));
        Assert.True(json.GetProperty("running").GetBoolean());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/agent/status");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task UiTree_WithDepth_UsesV1TreeRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "tree", "--depth", "2", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.NotEmpty(server.RecordedRequests);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
        Assert.Contains("depth=2", request.QueryString);
    }

    [Fact]
    public async Task UiQuery_ByAutomationId_UsesV1ElementsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "query", "--automationId", "ClickMeButton", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal(JsonValueKind.Array, json.ValueKind);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/elements");
        Assert.Contains("automationId=ClickMeButton", request.QueryString);
    }

    [Fact]
    public async Task UiTap_UsesV1ActionRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "ui", "tap", "el-1", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/ui/actions/tap");
        Assert.Equal("POST", request.Method);
        Assert.Contains("el-1", request.Body);
    }

    [Fact]
    public async Task StoragePreferencesSet_UsesPutV1Route()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "preferences", "set", "theme", "dark", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/preferences/theme");
        Assert.Equal("PUT", request.Method);
        Assert.Contains("dark", request.Body);
    }

    [Fact]
    public async Task StorageRoots_UsesV1StorageRootsRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "roots", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("appData", json.GetProperty("roots")[0].GetProperty("id").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/roots");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task StorageFilesList_UsesV1FilesRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "list", "logs", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("logs", json.GetProperty("path").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files");
        Assert.Equal("GET", request.Method);
        Assert.Contains("path=logs", request.QueryString);
    }

    [Fact]
    public async Task StorageFilesList_WithRoot_UsesRootQuery()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "list", "logs", "--root", "appData", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("appData", json.GetProperty("root").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files");
        Assert.Equal("GET", request.Method);
        Assert.Contains("path=logs", request.QueryString);
        Assert.Contains("root=appData", request.QueryString);
    }

    [Fact]
    public async Task StorageFilesDownload_UsesV1FilesRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "app.log", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("aGVsbG8=", json.GetProperty("contentBase64").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task StorageFilesDownload_WithRoot_UsesRootQuery()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "app.log", "--root", "appData", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("appData", json.GetProperty("root").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("GET", request.Method);
        Assert.Contains("root=appData", request.QueryString);
    }

    [Fact]
    public async Task StorageFilesDownload_WithOutputDirectory_WritesRemoteFileName()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-download-");

        try
        {
            var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "app.log", "--output", tempDir.FullName, "--json");

            Assert.Equal(0, result.ExitCode);
            var outputFile = Path.Combine(tempDir.FullName, "app.log");
            Assert.Equal("hello", await File.ReadAllTextAsync(outputFile));
            var json = result.ParseJsonOutput();
            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.Equal(outputFile, json.GetProperty("localPath").GetString());
            Assert.False(json.TryGetProperty("contentBase64", out _));

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
            Assert.Equal("GET", request.Method);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesDownload_WithOutputFile_WritesExplicitPath()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-download-");

        try
        {
            var outputFile = Path.Combine(tempDir.FullName, "renamed.txt");

            var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "app.log", "--output", outputFile, "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("hello", await File.ReadAllTextAsync(outputFile));
            var json = result.ParseJsonOutput();
            Assert.Equal(outputFile, json.GetProperty("localPath").GetString());

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
            Assert.Equal("GET", request.Method);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesDownload_WithNestedDevicePathAndOutputDirectory_UsesRemoteFileName()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-download-");

        try
        {
            var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "logs/app.log", "--output", tempDir.FullName, "--json");

            Assert.Equal(0, result.ExitCode);
            var outputFile = Path.Combine(tempDir.FullName, "app.log");
            Assert.Equal("hello", await File.ReadAllTextAsync(outputFile));
            var json = result.ParseJsonOutput();
            Assert.Equal(outputFile, json.GetProperty("localPath").GetString());

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/logs%2Fapp.log");
            Assert.Equal("GET", request.Method);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesDownload_WithTrailingDirectorySeparator_CreatesDirectoryAndUsesRemoteFileName()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-download-");

        try
        {
            var outputDirectory = Path.Combine(tempDir.FullName, "created-downloads") + Path.DirectorySeparatorChar;

            var result = await cli.InvokeAsync("devflow", "storage", "files", "download", "logs/app.log", "--output", outputDirectory, "--json");

            Assert.Equal(0, result.ExitCode);
            var outputFile = Path.Combine(outputDirectory, "app.log");
            Assert.Equal("hello", await File.ReadAllTextAsync(outputFile));
            var json = result.ParseJsonOutput();
            Assert.Equal(outputFile, json.GetProperty("localPath").GetString());

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/logs%2Fapp.log");
            Assert.Equal("GET", request.Method);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesUpload_UsesPutV1FilesRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "aGVsbG8=", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.True(json.GetProperty("success").GetBoolean());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("PUT", request.Method);
        Assert.Contains("\"contentBase64\":\"aGVsbG8=\"", request.Body);
    }

    [Fact]
    public async Task StorageFilesUpload_WithLocalFile_ReadsFileContent()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-upload-");

        try
        {
            var localFile = Path.Combine(tempDir.FullName, "payload.txt");
            await File.WriteAllTextAsync(localFile, "from disk");

            var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "--file", localFile, "--json");

            Assert.Equal(0, result.ExitCode);
            var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("from disk"));

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
            Assert.Equal("PUT", request.Method);
            Assert.Contains($"\"contentBase64\":\"{expectedBase64}\"", request.Body);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesUpload_WithRelativeLocalFile_ReadsFromCurrentDirectory()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-upload-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        try
        {
            var localFile = Path.Combine(tempDir.FullName, "payload.txt");
            await File.WriteAllTextAsync(localFile, "relative content");
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "--file", "payload.txt", "--json");

            Assert.Equal(0, result.ExitCode);
            var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("relative content"));

            var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
            Assert.Equal("PUT", request.Method);
            Assert.Contains($"\"contentBase64\":\"{expectedBase64}\"", request.Body);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesUpload_WithContentAndLocalFile_ReturnsError()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-upload-");

        try
        {
            var localFile = Path.Combine(tempDir.FullName, "payload.txt");
            await File.WriteAllTextAsync(localFile, "from disk");

            var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "aGVsbG8=", "--file", localFile, "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Provide exactly one of contentBase64 or --file.", result.StdErr);
            Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task StorageFilesUpload_WithoutContentOrLocalFile_ReturnsError()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "--json");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Provide exactly one of contentBase64 or --file.", result.StdErr);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
    }

    [Fact]
    public async Task StorageFilesUpload_WithRoot_UsesRootQuery()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "upload", "app.log", "aGVsbG8=", "--root", "appData", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("appData", json.GetProperty("root").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("PUT", request.Method);
        Assert.Contains("root=appData", request.QueryString);
        Assert.Contains("\"contentBase64\":\"aGVsbG8=\"", request.Body);
    }

    [Fact]
    public async Task StorageFilesDelete_UsesDeleteV1FilesRoute()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "delete", "app.log", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("DELETE", request.Method);
    }

    [Fact]
    public async Task StorageFilesDelete_WithRoot_UsesRootQuery()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "storage", "files", "delete", "app.log", "--root", "appData", "--json");

        Assert.Equal(0, result.ExitCode);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/storage/files/app.log");
        Assert.Equal("DELETE", request.Method);
        Assert.Contains("root=appData", request.QueryString);
    }

    [Fact]
    public async Task DeviceInfo_UsesV1DeviceEndpoint()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "device", "device-info", "--json");

        Assert.Equal(0, result.ExitCode);
        var json = result.ParseJsonOutput();
        Assert.Equal("Apple", json.GetProperty("manufacturer").GetString());

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/device/info");
        Assert.Equal("GET", request.Method);
    }

    [Fact]
    public async Task WebViewBrowserGetVersion_UsesV1EvaluateEndpoint()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeAsync("devflow", "webview", "Browser", "getVersion", "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("protocolVersion", result.StdOut);

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", request.Method);
        Assert.Contains("Browser.getVersion", request.Body);
    }
}
