using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Source apply is deferred out of this layer. These tests pin that boundary from the outside
/// against a real broker-backed Inspector — the configuration in which every workflow service is
/// present and the source routes would otherwise be fully live.
///
/// <para>
/// The credential used is the Inspector's browser read token, which is the strongest thing an
/// embedded page, a localhost tab, or a chat agent proxying through one can hold. No matter what
/// <c>hostKind</c>, <c>humanConfirmed</c>, or host capability the body claims, it must not reach a
/// route that writes source, mints authority to write source, or acknowledges that a write happened.
/// </para>
/// </summary>
public sealed class InspectorSourceAuthorityAbsenceTests
{
    /// <summary>
    /// A body claiming every native-host privilege the removed routes used to read. The defect being
    /// fixed was exactly this: the broker trusted a caller-declared host identity.
    /// </summary>
    private const string SpoofedNativeHostBody = """
        {
          "proposalId": "src-1",
          "kind": "apply",
          "reviewer": "attacker",
          "humanConfirmed": true,
          "applied": true,
          "reverted": true,
          "approvalGrant": "grant-1",
          "rollbackGrant": "grant-1",
          "hostKind": "vscode",
          "hostCapability": {
            "hostKind": "vscode",
            "canOpenNativeDiff": true,
            "canDownloadPatch": true,
            "canApplySource": true,
            "canApplyCSharpSource": true,
            "isExplicitLocalHostAction": true
          }
        }
        """;

    private static readonly string[] SourceWriteRoutes =
    [
        "/api/workbench/source/grant",
        "/api/workbench/source/csharp/grant",
        "/api/workbench/source/src-1/approve",
        "/api/workbench/source/src-1/await-host-apply",
        "/api/workbench/source/src-1/apply",
        "/api/workbench/source/src-1/apply-result",
        "/api/workbench/source/src-1/verify",
        "/api/workbench/source/src-1/rollback",
        "/api/workbench/source/csharp/src-1/approve",
        "/api/workbench/source/csharp/src-1/await-host-apply",
        "/api/workbench/source/csharp/src-1/begin-host-apply",
        "/api/workbench/source/csharp/src-1/apply-ack",
        "/api/workbench/source/csharp/src-1/verify",
        "/api/workbench/source/csharp/src-1/begin-rollback",
        "/api/workbench/source/csharp/src-1/rollback-ack",
    ];

    /// <summary>The inert review vocabulary this layer keeps: it must survive the removal.</summary>
    private static readonly string[] InertSourceRoutes =
    [
        "/api/workbench/source/analyze",
        "/api/workbench/source/propose",
        "/api/workbench/source/csharp/analyze",
        "/api/workbench/source/csharp/propose",
        "/api/workbench/source/src-1/status",
        "/api/workbench/source/src-1/preview",
        "/api/workbench/source/src-1/reject",
        "/api/workbench/source/csharp/src-1/status",
        "/api/workbench/source/csharp/src-1/preview",
        "/api/workbench/source/csharp/src-1/reject",
    ];

