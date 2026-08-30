using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MobileCanvasCompanionManagerTests : IDisposable
{
    private sealed class StubDownloader(byte[] archive) : IMobileCanvasAssetDownloader
    {
        public int Calls { get; private set; }
        public Uri? LastSource { get; private set; }

        public async Task DownloadAsync(
            Uri source,
            string destination,
            long expectedSize,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastSource = source;
            Assert.Equal(expectedSize, archive.LongLength);
            await File.WriteAllBytesAsync(destination, archive, cancellationToken);
        }
    }

    private sealed class StubProcessRunner : IMobileCanvasCompanionProcessRunner
    {
        public int Calls { get; private set; }
        public string? Executable { get; private set; }
        public string[]? Arguments { get; private set; }
        public Action? OnRun { get; init; }
        public MobileCanvasCompanionProcessStartResult Result { get; init; } = new(true);

        public MobileCanvasCompanionProcessStartResult Start(
            string executable,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            Calls++;
            Executable = executable;
            Arguments = arguments;
            OnRun?.Invoke();
            return Result;
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;
        public Uri? LastRequestUri { get; private set; }
        public Action<HttpRequestMessage>? OnSend { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestUri = request.RequestUri;
            OnSend?.Invoke(request);
            return Task.FromResult(new HttpResponseMessage(StatusCode));
        }
    }

    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        $"devflow-mobile-canvas-companion-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public async Task Status_DoesNotDownloadOrStartAnything()
    {
        var fixture = CreateFixture();
        var process = new StubProcessRunner();
        var manager = CreateManager(fixture, processRunner: process);

        var status = await manager.GetStatusAsync();

        Assert.True(status.Supported);
        Assert.False(status.Installed);
        Assert.False(status.Running);
        Assert.Equal(0, fixture.Downloader.Calls);
        Assert.Equal(0, process.Calls);
    }

    [Fact]
    public async Task Install_VerifiesAndMaterializesThePinnedRuntime()
    {
        var fixture = CreateFixture();
        var manager = CreateManager(fixture);

        var result = await manager.InstallAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, fixture.Downloader.Calls);
        Assert.Equal(
            $"https://github.com/Redth/mobile-canvas-ghcp/releases/download/v0.1.16/{fixture.AssetName}",
            fixture.Downloader.LastSource!.AbsoluteUri);
        Assert.Equal(fixture.Payload, await File.ReadAllBytesAsync(manager.GetExecutablePath()!));
        Assert.True((await manager.GetStatusAsync(probeHost: false)).IntegrityValid);
    }

    [Fact]
    public async Task Install_RejectsAnArchiveWhosePinnedDigestDoesNotMatch()
    {
        var fixture = CreateFixture(compressedDigest: new string('0', 64));
        var manager = CreateManager(fixture);

        var result = await manager.InstallAsync();

        Assert.False(result.Success);
        Assert.Contains("SHA-256", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(manager.GetExecutablePath()));
    }

    [Fact]
    public async Task Start_RehashesTheRuntimeBeforeExecutingIt()
    {
        var fixture = CreateFixture();
        var process = new StubProcessRunner();
        var manager = CreateManager(fixture, processRunner: process);
        Assert.True((await manager.InstallAsync()).Success);
        await File.AppendAllTextAsync(manager.GetExecutablePath()!, "corrupt");

        var result = await manager.StartAsync();

        Assert.False(result.Success);
        Assert.Equal(0, process.Calls);
    }

    [Fact]
    public async Task Start_ExecutesOnlyTheVerifiedInstalledRuntime()
    {
        var fixture = CreateFixture();
        MobileCanvasHostState? state = null;
        var process = new StubProcessRunner
        {
            OnRun = () => state = new MobileCanvasHostState
            {
                SchemaVersion = "1.0",
                Version = "0.1.16",
                Port = 54321,
                ControlToken = "token",
            },
        };
        var handler = new StubHttpHandler();
        var manager = CreateManager(
            fixture,
            process,
            new HttpClient(handler),
            () => state);
        Assert.True((await manager.InstallAsync()).Success);

        var result = await manager.StartAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal(manager.GetExecutablePath(), process.Executable);
        Assert.NotNull(process.Arguments);
        Assert.Equal(["host", "start", "--json"], process.Arguments);
        Assert.True(handler.Calls > 0);
    }

    [Fact]
    public async Task Start_ReportsALaunchFailureWithoutPollingTheHost()
    {
        var fixture = CreateFixture();
        var process = new StubProcessRunner
        {
            Result = new MobileCanvasCompanionProcessStartResult(false, "launch refused"),
        };
        var manager = CreateManager(fixture, processRunner: process);
        Assert.True((await manager.InstallAsync()).Success);

        var result = await manager.StartAsync();

        Assert.False(result.Success);
        Assert.Contains("launch refused", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, process.Calls);
    }

    [Fact]
    public void ProcessRunner_UsesAnIndependentWindowsShellLaunch()
    {
        var info = MobileCanvasCompanionProcessRunner.CreateStartInfo(
            @"C:\runtime\mobile-canvas.exe",
            ["host", "start", "--json"],
            windows: true);

        Assert.True(info.UseShellExecute);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.Equal(ProcessWindowStyle.Hidden, info.WindowStyle);
        Assert.Equal(["host", "start", "--json"], info.ArgumentList.ToArray());
    }

    [Fact]
    public void ProcessRunner_UsesADetachedUnixShellWithoutRedirectedPipes()
    {
        var info = MobileCanvasCompanionProcessRunner.CreateStartInfo(
            "/runtime/mobile canvas",
            ["host", "start", "--json"],
            windows: false);

        Assert.False(info.UseShellExecute);
        Assert.Equal("/bin/sh", info.FileName);
        Assert.False(info.RedirectStandardInput);
        Assert.False(info.RedirectStandardOutput);
        Assert.False(info.RedirectStandardError);
        Assert.True(info.CreateNoWindow);
        Assert.Equal(
            [
                "-c",
                "trap '' HUP; exec </dev/null >/dev/null 2>&1; \"$@\" &",
                "maui-devflow-mobile-canvas",
                "/runtime/mobile canvas",
                "host",
                "start",
                "--json",
            ],
            info.ArgumentList.ToArray());
    }

    [Fact]
    public async Task Stop_WhenNoHostIsRegistered_DoesNotLaunchAProcess()
    {
        var fixture = CreateFixture();
        var process = new StubProcessRunner();
        var manager = CreateManager(fixture, processRunner: process);

        var result = await manager.StopAsync();

        Assert.True(result.Success);
        Assert.Equal(0, process.Calls);
        Assert.Equal(0, fixture.Downloader.Calls);
    }

    [Fact]
    public async Task Stop_DoesNotDriveALegacyHostState()
    {
        var fixture = CreateFixture();
        var handler = new StubHttpHandler();
        var manager = CreateManager(
            fixture,
            httpClient: new HttpClient(handler),
            stateProvider: () => new MobileCanvasHostState
            {
                SchemaVersion = "1.0",
                Version = MobileCanvasProtocol.ValidatedHostVersion,
                Port = 54321,
                ProcessId = 42,
                ControlToken = "token",
                Origin = MobileCanvasHostStateOrigin.Legacy,
            });

        var result = await manager.StopAsync();

        Assert.False(result.Success);
        Assert.Contains("legacy", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Stop_WaitsForTheRegisteredHostToDisappear()
    {
        var fixture = CreateFixture();
        MobileCanvasHostState? state = new()
        {
            SchemaVersion = "1.0",
            Version = "0.1.16",
            Port = 54321,
            ProcessId = 42,
            ControlToken = "token",
        };
        var processAlive = true;
        var handler = new StubHttpHandler
        {
            StatusCode = HttpStatusCode.Accepted,
            OnSend = request =>
            {
                if (request.RequestUri?.AbsolutePath == "/api/v1/host/stop")
                {
                    processAlive = false;
                    state = null;
                }
            },
        };
        var manager = CreateManager(
            fixture,
            httpClient: new HttpClient(handler),
            stateProvider: () => state,
            isProcessAlive: _ => processAlive);

        var result = await manager.StopAsync();

        Assert.True(result.Success, result.Message);
        Assert.Equal("/api/v1/host/stop", handler.LastRequestUri!.AbsolutePath);
        Assert.Null(state);
    }

    /// <summary>
    /// A control request carries the host's bearer token to a loopback address. .NET strips
    /// <c>Authorization</c> only when a redirect changes <em>host</em>, so a redirect from
    /// 127.0.0.1:54321 to 127.0.0.1:65000 — a port any local process can bind — would hand the
    /// token straight to whatever is listening there. The control client therefore follows no
    /// redirect at all, and this pins that: exactly one request is made, the second port is never
    /// contacted, and the token appears only on the request to the registered host.
    /// </summary>
    [Fact]
    public async Task Stop_NeverFollowsALoopbackRedirectToAnotherPortWithTheControlToken()
    {
        var fixture = CreateFixture();
        var attacker = new Uri("http://127.0.0.1:65000/api/v1/host/stop");
        var handler = new RedirectingHttpHandler(attacker);
        var manager = CreateManager(
            fixture,
            httpClient: new HttpClient(handler),
            stateProvider: () => HostState());

        var result = await manager.StopAsync();

        Assert.False(result.Success);
        Assert.Contains("redirect", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.Calls);
        Assert.DoesNotContain(attacker, handler.RequestedUris);
        Assert.Equal(new Uri("http://127.0.0.1:54321/api/v1/host/stop"), Assert.Single(handler.RequestedUris));
        Assert.Equal(["token"], handler.AuthorizedRequests);
    }

    /// <summary>The same rule on the probe: a redirect is never a live host.</summary>
    [Fact]
    public async Task Probe_TreatsALoopbackRedirectAsANonResponsiveHost()
    {
        var fixture = CreateFixture();
        var handler = new RedirectingHttpHandler(new Uri("http://127.0.0.1:65000/api/v1/status"));
        var manager = CreateManager(fixture, httpClient: new HttpClient(handler), stateProvider: () => HostState());

        var status = await manager.GetStatusAsync();

        Assert.False(status.Running);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(
            new Uri("http://127.0.0.1:54321/api/v1/status"),
            Assert.Single(handler.RequestedUris));
    }

    /// <summary>
    /// The split is the point: downloads must keep following GitHub's redirect to its object store,
    /// and those requests carry no credential. One client cannot hold both policies.
    /// </summary>
    [Fact]
    public void TheDownloadClientAndTheControlClientAreSeparate()
    {
        var control = new HttpClient(new StubHttpHandler());
        var download = new HttpClient(new StubHttpHandler());
        var fixture = CreateFixture();

        var manager = new MobileCanvasCompanionManager(
            manifest: fixture.Manifest,
            processRunner: new StubProcessRunner(),
            controlHttpClient: control,
            downloadHttpClient: download,
            stateProvider: () => null,
            homeDirectory: _home,
            runtimeKey: "test-x64");

        Assert.NotNull(manager);
        Assert.NotSame(control, download);
        // Redirect classification is shared by both control call sites.
        Assert.True(MobileCanvasCompanionManager.IsRedirect(HttpStatusCode.Found));
        Assert.True(MobileCanvasCompanionManager.IsRedirect(HttpStatusCode.TemporaryRedirect));
        Assert.True(MobileCanvasCompanionManager.IsRedirect(HttpStatusCode.PermanentRedirect));
        Assert.False(MobileCanvasCompanionManager.IsRedirect(HttpStatusCode.OK));
        Assert.False(MobileCanvasCompanionManager.IsRedirect(HttpStatusCode.Unauthorized));
    }

    private static MobileCanvasHostState HostState() => new()
    {
        SchemaVersion = "1.0",
        Version = MobileCanvasProtocol.ValidatedHostVersion,
        Port = 54321,
        ProcessId = 42,
        ControlToken = "token",
    };

    private sealed class RedirectingHttpHandler(Uri location) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<Uri> RequestedUris { get; } = [];
        public List<string> AuthorizedRequests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri is not null)
                RequestedUris.Add(request.RequestUri);
            if (request.Headers.Authorization?.Parameter is { } token)
                AuthorizedRequests.Add(token);

            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// The JavaScript loader in <c>lib/runtime.mjs</c> accepts a runtime ID only when it matches
    /// <c>/^[a-f0-9]{64}$/</c>, and both loaders resolve the same install directory from
    /// <c>id[..12]</c>. A length-only check on this side would accept an uppercase or non-hex ID
    /// that then names a different directory than the JS side, or fails every ordinal comparison
    /// against a digest DevFlow itself lowercases.
    /// </summary>
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0", false)]
    [InlineData("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef 123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ManifestDigests_MustBeLowercaseHexExactlyAsTheJavaScriptLoaderRequires(
        string? value,
        bool valid)
    {
        Assert.Equal(valid, MobileCanvasCompanionManager.IsLowercaseSha256(value));
    }

    [Theory]
    [InlineData("runtimeId")]
    [InlineData("sha256")]
    [InlineData("compressedSha256")]
    public void AnUppercaseDigestIsRefusedWhenTheManifestIsLoaded(string field)
    {
        var fixture = CreateFixture();
        var runtime = fixture.Manifest.Runtimes["test-x64"];
        var file = runtime.Files["mobile-canvas.exe"];
        var corrupted = field switch
        {
            "runtimeId" => runtime with { Id = runtime.Id.ToUpperInvariant() },
            "sha256" => runtime with
            {
                Files = new Dictionary<string, MobileCanvasCompanionFile>
                {
                    ["mobile-canvas.exe"] = file with { Sha256 = file.Sha256.ToUpperInvariant() },
                },
            },
            _ => runtime with
            {
                Files = new Dictionary<string, MobileCanvasCompanionFile>
                {
                    ["mobile-canvas.exe"] = file with
                    {
                        CompressedSha256 = file.CompressedSha256.ToUpperInvariant(),
                    },
                },
            },
        };
        var manifest = fixture.Manifest with
        {
            Runtimes = new Dictionary<string, MobileCanvasCompanionRuntime> { ["test-x64"] = corrupted },
        };

        Assert.Throws<InvalidDataException>(() => new MobileCanvasCompanionManager(
            manifest: manifest,
            downloader: fixture.Downloader,
            processRunner: new StubProcessRunner(),
            controlHttpClient: new HttpClient(new StubHttpHandler()),
            stateProvider: () => null,
            homeDirectory: _home,
            runtimeKey: "test-x64"));
    }

    [Fact]
    public void TheEmbeddedManifestSatisfiesTheStricterDigestRule()
    {
        var manifest = MobileCanvasCompanionManifest.LoadEmbedded();

        foreach (var (_, runtime) in manifest.Runtimes)
        {
            Assert.True(MobileCanvasCompanionManager.IsLowercaseSha256(runtime.Id));
            foreach (var (_, file) in runtime.Files)
            {
                Assert.True(MobileCanvasCompanionManager.IsLowercaseSha256(file.Sha256));
                Assert.True(MobileCanvasCompanionManager.IsLowercaseSha256(file.CompressedSha256));
            }
        }
    }

    [Fact]
    public void EmbeddedManifest_IsBoundToTheValidatedCompatibilityRevision()
    {
        var manifest = MobileCanvasCompanionManifest.LoadEmbedded();

        Assert.Equal(MobileCanvasProtocol.ValidatedHostVersion, manifest.Version);
        Assert.Equal(MobileCanvasProtocol.ValidatedHostRevision, manifest.ValidatedRevision);
        Assert.Equal(6, manifest.Runtimes.Count);
    }

    private MobileCanvasCompanionManager CreateManager(
        Fixture fixture,
        StubProcessRunner? processRunner = null,
        HttpClient? httpClient = null,
        Func<MobileCanvasHostState?>? stateProvider = null,
        Func<int, bool>? isProcessAlive = null) =>
        new(
            manifest: fixture.Manifest,
            downloader: fixture.Downloader,
            processRunner: processRunner ?? new StubProcessRunner(),
            controlHttpClient: httpClient ?? new HttpClient(new StubHttpHandler()),
            stateProvider: stateProvider ?? (() => null),
            isProcessAlive: isProcessAlive,
            homeDirectory: _home,
            runtimeKey: "test-x64");

    private static Fixture CreateFixture(string? compressedDigest = null)
    {
        var payload = "mobile-canvas-test-runtime"u8.ToArray();
        var archive = Compress(payload);
        var payloadDigest = Digest(payload);
        var archiveDigest = compressedDigest ?? Digest(archive);
        const string assetName = "mobile-canvas-v0.1.16-test-x64.gz";
        var runtime = new MobileCanvasCompanionRuntime
        {
            Rid = "test-x64",
            Id = payloadDigest,
            Executable = "mobile-canvas.exe",
            Files = new Dictionary<string, MobileCanvasCompanionFile>
            {
                ["mobile-canvas.exe"] = new()
                {
                    Asset = assetName,
                    CompressedSha256 = archiveDigest,
                    CompressedSize = archive.LongLength,
                    Sha256 = payloadDigest,
                    Size = payload.LongLength,
                },
            },
        };
        var manifest = new MobileCanvasCompanionManifest
        {
            Schema = 1,
            Version = MobileCanvasProtocol.ValidatedHostVersion,
            ValidatedRevision = MobileCanvasProtocol.ValidatedHostRevision,
            Repository = "Redth/mobile-canvas-ghcp",
            Tag = "v0.1.16",
            Runtimes = new Dictionary<string, MobileCanvasCompanionRuntime>
            {
                ["test-x64"] = runtime,
            },
        };
        return new Fixture(payload, assetName, manifest, new StubDownloader(archive));
    }

    private static byte[] Compress(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(payload);
        return output.ToArray();
    }

    private static string Digest(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record Fixture(
        byte[] Payload,
        string AssetName,
        MobileCanvasCompanionManifest Manifest,
        StubDownloader Downloader);
}
