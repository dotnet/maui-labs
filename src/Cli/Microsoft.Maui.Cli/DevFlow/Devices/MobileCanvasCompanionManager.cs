using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed record MobileCanvasCompanionManifest
{
    [JsonPropertyName("schema")] public int Schema { get; init; }
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("validatedRevision")] public string ValidatedRevision { get; init; } = "";
    [JsonPropertyName("repository")] public string Repository { get; init; } = "";
    [JsonPropertyName("tag")] public string Tag { get; init; } = "";
    [JsonPropertyName("runtimes")] public Dictionary<string, MobileCanvasCompanionRuntime> Runtimes { get; init; } = [];

    public static MobileCanvasCompanionManifest LoadEmbedded()
    {
        using var stream = typeof(MobileCanvasCompanionManifest).Assembly.GetManifestResourceStream(
            "Microsoft.Maui.Cli.DevFlow.Devices.mobile-canvas-runtime.json")
            ?? throw new InvalidOperationException("The pinned Mobile Canvas runtime manifest is missing.");
        return JsonSerializer.Deserialize(
            stream,
            MobileCanvasCompanionJsonContext.Default.MobileCanvasCompanionManifest)
            ?? throw new InvalidDataException("The pinned Mobile Canvas runtime manifest is invalid.");
    }
}

internal sealed record MobileCanvasCompanionRuntime
{
    [JsonPropertyName("rid")] public string Rid { get; init; } = "";
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("executable")] public string Executable { get; init; } = "";
    [JsonPropertyName("files")] public Dictionary<string, MobileCanvasCompanionFile> Files { get; init; } = [];
}

internal sealed record MobileCanvasCompanionFile
{
    [JsonPropertyName("asset")] public string Asset { get; init; } = "";
    [JsonPropertyName("compressedSha256")] public string CompressedSha256 { get; init; } = "";
    [JsonPropertyName("compressedSize")] public long CompressedSize { get; init; }
    [JsonPropertyName("sha256")] public string Sha256 { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
}

internal sealed record MobileCanvasInstallationReceipt
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("runtimeKey")] public string RuntimeKey { get; init; } = "";
    [JsonPropertyName("runtimeId")] public string RuntimeId { get; init; } = "";
    [JsonPropertyName("validatedRevision")] public string ValidatedRevision { get; init; } = "";
    [JsonPropertyName("installedAt")] public DateTimeOffset InstalledAt { get; init; }
}

internal sealed record MobileCanvasCompanionStatus
{
    public bool Supported { get; init; }
    public string Version { get; init; } = "";
    public string? RuntimeKey { get; init; }
    public string? RuntimeId { get; init; }
    public string? InstallDirectory { get; init; }
    public string? ExecutablePath { get; init; }
    public bool Installed { get; init; }
    public bool IntegrityValid { get; init; }
    public bool Running { get; init; }
    public bool HostRegistered { get; init; }
    public string? HostVersion { get; init; }
    public string? Reason { get; init; }
}

internal sealed record MobileCanvasCompanionOperationResult
{
    public bool Success { get; init; }
    public string Action { get; init; } = "";
    public string Message { get; init; } = "";
    public MobileCanvasCompanionStatus? Status { get; init; }
}

internal sealed record MobileCanvasMcpRunResult(bool Started, int ExitCode, string? Error = null);

internal interface IMobileCanvasAssetDownloader
{
    Task DownloadAsync(
        Uri source,
        string destination,
        long expectedSize,
        CancellationToken cancellationToken);
}

internal sealed class MobileCanvasAssetDownloader(HttpClient httpClient) : IMobileCanvasAssetDownloader
{
    public async Task DownloadAsync(
        Uri source,
        string destination,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength && contentLength != expectedSize)
        {
            throw new InvalidDataException(
                $"The Mobile Canvas asset length was {contentLength}, expected {expectedSize}.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        var buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;

            written += count;
            if (written > expectedSize)
                throw new InvalidDataException("The Mobile Canvas asset exceeded its pinned size.");

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }

        if (written != expectedSize)
        {
            throw new InvalidDataException(
                $"The Mobile Canvas asset length was {written}, expected {expectedSize}.");
        }
    }
}