    [Fact]
    public async Task ABrowserReadTokenWithASpoofedNativeHostReachesNoSourceWriteRoute()
    {
        await using var fixture = await InspectorFixture.StartAsync();

        foreach (var route in SourceWriteRoutes)
        {
            using var response = await fixture.PostAsync(route, SpoofedNativeHostBody);
            var payload = await response.Content.ReadAsStringAsync();

            // 404 "Not found." is the routing table's miss, not a handler's refusal: the route does
            // not exist at all, so there is nothing for a spoofed host identity to get past.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("\"error\":\"Not found.\"", payload, StringComparison.Ordinal);

            // A refusal that still leaked a grant or an applied acknowledgment would defeat the point.
            Assert.DoesNotContain("grant", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("applied", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confirmationCapability", payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheInertReviewedSourceRoutesAreStillServed()
    {
        await using var fixture = await InspectorFixture.StartAsync();

        foreach (var route in InertSourceRoutes)
        {
            using var response = await fixture.PostAsync(route, "{}");
            var payload = await response.Content.ReadAsStringAsync();

            // These reach a handler. The handler may still refuse an unknown proposal id, but it
            // must not be the routing table's "Not found." — that would mean the surface is gone.
            Assert.DoesNotContain(
                "\"error\":\"Not found.\"",
                payload,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The removed routes must not come back through the replay guard either: a path that is only
    /// "blocked during replay" would read as reachable the rest of the time.
    /// </summary>
    [Fact]
    public void NoSourceApplyPathSurvivesInTheReplayBlockList()
    {
        foreach (var path in SourceWriteRoutes)
            Assert.False(InspectorServer.IsBlockedDuringReplay(path), path);
    }

    /// <summary>
    /// No trust decision may be keyed on a caller-declared host identity anywhere in the CLI. The
    /// original defect was a positive allowlist (<c>InspectorHostIdentity</c>) that turned the
    /// string "vscode" into source-write authority.
    /// </summary>
    [Fact]
    public void TheCliCarriesNoHostIdentityAllowlistAndNoSourceApplyCapabilityContract()
    {
        var cli = typeof(InspectorServer).Assembly;
        Assert.DoesNotContain(
            cli.GetTypes(),
            type => type.Name is "InspectorHostIdentity"
                or "WorkflowXamlSourceHostCapability"
                or "WorkflowCSharpSourceHostCapability"
                or "WorkflowXamlSourceGrantBinding"
                or "WorkflowCSharpSourceGrantBinding");

        // The workflow service facade must expose no way to mint or redeem source authority.
        var services = cli.GetType("Microsoft.Maui.Cli.DevFlow.Broker.InspectorWorkflowServices");
        Assert.NotNull(services);
        foreach (var member in services!.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = member.Name;
            if (!name.Contains("XamlSource", StringComparison.Ordinal) &&
                !name.Contains("CSharpSource", StringComparison.Ordinal))
            {
                continue;
            }
            Assert.DoesNotContain("Grant", name, StringComparison.Ordinal);
            Assert.DoesNotContain("Apply", name, StringComparison.Ordinal);
            Assert.DoesNotContain("Rollback", name, StringComparison.Ordinal);
            Assert.DoesNotContain("Verification", name, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The AutomationId proposal services analyse, build and preview — they do not write. The
    /// removed <c>ApplyAsync</c>/<c>RollbackAsync</c> pair was the last disk-write primitive in the
    /// AutomationId proposal services, so its absence is pinned from three directions: the compiled
    /// surface carries no write member or write result, the source carries no file-mutating call a
    /// future route could be wired to, and the Inspector calls nothing but <c>BuildAsync</c> on
    /// either service.
    /// </summary>
    [Fact]
    public void TheSourceProposalServicesOnlyBuildProposalsAndOwnNoDiskWritePrimitive()
    {
        var cli = typeof(InspectorServer).Assembly;
        Assert.DoesNotContain(
            cli.GetTypes(),
            type => type.Name is "XamlSourceProposalWriteResult" or "CSharpSourceProposalWriteResult");

        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        string[] writeVocabulary =
            ["Apply", "Rollback", "Write", "Commit", "Persist", "Revert", "Save", "Flush"];
        foreach (var name in new[] { "XamlAutomationIdProposalService", "CSharpAutomationIdProposalService" })
        {
            var service = cli.GetType($"Microsoft.Maui.Cli.DevFlow.Inspector.{name}");
            Assert.NotNull(service);

            // Nested helpers count: the deleted writer was a private nested class, so a restored one
            // would hide from a members-only check.
            var members = service!.GetMethods(declared).Select(method => method.Name)
                .Concat(service.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(nested => nested.Name));
            foreach (var member in members)
            {
                foreach (var forbidden in writeVocabulary)
                {
                    Assert.False(
                        member.Contains(forbidden, StringComparison.Ordinal),
                        $"{name} declares '{member}', which reintroduces a source-write primitive.");
                }
            }
        }

        var inspector = RepositoryPath("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Inspector");
        foreach (var file in new[] { "XamlAutomationIdProposalService.cs", "CSharpAutomationIdProposalService.cs" })
        {
            var text = File.ReadAllText(Path.Combine(inspector, file));
            foreach (var forbidden in new[]
            {
                "File.WriteAllBytes",
                "File.WriteAllText",
                "File.Replace",
                "File.Move",
                "File.Delete",
                "File.Create",
                "File.Open",
                "File.AppendAll",
                "FileMode.",
                "FileStream",
                "StreamWriter",
                "File.SetAttributes",
                "File.SetUnixFileMode",
            })
            {
                Assert.False(
                    text.Contains(forbidden, StringComparison.Ordinal),
                    $"{file} still references '{forbidden}'.");
            }
        }

        // Route wiring is the other way this could come back: a handler reaching past BuildAsync.
        // The match count is asserted too, so a renamed field cannot make this loop pass vacuously.
        var wired = 0;
        foreach (var file in new[] { "InspectorServer.cs", "InspectorServer.Routes.cs" })
        {
            var text = File.ReadAllText(Path.Combine(inspector, file));
            foreach (Match call in Regex.Matches(text, @"_(?:xaml|csharp)SourceProposalService\.(\w+)"))
            {
                wired++;
                Assert.Equal("BuildAsync", call.Groups[1].Value);
            }
        }
        Assert.Equal(2, wired);
    }

    /// <summary>
    /// GitHub Copilot Canvas is not a trusted approval host in this layer. A Canvas
    /// <c>window.confirm()</c> runs in a surface the embedded page can reach, so it is not evidence
    /// that the local human agreed; only VS Code, whose modal runs in the extension process and
    /// whose owner token never enters webview JavaScript, may mediate a native approval.
    /// </summary>
    [Fact]
    public void CanvasAdvertisesNoApprovalOrSourceAuthorityAndHoldsNoOwnerToken()
    {
        var canvas = RepositoryPath(".github", "extensions", "maui-devflow-canvas");
        Assert.True(Directory.Exists(canvas), canvas);
        Assert.False(File.Exists(Path.Combine(canvas, "native-approval.mjs")));

        // Production modules only — the sibling JS suite names these tokens to assert they are gone.
        foreach (var file in Directory.EnumerateFiles(canvas, "*.mjs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "nativeApproval",
                "native-approval",
                "X-DevFlow-Host-Approval-Token",
                "nativeApprovalToken",
                "approval-confirmations/issue",
                "agent-requests/",
                "applySourceProposal",
                "applyCSharpSourceProposal",
                "getCSharpSourceSelection",
            })
            {
                Assert.False(
                    text.Contains(forbidden, StringComparison.Ordinal),
                    $"{Path.GetFileName(file)} still references '{forbidden}'.");
            }
        }
    }

    /// <summary>
    /// VS Code remains the one trusted native approval host and keeps its in-process modal — but it
    /// must no longer advertise a capability that writes source, or one that hands the page an
    /// editor selection to build a source-writing authority from.
    /// </summary>
    [Fact]
    public void VSCodeKeepsItsInProcessApprovalModalButNoSourceApplyCapability()
    {
        var vscode = RepositoryPath("src", "DevFlow", "js", "vscode-inspector", "src");
        var extension = File.ReadAllText(Path.Combine(vscode, "extension.ts"));

        Assert.Contains("\"nativeApproval\"", extension, StringComparison.Ordinal);
        Assert.Contains("{ modal: true }", extension, StringComparison.Ordinal);
        foreach (var forbidden in new[]
        {
            "applySourceProposal",
            "applyCSharpSourceProposal",
            "getCSharpSourceSelection",
        })
        {
            Assert.DoesNotContain(forbidden, extension, StringComparison.Ordinal);
        }

        var contract = File.ReadAllText(Path.Combine(vscode, "bridge-contract.ts"));
        Assert.Contains("devflow:nativeApproval", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("devflow:applySourceProposal", contract, StringComparison.Ordinal);

        // The owner token is read in the extension process and never handed to webview JavaScript.
        var approval = File.ReadAllText(Path.Combine(vscode, "native-approval.ts"));
        Assert.Contains("X-DevFlow-Host-Approval-Token", approval, StringComparison.Ordinal);
    }

    /// <summary>
    /// The browser page must not offer an authority no host can honour. Source apply and the C#
    /// selection handoff belong to later layers. The layout suppression policy bridge is served
    /// here, but only through a trusted native approval host, so it is checked separately below.
    /// </summary>
    [Fact]
    public void TheInspectorPageOffersNoSourceApplyOrLayoutPolicyCapability()
    {
        var web = RepositoryPath("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Inspector", "Web");
        foreach (var asset in new[] { "inspector-host-bridge.js", "inspector-source.js", "devflow.js" })
        {
            var text = File.ReadAllText(Path.Combine(web, asset));
            foreach (var forbidden in new[]
            {
                "applySourceProposal",
                "applyCSharpSourceProposal",
                "getCSharpSourceSelection",
            })
            {
                Assert.False(
                    text.Contains(forbidden, StringComparison.Ordinal),
                    $"{asset} still references '{forbidden}'.");
            }
        }
    }

    /// <summary>
    /// A layout suppression is a policy-file write, so the page may only reach it through a trusted
    /// native approval host. The registry entry must therefore have no in-page fallback: a fallback
    /// would let a plain browser tab believe it saved a suppression it never wrote. VS Code declares
    /// the capability; the Canvas shell must not, because Canvas has no approval authority.
    /// </summary>
    [Fact]
    public void TheLayoutPolicyBridgeIsGatedOnATrustedNativeApprovalHost()
    {
        var registry = File.ReadAllText(RepositoryPath(
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Inspector", "Web", "inspector-host-bridge.js"));
        var entry = registry[registry.IndexOf("layoutPolicyMutation:", StringComparison.Ordinal)..];
        entry = entry[..entry.IndexOf("}),", StringComparison.Ordinal)];
        Assert.Contains("mode: 'request'", entry, StringComparison.Ordinal);
        Assert.Contains("fallback: null", entry, StringComparison.Ordinal);

        var vscode = RepositoryPath("src", "DevFlow", "js", "vscode-inspector", "src");
        Assert.Contains(
            "layoutPolicyMutation",
            File.ReadAllText(Path.Combine(vscode, "extension.ts")),
            StringComparison.Ordinal);

        var canvas = RepositoryPath(".github", "extensions", "maui-devflow-canvas", "shell.mjs");
        Assert.False(
            File.ReadAllText(canvas).Contains("layoutPolicyMutation", StringComparison.Ordinal),
            "The Canvas shell advertises the layout policy bridge, but Canvas has no approval authority.");
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    /// <summary>
    /// A live broker with every preview flag on, one registered agent, and the per-agent Inspector
    /// materialised — the only configuration in which the source routes carry workflow services, so
    /// a 404 here really means "this route does not exist" rather than "no services attached".
    /// </summary>
    private sealed class InspectorFixture : IAsyncDisposable
    {
        private readonly BrokerServer _broker;
        private readonly CancellationTokenSource _cancellation;
        private readonly AgentHttpServer _agent;
        private readonly ClientWebSocket _socket;
        private readonly HttpClient _http;
        private readonly string _inspectorBase;

        private InspectorFixture(
            BrokerServer broker,
            CancellationTokenSource cancellation,
            AgentHttpServer agent,
            ClientWebSocket socket,
            HttpClient http,
            string inspectorBase)
        {
            _broker = broker;
            _cancellation = cancellation;
            _agent = agent;
            _socket = socket;
            _http = http;
            _inspectorBase = inspectorBase;
        }

        public static async Task<InspectorFixture> StartAsync()
        {
            var brokerPort = TestPorts.Reserve();
            var agentPort = TestPorts.Reserve();
            var broker = new BrokerServer(
                brokerPort,
                TimeSpan.FromMinutes(1),
                previewFlags: PreviewTestFeatures.AllEnabled());
            var cancellation = new CancellationTokenSource();
            _ = broker.RunAsync(cancellation.Token);
            await WaitForBrokerAsync(brokerPort);

            var agent = new AgentHttpServer(agentPort);
            agent.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
            {
                running = true,
                agent = new { name = "DevFlow", instanceId = "source-authority-instance", version = "1" },
                app = new { name = "Source Authority", build = "build-1", packageId = "com.example.source", version = "1.0" },
                device = new { platform = "android", deviceType = "emulator", idiom = "phone" },
                capabilities = new { ui = true, logs = true, mutations = true },
                route = "/",
            })));
            agent.Start();

            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{brokerPort}/ws/agent"), CancellationToken.None);
            await socket.SendAsync(
                Encoding.UTF8.GetBytes($$"""
                    {"type":"register","project":"source-authority-test","tfm":"net10.0","platform":"android","appName":"Source Authority","currentPort":{{agentPort}}}
                    """),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
            var handshake = new byte[4096];
            await socket.ReceiveAsync(handshake, CancellationToken.None);

            var http = new HttpClient();
            using var agents = JsonDocument.Parse(
                await http.GetStringAsync($"http://127.0.0.1:{brokerPort}/api/agents"));
            var agentId = agents.RootElement[0].GetProperty("id").GetString()!;
            var inspectorBase = $"http://127.0.0.1:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}";

            // Materialises the per-agent InspectorServer inside the broker.
            using (await http.GetAsync($"{inspectorBase}/")) { }
            var inspector = await GetInspectorAsync(broker, agentId);
            var token = (string)typeof(InspectorServer)
                .GetField("_readToken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(inspector)!;
            http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", token);

            return new InspectorFixture(broker, cancellation, agent, socket, http, inspectorBase);
        }

        public Task<HttpResponseMessage> PostAsync(string route, string body)
            => _http.PostAsync(
                $"{_inspectorBase}{route}",
                new StringContent(body, Encoding.UTF8, "application/json"));

        private static async Task<InspectorServer> GetInspectorAsync(BrokerServer broker, string agentId)
        {
            var field = typeof(BrokerServer).GetField("_inspectors", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var inspectors = (IReadOnlyDictionary<string, InspectorServer>)field.GetValue(broker)!;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (inspectors.TryGetValue(agentId, out var inspector))
                    return inspector;
                await Task.Delay(25);
            }
            throw new InvalidOperationException("Broker did not create the Inspector.");
        }

        private static async Task WaitForBrokerAsync(int port)
        {
            using var http = new HttpClient();
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/health");
                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(25);
            }
            throw new InvalidOperationException("Broker did not start.");
        }

        public async ValueTask DisposeAsync()
        {
            _http.Dispose();
            try { _socket.Dispose(); } catch { }
            try { _agent.Dispose(); } catch { }
            await _cancellation.CancelAsync();
            _broker.Dispose();
            _cancellation.Dispose();
        }
    }
}
