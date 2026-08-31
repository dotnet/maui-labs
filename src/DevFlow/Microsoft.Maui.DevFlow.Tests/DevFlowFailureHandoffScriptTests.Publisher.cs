using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers <c>eng/devflow/Publish-DevFlowFailureIssue.ps1</c> against archives produced by the real
/// handoff producer. The publisher is the only component that holds a repository token, so the
/// tests here assert what it refuses to do with one as firmly as what it verifies.
/// </summary>
public sealed partial class DevFlowFailureHandoffScriptTests
{
    private const string DefaultBranch = HeadRef;
    private const string TokenSentinel = "PUBLISHER-TOKEN-SENTINEL-MUST-NOT-LEAVE-THIS-PROCESS";

    [Fact]
    public async Task Publisher_VerifyOnly_VerifiesAProducedHandoffWithoutAToken()
    {
        var inputs = CreateInputs("publish-verified");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-verified");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        var result = await RunPublisherAsync(
            ArchivePath("publish-verified"),
            verifyOnly: true,
            gitHubToken: string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("verified", result.Json.GetProperty("status").GetString());
        Assert.Equal("qualified-failure", result.Json.GetProperty("reason").GetString());
        Assert.StartsWith(
            "sha256:",
            result.Json.GetProperty("fingerprint").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publisher_UntrustedSourceBranch_VerifiesAsDiagnosticOnly()
    {
        var inputs = CreateInputs("publish-diagnostic");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-diagnostic");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        var result = await RunPublisherAsync(
            ArchivePath("publish-diagnostic"),
            verifyOnly: true,
            gitHubToken: string.Empty,
            defaultBranch: "release/never-published");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("verified-diagnostic-only", result.Json.GetProperty("status").GetString());
        Assert.Equal("source-head-ref-untrusted", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Publisher_PublishingModeWithoutAToken_IsIgnoredAsUnverifiable()
    {
        var inputs = CreateInputs("publish-no-token");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-no-token");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        var result = await RunPublisherAsync(
            ArchivePath("publish-no-token"),
            verifyOnly: false,
            gitHubToken: string.Empty);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ignored-unverifiable", result.Json.GetProperty("status").GetString());
        Assert.Equal("github-token-missing", result.Json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Publisher_LoopbackTestApiInPublishingMode_IsRefusedAndNeverContactsTheListener()
    {
        var inputs = CreateInputs("publish-loopback");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-loopback");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        using var listener = new LoopbackListener();
        var result = await RunPublisherAsync(
            ArchivePath("publish-loopback"),
            verifyOnly: false,
            gitHubToken: TokenSentinel,
            apiBaseUrl: listener.BaseUrl,
            allowLoopbackTestApi: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ignored-unverifiable", result.Json.GetProperty("status").GetString());
        Assert.Equal("loopback-test-api-requires-verify-only", result.Json.GetProperty("reason").GetString());
        Assert.Equal(0, listener.AcceptedConnections);
        Assert.DoesNotContain(TokenSentinel, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSentinel, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publisher_LoopbackTestApiWithVerifyOnly_VerifiesWithoutContactingTheListener()
    {
        var inputs = CreateInputs("publish-loopback-verify");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-loopback-verify");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        using var listener = new LoopbackListener();
        var result = await RunPublisherAsync(
            ArchivePath("publish-loopback-verify"),
            verifyOnly: true,
            gitHubToken: TokenSentinel,
            apiBaseUrl: listener.BaseUrl,
            allowLoopbackTestApi: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("verified", result.Json.GetProperty("status").GetString());
        Assert.Equal(0, listener.AcceptedConnections);
        Assert.DoesNotContain(TokenSentinel, result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publisher_LoopbackApiWithoutTheOptIn_IsRefusedAsUntrusted()
    {
        var inputs = CreateInputs("publish-loopback-optout");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-loopback-optout");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        using var listener = new LoopbackListener();
        var result = await RunPublisherAsync(
            ArchivePath("publish-loopback-optout"),
            verifyOnly: true,
            gitHubToken: TokenSentinel,
            apiBaseUrl: listener.BaseUrl,
            allowLoopbackTestApi: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ignored-unverifiable", result.Json.GetProperty("status").GetString());
        Assert.Equal("untrusted-github-api-base-url", result.Json.GetProperty("reason").GetString());
        Assert.Equal(0, listener.AcceptedConnections);
    }

    [Fact]
    public async Task Publisher_PassingHandoff_IsNeverPublished()
    {
        var inputs = CreateInputs("publish-pass", outcome: "passed");
        var producer = await RunProducerAsync(inputs.ManifestPath, inputs.QualificationPath, "publish-pass");
        Assert.Equal("skipped", producer.Json.GetProperty("status").GetString());
        Assert.False(File.Exists(ArchivePath("publish-pass")));
    }

    [Fact]
    public void Publisher_DeclaresItsMinimumPowerShellVersion()
    {
        var script = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Publish-DevFlowFailureIssue.ps1"));
        Assert.StartsWith("#Requires -Version 7.3", script, StringComparison.Ordinal);
        Assert.Contains("loopback-test-api-requires-verify-only", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The header builder is loaded from the shipped script source and asked to build a header for
    /// a sentinel credential. The comparison happens inside the probe so the assertion can prove
    /// the real credential is carried without ever printing it: a placeholder or a dropped scheme
    /// would send an unauthenticated request that GitHub answers with 401.
    /// </summary>
    [Fact]
    public async Task Publisher_HeaderBuilder_CarriesTheSuppliedCredentialAndOmitsItWhenAbsent()
    {
        const string probe = """
            param([string] $ScriptPath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -eq 'Get-GitHubHeaders'
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $token = $env:DEVFLOW_PUBLISHER_PROBE_TOKEN
            $withToken = Get-GitHubHeaders $token
            $withoutToken = Get-GitHubHeaders ''

            [ordered]@{
                carriesSuppliedCredential =
                    [string]::Equals($withToken['Authorization'], "Bearer $token", [StringComparison]::Ordinal)
                omitsCredentialWhenAbsent = -not $withoutToken.ContainsKey('Authorization')
                declaresApiVersion =
                    [string]::Equals($withToken['X-GitHub-Api-Version'], '2022-11-28', [StringComparison]::Ordinal)
                declaresJsonAccept =
                    [string]::Equals($withToken['Accept'], 'application/vnd.github+json', [StringComparison]::Ordinal)
            } | ConvertTo-Json -Compress
            """;

        var result = await RunProbeAsync(
            probe,
            new Dictionary<string, string?> { ["DEVFLOW_PUBLISHER_PROBE_TOKEN"] = TokenSentinel });

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain(TokenSentinel, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSentinel, result.StandardError, StringComparison.Ordinal);
        Assert.True(result.Json.GetProperty("carriesSuppliedCredential").GetBoolean());
        Assert.True(result.Json.GetProperty("omitsCredentialWhenAbsent").GetBoolean());
        Assert.True(result.Json.GetProperty("declaresApiVersion").GetBoolean());
        Assert.True(result.Json.GetProperty("declaresJsonAccept").GetBoolean());
    }

    /// <summary>
    /// The request the publisher would really put on the wire, observed by a loopback listener.
    /// The shipped header builder, URI builder, and request helper are loaded from the script
    /// source and driven against the listener, so the assertion is about what the publisher sends
    /// rather than about a re-implementation - and the publisher's own trust boundary is untouched:
    /// it still refuses to publish against a loopback API and still drops the token there.
    /// </summary>
    [Fact]
    public async Task Publisher_RequestBuilder_SendsTheHeldCredentialToTheEndpointItCalls()
    {
        using var api = new RecordingLoopbackApi(
            $$"""{"full_name":"{{Repository}}","default_branch":"{{DefaultBranch}}","has_issues":false}""");

        const string probe = """
            param([string] $ScriptPath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            $wanted = @('Get-GitHubHeaders', 'Get-GitHubUri', 'Invoke-GitHubJson')
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in $wanted
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $GitHubApiBaseUrl = $env:DEVFLOW_PUBLISHER_PROBE_BASE_URL
            $GitHubToken = $env:DEVFLOW_PUBLISHER_PROBE_TOKEN
            $response = Invoke-GitHubJson -Method GET -Path "/repos/$($env:DEVFLOW_PUBLISHER_PROBE_REPOSITORY)"

            [ordered]@{
                answered = [string]::Equals(
                    [string] $response.full_name,
                    $env:DEVFLOW_PUBLISHER_PROBE_REPOSITORY,
                    [StringComparison]::Ordinal)
            } | ConvertTo-Json -Compress
            """;

        var result = await RunProbeAsync(
            probe,
            new Dictionary<string, string?>
            {
                ["DEVFLOW_PUBLISHER_PROBE_TOKEN"] = TokenSentinel,
                ["DEVFLOW_PUBLISHER_PROBE_BASE_URL"] = api.BaseUrl,
                ["DEVFLOW_PUBLISHER_PROBE_REPOSITORY"] = Repository,
            });

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Json.GetProperty("answered").GetBoolean());

        var request = Assert.Single(api.Requests);
        Assert.Equal($"GET /repos/{Repository}", request.RequestLine);
        Assert.Equal($"Bearer {TokenSentinel}", request.Authorization);
        Assert.Equal("2022-11-28", request.ApiVersion);
        Assert.DoesNotContain(TokenSentinel, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSentinel, result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The loopback default is unconditional: no token is attached, and publishing mode is refused
    /// before a single connection is made. There is no opt-in that lets a local listener receive a
    /// repository token from the publisher itself.
    /// </summary>
    [Fact]
    public async Task Publisher_LoopbackTestApi_NeverSendsACredentialAndNeverPublishes()
    {
        var inputs = CreateInputs("publish-loopback-unauthorized");
        var producer = await RunProducerAsync(
            inputs.ManifestPath, inputs.QualificationPath, "publish-loopback-unauthorized");
        Assert.Equal("created", producer.Json.GetProperty("status").GetString());

        using var api = new RecordingLoopbackApi(
            $$"""{"full_name":"{{Repository}}","default_branch":"{{DefaultBranch}}","has_issues":true}""");
        var result = await RunPublisherAsync(
            ArchivePath("publish-loopback-unauthorized"),
            verifyOnly: false,
            gitHubToken: TokenSentinel,
            apiBaseUrl: api.BaseUrl,
            allowLoopbackTestApi: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ignored-unverifiable", result.Json.GetProperty("status").GetString());
        Assert.Equal("loopback-test-api-requires-verify-only", result.Json.GetProperty("reason").GetString());
        Assert.Empty(api.Requests);
        Assert.DoesNotContain(TokenSentinel, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSentinel, result.StandardError, StringComparison.Ordinal);

        var script = File.ReadAllText(
            Path.Combine(_repositoryRoot, "eng", "devflow", "Publish-DevFlowFailureIssue.ps1"));
        Assert.DoesNotContain("LOOPBACK_TEST_AUTHORIZATION", script, StringComparison.Ordinal);
    }

    private async Task<PublisherResult> RunPublisherAsync(
        string archivePath,
        bool verifyOnly,
        string gitHubToken,
        string? apiBaseUrl = null,
        bool allowLoopbackTestApi = false,
        string sourceEvent = SourceEvent,
        int pullRequestNumber = PullRequestNumber,
        string workflowConclusion = "failure",
        string defaultBranch = DefaultBranch)
    {
        var script = Path.Combine(_repositoryRoot, "eng", "devflow", "Publish-DevFlowFailureIssue.ps1");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
            {
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.Environment["GITHUB_TOKEN"] = string.Empty;
        process.StartInfo.Environment["DEVFLOW_PUBLISHER_ALLOW_LOOPBACK_TEST_API"] =
            allowLoopbackTestApi ? "1" : string.Empty;

        AddArgument("-NoLogo");
        AddArgument("-NoProfile");
        AddArgument("-File");
        AddArgument(script);
        AddArgument("-Repository");
        AddArgument(Repository);
        AddArgument("-WorkflowName");
        AddArgument(WorkflowName);
        AddArgument("-WorkflowPath");
        AddArgument(WorkflowPath);
        AddArgument("-SourceEvent");
        AddArgument(sourceEvent);
        AddArgument("-HeadRepository");
        AddArgument(Repository);
        AddArgument("-HeadRef");
        AddArgument(HeadRef);
        AddArgument("-DefaultBranch");
        AddArgument(defaultBranch);
        AddArgument("-WorkflowConclusion");
        AddArgument(workflowConclusion);
        AddArgument("-RunId");
        AddArgument(RunId.ToString());
        AddArgument("-RunAttempt");
        AddArgument(RunAttempt.ToString());
        AddArgument("-CommitSha");
        AddArgument(CommitSha);
        AddArgument("-PullRequestNumber");
        AddArgument(pullRequestNumber.ToString());
        AddArgument("-ArchivePath");
        AddArgument(archivePath);
        AddArgument("-GitHubToken");
        AddArgument(gitHubToken);
        if (apiBaseUrl is not null)
        {
            AddArgument("-GitHubApiBaseUrl");
            AddArgument(apiBaseUrl);
        }
        if (verifyOnly)
            AddArgument("-VerifyOnly");

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which is exactly what a chatty failure would do.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        await process.WaitForExitAsync();

        var jsonLine = stdout.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static line => line.StartsWith("{", StringComparison.Ordinal));
        Assert.True(jsonLine is not null, $"No JSON result. stdout={stdout}; stderr={stderr}");
        using var document = JsonDocument.Parse(jsonLine!);
        return new PublisherResult(process.ExitCode, stdout, stderr, document.RootElement.Clone());

        void AddArgument(string value) => process.StartInfo.ArgumentList.Add(value);
    }

    private async Task<PublisherResult> RunProbeAsync(
        string probe,
        IReadOnlyDictionary<string, string?> environment)
    {
        var directory = Path.Combine(
            _repositoryRoot,
            "artifacts",
            "TestResults",
            "devflow-publisher-probe",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, "publisher-probe.ps1");
        try
        {
            File.WriteAllText(probePath, probe, new System.Text.UTF8Encoding(false));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")
                {
                    WorkingDirectory = _repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            foreach (var (key, value) in environment)
                process.StartInfo.Environment[key] = value;
            foreach (var argument in new[]
                     {
                         "-NoLogo",
                         "-NoProfile",
                         "-File",
                         probePath,
                         "-ScriptPath",
                         Path.Combine(_repositoryRoot, "eng", "devflow", "Publish-DevFlowFailureIssue.ps1"),
                     })
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(stdoutTask, stderrTask);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            await process.WaitForExitAsync();

            var jsonLine = stdout.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(static line => line.StartsWith("{", StringComparison.Ordinal));
            Assert.True(jsonLine is not null, $"No JSON probe result. stdout={stdout}; stderr={stderr}");
            using var document = JsonDocument.Parse(jsonLine!);
            return new PublisherResult(process.ExitCode, stdout, stderr, document.RootElement.Clone());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record PublisherResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        JsonElement Json);

    /// <summary>
    /// A loopback socket that accepts nothing and only counts arrivals. A publisher that forwarded
    /// a repository token to a test double would have to connect first, so an accepted connection
    /// is itself the failure.
    /// </summary>
    private sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener _listener;
        private int _accepted;

        public LoopbackListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public string BaseUrl => $"http://127.0.0.1:{Port}";

        public int AcceptedConnections
        {
            get
            {
                while (_listener.Pending())
                {
                    using var client = _listener.AcceptTcpClient();
                    _accepted++;
                }

                return _accepted;
            }
        }

        public void Dispose() => _listener.Stop();
    }

    /// <summary>
    /// A minimal loopback HTTP endpoint that records the request line and the credential headers it
    /// was sent, then answers one canned JSON document. It exists so a test can assert the header
    /// the publisher really builds without a network, a token store, or a live GitHub API.
    /// </summary>
    private sealed class RecordingLoopbackApi : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<RecordedRequest> _requests = new();
        private readonly Task _acceptLoop;
        private readonly byte[] _response;

        public RecordingLoopbackApi(string responseBody)
        {
            _response = System.Text.Encoding.UTF8.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {System.Text.Encoding.UTF8.GetByteCount(responseBody)}\r\n" +
                "Connection: close\r\n" +
                "\r\n" +
                responseBody);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint) _listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptAsync);
        }

        public int Port { get; }

        public string BaseUrl => $"http://127.0.0.1:{Port}";

        public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

        public void Dispose()
        {
            _cancellation.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // The loop is torn down with the listener; a cancelled accept is expected.
            }

            _cancellation.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
                }
                catch (Exception) when (_cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }

                using (client)
                using (var stream = client.GetStream())
                {
                    var header = await ReadHeaderAsync(stream);
                    _requests.Enqueue(RecordedRequest.Parse(header));
                    await stream.WriteAsync(_response, _cancellation.Token);
                    await stream.FlushAsync(_cancellation.Token);
                }
            }
        }

        private async Task<string> ReadHeaderAsync(NetworkStream stream)
        {
            var buffer = new byte[1];
            var builder = new System.Text.StringBuilder();
            while (builder.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer, _cancellation.Token);
                if (read == 0)
                    break;
                builder.Append((char) buffer[0]);
                if (builder.Length >= 4 &&
                    builder[^4] == '\r' && builder[^3] == '\n' &&
                    builder[^2] == '\r' && builder[^1] == '\n')
                {
                    break;
                }
            }

            return builder.ToString();
        }
    }

    private sealed record RecordedRequest(string RequestLine, string? Authorization, string? ApiVersion)
    {
        public static RecordedRequest Parse(string header)
        {
            var lines = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var requestLine = lines.Length > 0 ? lines[0] : string.Empty;
            var target = requestLine.Split(' ');
            var normalized = target.Length >= 2 ? $"{target[0]} {target[1]}" : requestLine;
            return new RecordedRequest(
                normalized,
                HeaderValue(lines, "Authorization"),
                HeaderValue(lines, "X-GitHub-Api-Version"));
        }

        private static string? HeaderValue(IEnumerable<string> lines, string name)
            => lines
                .Skip(1)
                .Where(line => line.StartsWith($"{name}:", StringComparison.OrdinalIgnoreCase))
                .Select(line => line[(name.Length + 1)..].Trim())
                .FirstOrDefault();
    }
}