internal interface IMobileCanvasCompanionProcessRunner
{
    MobileCanvasCompanionProcessStartResult Start(
        string executable,
        string[] arguments,
        CancellationToken cancellationToken);
}

internal sealed record MobileCanvasCompanionProcessStartResult(bool Success, string? Error = null);

internal sealed class MobileCanvasCompanionProcessRunner : IMobileCanvasCompanionProcessRunner
{
    public MobileCanvasCompanionProcessStartResult Start(
        string executable,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var startInfo = CreateStartInfo(executable, arguments, OperatingSystem.IsWindows());
            var process = new Process
            {
                StartInfo = startInfo,
            };

            if (!process.Start())
            {
                process.Dispose();
                return new MobileCanvasCompanionProcessStartResult(
                    false,
                    "The Mobile Canvas start process did not launch.");
            }

            // Disposing Process closes only our wrapper, not the detached child.
            process.Dispose();
            return new MobileCanvasCompanionProcessStartResult(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new MobileCanvasCompanionProcessStartResult(false, ex.Message);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments,
        bool windows)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };
        if (windows)
        {
            startInfo.FileName = executable;
            startInfo.UseShellExecute = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }
        else
        {
            startInfo.FileName = "/bin/sh";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(
                "trap '' HUP; exec </dev/null >/dev/null 2>&1; \"$@\" &");
            startInfo.ArgumentList.Add("maui-devflow-mobile-canvas");
            startInfo.ArgumentList.Add(executable);
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }
}

internal sealed class MobileCanvasCompanionManager
{
    private const string ReceiptFileName = "devflow-install.json";
    private static readonly TimeSpan HostProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HostStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan HostStopTimeout = TimeSpan.FromSeconds(20);

    private readonly MobileCanvasCompanionManifest _manifest;
    private readonly IMobileCanvasAssetDownloader _downloader;
    private readonly IMobileCanvasCompanionProcessRunner _processRunner;
    private readonly HttpClient _controlHttpClient;
    private readonly HttpClient _downloadHttpClient;
    private readonly Func<MobileCanvasHostState?> _stateProvider;
    private readonly Func<int, bool> _isProcessAlive;
    private readonly string _runtimeRoot;
    private readonly string? _runtimeKeyOverride;

