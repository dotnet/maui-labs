using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class FileStorageRootTests
{
    [Fact]
    public async Task StorageRoots_ReturnsContributedRootsWithoutPhysicalPath()
    {
        var appDataPath = CreateTempDirectory();
        var customPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath), ("custom", customPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var result = await WaitForJsonAsync(client.ListStorageRootsAsync);

        Assert.Equal(JsonValueKind.Array, result.GetProperty("roots").ValueKind);
        Assert.Equal("appData", result.GetProperty("roots")[0].GetProperty("id").GetString());
        Assert.Equal("custom", result.GetProperty("roots")[1].GetProperty("id").GetString());
        Assert.DoesNotContain("basePath", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appDataPath, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(customPath, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileEndpoints_ExplicitAppDataRoot_RoundTrips()
    {
        var appDataPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var path = $"root-tests/{Guid.NewGuid():N}.txt";
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello app data"));

        var upload = await WaitForJsonAsync(() => client.UploadFileAsync(path, contentBase64, "appData"));
        Assert.True(upload.GetProperty("success").GetBoolean());
        Assert.Equal("appData", upload.GetProperty("root").GetString());
        Assert.Equal(path, upload.GetProperty("path").GetString());

        var list = await client.ListFilesAsync("root-tests", "appData");
        Assert.Equal("appData", list.GetProperty("root").GetString());
        Assert.Contains(Path.GetFileName(path), list.ToString(), StringComparison.Ordinal);

        var download = await client.DownloadFileAsync(path, "appData");
        Assert.Equal("appData", download.GetProperty("root").GetString());
        Assert.Equal(contentBase64, download.GetProperty("contentBase64").GetString());

        Assert.True(await client.DeleteFileAsync(path, "appData"));
    }

    [Fact]
    public async Task FileEndpoints_UnadvertisedRoot_ReturnsClearError()
    {
        var appDataPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var result = await WaitForJsonAsync(() => client.UploadFileAsync("cache.txt", "aGVsbG8=", "cache"));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Storage root 'cache' is not available", result.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileEndpoints_RejectsOperationNotSupportedByContributedRoot()
    {
        var appDataPath = CreateTempDirectory();
        var readOnlyPath = CreateTempDirectory();
        using var service = CreateService(
            new TestStorageRoot("appData", appDataPath),
            new TestStorageRoot("readonly", readOnlyPath, false, "list", "download"));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var result = await WaitForJsonAsync(() => client.UploadFileAsync("blocked.txt", "aGVsbG8=", "readonly"));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Storage root 'readonly' does not support 'upload'", result.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileEndpoints_UsesContributedCustomRoot()
    {
        var appDataPath = CreateTempDirectory();
        var customPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath), ("custom", customPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var path = $"custom-root/{Guid.NewGuid():N}.txt";
        var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("custom root content"));

        var upload = await WaitForJsonAsync(() => client.UploadFileAsync(path, contentBase64, "custom"));

        Assert.True(upload.GetProperty("success").GetBoolean());
        Assert.Equal("custom", upload.GetProperty("root").GetString());
        Assert.True(File.Exists(Path.Combine(customPath, path)));
        Assert.False(File.Exists(Path.Combine(appDataPath, path)));
    }

    [Fact]
    public async Task FileUpload_RejectsSymlinkedTargetFile()
    {
        var appDataPath = CreateTempDirectory();
        var outsidePath = CreateTempDirectory();
        var outsideFile = Path.Combine(outsidePath, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "outside");
        var linkPath = Path.Combine(appDataPath, "linked.txt");

        try
        {
            File.CreateSymbolicLink(linkPath, outsideFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var result = await WaitForJsonAsync(() => client.UploadFileAsync("linked.txt", "aGVsbG8="));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("Symbolic links are not allowed", result.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal("outside", await File.ReadAllTextAsync(outsideFile));
    }

    private static RootedDevFlowAgentService CreateService(params (string Id, string BasePath)[] roots)
        => CreateService(roots.Select(root => new TestStorageRoot(root.Id, root.BasePath)).ToArray());

    private static RootedDevFlowAgentService CreateService(params TestStorageRoot[] roots)
    {
        var port = GetFreePort();
        return new RootedDevFlowAgentService(port, roots);
    }

    private static async Task<JsonElement> WaitForJsonAsync(Func<Task<JsonElement>> action)
    {
        for (var i = 0; i < 10; i++)
        {
            var result = await action();
            if (result.ValueKind != JsonValueKind.Undefined)
                return result;

            await Task.Delay(100);
        }

        throw new InvalidOperationException("Agent endpoint did not return JSON before timeout.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mauidevflow-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RootedDevFlowAgentService : DevFlowAgentService
    {
        private readonly IReadOnlyList<FileStorageRoot> _roots;

        public RootedDevFlowAgentService(int port, IReadOnlyList<TestStorageRoot> roots)
            : base(new AgentOptions { Port = port })
        {
            ServicePort = port;
            _roots = roots.Select(root => new FileStorageRoot(
                root.Id,
                root.Id == "appData" ? "App data" : root.Id,
                root.Id,
                root.BasePath,
                root.IsWritable,
                isPersistent: true,
                isBackedUp: root.Id == "appData",
                mayBeClearedBySystem: false,
                isUserVisible: false,
                root.SupportedOperations.ToArray())).ToArray();
        }

        public int ServicePort { get; }

        protected override IReadOnlyList<FileStorageRoot> GetFileStorageRoots() => _roots;
    }

    private sealed class TestStorageRoot
    {
        private static readonly string[] s_defaultOperations = ["list", "download", "upload", "delete"];

        public TestStorageRoot(string id, string basePath, bool isWritable = true, params string[] supportedOperations)
        {
            Id = id;
            BasePath = basePath;
            IsWritable = isWritable;
            SupportedOperations = supportedOperations.Length == 0 ? s_defaultOperations : supportedOperations;
        }

        public string Id { get; }
        public string BasePath { get; }
        public bool IsWritable { get; }
        public IReadOnlyList<string> SupportedOperations { get; }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;

        public bool Dispatch(Action action)
        {
            action();
            return true;
        }

        public bool DispatchDelayed(TimeSpan delay, Action action)
        {
            action();
            return true;
        }

        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick
        {
            add { }
            remove { }
        }

        public void Start()
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }
    }
}
