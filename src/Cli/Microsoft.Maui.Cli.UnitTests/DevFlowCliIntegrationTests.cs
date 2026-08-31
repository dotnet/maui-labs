using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.Cli.UnitTests.Fakes;
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
    public async Task DiagnoseJson_WhenBrokerIsNotRunning_ReportsJsonArraysWithoutStartingBroker()
    {
        var cli = new CliTestHarness(mockAgentPort: 9223);
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-diagnose-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var brokerPortResolverCalled = false;

        DevFlowCommands.ResolveRunningBrokerPortAsync = () =>
        {
            brokerPortResolverCalled = true;
            return Task.FromResult<int?>(null);
        };
        DevFlowCommands.ListBrokerAgentsAsync = _ => throw new InvalidOperationException("Diagnose should not list agents when the broker is not running.");

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir.FullName, "App.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview" />
                  </ItemGroup>
                </Project>
                """);
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeRawAsync("devflow", "diagnose", "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.True(brokerPortResolverCalled);

            var json = result.ParseJsonOutput();
            Assert.False(json.GetProperty("broker_running").GetBoolean());
            Assert.False(json.TryGetProperty("broker_port", out _));
            Assert.Equal(0, json.GetProperty("agent_count").GetInt32());
            Assert.Equal(JsonValueKind.Array, json.GetProperty("agents").ValueKind);
            Assert.Empty(json.GetProperty("agents").EnumerateArray());
            Assert.Equal(JsonValueKind.Array, json.GetProperty("projects").ValueKind);
            Assert.Equal("App.csproj", Assert.Single(json.GetProperty("projects").EnumerateArray()).GetString());
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DiagnoseJson_WhenBrokerIsRunning_ReportsAgentsAsJsonArray()
    {
        var cli = new CliTestHarness(mockAgentPort: 9223);
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-diagnose-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = brokerPort =>
        {
            Assert.Equal(19223, brokerPort);
            return Task.FromResult<AgentRegistration[]?>(
            [
                new AgentRegistration
                {
                    Id = "agent-1",
                    Project = "/src/App.csproj",
                    Tfm = "net10.0-windows10.0.19041.0",
                    Platform = "Windows",
                    AppName = "SampleApp",
                    Port = 9223,
                    Version = "0.1.0-preview",
                    ConnectedAt = DateTime.UnixEpoch
                }
            ]);
        };
        DevFlowCommands.IsAndroidAdbLikelyAvailable = () => throw new InvalidOperationException("Non-Android diagnostics must not probe adb.");
        DevFlowCommands.CreateAndroidPortForwarder = () => throw new InvalidOperationException("Non-Android diagnostics must not create an Android port forwarder.");

        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeRawAsync("devflow", "diagnose", "--json");

            Assert.Equal(0, result.ExitCode);

            var json = result.ParseJsonOutput();
            Assert.True(json.GetProperty("broker_running").GetBoolean());
            Assert.Equal(19223, json.GetProperty("broker_port").GetInt32());
            Assert.Equal(1, json.GetProperty("agent_count").GetInt32());
            Assert.Equal(JsonValueKind.Array, json.GetProperty("agents").ValueKind);
            var agent = Assert.Single(json.GetProperty("agents").EnumerateArray());
            Assert.Equal("agent-1", agent.GetProperty("id").GetString());
            Assert.Equal("SampleApp", agent.GetProperty("appName").GetString());
            Assert.Equal(JsonValueKind.Array, json.GetProperty("projects").ValueKind);
            Assert.Empty(json.GetProperty("projects").EnumerateArray());
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DiagnoseJson_WhenAndroidAgentIsRegistered_ReportsForwardingState()
    {
        var cli = new CliTestHarness(mockAgentPort: 9223);
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-diagnose-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();
        var runner = new FakeAdbRunner(forwardPorts: [9223], reversePorts: [19223]);

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(
        [
            new AgentRegistration
            {
                Id = "android-agent",
                Project = "/src/App.csproj",
                Tfm = "net10.0-android",
                Platform = "Android",
                AppName = "SampleApp",
                Port = 9223,
                Version = "0.1.0-preview",
                ConnectedAt = DateTime.UnixEpoch
            }
        ]);
        DevFlowCommands.IsAndroidAdbLikelyAvailable = () => true;
        DevFlowCommands.CreateAndroidPortForwarder = () =>
        {
            var provider = new FakeAndroidProvider
            {
                SdkPath = "/android-sdk",
                IsSdkInstalled = true,
                Devices =
                [
                    new Device
                    {
                        Id = "emulator-5554",
                        Name = "Pixel",
                        Platforms = ["android"],
                        Type = DeviceType.Emulator,
                        State = DeviceState.Connected,
                        IsEmulator = true,
                        IsRunning = true
                    }
                ]
            };

            return new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);
        };

        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeRawAsync("devflow", "diagnose", "--json", "--device", "emulator-5554");

            Assert.Equal(0, result.ExitCode);
            var json = result.ParseJsonOutput();
            var android = json.GetProperty("android");
            Assert.Equal("emulator-5554", android.GetProperty("selected_serial").GetString());
            Assert.True(android.GetProperty("broker_reverse_present").GetBoolean());
            var forward = Assert.Single(android.GetProperty("agent_forwards").EnumerateArray());
            Assert.Equal(9223, forward.GetProperty("port").GetInt32());
            Assert.True(forward.GetProperty("present_after").GetBoolean());
            Assert.Contains("-s emulator-5554 reverse --list", runner.Commands);
            Assert.Contains("-s emulator-5554 forward --list", runner.Commands);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task DiagnoseHuman_WhenBrokerUsesCustomPort_ReportsCustomBrokerReversePort()
    {
        var cli = new CliTestHarness(mockAgentPort: 9223);
        var tempDir = Directory.CreateTempSubdirectory("maui-devflow-diagnose-");
        var originalCurrentDirectory = Directory.GetCurrentDirectory();

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19225);
        DevFlowCommands.ListBrokerAgentsAsync = brokerPort =>
        {
            Assert.Equal(19225, brokerPort);
            return Task.FromResult<AgentRegistration[]?>(
            [
                new AgentRegistration
                {
                    Id = "android-agent",
                    Project = "/src/App.csproj",
                    Tfm = "net10.0-android",
                    Platform = "Android",
                    AppName = "SampleApp",
                    Port = 9223,
                    Version = "0.1.0-preview",
                    ConnectedAt = DateTime.UnixEpoch
                }
            ]);
        };
        DevFlowCommands.IsAndroidAdbLikelyAvailable = () => true;
        DevFlowCommands.CreateAndroidPortForwarder = () =>
        {
            var provider = new FakeAndroidProvider
            {
                SdkPath = "/android-sdk",
                IsSdkInstalled = true,
                Devices =
                [
                    new Device
                    {
                        Id = "emulator-5554",
                        Name = "Pixel",
                        Platforms = ["android"],
                        Type = DeviceType.Emulator,
                        State = DeviceState.Connected,
                        IsEmulator = true,
                        IsRunning = true
                    }
                ]
            };

            return new AndroidDevFlowPortForwarder(
                provider,
                "/android-sdk/platform-tools/adb",
                new FakeAdbRunner(forwardPorts: [9223], reversePorts: [19225]));
        };

        try
        {
            Directory.SetCurrentDirectory(tempDir.FullName);

            var result = await cli.InvokeRawAsync("devflow", "diagnose", "--no-json", "--device", "emulator-5554");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Broker reverse:   ready (tcp:19225)", result.StdOut);
            Assert.DoesNotContain("Broker reverse:   ready (tcp:19223)", result.StdOut);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
            Directory.SetCurrentDirectory(originalCurrentDirectory);
            tempDir.Delete(recursive: true);
        }
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

    // ── issue #343: multi-agent ambiguity refusal + resolved-target labeling ──

    [Fact]
    public async Task UiTree_WithSentinelPort_RefusesAndExitsNonZero()
    {
        // --agent-port 0 simulates the ambiguous multi-agent case (SelectAgentPort sentinel).
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeRawAsync("devflow", "ui", "tree", "--agent-port", "0", "--json");

        Assert.Equal(1, result.ExitCode);
        var combined = result.StdOut + result.StdErr;
        Assert.Contains("--agent-port", combined);
        Assert.Contains("Multiple", combined);
        // The command must not have reached the agent.
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/ui/tree");
    }

    [Fact]
    public async Task UiScreenshot_WithSentinelPort_RefusesAndExitsNonZero()
    {
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeRawAsync("devflow", "ui", "screenshot", "--agent-port", "0", "--json");

        Assert.Equal(1, result.ExitCode);
        var combined = result.StdOut + result.StdErr;
        Assert.Contains("--agent-port", combined);
    }

    [Fact]
    public async Task UiStatus_WithMultipleAgents_LabelsResolvedTargetOnStderr()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        await using var serverHandle = server;
        var cli = new CliTestHarness(server.Port);

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(
        [
            new AgentRegistration
            {
                Id = "agent-mac",
                Project = "/src/App.csproj",
                Tfm = "net10.0-maccatalyst",
                Platform = "MacCatalyst",
                AppName = "TargetApp",
                Port = server.Port,
                Version = "0.1.0-preview"
            },
            new AgentRegistration
            {
                Id = "agent-ios",
                Project = "/src/Other.csproj",
                Tfm = "net10.0-ios",
                Platform = "iOS",
                AppName = "OtherApp",
                Port = server.Port + 1,
                Version = "0.1.0-preview"
            }
        ]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "ui", "status", "--json");

            Assert.Equal(0, result.ExitCode);
            // stdout stays clean JSON; the target label is written to stderr only.
            var json = result.ParseJsonOutput();
            Assert.True(json.GetProperty("running").GetBoolean());
            Assert.Contains("target:", result.StdErr);
            Assert.Contains("TargetApp", result.StdErr);
            Assert.DoesNotContain("target:", result.StdOut);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task UiStatus_WithSingleAgent_DoesNotLabel()
    {
        var server = new MockAgentServer();
        await server.StartAsync();
        await using var serverHandle = server;
        var cli = new CliTestHarness(server.Port);

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(
        [
            new AgentRegistration
            {
                Id = "agent-mac",
                Project = "/src/App.csproj",
                Tfm = "net10.0-maccatalyst",
                Platform = "MacCatalyst",
                AppName = "OnlyApp",
                Port = server.Port,
                Version = "0.1.0-preview"
            }
        ]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "ui", "status", "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("target:", result.StdErr);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task DeviceInfo_WithMultipleAgents_LabelsResolvedTargetOnStderr()
    {
        // device info goes through the SimpleGetAsync raw-HTTP path (not CreateAgentClientAsync),
        // so this guards that those commands now label which app produced the output too (#343 #1).
        var server = new MockAgentServer();
        await server.StartAsync();
        await using var serverHandle = server;
        var cli = new CliTestHarness(server.Port);

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(
        [
            new AgentRegistration
            {
                Id = "agent-mac",
                Project = "/src/App.csproj",
                Tfm = "net10.0-maccatalyst",
                Platform = "MacCatalyst",
                AppName = "TargetApp",
                Port = server.Port,
                Version = "0.1.0-preview"
            },
            new AgentRegistration
            {
                Id = "agent-ios",
                Project = "/src/Other.csproj",
                Tfm = "net10.0-ios",
                Platform = "iOS",
                AppName = "OtherApp",
                Port = server.Port + 1,
                Version = "0.1.0-preview"
            }
        ]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "device", "device-info", "--json");

            Assert.Equal(0, result.ExitCode);
            // stdout stays the raw agent JSON; the target label is written to stderr only.
            Assert.Contains("target:", result.StdErr);
            Assert.Contains("TargetApp", result.StdErr);
            Assert.DoesNotContain("target:", result.StdOut);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task ExtensionsList_WithMultipleAgents_LabelsResolvedTargetOnStderr()
    {
        // `extensions list` constructs its AgentClient via the shared CreateAgentClientAsync
        // helper (issue #343), so it must inherit the same resolved-target labeling as the
        // other output-producing commands.
        var server = new MockAgentServer();
        await server.StartAsync();
        await using var serverHandle = server;
        var cli = new CliTestHarness(server.Port);

        DevFlowCommands.ResolveRunningBrokerPortAsync = () => Task.FromResult<int?>(19223);
        DevFlowCommands.ListBrokerAgentsAsync = _ => Task.FromResult<AgentRegistration[]?>(
        [
            new AgentRegistration
            {
                Id = "agent-mac",
                Project = "/src/App.csproj",
                Tfm = "net10.0-maccatalyst",
                Platform = "MacCatalyst",
                AppName = "TargetApp",
                Port = server.Port,
                Version = "0.1.0-preview"
            },
            new AgentRegistration
            {
                Id = "agent-ios",
                Project = "/src/Other.csproj",
                Tfm = "net10.0-ios",
                Platform = "iOS",
                AppName = "OtherApp",
                Port = server.Port + 1,
                Version = "0.1.0-preview"
            }
        ]);

        try
        {
            var result = await cli.InvokeAsync("devflow", "extensions", "list", "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("target:", result.StdErr);
            Assert.Contains("TargetApp", result.StdErr);
            Assert.DoesNotContain("target:", result.StdOut);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task Batch_WithSentinelPort_RefusesBeforeRunningAnyCommand()
    {
        // Regression guard: BatchAsync injects one resolved port into every sub-command, so an
        // ambiguous target (sentinel 0) must fail fast before the loop rather than throwing
        // mid-iteration and aborting the batch in a way that bypasses --continue-on-error.
        var (server, cli) = await CreateFixturesAsync();
        await using var serverHandle = server;

        var result = await cli.InvokeRawAsync("devflow", "batch", "--agent-port", "0");

        Assert.Equal(1, result.ExitCode);
        var combined = result.StdOut + result.StdErr;
        Assert.Contains("--agent-port", combined);
        Assert.Contains("Multiple", combined);
        // The batch must never have reached the agent for any sub-command.
        Assert.Empty(server.RecordedRequests);
    }
}