    public MobileCanvasCompanionManager(
        MobileCanvasCompanionManifest? manifest = null,
        IMobileCanvasAssetDownloader? downloader = null,
        IMobileCanvasCompanionProcessRunner? processRunner = null,
        HttpClient? controlHttpClient = null,
        Func<MobileCanvasHostState?>? stateProvider = null,
        Func<int, bool>? isProcessAlive = null,
        string? homeDirectory = null,
        string? runtimeKey = null,
        HttpClient? downloadHttpClient = null)
    {
        _manifest = manifest ?? MobileCanvasCompanionManifest.LoadEmbedded();
        // Two clients, because the two conversations have opposite redirect requirements and one
        // client cannot hold both.
        //
        // Downloads: GitHub release assets redirect to their object store, so redirects are
        // followed, but only a bounded number of them, and .NET refuses an HTTPS→HTTP downgrade.
        // Content substitution is impossible regardless: every file is pinned by sha256 and size,
        // compressed and decompressed, and a mismatch fails the install rather than degrading it.
        // These requests carry no credential.
        _downloadHttpClient = downloadHttpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        });
        // Control: every request carries the host's bearer token to a loopback address. .NET strips
        // Authorization only when a redirect crosses hosts, so a redirect to another *port* on
        // 127.0.0.1 — which any local process can listen on — would hand the token to it. There is
        // no legitimate redirect on this path at all, so none is followed; a 3xx is simply a
        // non-success response and the operation fails with the token still unsent.
        _controlHttpClient = controlHttpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        });
        _downloader = downloader ?? new MobileCanvasAssetDownloader(_downloadHttpClient);
        _processRunner = processRunner ?? new MobileCanvasCompanionProcessRunner();
        _stateProvider = stateProvider ?? MobileCanvasHost.TryRead;
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
        _runtimeRoot = Path.Combine(homeDirectory ?? MobileCanvasHost.HomeDirectory, "runtimes");
        _runtimeKeyOverride = runtimeKey;
        ValidateManifest(_manifest);
    }

    public async Task<MobileCanvasCompanionStatus> GetStatusAsync(
        bool probeHost = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveRuntime(out var runtimeKey, out var runtime, out var unsupportedReason))
        {
            return new MobileCanvasCompanionStatus
            {
                Supported = false,
                Version = _manifest.Version,
                Reason = unsupportedReason,
            };
        }

        var installDirectory = GetInstallDirectory(runtimeKey, runtime);
        var executablePath = Path.Combine(installDirectory, runtime.Executable);
        var validation = await ValidateInstallationAsync(
            installDirectory,
            runtime,
            cancellationToken).ConfigureAwait(false);
        var state = _stateProvider();
        var compatibleState = MobileCanvasHost.IsTrustedForControl(state);
        var running = state is not null && compatibleState &&
            (!probeHost || await ProbeHostAsync(state, cancellationToken).ConfigureAwait(false));

        return new MobileCanvasCompanionStatus
        {
            Supported = true,
            Version = _manifest.Version,
            RuntimeKey = runtimeKey,
            RuntimeId = runtime.Id,
            InstallDirectory = installDirectory,
            ExecutablePath = executablePath,
            Installed = Directory.Exists(installDirectory),
            IntegrityValid = validation.Valid,
            Running = running,
            HostRegistered = state is not null,
            HostVersion = state?.Version,
            Reason = validation.Reason ??
                (state is not null && !compatibleState
                    ? $"The registered host is incompatible; version {MobileCanvasProtocol.ValidatedHostVersion} or newer is required."
                    : state is not null && !running
                        ? "The registered host is not responding."
                        : null),
        };
    }

    public async Task<MobileCanvasCompanionOperationResult> InstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveRuntime(out var runtimeKey, out var runtime, out var unsupportedReason))
            return Failure("install", unsupportedReason!);

        var installDirectory = GetInstallDirectory(runtimeKey, runtime);
        var existing = await ValidateInstallationAsync(
            installDirectory,
            runtime,
            cancellationToken).ConfigureAwait(false);
        if (existing.Valid)
        {
            return Success(
                "install",
                $"Mobile Canvas {_manifest.Version} is already installed.",
                await GetStatusAsync(probeHost: false, cancellationToken).ConfigureAwait(false));
        }

        Directory.CreateDirectory(_runtimeRoot);
        var staging = Path.Combine(_runtimeRoot, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        SetOwnerOnlyDirectory(staging);

        try
        {
            foreach (var (fileName, file) in runtime.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                ValidateFileName(fileName, nameof(fileName));
                ValidateFileName(file.Asset, nameof(file.Asset));

                var archivePath = Path.Combine(staging, $".{file.Asset}");
                var destination = Path.Combine(staging, fileName);
                var source = BuildReleaseAssetUri(file.Asset);

                await _downloader.DownloadAsync(
                    source,
                    archivePath,
                    file.CompressedSize,
                    cancellationToken).ConfigureAwait(false);
                await VerifyFileAsync(
                    archivePath,
                    file.CompressedSize,
                    file.CompressedSha256,
                    cancellationToken).ConfigureAwait(false);
                await DecompressAsync(
                    archivePath,
                    destination,
                    file.Size,
                    file.Sha256,
                    cancellationToken).ConfigureAwait(false);
                File.Delete(archivePath);
                SetOwnerExecutable(destination);
            }

            var receipt = new MobileCanvasInstallationReceipt
            {
                Version = _manifest.Version,
                RuntimeKey = runtimeKey,
                RuntimeId = runtime.Id,
                ValidatedRevision = _manifest.ValidatedRevision,
                InstalledAt = DateTimeOffset.UtcNow,
            };
            await File.WriteAllTextAsync(
                Path.Combine(staging, ReceiptFileName),
                JsonSerializer.Serialize(
                    receipt,
                    MobileCanvasCompanionJsonContext.Default.MobileCanvasInstallationReceipt),
                cancellationToken).ConfigureAwait(false);

            var staged = await ValidateInstallationAsync(staging, runtime, cancellationToken).ConfigureAwait(false);
            if (!staged.Valid)
                throw new InvalidDataException(staged.Reason ?? "The staged Mobile Canvas runtime is invalid.");

            ReplaceInstallation(staging, installDirectory);

            return Success(
                "install",
                $"Installed Mobile Canvas {_manifest.Version} for {runtime.Rid}.",
                await GetStatusAsync(probeHost: false, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Failure("install", ex.Message);
        }
        finally
        {
            DeleteDirectoryQuietly(staging);
        }
    }

    public async Task<MobileCanvasCompanionOperationResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(probeHost: true, cancellationToken).ConfigureAwait(false);
        if (!status.Supported)
            return Failure("start", status.Reason ?? "This platform is not supported.");
        if (!status.Installed || !status.IntegrityValid || string.IsNullOrWhiteSpace(status.ExecutablePath))
            return Failure("start", status.Reason ?? "Install the pinned Mobile Canvas runtime first.");
        if (status.Running)
            return Success("start", "Mobile Canvas is already running.", status);

        var result = _processRunner.Start(
            status.ExecutablePath,
            ["host", "start", "--json"],
            cancellationToken);
        if (!result.Success)
            return Failure("start", result.Error ?? "The Mobile Canvas start process did not launch.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HostStartTimeout);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var state = _stateProvider();
                if (MobileCanvasHost.IsTrustedForControl(state) &&
                    await ProbeHostAsync(state!, timeout.Token).ConfigureAwait(false))
                {
                    return Success(
                        "start",
                        "Mobile Canvas started.",
                        await GetStatusAsync(probeHost: false, cancellationToken).ConfigureAwait(false));
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return Failure("start", "Mobile Canvas did not publish a responsive host before the timeout.");
    }

    public async Task<MobileCanvasCompanionOperationResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        var state = _stateProvider();
        if (state is null)
        {
            return Success(
                "stop",
                "Mobile Canvas is already stopped.",
                await GetStatusAsync(probeHost: false, cancellationToken).ConfigureAwait(false));
        }
        if (!MobileCanvasHost.IsTrustedForControl(state))
        {
            return Failure(
                "stop",
                "The registered Mobile Canvas state is legacy or incompatible and will not be used for control. "
                + "Replace it with the pinned companion.");
        }
        if (state.ProcessId <= 0)
            return Failure("stop", "The registered Mobile Canvas host did not publish a process ID, so shutdown cannot be verified.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{state.BaseUrl}/api/v1/host/stop");
        if (!string.IsNullOrWhiteSpace(state.ControlToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.ControlToken);

        try
        {
            using var response = await _controlHttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode))
            {
                return Failure(
                    "stop",
                    "The registered Mobile Canvas host answered shutdown with a redirect, which is never followed on "
                    + "the control path. Replace the registered host with the pinned companion.");
            }
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    "stop",
                    $"The Mobile Canvas host refused shutdown with HTTP {(int)response.StatusCode}.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(HostStopTimeout);
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    if (!_isProcessAlive(state.ProcessId))
                        return Success("stop", "Mobile Canvas stopped.");

                    await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return Failure("stop", "Mobile Canvas accepted shutdown but did not exit before the timeout.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failure("stop", "The registered Mobile Canvas host is not responding.");
        }
    }

    public async Task<MobileCanvasCompanionOperationResult> UpdateAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await InstallAsync(cancellationToken).ConfigureAwait(false);
        return result with
        {
            Action = "update",
            Message = result.Success
                ? $"Mobile Canvas is current at {_manifest.Version}."
                : result.Message,
        };
    }

    /// <summary>Runs the companion's separate MCP server through DevFlow's lease proxy.</summary>
    public async Task<MobileCanvasMcpRunResult> RunMcpAsync(
        CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(probeHost: false, cancellationToken).ConfigureAwait(false);
        if (!status.Supported || !status.Installed || !status.IntegrityValid ||
            string.IsNullOrWhiteSpace(status.ExecutablePath))
        {
            return new MobileCanvasMcpRunResult(
                false,
                1,
                status.Reason ?? "Install the pinned Mobile Canvas runtime first.");
        }

        var brokerPort = await BrokerClient.EnsureBrokerRunningAsync().ConfigureAwait(false);
        if (brokerPort is null)
            return new MobileCanvasMcpRunResult(false, 1, "The DevFlow broker could not be started for device lease coordination.");

        return await MobileCanvasMcpProxy.RunProcessAsync(
            status.ExecutablePath,
            brokerPort.Value,
            cancellationToken).ConfigureAwait(false);
    }

    internal string? GetExecutablePath()
    {
        if (!TryResolveRuntime(out var runtimeKey, out var runtime, out _))
            return null;
        return Path.Combine(GetInstallDirectory(runtimeKey, runtime), runtime.Executable);
    }

    private bool TryResolveRuntime(
        out string runtimeKey,
        out MobileCanvasCompanionRuntime runtime,
        out string? reason)
    {
        runtimeKey = _runtimeKeyOverride ?? CurrentRuntimeKey() ?? "";
        runtime = null!;
        if (runtimeKey.Length == 0 ||
            !_manifest.Runtimes.TryGetValue(runtimeKey, out var resolved) ||
            resolved is null)
        {
            reason = $"Mobile Canvas {_manifest.Version} does not provide a runtime for this operating system and architecture.";
            return false;
        }

        runtime = resolved;
        reason = null;
        return true;
    }

    private string GetInstallDirectory(string runtimeKey, MobileCanvasCompanionRuntime runtime) =>
        Path.Combine(_runtimeRoot, $"{runtimeKey}-{runtime.Id[..12]}");

    private Uri BuildReleaseAssetUri(string asset) =>
        new($"https://github.com/{_manifest.Repository}/releases/download/{_manifest.Tag}/{asset}");

    private async Task<(bool Valid, string? Reason)> ValidateInstallationAsync(
        string installDirectory,
        MobileCanvasCompanionRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(installDirectory))
            return (false, "The pinned Mobile Canvas runtime is not installed.");

        try
        {
            foreach (var (fileName, file) in runtime.Files)
            {
                var path = Path.Combine(installDirectory, fileName);
                if (!File.Exists(path))
                    return (false, $"The Mobile Canvas installation is missing '{fileName}'.");
                await VerifyFileAsync(path, file.Size, file.Sha256, cancellationToken).ConfigureAwait(false);
            }

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, ex.Message);
        }
    }

    private async Task<bool> ProbeHostAsync(
        MobileCanvasHostState state,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HostProbeTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{state.BaseUrl}/api/v1/status");
        if (!string.IsNullOrWhiteSpace(state.ControlToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", state.ControlToken);

        try
        {
            using var response = await _controlHttpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
            // A redirect is never a live host. Treating it as "not responding" is also what keeps a
            // caller from retrying the probe against whatever the Location header names.
            return !IsRedirect(response.StatusCode) && response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether a status code is a redirect. Control requests carry the host's bearer token to a
    /// loopback address, and .NET only strips <c>Authorization</c> when a redirect changes host —
    /// a redirect to a different <em>port</em> on 127.0.0.1 keeps it. Any local process can listen
    /// on a port, so no redirect is followed on this path and every one is a refusal.
    /// </summary>
    internal static bool IsRedirect(System.Net.HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and <= 399;

    private static async Task VerifyFileAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != expectedSize)
        {
            throw new InvalidDataException(
                $"'{info.Name}' has size {info.Length}, expected {expectedSize}.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
            .ToLowerInvariant();
        if (!string.Equals(digest, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException($"'{info.Name}' failed SHA-256 verification.");
    }

    private static async Task DecompressAsync(
        string archivePath,
        string destination,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        await using var archive = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: false);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            var count = await gzip.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;

            written += count;
            if (written > expectedSize)
                throw new InvalidDataException("The decompressed Mobile Canvas asset exceeded its pinned size.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }

        if (written != expectedSize)
        {
            throw new InvalidDataException(
                $"The decompressed Mobile Canvas asset size was {written}, expected {expectedSize}.");
        }

        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(digest, expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The decompressed Mobile Canvas asset failed SHA-256 verification.");
    }

    private static void ReplaceInstallation(string staging, string destination)
    {
        string? backup = null;
        try
        {
            if (Directory.Exists(destination))
            {
                backup = $"{destination}.replaced-{Guid.NewGuid():N}";
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
            if (backup is not null)
                DeleteDirectoryQuietly(backup);
        }
        catch
        {
            if (!Directory.Exists(destination) && backup is not null && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }
    }

    private static void ValidateManifest(MobileCanvasCompanionManifest manifest)
    {
        if (manifest.Schema != 1 ||
            !string.Equals(manifest.Version, MobileCanvasProtocol.ValidatedHostVersion, StringComparison.Ordinal) ||
            !string.Equals(manifest.ValidatedRevision, MobileCanvasProtocol.ValidatedHostRevision, StringComparison.Ordinal) ||
            manifest.Runtimes.Count == 0)
        {
            throw new InvalidDataException("The embedded Mobile Canvas compatibility manifest is inconsistent.");
        }

        foreach (var (runtimeKey, runtime) in manifest.Runtimes)
        {
            ValidateFileName(runtimeKey, nameof(runtimeKey));
            ValidateFileName(runtime.Executable, nameof(runtime.Executable));
            if (!IsLowercaseSha256(runtime.Id) || runtime.Files.Count == 0)
                throw new InvalidDataException($"Mobile Canvas runtime '{runtimeKey}' is invalid.");

            foreach (var (fileName, file) in runtime.Files)
            {
                ValidateFileName(fileName, nameof(fileName));
                ValidateFileName(file.Asset, nameof(file.Asset));
                if (file.Size <= 0 || file.CompressedSize <= 0 ||
                    !IsLowercaseSha256(file.Sha256) || !IsLowercaseSha256(file.CompressedSha256))
                {
                    throw new InvalidDataException($"Mobile Canvas runtime file '{fileName}' is invalid.");
                }
            }
        }
    }

    /// <summary>
    /// Whether a value is a lowercase 64-character hex SHA-256, exactly as the JavaScript loader in
    /// <c>lib/runtime.mjs</c> requires with <c>/^[a-f0-9]{64}$/</c>.
    /// <para>
    /// The two loaders resolve the same install directory from <c>runtime.id[..12]</c> and compare
    /// digests with <see cref="StringComparison.Ordinal"/>. A length-only check would let an
    /// uppercase or non-hex id through on the C# side, which then either points at a different
    /// directory than the JS side or fails every comparison against a digest DevFlow itself
    /// lowercases. Matching the stricter rule keeps the two implementations describing one install.
    /// </para>
    /// </summary>
    internal static bool IsLowercaseSha256(string? value)
    {
        if (value is not { Length: 64 })
            return false;
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    private static void ValidateFileName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
            value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new InvalidDataException($"The Mobile Canvas manifest contains an invalid {parameterName}.");
        }
    }

    private static string? CurrentRuntimeKey()
    {
        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "win32"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "darwin"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "linux"
                    : null;
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return platform is null || architecture is null ? null : $"{platform}-{architecture}";
    }

    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0)
            return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void SetOwnerOnlyDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void SetOwnerExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void DeleteDirectoryQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static MobileCanvasCompanionOperationResult Success(
        string action,
        string message,
        MobileCanvasCompanionStatus? status = null) =>
        new() { Success = true, Action = action, Message = message, Status = status };

    private static MobileCanvasCompanionOperationResult Failure(string action, string message) =>
        new() { Success = false, Action = action, Message = message };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MobileCanvasCompanionManifest))]
[JsonSerializable(typeof(MobileCanvasInstallationReceipt))]
[JsonSerializable(typeof(MobileCanvasCompanionStatus))]
[JsonSerializable(typeof(MobileCanvasCompanionOperationResult))]
[JsonSerializable(typeof(MobileCanvasMcpRunResult))]
internal sealed partial class MobileCanvasCompanionJsonContext : JsonSerializerContext;
