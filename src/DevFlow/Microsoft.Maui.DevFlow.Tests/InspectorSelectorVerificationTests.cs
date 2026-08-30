using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Agent.Core;
using DriverBoundsInfo = Microsoft.Maui.DevFlow.Driver.BoundsInfo;
using DriverElementInfo = Microsoft.Maui.DevFlow.Driver.ElementInfo;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class InspectorSelectorVerificationTests
{
    [Fact]
    public void AuthoringIssues_MapSelectorFailuresToBlockingStepRemediation()
    {
        var issues = InspectorServer.CreateAuthoringIssues(
        [
            "step 1: selector must resolve exactly one element; it currently reports 9 matches.",
        ],
        [
            "step 1: selector is fragile.",
        ]);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(issues));
        var entries = json.RootElement.EnumerateArray().ToArray();

        Assert.Equal("selector-match-count", entries[0].GetProperty("code").GetString());
        Assert.Equal(1, entries[0].GetProperty("stepSequence").GetInt32());
        Assert.True(entries[0].GetProperty("blocking").GetBoolean());
        Assert.Equal("resolve-selector", entries[0].GetProperty("remediation").GetString());
        Assert.Equal("warning", entries[1].GetProperty("severity").GetString());
    }

    [Fact]
    public async Task SelectorVerify_AmbiguousSelector_ReturnsBoundedValueFreeMatchSummaries()
    {
        var stableKeys = Enumerable.Range(1, 25)
            .ToDictionary(index => index, index => "sha256:" + index.ToString("x64"));
        const string sensitiveText = "CorrectHorseBatteryStaple";
        const string sensitiveValue = "private-control-value";
        const string sensitivePath = @"C:\private\CustomerSecrets.xaml";
        var elements = Enumerable.Range(1, 25)
            .Select(index => new DriverElementInfo
            {
                Id = $"ephemeral-{index}",
                Type = "Button",
                Role = "button",
                AutomationId = "shared",
                StableItemKey = stableKeys[index],
                CollectionScope = "Items",
                Text = sensitiveText,
                Value = sensitiveValue,
                FrameworkProperties = new Dictionary<string, string?> { ["SecretProperty"] = sensitiveValue },
                IsVisible = index % 2 == 1,
                IsEnabled = true,
                Bounds = new DriverBoundsInfo { X = index, Y = index + 1, Width = 40, Height = 24 },
                WindowBounds = new DriverBoundsInfo { X = 0, Y = 0, Width = 320, Height = 640 },
                SourceFile = sensitivePath,
                SourceLine = index,
            })
            .ToArray();

        await using var harness = await SelectorVerificationHarness.StartAsync(elements);
        using var client = new HttpClient();
        var token = await harness.ReadTokenAsync(client);
        using var response = await PostSelectorAsync(client, harness.Url, token, "shared");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var root = body.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(25, root.GetProperty("matchCount").GetInt32());
        Assert.Equal(25, root.GetProperty("totalCount").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(20, root.GetProperty("matches").GetArrayLength());

        var ambiguity = root.GetProperty("ambiguity");
        Assert.Equal(25, ambiguity.GetProperty("totalCount").GetInt32());
        Assert.True(ambiguity.GetProperty("truncated").GetBoolean());
        var matches = ambiguity.GetProperty("matches").EnumerateArray().ToArray();
        Assert.Equal(20, matches.Length);
        Assert.Equal("ephemeral-1", matches[0].GetProperty("id").GetString());
        Assert.Equal("Button", matches[0].GetProperty("type").GetString());
        Assert.Equal("button", matches[0].GetProperty("role").GetString());
        Assert.Equal("shared", matches[0].GetProperty("automationId").GetString());
        Assert.Equal(stableKeys[1], matches[0].GetProperty("stableItemKey").GetString());
        Assert.Equal("Items", matches[0].GetProperty("collectionScope").GetString());
        Assert.True(matches[0].GetProperty("isVisible").GetBoolean());
        Assert.True(matches[0].GetProperty("isEnabled").GetBoolean());
        Assert.Equal(1, matches[0].GetProperty("bounds").GetProperty("x").GetInt32());
        Assert.Equal(320, matches[0].GetProperty("windowBounds").GetProperty("width").GetInt32());
        Assert.True(matches[0].GetProperty("hasSource").GetBoolean());
        Assert.Equal(1, matches[0].GetProperty("sourceLine").GetInt32());

        foreach (var match in matches)
        {
            Assert.False(match.TryGetProperty("text", out _));
            Assert.False(match.TryGetProperty("value", out _));
            Assert.False(match.TryGetProperty("frameworkProperties", out _));
            Assert.False(match.TryGetProperty("sourceFile", out _));
        }

        Assert.DoesNotContain(sensitiveText, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveValue, json, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitivePath, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectorVerify_ScopedStableItem_ResolvesOneRepeatedControl()
    {
        var firstKey = "sha256:" + new string('1', 64);
        var secondKey = "sha256:" + new string('2', 64);
        var elements = new[]
        {
            new DriverElementInfo
            {
                Id = "first",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = firstKey,
                CollectionScope = "TodoList",
                IsVisible = true,
                IsEnabled = true,
            },
            new DriverElementInfo
            {
                Id = "second",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = secondKey,
                CollectionScope = "TodoList",
                IsVisible = true,
                IsEnabled = true,
            },
        };

        await using var harness = await SelectorVerificationHarness.StartAsync(elements);
        using var client = new HttpClient();
        var token = await harness.ReadTokenAsync(client);
        using var response = await PostSelectorAsync(
            client,
            harness.Url,
            token,
            "TodoCheckBox",
            stableItemKey: secondKey,
            collectionScope: "TodoList");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("matchCount").GetInt32());
        Assert.Equal("stable-item-key", root.GetProperty("quality").GetString());
        var element = root.GetProperty("element");
        Assert.Equal(secondKey, element.GetProperty("stableItemKey").GetString());
        Assert.Equal("TodoList", element.GetProperty("collectionScope").GetString());
    }

    [Fact]
    public async Task SelectorVerify_UniqueSelector_PreservesExistingElementResponse()
    {
        var element = new DriverElementInfo
        {
            Id = "ephemeral-save",
            Type = "Button",
            Role = "button",
            AutomationId = "save",
            StableItemKey = "customer@example.com",
            CollectionScope = "Customers",
            Text = "Save changes",
            IsVisible = true,
            IsEnabled = true,
            SourceFile = @"C:\source\Page.xaml",
            SourceLine = 42,
        };

        await using var harness = await SelectorVerificationHarness.StartAsync([element]);
        using var client = new HttpClient();
        var token = await harness.ReadTokenAsync(client);
        using var response = await PostSelectorAsync(client, harness.Url, token, "save");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var root = body.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("matchCount").GetInt32());
        Assert.Equal("AutomationId", root.GetProperty("quality").GetString());
        var result = root.GetProperty("element");
        Assert.Equal("Button", result.GetProperty("type").GetString());
        Assert.Equal("save", result.GetProperty("automationId").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("stableItemKey").ValueKind);
        Assert.Equal("Save changes", result.GetProperty("text").GetString());
        Assert.True(result.GetProperty("hasSource").GetBoolean());
        Assert.False(root.TryGetProperty("ambiguity", out _));
        Assert.DoesNotContain("customer@example.com", json, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> PostSelectorAsync(
        HttpClient client,
        string inspectorUrl,
        string token,
        string automationId,
        string? stableItemKey = null,
        string? collectionScope = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{inspectorUrl}/api/flows/selector/verify")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    selector = new
                    {
                        automationId,
                        stableItemKey,
                        collectionScope,
                    },
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Add("X-DevFlow-Inspector-Token", token);
        return await client.SendAsync(request);
    }

    private sealed class SelectorVerificationHarness : IAsyncDisposable
    {
        private readonly AgentHttpServer _agent;
        private readonly InspectorServer _inspector;

        private SelectorVerificationHarness(AgentHttpServer agent, InspectorServer inspector, string url)
        {
            _agent = agent;
            _inspector = inspector;
            Url = url;
        }

        public string Url { get; }

        public static async Task<SelectorVerificationHarness> StartAsync(IReadOnlyList<DriverElementInfo> elements)
        {
            var agent = new AgentHttpServer(FreePort());
            agent.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
            {
                running = true,
                app = new { name = "Selector verify test" },
                device = new { platform = "test" },
            })));
            agent.MapGet("/api/v1/ui/elements", _ => Task.FromResult(HttpResponse.Json(elements)));
            agent.Start();

            var port = FreePort();
            var inspector = new InspectorServer(port, "127.0.0.1", agent.Port);
            inspector.Start();
            var url = $"http://127.0.0.1:{port}";
            using var client = new HttpClient();
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync($"{url}/");
                    if (response.IsSuccessStatusCode)
                        return new SelectorVerificationHarness(agent, inspector, url);
                }
                catch (HttpRequestException)
                {
                }
                await Task.Delay(25);
            }

            await inspector.StopAsync();
            inspector.Dispose();
            agent.Dispose();
            throw new InvalidOperationException("Inspector did not start.");
        }

        public async Task<string> ReadTokenAsync(HttpClient client)
        {
            var html = await client.GetStringAsync($"{Url}/");
            var token = Regex.Match(
                html,
                "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            return token;
        }

        public async ValueTask DisposeAsync()
        {
            await _inspector.StopAsync();
            _inspector.Dispose();
            _agent.Dispose();
        }
    }

    private static int FreePort() => TestPorts.Reserve();
}
