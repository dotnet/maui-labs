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

    [Fact]
    public async Task Directories_CreateListAndDelete()
    {
        var appDataPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var created = await WaitForJsonAsync(() => client.CreateDirectoryAsync("reports/2026"));
        Assert.True(created.GetProperty("success").GetBoolean());
        Assert.Equal("reports/2026", created.GetProperty("path").GetString());
        Assert.True(Directory.Exists(Path.Combine(appDataPath, "reports", "2026")));

        var list = await client.ListFilesAsync("reports");
        var entry = list.GetProperty("entries")[0];
        Assert.Equal("2026", entry.GetProperty("name").GetString());
        Assert.Equal("directory", entry.GetProperty("type").GetString());
        Assert.Equal("reports/2026", entry.GetProperty("path").GetString());

        Assert.True(await client.DeleteDirectoryAsync("reports/2026"));
        Assert.False(Directory.Exists(Path.Combine(appDataPath, "reports", "2026")));
    }

    [Fact]
    public async Task DirectoryDelete_RefusesNonEmptyDirectoryWithoutRecursive()
    {
        var appDataPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(appDataPath, "logs"));
        await File.WriteAllTextAsync(Path.Combine(appDataPath, "logs", "today.txt"), "noisy");

        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        // Wait for the server rather than asserting on the first call - the port may not be up yet.
        await WaitForJsonAsync(client.ListStorageRootsAsync);

        Assert.False(await client.DeleteDirectoryAsync("logs"));
        Assert.True(Directory.Exists(Path.Combine(appDataPath, "logs")));

        Assert.True(await client.DeleteDirectoryAsync("logs", recursive: true));
        Assert.False(Directory.Exists(Path.Combine(appDataPath, "logs")));
    }

    [Fact]
    public async Task Move_RenamesFileAndRefusesToClobberWithoutOverwrite()
    {
        var appDataPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        await WaitForJsonAsync(() => client.UploadFileAsync("notes.txt", Convert.ToBase64String(Encoding.UTF8.GetBytes("first"))));
        await client.UploadFileAsync("keep.txt", Convert.ToBase64String(Encoding.UTF8.GetBytes("second")));

        var moved = await client.MoveAsync("notes.txt", "archive/notes.txt");
        Assert.True(moved.GetProperty("success").GetBoolean());
        Assert.Equal("archive/notes.txt", moved.GetProperty("path").GetString());
        Assert.Equal("file", moved.GetProperty("type").GetString());
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(appDataPath, "archive", "notes.txt")));

        var refused = await client.MoveAsync("keep.txt", "archive/notes.txt");
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("already exists", refused.GetProperty("error").GetString(), StringComparison.Ordinal);

        var overwritten = await client.MoveAsync("keep.txt", "archive/notes.txt", overwrite: true);
        Assert.True(overwritten.GetProperty("success").GetBoolean());
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(appDataPath, "archive", "notes.txt")));
    }

    [Fact]
    public async Task Move_RefusesToMoveDirectoryInsideItself()
    {
        var appDataPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(appDataPath, "tree"));

        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        var result = await WaitForJsonAsync(() => client.MoveAsync("tree", "tree/inner"));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("inside itself", result.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(appDataPath, "tree")));
    }

    [Fact]
    public async Task RawTransfer_RoundTripsBinaryContentUntouched()
    {
        var appDataPath = CreateTempDirectory();
        using var service = CreateService(("appData", appDataPath));
        using var client = new AgentClient("localhost", service.ServicePort);

        service.StartServerOnly(new ImmediateDispatcher());

        // Every byte value, including the ones a UTF-8 round trip would replace with U+FFFD.
        var payload = new byte[256];
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)i;

        var upload = await WaitForJsonAsync(() => client.UploadFileBytesAsync("binary/all-bytes.bin", payload));
        Assert.True(upload.GetProperty("success").GetBoolean());
        Assert.Equal(payload.Length, upload.GetProperty("size").GetInt32());

        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(appDataPath, "binary", "all-bytes.bin")));
        Assert.Equal(payload, await client.DownloadFileBytesAsync("binary/all-bytes.bin"));
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

    private sealed class RootedDevFlowAgentService : MauiDevFlowAgentService
    {
        private readonly IReadOnlyList<FileStorageRoot> _roots;

        public RootedDevFlowAgentService(int port, IReadOnlyList<TestStorageRoot> roots)
            : base(new AgentOptions { Port = port, RequireMutationLease = false })
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
        private static readonly string[] s_defaultOperations =
            ["list", "download", "upload", "delete", "create-directory", "delete-directory", "move"];

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
