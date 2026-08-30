using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Security and correctness tests for the <c>.mauitrace</c> evidence bundle.
///
/// These pin the privacy contract (default-deny projection, redaction at ingestion), the atomic
/// write behaviour, the hostile-input reader, and the regenerated HTML report. A regression in any
/// of them would leak app data off a developer's machine or execute untrusted content, so they are
/// intentionally exhaustive.
/// </summary>
public class EvidenceCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory, "evidence-tests", Guid.NewGuid().ToString("N"));

    public EvidenceCaptureTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    // ── redaction ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("token=eyJhbGciOi.eyJzdWIiOi.SflKxwRJSM", "token=<jwt>")]
    [InlineData("{\"apiKey\":\"supersecret12345\"}", "{\"apiKey\":\"<redacted>\"}")]
    [InlineData("{\"access_token\":\"xyz\",\"foo\":\"bar\"}", "{\"access_token\":\"<redacted>\",\"foo\":\"bar\"}")]
    public void MaskSecrets_RedactsKnownSecretShapes(string input, string expected)
        => Assert.Equal(expected, EvidenceRedaction.MaskSecrets(input));

    [Fact]
    public void Scrub_RedactsBearerTokensAndSecretAssignments()
    {
        var scrubbed = EvidenceRedaction.Scrub(
            "auth failed: Bearer abcdefghijklmnop and api_key=zzz-9999 for user alice",
            EvidenceFormat.MaxLogMessageChars)!;

        Assert.DoesNotContain("abcdefghijklmnop", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("zzz-9999", scrubbed, StringComparison.Ordinal);
        Assert.Contains("<redacted>", scrubbed, StringComparison.Ordinal);
        Assert.Contains("alice", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"failed to load C:\Users\alice\src\App\MainPage.xaml", "MainPage.xaml", @"C:\Users\alice")]
    [InlineData("failed to load /Users/alice/src/App/MainPage.xaml", "MainPage.xaml", "/Users/alice")]
    [InlineData("failed to load file:///home/alice/App/MainPage.xaml", "MainPage.xaml", "/home/alice")]
    [InlineData(@"probing \\build-share\drops\team\Alice\app.dll", "app.dll", "build-share")]
    [InlineData("wsl load /mnt/c/Users/alice/src/App/MainPage.xaml", "MainPage.xaml", "alice")]
    [InlineData("ci load /workspace/acme/private-project/MainPage.xaml", "MainPage.xaml", "private-project")]
    [InlineData("gitlab load /builds/acme/private-project/MainPage.xaml", "MainPage.xaml", "private-project")]
    [InlineData("github load /github/workspace/private-project/MainPage.xaml", "MainPage.xaml", "private-project")]
    public void Scrub_ReplacesAbsolutePathsWithFileName(string input, string keeps, string drops)
    {
        var scrubbed = EvidenceRedaction.Scrub(input, EvidenceFormat.MaxLogMessageChars)!;

        Assert.Contains(keeps, scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain(drops, scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrub_DropsControlCharactersAndTruncates()
    {
        var scrubbed = EvidenceRedaction.Scrub("a\u0007b" + new string('x', 50), 20)!;

        Assert.DoesNotContain('\u0007', scrubbed);
        Assert.StartsWith("ab", scrubbed, StringComparison.Ordinal);
        Assert.EndsWith("[truncated]", scrubbed, StringComparison.Ordinal);
        Assert.True(scrubbed.Length <= 20);
    }

    [Fact]
    public void Scrub_DropsUnicodeFormatCharacters()
    {
        var scrubbed = EvidenceRedaction.Scrub("ab\u202Ecd\u200Bef", 20)!;

        Assert.Equal("abcdef", scrubbed);
    }

    [Theory]
    [InlineData("sk-live-AAAABBBBCCCCDDDDEEEEFFFF0000")]
    [InlineData("sk" + "_live_" + "AAAABBBBCCCC" + "DDDDEEEEFFFF0000")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("xoxb" + "-1234567890" + "12-abcdefghijklmnop")]
    [InlineData("https://alice:password@example.com/path")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIESECRET\n-----END RSA PRIVATE KEY-----")]
    [InlineData("private_key=super-secret-value")]
    [InlineData("pwd=super-secret-value")]
    [InlineData("cookie=session-value")]
    public void Scrub_MasksCommonBareSecretShapes(string secret)
    {
        var scrubbed = EvidenceRedaction.Scrub(secret, EvidenceFormat.MaxLogMessageChars)!;

        Assert.DoesNotContain(secret, scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeSourcePath_UsesProjectRelativeFormInsideTheProject()
    {
        var projectRoot = Path.Combine(_root, "MyApp");
        var file = Path.Combine(projectRoot, "Views", "MainPage.xaml");

        Assert.Equal("Views/MainPage.xaml", EvidenceRedaction.NormalizeSourcePath(file, projectRoot));
    }

    [Fact]
    public void NormalizeSourcePath_FallsBackToFileNameOutsideTheProject()
    {
        var projectRoot = Path.Combine(_root, "MyApp");
        var outside = OperatingSystem.IsWindows()
            ? @"D:\shared\packages\Other\Page.xaml"
            : "/opt/shared/packages/Other/Page.xaml";

        Assert.Equal("Page.xaml", EvidenceRedaction.NormalizeSourcePath(outside, projectRoot));
    }

    [Theory]
    [InlineData("Views/MainPage.xaml", "Views/MainPage.xaml")]
    [InlineData(@"Views\MainPage.xaml", "Views/MainPage.xaml")]
    [InlineData("../../secrets/MainPage.xaml", "MainPage.xaml")]
    public void NormalizeSourcePath_KeepsRelativePathsButRejectsTraversal(string input, string expected)
        => Assert.Equal(expected, EvidenceRedaction.NormalizeSourcePath(input, projectRoot: null));

    [Fact]
    public void SafeIdentifier_DoesNotRetainAbsolutePaths()
    {
        var identifier = EvidenceRedaction.SafeIdentifier(
            @"control-C:\Users\alice\src\App\MainPage.xaml");

        Assert.DoesNotContain(@"C:\Users\alice", identifier, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MainPage.xaml", identifier, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrubRoute_RemovesAllQueryValuesAndFragments()
    {
        var route = EvidenceRedaction.ScrubRoute(
            "//orders/detail?customer=alice@example.com&access_token=secret#receipt");

        Assert.Equal("//orders/detail?customer=<redacted>&access_token=<redacted>", route);
        Assert.DoesNotContain("alice@example.com", route, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", route, StringComparison.Ordinal);
        Assert.DoesNotContain("receipt", route, StringComparison.Ordinal);
    }

    // ── projections ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProjectTree_OmitsTextValuesAndNativeProperties()
    {
        var projectRoot = Path.Combine(_root, "MyApp");
        var tree = new List<ElementInfo>
        {
            new()
            {
                Id = "e1",
                Type = "Entry",
                AutomationId = "PasswordField",
                Text = "hunter2-secret-text",
                Value = "value-secret",
                NativeType = "UITextField",
                NativeProperties = new Dictionary<string, string?> { ["placeholder"] = "native-secret" },
                FrameworkProperties = new Dictionary<string, string?> { ["BindingContext"] = "framework-secret" },
                Bounds = new BoundsInfo { X = 1, Y = 2, Width = 3, Height = 4 },
                SourceFile = Path.Combine(projectRoot, "Views", "LoginPage.xaml"),
                SourceLine = 12,
                SourceColumn = 5,
                SourceHash = "abc123",
                IsVisible = true,
                IsEnabled = true,
            },
        };

        var document = EvidenceBuilder.ProjectTree(tree, projectRoot);
        var json = EvidenceJson.Serialize(document);

        Assert.Equal(1, document.Count);
        foreach (var secret in new[] { "hunter2-secret-text", "value-secret", "native-secret", "framework-secret" })
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(projectRoot, json, StringComparison.Ordinal);

        var node = document.Roots[0];
        Assert.Equal("Entry", node.Type);
        Assert.Equal("PasswordField", node.AutomationId);
        Assert.Equal("abc123", node.SourceHash);
        Assert.Equal("Views/LoginPage.xaml", node.SourceFile);
        Assert.Equal(12, node.SourceLine);
        Assert.NotNull(node.Bounds);
    }

    [Fact]
    public void ProjectProblems_DropsRawMessagesWithArbitraryRejectedValues()
    {
        const string secret = "CorrectHorseBatteryStaple!";
        var projected = EvidenceBuilder.ProjectProblems(
            new DiagnosticProblemBatch
            {
                Enabled = true,
                Revision = 1,
                Count = 1,
                Problems =
                [
                    new DiagnosticProblem
                    {
                        Id = "p1",
                        Kind = "binding",
                        Code = "conversion",
                        Message = $"'{secret}' cannot be converted to System.Double",
                        Count = 1,
                        BindingPath = "SecretNumber",
                        ElementType = "Microsoft.Maui.Controls.Slider",
                        Property = "Value"
                    }
                ]
            },
            projectRoot: null);

        var problem = Assert.Single(projected.Problems);
        Assert.DoesNotContain(secret, problem.Message, StringComparison.Ordinal);
        Assert.Contains("SecretNumber", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectTree_StopsAtTheElementBudget()
    {
        var roots = new List<ElementInfo>();
        for (var i = 0; i < EvidenceFormat.MaxTreeElements + 25; i++)
            roots.Add(new ElementInfo { Id = $"e{i}", Type = "Label" });

        var document = EvidenceBuilder.ProjectTree(roots, projectRoot: null);

        Assert.True(document.Truncated);
        Assert.Equal(EvidenceFormat.MaxTreeElements, document.Count);
    }

    [Fact]
    public void ProjectNetwork_KeepsSummaryMetadataOnly()
    {
        var requests = new List<NetworkRequest>
        {
            new()
            {
                Id = "r1",
                Method = "GET",
                Url = "https://api.example.com/users?access_token=super-secret&page=2",
                Host = "api.example.com",
                Path = "/users?access_token=super-secret&page=2",
                StatusCode = 200,
                DurationMs = 42,
                RequestSize = 10,
                ResponseSize = 2048,
                RequestContentType = "application/json",
                ResponseContentType = "application/json",
                RequestHeaders = new Dictionary<string, string[]> { ["Authorization"] = ["Bearer abcdefghijklmnop"] },
                ResponseHeaders = new Dictionary<string, string[]> { ["Set-Cookie"] = ["session=zzz"] },
                RequestBody = "{\"password\":\"pw\"}",
                ResponseBody = "{\"token\":\"tk\"}",
                Error = @"connect failed for C:\Users\alice\app.db",
            },
        };

        var document = EvidenceBuilder.ProjectNetwork(requests, EvidenceFormat.DefaultNetworkLimit);
        var json = EvidenceJson.Serialize(document);

        foreach (var secret in new[] { "super-secret", "Bearer abcdefghijklmnop", "session=zzz", "password", @"C:\Users\alice" })
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);

        var entry = document.Requests[0];
        Assert.Equal("GET", entry.Method);
        Assert.Equal("api.example.com", entry.Host);
        Assert.Equal("/users", entry.Path);
        Assert.Equal(new[] { "access_token", "page" }, entry.QueryKeys!.ToArray());
        Assert.Equal(200, entry.StatusCode);
        Assert.Equal(42, entry.DurationMs);
        Assert.Equal(2048, entry.ResponseBytes);
        Assert.Contains("app.db", entry.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectLogs_ScrubsAndBounds()
    {
        var raw = """
            [
              { "t": "2026-07-29T10:00:00Z", "l": "error", "c": "App", "m": "login failed api_key=zzz-999", "s": "native" },
              { "t": "2026-07-29T10:00:01Z", "l": "info", "c": "App", "m": "second" },
              { "t": "2026-07-29T10:00:02Z", "l": "info", "c": "App", "m": "third" }
            ]
            """;

        var document = EvidenceBuilder.ProjectLogs(raw, limit: 2);

        Assert.Equal(2, document.Count);
        Assert.True(document.Truncated);
        Assert.DoesNotContain("zzz-999", document.Entries[0].Message, StringComparison.Ordinal);
        Assert.Equal("error", document.Entries[0].Level);
    }

    [Fact]
    public void ProjectLogs_IgnoresMalformedPayloads()
    {
        Assert.Equal(0, EvidenceBuilder.ProjectLogs("not json", 10).Count);
        Assert.Equal(0, EvidenceBuilder.ProjectLogs("{\"not\":\"an array\"}", 10).Count);
    }

    // ── manifest / plan ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_OmitsScreenshotUnlessExplicitlyRequested()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());

        Assert.False(bundle.Manifest.Screenshot.Requested);
        Assert.False(bundle.Manifest.Screenshot.Included);
        Assert.DoesNotContain(bundle.Entries, e => e.Name == EvidenceFormat.ScreenshotEntry);
        Assert.Contains(bundle.Manifest.Excluded, e => e.Name == EvidenceFormat.ScreenshotEntry);
        Assert.Equal(bundle.Manifest.Counts.TreeElements, bundle.Plan.Counts.TreeElements);
        Assert.Contains(bundle.Plan.NeverIncluded, entry => entry.Contains("secure storage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_IncludesScreenshotOnOptIn()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(), Options(includeScreenshot: true));

        Assert.True(bundle.Manifest.Screenshot.Requested);
        Assert.True(bundle.Manifest.Screenshot.Included);
        Assert.Contains(bundle.Entries, e => e.Name == EvidenceFormat.ScreenshotEntry);
        Assert.Contains(bundle.Manifest.Warnings, w => w.Contains("screenshot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_PreviewSkipsTheScreenshotRead()
    {
        var source = new FakeEvidenceSource();

        var bundle = await EvidenceBuilder.BuildAsync(
            source, Options(includeScreenshot: true, previewOnly: true));

        Assert.Equal(0, source.ScreenshotReads);
        Assert.True(bundle.Plan.Screenshot.Requested);
    }

    [Fact]
    public async Task BuildAsync_ManifestDescribesEveryIncludedEntry()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());

        Assert.Equal(EvidenceFormat.SchemaId, bundle.Manifest.Schema);
        Assert.Equal(EvidenceFormat.Version, bundle.Manifest.FormatVersion);
        Assert.Equal(EvidenceRedaction.Version, bundle.Manifest.RedactionVersion);
        Assert.All(bundle.Manifest.Entries, entry =>
        {
            Assert.Contains(entry.Name, EvidenceFormat.AllowedEntries);
            Assert.True(entry.Bytes > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.Sha256));
        });
        // Every non-manifest entry has a manifest record, and vice versa.
        Assert.Equal(
            bundle.Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            bundle.Manifest.Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Contains(bundle.Plan.Included, entry => entry.Name == EvidenceFormat.ManifestEntry);
    }

    [Fact]
    public async Task BuildAsync_FlowRunLink_IsManifestOnlyAndRedactsTheLocalPath()
    {
        var reportPath = Path.Combine(_root, "artifacts", "run-1", "flow-run.json");
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with
            {
                ProjectRoot = _root,
                FlowRun = new EvidenceFlowRunLink
                {
                    RunId = "run-1",
                    FailedStepId = "4",
                    FailureCode = "locator-not-found",
                    ReportDigest = "sha256:abc",
                    ReportPath = reportPath,
                    ReportReference = "flow-run:run-1",
                    CaptureCompleteness = "failure-only-redacted",
                },
            });

        Assert.NotNull(bundle.Manifest.FlowRun);
        var link = bundle.Manifest.FlowRun!;
        Assert.Equal("run-1", link.RunId);
        Assert.Equal("4", link.FailedStepId);
        Assert.Equal("locator-not-found", link.FailureCode);
        Assert.Equal("artifacts/run-1/flow-run.json", link.ReportPath);
        Assert.DoesNotContain(bundle.Entries, entry => entry.Name.Contains("flow-run", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("flow-run.json", EvidenceFormat.AllowedEntries.Where(entry => entry != EvidenceFormat.ManifestEntry));
    }

    [Fact]
    public async Task BuildAsync_TurnsSectionFailuresIntoExclusions()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource { FailNetwork = true }, Options());

        Assert.Contains(bundle.Manifest.Excluded, e => e.Name == EvidenceFormat.NetworkEntry);
        Assert.DoesNotContain(bundle.Entries, e => e.Name == EvidenceFormat.NetworkEntry);
        // The rest of the bundle still built.
        Assert.Contains(bundle.Entries, e => e.Name == EvidenceFormat.TreeEntry);
    }

    [Fact]
    public async Task BuildAsync_RejectsOversizedWorkflow()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with { WorkflowMarkdown = new string('w', (int)EvidenceFormat.MaxWorkflowBytes + 10) });

        Assert.DoesNotContain(bundle.Entries, e => e.Name == EvidenceFormat.WorkflowEntry);
        Assert.Contains(bundle.Manifest.Excluded,
            e => e.Name == EvidenceFormat.WorkflowEntry && e.Reason.Contains("limit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_ScrubsTheAttachedWorkflowAndWarnsAboutIt()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with
            {
                WorkflowMarkdown = "1. Fill Password = \"hunter2\"\n2. Open C:\\Users\\alice\\App\\MainPage.xaml\n3. Use api_key=zzz-9999",
            });

        var workflow = Encoding.UTF8.GetString(
            bundle.Entries.Single(e => e.Name == EvidenceFormat.WorkflowEntry).Content);

        Assert.DoesNotContain("zzz-9999", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\alice", workflow, StringComparison.Ordinal);
        Assert.Contains("MainPage.xaml", workflow, StringComparison.Ordinal);
        Assert.Contains(bundle.Manifest.Warnings,
            w => w.Contains("reproduction steps", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_RedactsStructuredFlowValuesBeforeAttachingWorkflow()
    {
        const string secret = "literal-flow-password";
        var flow = new MauiFlow
        {
            Name = "login",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Fill,
                    Target = new FlowSelector { AutomationId = "PasswordEntry", Text = "private label" },
                    Value = secret,
                    Args = new FlowStepArgs { Text = secret },
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "propEquals",
                            Selector = new FlowSelector { AutomationId = "PasswordEntry" },
                            Name = "Text",
                            Expected = secret,
                            Verify = true
                        }
                    ]
                }
            }
        };

        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with { WorkflowMarkdown = FlowMarkdown.Serialize(flow) });
        var workflow = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.WorkflowEntry).Content);

        Assert.DoesNotContain(secret, workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("private label", workflow, StringComparison.Ordinal);
        Assert.Contains("<redacted>", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_RedactsUnexpectedStructuredValueFieldsRegardlessOfAction()
    {
        const string secret = "unexpected-action-secret";
        var flow = new MauiFlow
        {
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Target = new FlowSelector { AutomationId = "Button" },
                    Value = secret,
                    Args = new FlowStepArgs
                    {
                        Route = "//account?email=alice@example.com",
                        Theme = "secret-theme",
                        ValueSource = "secret-source"
                    },
                    Asserts =
                    [
                        new FlowAssert
                        {
                            Kind = "exists",
                            Selector = new FlowSelector { AutomationId = "Button" },
                            Expected = secret,
                            Verify = true
                        }
                    ]
                }
            }
        };

        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with { WorkflowMarkdown = FlowMarkdown.Serialize(flow) });
        var workflow = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.WorkflowEntry).Content);

        Assert.DoesNotContain(secret, workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("alice@example.com", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-theme", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-source", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_OmitsMalformedStructuredWorkflowBlock()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options() with
            {
                WorkflowMarkdown = "```json maui-test\n{\"steps\":[\n```"
            });

        Assert.DoesNotContain(
            bundle.Entries,
            entry => entry.Name == EvidenceFormat.WorkflowEntry);
    }

    [Fact]
    public async Task BuildAsync_PropagatesCancellationInsteadOfWritingAPartialBundle()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EvidenceBuilder.BuildAsync(
                new FakeEvidenceSource { CancelOnTree = true },
                Options()));
    }

    [Fact]
    public async Task BundleWriter_ObservesCancellationBeforePublishing()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options());
        var destination = Path.Combine(_root, "cancelled.mauitrace");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            EvidenceBundleWriter.Write(bundle, destination, overwrite: false, cts.Token));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task BundleWriter_PreservesGeneratedSectionsAtTheirExactStringLimits()
    {
        var longMessage = new string('x', EvidenceFormat.MaxLogMessageChars + 500);
        var logs = JsonSerializer.Serialize(new[]
        {
            new { t = "2026-07-29T10:00:00Z", l = "info", c = "App", m = longMessage, s = "native" }
        });
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource { LogsJson = logs },
            Options());

        var bytes = EvidenceBundleWriter.ToBytes(bundle);
        var read = EvidenceBundleReader.Read(new MemoryStream(bytes));

        Assert.True(read.Ok, read.Error);
        var entry = Assert.Single(read.Logs!.Entries);
        Assert.True(entry.Message.Length <= EvidenceFormat.MaxLogMessageChars);
        Assert.EndsWith("[truncated]", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_DeepTreesSurviveSerializationAndReadBack()
    {
        // The tree nests two JSON levels per element level, so a 40-level tree needs a serializer
        // depth well past the 64 default on BOTH the write and read paths.
        ElementInfo leaf = new() { Id = "leaf", Type = "Label" };
        for (var depth = 0; depth < 40; depth++)
            leaf = new ElementInfo { Id = $"n{depth}", Type = "Grid", Children = [leaf] };

        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource { Tree = [leaf] }, Options());
        var destination = Path.Combine(_root, "deep.mauitrace");
        Assert.True(EvidenceBundleWriter.Write(bundle, destination, overwrite: false).Ok);

        var read = EvidenceBundleReader.Read(destination);

        Assert.DoesNotContain(bundle.Manifest.Excluded, e => e.Name == EvidenceFormat.TreeEntry);
        Assert.True(read.Ok, read.Error);
        Assert.NotNull(read.Tree);
        Assert.Equal(41, read.Tree!.Count);
    }

    // ── atomic write ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Write_ProducesAReadableBundleAndLeavesNoTemporaryFiles()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var destination = Path.Combine(_root, "out", "capture.mauitrace");

        var result = EvidenceBundleWriter.Write(bundle, destination, overwrite: false);

        Assert.True(result.Ok, result.Error);
        Assert.True(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
        Assert.True(EvidenceBundleReader.Read(destination).Ok);
    }

    [Fact]
    public async Task ToBytes_ProducesAValidatedReadableBundle()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());

        var bytes = EvidenceBundleWriter.ToBytes(bundle);
        using var stream = new MemoryStream(bytes, writable: false);
        var read = EvidenceBundleReader.Read(stream);

        Assert.True(read.Ok, read.Error);
        Assert.Equal(EvidenceFormat.Version, read.Manifest!.FormatVersion);
    }

    [Fact]
    public async Task Write_WithoutOverwrite_RefusesAndKeepsTheOriginal()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var destination = Path.Combine(_root, "capture.mauitrace");
        File.WriteAllText(destination, "original");

        var result = EvidenceBundleWriter.Write(bundle, destination, overwrite: false);

        Assert.False(result.Ok);
        Assert.Contains("already exists", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("original", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Write_WithOverwrite_ReplacesTheFileAtomically()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var destination = Path.Combine(_root, "capture.mauitrace");
        File.WriteAllText(destination, "original");

        var result = EvidenceBundleWriter.Write(bundle, destination, overwrite: true);

        Assert.True(result.Ok, result.Error);
        Assert.True(EvidenceBundleReader.Read(destination).Ok);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Read_RoundTripsEveryProjectedSection()
    {
        var bundle = await EvidenceBuilder.BuildAsync(
            new FakeEvidenceSource(),
            Options(includeScreenshot: true) with { WorkflowMarkdown = "# Repro\n1. Tap" });
        var destination = Path.Combine(_root, "round-trip.mauitrace");
        Assert.True(EvidenceBundleWriter.Write(bundle, destination, overwrite: false).Ok);

        var read = EvidenceBundleReader.Read(destination);

        Assert.True(read.Ok, read.Error);
        Assert.NotNull(read.Manifest);
        Assert.NotNull(read.Environment);
        Assert.NotNull(read.Tree);
        Assert.NotNull(read.Problems);
        Assert.NotNull(read.Logs);
        Assert.NotNull(read.Network);
        Assert.NotNull(read.Screenshot);
        Assert.Contains("# Repro", read.Workflow!, StringComparison.Ordinal);
    }

    // ── hostile bundles ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("nested/manifest.json")]
    [InlineData(@"C:\windows\system32\evil.json")]
    [InlineData("evil.exe")]
    [InlineData("manifest.json.bak")]
    public void ValidateEntryName_RejectsAnythingOutsideTheAllowList(string name)
        => Assert.NotNull(EvidenceBundleReader.ValidateEntryName(name));

    [Fact]
    public void ValidateEntryName_AcceptsAllowListedNames()
        => Assert.All(EvidenceFormat.AllowedEntries, name => Assert.Null(EvidenceBundleReader.ValidateEntryName(name)));

    [Fact]
    public void Read_RejectsTraversingEntries()
    {
        var path = WriteArchive("traversal.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ValidManifestJson());
            AddText(archive, "../evil.json", "{}");
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("travers", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsDuplicateEntries()
    {
        var path = WriteArchive("duplicate.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ValidManifestJson());
            AddText(archive, EvidenceFormat.LogsEntry, "{\"entries\":[]}");
            AddText(archive, EvidenceFormat.LogsEntry, "{\"entries\":[]}");
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("duplicate", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsTooManyEntries()
    {
        var path = Path.Combine(_root, "many.mauitrace");
        using (var file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ValidManifestJson());
            for (var i = 0; i < EvidenceFormat.MaxBundleEntries + 4; i++)
                AddText(archive, $"filler-{i}.json", "{}");
        }

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("too many entries", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsZipBombRatios()
    {
        var path = WriteArchive("bomb.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ValidManifestJson());
            // ~3 MB of zeros stays within the logs entry cap but compresses to a few KB — far past
            // the ratio guard.
            var entry = archive.CreateEntry(EvidenceFormat.LogsEntry, CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(new byte[3 * 1024 * 1024]);
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("compression ratio", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsBundlesThatLieAboutTheirDeclaredSize()
    {
        // The central directory is attacker-controlled, so the size/ratio guards must not depend on
        // it alone. Understating the declared size makes the runtime truncate the inflated stream
        // (so the content no longer validates) and the reader's own accounting works on the bytes
        // actually read — either way the bundle is refused rather than silently over-expanding.
        var path = WriteArchive("lying-bomb.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ValidManifestJson());
            var entry = archive.CreateEntry(EvidenceFormat.LogsEntry, CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            stream.Write(new byte[8 * 1024 * 1024]);
        });
        PatchDeclaredUncompressedSizes(path);

        using (var probe = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
            Assert.Equal(0, probe.GetEntry(EvidenceFormat.LogsEntry)!.Length); // the lie is in place

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.False(string.IsNullOrWhiteSpace(read.Error));
    }

    /// <summary>
    /// Rewrites every central-directory record's uncompressed-size field to zero, simulating an
    /// archive that lies about how much it expands to. The central directory is located through the
    /// end-of-central-directory record, so no signature scanning heuristics are involved.
    /// </summary>
    private static void PatchDeclaredUncompressedSizes(string path)
    {
        var bytes = File.ReadAllBytes(path);

        var eocd = -1;
        for (var i = bytes.Length - 22; i >= 0; i--)
        {
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 0x05 && bytes[i + 3] == 0x06)
            {
                eocd = i;
                break;
            }
        }
        Assert.True(eocd >= 0, "end-of-central-directory record not found");

        var count = BitConverter.ToUInt16(bytes, eocd + 10);
        var offset = (int)BitConverter.ToUInt32(bytes, eocd + 16);
        for (var i = 0; i < count; i++)
        {
            Assert.True(bytes[offset] == 0x50 && bytes[offset + 1] == 0x4B &&
                        bytes[offset + 2] == 0x01 && bytes[offset + 3] == 0x02,
                "central directory record not found");
            var nameLength = BitConverter.ToUInt16(bytes, offset + 28);
            var extraLength = BitConverter.ToUInt16(bytes, offset + 30);
            var commentLength = BitConverter.ToUInt16(bytes, offset + 32);
            BitConverter.GetBytes(0u).CopyTo(bytes, offset + 24); // uncompressed size
            offset += 46 + nameLength + extraLength + commentLength;
        }

        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void Read_RejectsForeignOrUnsupportedManifests()
    {
        var wrongSchema = WriteArchive("wrong-schema.mauitrace", archive =>
            AddText(archive, EvidenceFormat.ManifestEntry, "{\"schema\":\"something-else\",\"formatVersion\":1,\"capturedUtc\":\"now\"}"));
        var futureVersion = WriteArchive("future.mauitrace", archive =>
            AddText(archive, EvidenceFormat.ManifestEntry, $"{{\"schema\":\"{EvidenceFormat.SchemaId}\",\"formatVersion\":99,\"capturedUtc\":\"now\"}}"));
        var notAnObject = WriteArchive("array.mauitrace", archive =>
            AddText(archive, EvidenceFormat.ManifestEntry, "[]"));
        var missingManifest = WriteArchive("no-manifest.mauitrace", archive =>
            AddText(archive, EvidenceFormat.LogsEntry, "{}"));

        Assert.Contains("not a MAUI DevFlow evidence manifest", EvidenceBundleReader.Read(wrongSchema).Error!, StringComparison.Ordinal);
        Assert.Contains("Unsupported evidence format version", EvidenceBundleReader.Read(futureVersion).Error!, StringComparison.Ordinal);
        Assert.Contains("must be a JSON object", EvidenceBundleReader.Read(notAnObject).Error!, StringComparison.Ordinal);
        Assert.Contains("missing manifest.json", EvidenceBundleReader.Read(missingManifest).Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsManifestTerminalControlSequences()
    {
        var path = WriteArchive("terminal-control.mauitrace", archive =>
            AddText(
                archive,
                EvidenceFormat.ManifestEntry,
                $$"""{"schema":"{{EvidenceFormat.SchemaId}}","formatVersion":{{EvidenceFormat.Version}},"capturedUtc":"2026-07-29T10:00:00Z","source":"cli\u001b[31m","entries":[]}"""));

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("invalid source", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatView_EscapesTerminalControlCharactersDefensively()
    {
        var formatted = EvidenceCommands.FormatView(new EvidenceViewResult
        {
            Ok = true,
            Report = "report\u001b[31m.html",
            Bundle = "bundle\nname.mauitrace",
            Entries = ["tree.json\u202E"],
            Manifest = new EvidenceManifest
            {
                CapturedUtc = "now\rspoof",
                Source = "cli\u001b[0m"
            }
        });

        Assert.DoesNotContain('\u001b', formatted);
        Assert.DoesNotContain('\u202E', formatted);
        Assert.Contains("\\u001B", formatted, StringComparison.Ordinal);
        Assert.Contains("\\u000A", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_IgnoresMalformedSectionsWithoutFailingTheBundle()
    {
        const string malformedLogs = "not json";
        const string invalidScreenshot = "definitely not a png";
        var path = WriteArchive("bad-section.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ManifestForEntries(
                (EvidenceFormat.LogsEntry, Encoding.UTF8.GetBytes(malformedLogs)),
                (EvidenceFormat.ScreenshotEntry, Encoding.UTF8.GetBytes(invalidScreenshot))));
            AddText(archive, EvidenceFormat.LogsEntry, malformedLogs);
            AddText(archive, EvidenceFormat.ScreenshotEntry, invalidScreenshot);
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.True(read.Ok, read.Error);
        Assert.Null(read.Logs);
        Assert.Null(read.Screenshot);
        Assert.Contains(read.Warnings, w => w.Contains("logs.json", StringComparison.Ordinal));
        Assert.Contains(read.Warnings, w => w.Contains("screenshot.png", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_IgnoresSemanticallyInvalidNullCollections()
    {
        const string invalidLayout =
            """{"schemaVersion":"1.0","ruleSetVersion":"1.0","rules":null,"findings":[],"limitations":[],"neverCaptured":[]}""";
        const string invalidProblems =
            """{"enabled":true,"revision":1,"count":1,"evicted":0,"problems":null}""";
        var layoutBytes = Encoding.UTF8.GetBytes(invalidLayout);
        var problemBytes = Encoding.UTF8.GetBytes(invalidProblems);
        var path = WriteArchive("null-collections.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ManifestForEntries(
                (EvidenceFormat.LayoutEntry, layoutBytes),
                (EvidenceFormat.ProblemsEntry, problemBytes)));
            AddBytes(archive, EvidenceFormat.LayoutEntry, layoutBytes);
            AddBytes(archive, EvidenceFormat.ProblemsEntry, problemBytes);
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.True(read.Ok, read.Error);
        Assert.Null(read.Layout);
        Assert.Null(read.Problems);
        Assert.Contains(read.Warnings, warning => warning.Contains("layout.json", StringComparison.Ordinal));
        Assert.Contains(read.Warnings, warning => warning.Contains("problems.json", StringComparison.Ordinal));
        var html = EvidenceReportRenderer.Render(read);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_IgnoresLayoutFindingsWithInvalidV2Collections()
    {
        const string invalidLayout =
            """
            {
              "schemaVersion":"2.0",
              "ruleSetVersion":"2.0",
              "rules":[],
              "findings":[{
                "id":"finding-1",
                "ruleId":"layout.visible-zero-area",
                "outcome":"violation",
                "confidence":"high",
                "severity":"serious",
                "actionability":"fix",
                "message":"message",
                "explanation":"explanation",
                "relatedElementIds":null,
                "fixCategories":[],
                "limitations":[]
              }],
              "limitations":[],
              "neverCaptured":[]
            }
            """;
        var layoutBytes = Encoding.UTF8.GetBytes(invalidLayout);
        var path = WriteArchive("invalid-layout-v2.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ManifestForEntries(
                (EvidenceFormat.LayoutEntry, layoutBytes)));
            AddBytes(archive, EvidenceFormat.LayoutEntry, layoutBytes);
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.True(read.Ok, read.Error);
        Assert.Null(read.Layout);
        Assert.Contains(read.Warnings, warning => warning.Contains("layout.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(EvidenceFormat.WorkflowEntry, EvidenceFormat.MaxWorkflowBytes)]
    [InlineData(EvidenceFormat.ScreenshotEntry, EvidenceFormat.MaxScreenshotBytes)]
    public void Read_RejectsEntriesBeyondTheirCaptureSpecificLimit(string entryName, long limit)
    {
        var content = new byte[checked((int)limit + 1)];
        var path = WriteArchive($"oversized-{entryName.Replace('.', '-')}.mauitrace", archive =>
        {
            AddText(archive, EvidenceFormat.ManifestEntry, ManifestForEntries((entryName, content)));
            AddBytes(archive, entryName, content);
        });

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("larger than", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_RejectsEntryWhoseContentDoesNotMatchManifestHash()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var path = Path.Combine(_root, "tampered.mauitrace");
        Assert.True(EvidenceBundleWriter.Write(bundle, path, overwrite: false).Ok);

        byte[] tampered;
        using (var archive = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
        using (var source = archive.GetEntry(EvidenceFormat.LogsEntry)!.Open())
        using (var buffer = new MemoryStream())
        {
            source.CopyTo(buffer);
            tampered = buffer.ToArray();
        }
        tampered[^1] ^= 0x01;

        using (var archive = new ZipArchive(
            new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
            ZipArchiveMode.Update))
        {
            archive.GetEntry(EvidenceFormat.LogsEntry)!.Delete();
            AddBytes(archive, EvidenceFormat.LogsEntry, tampered);
        }

        var read = EvidenceBundleReader.Read(path);

        Assert.False(read.Ok);
        Assert.Contains("integrity hash", read.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsFilesThatAreNotArchives()
    {
        var path = Path.Combine(_root, "garbage.mauitrace");
        File.WriteAllText(path, "this is not a zip");

        Assert.False(EvidenceBundleReader.Read(path).Ok);
    }

    // ── report ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Render_EncodesHostileContentAndDeclaresARestrictiveCsp()
    {
        var read = new EvidenceReadResult
        {
            Ok = true,
            Manifest = new EvidenceManifest
            {
                CapturedUtc = "2026-07-29T10:00:00Z",
                App = new EvidenceAppInfo { Name = "<script>alert('xss')</script>" },
                Entries = [new EvidenceEntryInfo { Name = "tree.json", Description = "<img src=x onerror=alert(1)>", Bytes = 10 }],
                Warnings = ["</style><script>alert(2)</script>"],
            },
            Workflow = "# Repro\n<script>alert('workflow')</script>",
            Tree = new EvidenceTreeDocument
            {
                Count = 1,
                Roots = [new EvidenceTreeNode { Id = "e1", Type = "<b>Label</b>", AutomationId = "\"onmouseover=alert(3)" }],
            },
        };

        var html = EvidenceReportRenderer.Render(read);

        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'none'", html, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", html, StringComparison.Ordinal);
        // Hostile values survive only as inert, encoded text: no injected tags and no raw quote
        // that could break out of an attribute.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<b>Label</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\"onmouseover", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&quot;onmouseover", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DropsTerminalControlAndFormatCharacters()
    {
        var read = new EvidenceReadResult
        {
            Ok = true,
            Manifest = new EvidenceManifest
            {
                CapturedUtc = "2026-07-31T10:00:00Z"
            },
            Warnings = ["before\u001b[31mred\u0007after\u202E"]
        };

        var html = EvidenceReportRenderer.Render(read);

        Assert.DoesNotContain('\u001b', html);
        Assert.DoesNotContain('\u0007', html);
        Assert.DoesNotContain('\u202E', html);
        Assert.Contains("before[31mredafter", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task View_RegeneratesAStaticReportWithoutOpeningIt()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var destination = Path.Combine(_root, "view.mauitrace");
        Assert.True(EvidenceBundleWriter.Write(bundle, destination, overwrite: false).Ok);
        var reportPath = Path.Combine(_root, "report", "report.html");

        var result = EvidenceCapture.View(destination, reportPath, open: false);

        Assert.True(result.Ok, result.Error);
        Assert.False(result.Opened);
        Assert.True(File.Exists(reportPath));
        Assert.Contains(EvidenceFormat.ManifestEntry, result.Entries);
        Assert.Contains("Content-Security-Policy", File.ReadAllText(reportPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task View_RefusesExistingReportUnlessOverwriteIsExplicit()
    {
        var bundle = await EvidenceBuilder.BuildAsync(new FakeEvidenceSource(), Options());
        var destination = Path.Combine(_root, "view-overwrite.mauitrace");
        Assert.True(EvidenceBundleWriter.Write(bundle, destination, overwrite: false).Ok);
        var reportPath = Path.Combine(_root, "report.html");
        File.WriteAllText(reportPath, "original");

        var refused = EvidenceCapture.View(destination, reportPath, open: false);
        Assert.False(refused.Ok);
        Assert.Equal("original", File.ReadAllText(reportPath));

        var overwritten = EvidenceCapture.View(
            destination,
            reportPath,
            open: false,
            overwrite: true);

        Assert.True(overwritten.Ok, overwritten.Error);
        Assert.Contains("Content-Security-Policy", File.ReadAllText(reportPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("report.txt")]
    [InlineData("report.html:hidden")]
    [InlineData("NUL")]
    public void ValidateReportPath_RejectsUnsafeDestinations(string requested)
    {
        if (!OperatingSystem.IsWindows() && requested is "report.html:hidden" or "NUL")
            return;

        var result = EvidencePaths.ValidateReportPath(Path.Combine(_root, requested));

        Assert.False(result.Ok);
    }

    [Fact]
    public void View_RejectsAMissingBundle()
    {
        var result = EvidenceCapture.View(Path.Combine(_root, "nope.mauitrace"), null, open: false);

        Assert.False(result.Ok);
        Assert.Contains("not found", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // ── paths ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveDefaultOutputPath_UsesTheProjectLocalTraceFolder()
    {
        var path = EvidencePaths.ResolveDefaultOutputPath(_root, "My App", new DateTime(2026, 7, 29, 11, 22, 33, DateTimeKind.Utc));

        Assert.Equal(Path.Combine(_root, EvidenceFormat.DefaultFolderName), Path.GetDirectoryName(path));
        Assert.Equal("MyApp-20260729-112233.mauitrace", Path.GetFileName(path));
    }

    [Fact]
    public void ResolveDefaultOutputPath_FallsBackToTheCurrentDirectory()
    {
        var path = EvidencePaths.ResolveDefaultOutputPath(null, "App", DateTime.UtcNow);

        Assert.Equal(Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetDirectoryName(path));
    }

    [Theory]
    [InlineData("bundle.zip", "extension")]
    [InlineData("bundle", "extension")]
    public void ValidateOutputPath_RequiresTheMauitraceExtension(string requested, string expectedFragment)
    {
        var result = EvidencePaths.ValidateOutputPath(requested, _root, "App", DateTime.UtcNow);

        Assert.False(result.Ok);
        Assert.Contains(expectedFragment, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateOutputPath_FillsInADefaultNameForADirectory()
    {
        var result = EvidencePaths.ValidateOutputPath(_root, _root, "App", DateTime.UtcNow);

        Assert.True(result.Ok);
        Assert.Equal(_root, Path.GetDirectoryName(result.Path));
        Assert.EndsWith(EvidenceFormat.FileExtension, result.Path!, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateOutputPath_RejectsControlCharacters()
        => Assert.False(EvidencePaths.ValidateOutputPath("bad\u0000name.mauitrace", _root, "App", DateTime.UtcNow).Ok);

    [Fact]
    public void CleanupReports_RemovesExpiredReportsOnlyAndKeepsTheDirectory()
    {
        var original = EvidencePaths.ReportDirectory;
        var reportDirectory = Path.Combine(_root, "reports");
        try
        {
            EvidencePaths.ReportDirectory = reportDirectory;
            Directory.CreateDirectory(reportDirectory);

            var stale = Path.Combine(reportDirectory, "evidence-report-old.html");
            var unrelated = Path.Combine(reportDirectory, "keep-me.txt");
            File.WriteAllText(stale, "<html></html>");
            File.WriteAllText(unrelated, "not ours");
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3));

            var fresh = EvidencePaths.CreateReportPath(DateTime.UtcNow);

            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(unrelated));
            Assert.True(Directory.Exists(reportDirectory));
            Assert.Equal(reportDirectory, Path.GetDirectoryName(fresh));
        }
        finally
        {
            EvidencePaths.ReportDirectory = original;
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static EvidenceCaptureOptions Options(bool includeScreenshot = false, bool previewOnly = false) => new()
    {
        IncludeScreenshot = includeScreenshot,
        PreviewOnly = previewOnly,
        Source = "cli",
        ToolVersion = "1.2.3",
        UtcNow = new DateTime(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc),
    };

    private string WriteArchive(string name, Action<ZipArchive> build)
    {
        var path = Path.Combine(_root, name);
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        build(archive);
        return path;
    }

    private static void AddText(ZipArchive archive, string name, string content)
        => AddBytes(archive, name, Encoding.UTF8.GetBytes(content));

    private static void AddBytes(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string ValidManifestJson()
        => $"{{\"schema\":\"{EvidenceFormat.SchemaId}\",\"formatVersion\":{EvidenceFormat.Version},\"capturedUtc\":\"2026-07-29T10:00:00Z\"}}";

    private static string ManifestForEntries(params (string Name, byte[] Content)[] entries)
        => EvidenceJson.Serialize(new EvidenceManifest
        {
            CapturedUtc = "2026-07-29T10:00:00Z",
            Entries = entries.Select(entry => new EvidenceEntryInfo
            {
                Name = entry.Name,
                Description = "test",
                Bytes = entry.Content.LongLength,
                Sha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(entry.Content)).ToLowerInvariant(),
            }).ToList(),
        });

    private sealed class FakeEvidenceSource : IEvidenceDataSource
    {
        public bool FailNetwork { get; init; }
        public bool CancelOnTree { get; init; }
        public string? LogsJson { get; init; }
        public List<ElementInfo>? Tree { get; init; }
        public int ScreenshotReads { get; private set; }

        public Task<AgentStatus?> GetStatusAsync(CancellationToken ct) => Task.FromResult<AgentStatus?>(new AgentStatus
        {
            Agent = new AgentDescriptor { Version = "0.1.0", Framework = "maui", FrameworkVersion = "10.0" },
            Device = new DeviceDescriptor { Platform = "Windows", DeviceType = "Virtual", Idiom = "Desktop" },
            App = new AppDescriptor { Name = "Sample App", Version = "1.0", Build = "42", PackageId = "com.example.sample" },
            Route = "//home",
        });

        public Task<JsonElement> GetCapabilitiesAsync(CancellationToken ct)
            => Task.FromResult(Parse("""{"capabilities":{"ui.actions":{"version":1},"ui.events":{"version":1}}}"""));

        public Task<List<ElementInfo>> GetTreeAsync(CancellationToken ct)
        {
            if (CancelOnTree)
                throw new OperationCanceledException(ct);
            return Task.FromResult(Tree ?? new List<ElementInfo>
            {
                new()
                {
                    Id = "e1",
                    Type = "ContentPage",
                    Children =
                    [
                        new ElementInfo { Id = "e2", Type = "Label", Text = "secret label text", AutomationId = "Title" },
                    ],
                }
            });
        }

        public Task<DiagnosticProblemBatch> GetProblemsAsync(int limit, CancellationToken ct)
            => Task.FromResult(new DiagnosticProblemBatch
            {
                Enabled = true,
                Revision = 3,
                Count = 1,
                Problems =
                [
                    new DiagnosticProblem
                    {
                        Id = "p1",
                        Kind = "binding",
                        Severity = "warning",
                        Message = @"Binding failed in C:\Users\alice\App\MainPage.xaml",
                        Count = 2,
                        BindingPath = "User.Name",
                    },
                ],
            });

        public Task<string> GetLogsAsync(int limit, CancellationToken ct)
            => Task.FromResult(LogsJson ??
                """[{"t":"2026-07-29T10:00:00Z","l":"info","c":"App","m":"started","s":"native"}]""");

        public Task<List<NetworkRequest>> GetNetworkAsync(int limit, CancellationToken ct)
            => FailNetwork
                ? Task.FromException<List<NetworkRequest>>(new HttpRequestException("network capture is off"))
                : Task.FromResult(new List<NetworkRequest>
                {
                    new() { Id = "r1", Method = "GET", Url = "https://example.com/a", Host = "example.com", Path = "/a", StatusCode = 200 },
                });

        public Task<JsonElement> GetPlatformInfoAsync(string endpoint, CancellationToken ct) => Task.FromResult(endpoint switch
        {
            "device-info" => Parse("""{"manufacturer":"Contoso","model":"Surface","platform":"Windows","osVersion":"11","name":"Alice's PC"}"""),
            "device-display" => Parse("""{"width":1920,"height":1080,"density":2,"orientation":"landscape"}"""),
            _ => Parse("{}"),
        });

        public Task<byte[]?> GetScreenshotAsync(CancellationToken ct)
        {
            ScreenshotReads++;
            // Minimal PNG signature + payload so the reader's format check passes.
            var png = new byte[64];
            png[0] = 0x89; png[1] = 0x50; png[2] = 0x4E; png[3] = 0x47;
            png[4] = 0x0D; png[5] = 0x0A; png[6] = 0x1A; png[7] = 0x0A;
            return Task.FromResult<byte[]?>(png);
        }

        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}
