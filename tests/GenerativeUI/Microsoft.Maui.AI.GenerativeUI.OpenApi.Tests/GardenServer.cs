using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi.Tests;

/// <summary>
/// Launches the sample Garden server as a separate process (its own Microsoft.OpenApi 2.0.0) and
/// exposes an <see cref="HttpClient"/> bound to it. Running out-of-process keeps the server's
/// Microsoft.OpenApi off this 3.5.0 test's assembly-load path — they communicate only over HTTP.
/// </summary>
internal sealed class GardenServer : IAsyncDisposable
{
    private readonly Process _process;

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    private GardenServer(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
        Client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public static async Task<GardenServer> StartAsync()
    {
        var baseUrl = $"http://127.0.0.1:{GetFreePort()}";

        var startInfo = new ProcessStartInfo(ResolveServerExecutable())
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = baseUrl;
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Garden server.");
        var server = new GardenServer(process, baseUrl);

        try
        {
            await server.WaitUntilReadyAsync();
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        return server;
    }

    private async Task WaitUntilReadyAsync()
    {
        using var probe = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException($"Garden server exited early with code {_process.ExitCode}.");

            try
            {
                using var response = await probe.GetAsync("/products");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException)
            {
                // Server not accepting connections yet.
            }

            await Task.Delay(150);
        }

        throw new TimeoutException("The Garden server did not become ready within 30 seconds.");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ResolveServerExecutable()
    {
        // Test output: <artifacts>/bin/<TestProject>/<Config>/<Tfm>/
        var testBin = new DirectoryInfo(AppContext.BaseDirectory);
        var tfm = testBin.Name;
        var config = testBin.Parent!.Name;
        var binRoot = testBin.Parent!.Parent!.Parent!.FullName;

        var fileName = OperatingSystem.IsWindows()
            ? "GenerativeUI.Sample.Garden.Server.exe"
            : "GenerativeUI.Sample.Garden.Server";

        var path = Path.Combine(binRoot, "GenerativeUI.Sample.Garden.Server", config, tfm, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Garden server executable not found at '{path}'. Build the solution filter first.", path);

        return path;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }

        _process.Dispose();
    }
}
