using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Maui;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class SetPropertyBindableTests
{
    private const string SiblingXaml = """
        <ContentView xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
          <VerticalStackLayout>
            <Label Text="First" />
            <Label Text="Second" />
          </VerticalStackLayout>
        </ContentView>
        """;

    [Fact]
    public async Task GetPropertyAsync_AutomationIdWithSpace_ResolvesBindingContext()
    {
        var label = new Label
        {
            AutomationId = "Alice Johnson",
            BindingContext = new Person { Name = "Alice Johnson" }
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(label);
        var id = await harness.GetElementIdAsync(label.AutomationId);

        Assert.Equal("Alice Johnson", id);
        Assert.Equal("Alice Johnson", await harness.Client.GetPropertyAsync(id, "BindingContext.Name"));
    }

    private sealed class Person
    {
        public string Name { get; init; } = "";
    }

    [Theory]
    [InlineData("Alice%20Johnson")]
    [InlineData("Alice+Johnson")]
    [InlineData("Alice/Johnson")]
    [InlineData("Alice?Johnson#name")]
    [InlineData("100%")]
    [InlineData("Jos\u00e9 \u674e")]
    public async Task ElementEndpoints_ReservedCharactersInId_RoundTripThroughClient(string id)
    {
        var label = new Label { AutomationId = id, Text = "Original" };
        using var harness = await SetPropertyTestHarness.CreateAsync(label);
        Assert.Equal(id, await harness.GetElementIdAsync(id));

        var element = await harness.Client.GetElementAsync(id);
        Assert.NotNull(element);
        Assert.Equal(id, element.Id);

        var descriptors = await harness.Client.GetPropertyDescriptorsAsync(id);
        Assert.Contains(descriptors.GetProperty("properties").EnumerateArray(),
            property => property.GetProperty("name").GetString() == nameof(Label.Text));

        Assert.True(await harness.Client.SetPropertyAsync(id, nameof(Label.Text), "Updated"));
        Assert.Equal("Updated", label.Text);
        Assert.Equal("Updated", await harness.Client.GetPropertyAsync(id, nameof(Label.Text)));

        var result = await harness.Client.SetPropertyResultAsync(
            id, nameof(Label.Text), "Result updated", captureEpoch: null, registryGeneration: null);
        Assert.True(result.Success);
        Assert.Equal("Result updated", label.Text);
        Assert.Equal("Result updated", await harness.Client.GetPropertyAsync(id, nameof(Label.Text)));
    }

    [Fact]
    public async Task SetPropertyAsync_UpdatesButtonText_ThroughAgentEndpoint()
    {
        var button = new Button
        {
            AutomationId = "set-property-button",
            Text = "Original"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(button);
        var buttonId = await harness.GetElementIdAsync(button.AutomationId!);

        var success = await harness.Client.SetPropertyAsync(buttonId, "text", "Updated");

        Assert.True(success);
        Assert.Equal("Updated", button.Text);
        Assert.Equal("Updated", await harness.Client.GetPropertyAsync(buttonId, nameof(Button.Text)));
    }

    [Fact]
    public async Task PropertyDescriptors_ReportRuntimeTypesChoicesConstraintsAndValues()
    {
        var label = new Label
        {
            AutomationId = "property-descriptor-label",
            Text = "Hello",
            FontAttributes = FontAttributes.Bold,
            FontSize = 24,
            Opacity = 0.5
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(label);
        var labelId = await harness.GetElementIdAsync(label.AutomationId!);
        var result = await harness.Client.GetPropertyDescriptorsAsync(labelId);
        var properties = result.GetProperty("properties").EnumerateArray().ToArray();

        var text = properties.Single(property => property.GetProperty("name").GetString() == nameof(Label.Text));
        Assert.Equal("text", text.GetProperty("kind").GetString());
        Assert.Equal("Hello", text.GetProperty("value").GetString());
        Assert.True(text.GetProperty("writable").GetBoolean());

        var fontAttributes = properties.Single(property => property.GetProperty("name").GetString() == nameof(Label.FontAttributes));
        Assert.Equal("enum", fontAttributes.GetProperty("kind").GetString());
        Assert.Contains("Bold", fontAttributes.GetProperty("choices").EnumerateArray().Select(choice => choice.GetString()));

        var opacity = properties.Single(property => property.GetProperty("name").GetString() == nameof(VisualElement.Opacity));
        Assert.Equal("number", opacity.GetProperty("kind").GetString());
        Assert.Equal(0, opacity.GetProperty("min").GetDouble());
        Assert.Equal(1, opacity.GetProperty("max").GetDouble());
        Assert.Equal(0.05, opacity.GetProperty("step").GetDouble());
        Assert.Equal("0.5", opacity.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SetPropertyThenPersist_SiblingText_UpdatesOnlyItsSourceAttribute(int index)
    {
        var root = Path.Combine(Path.GetTempPath(), $"devflow-sibling-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "App.csproj");
        var source = Path.Combine(root, "MainPage.xaml");
        await File.WriteAllTextAsync(project, "<Project />");
        await File.WriteAllTextAsync(source, SiblingXaml);
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "Second" } };
        var view = new SourceMappedView
        {
            Content = new VerticalStackLayout { Children = { labels[0], labels[1] } }
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(
            new SiblingMapProvider(XamlSourceMap.Parse(SiblingXaml, "MainPage.xaml")!), view);
        using var inspector = new InspectorServer(
            SetPropertyTestHarness.GetFreePort(), "127.0.0.1", harness.Port,
            embedToken: null, agentId: null, appName: null, platform: null, project, sessionId: null);
        inspector.Start();
        try
        {
            var siblings = (await harness.Client.QueryAsync(type: "Label")).ToArray();
            for (var sibling = 0; sibling < siblings.Length; sibling++)
            {
                var detail = await harness.Client.GetElementAsync(siblings[sibling].Id);
                Assert.NotNull(detail);
                siblings[sibling] = detail;
            }
            Assert.Equal(new int?[] { 3, 4 }, siblings.Select(element => element.SourceLine));
            var id = siblings[index].Id;
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{inspector.Port}/") };
            var html = await http.GetStringAsync("");
            var token = Regex.Match(html, "<meta\\s+name=\"devflow-inspector-token\"\\s+content=\"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(token);
            http.DefaultRequestHeaders.Add("X-DevFlow-Inspector-Token", token);
            var original = labels[index].Text;

            // Even an edit to the other sibling's literal must retain the original declaration.
            foreach (var value in new[] { labels[1 - index].Text, "Saved twice" })
            {
                var json = JsonSerializer.Serialize(new { elementId = id, name = "Text", value });
                using var live = await http.PostAsync("api/setProperty", new StringContent(json, Encoding.UTF8, "application/json"));
                Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                var edited = await harness.Client.GetElementAsync(id);
                using var saved = await http.PostAsync("api/persistProperty", new StringContent(json, Encoding.UTF8, "application/json"));
                var response = await saved.Content.ReadAsStringAsync();

                Assert.True(saved.IsSuccessStatusCode,
                    $"Apply to XAML returned {(int)saved.StatusCode}: {response}; source before edit: " +
                    $"{siblings[index].SourceFile}:{siblings[index].SourceLine}; after edit: {edited?.SourceFile}:{edited?.SourceLine}");
                Assert.Equal(siblings[index].SourceLine, edited?.SourceLine);
                Assert.Equal(SiblingXaml.Replace($"Text=\"{original}\"", $"Text=\"{value}\"", StringComparison.Ordinal),
                    await File.ReadAllTextAsync(source));
                Assert.Equal(value, labels[index].Text);
            }

            var external = await File.ReadAllTextAsync(source) + "\n<!-- external edit -->";
            await File.WriteAllTextAsync(source, external);
            var blocked = JsonSerializer.Serialize(new { elementId = id, name = "Text", value = "Stale edit" });
            using var staleLive = await http.PostAsync("api/setProperty", new StringContent(blocked, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, staleLive.StatusCode);
            using var staleSave = await http.PostAsync("api/persistProperty", new StringContent(blocked, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Conflict, staleSave.StatusCode);
            Assert.Equal(external, await File.ReadAllTextAsync(source));
        }
        finally
        {
            await inspector.StopAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("First")]
    [InlineData("{Binding Title}")]
    public async Task SetPropertyAsync_AmbiguousSiblingText_DoesNotCreateSourceMetadata(string otherText)
    {
        var xaml = SiblingXaml.Replace("Text=\"Second\"", $"Text=\"{otherText}\"", StringComparison.Ordinal);
        var labels = new[] { new Label { Text = "First" }, new Label { Text = "First" } };
        var view = new SourceMappedView
        {
            Content = new VerticalStackLayout { Children = { labels[0], labels[1] } }
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(
            new SiblingMapProvider(XamlSourceMap.Parse(xaml, "MainPage.xaml")!), view);
        var siblings = (await harness.Client.QueryAsync(type: "Label")).ToArray();
        Assert.Equal(2, siblings.Length);

        Assert.True(await harness.Client.SetPropertyAsync(siblings[0].Id, "Text", "Edited"));

        foreach (var sibling in siblings)
        {
            var detail = await harness.Client.GetElementAsync(sibling.Id);
            Assert.NotNull(detail);
            Assert.Null(detail.SourceFile);
        }
    }

    [Fact]
    public async Task UiEventsWebSocket_PublishesTreeChangeAfterPropertyMutation()
    {
        var button = new Button
        {
            AutomationId = "event-stream-button",
            Text = "Original"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(button);
        var buttonId = await harness.GetElementIdAsync(button.AutomationId!);
        using var socket = new ClientWebSocket();
        using var setupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"ws://localhost:{harness.Port}/ws/v1/ui/events"), setupTimeout.Token);
        var subscription = Encoding.UTF8.GetBytes("{\"type\":\"subscribe\",\"data\":{\"events\":[\"treeChange\"]}}");
        await socket.SendAsync(subscription, WebSocketMessageType.Text, true, setupTimeout.Token);

        Assert.True(await harness.Client.SetPropertyAsync(buttonId, nameof(Button.Text), "Updated"));

        using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var message = await ReceiveMessageAsync(socket, receiveTimeout.Token);
            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var type) && type.GetString() == "treeChange")
            {
                Assert.Equal(buttonId, root.GetProperty("data").GetProperty("elementId").GetString());
                break;
            }
        }
    }

    [Fact]
    public async Task UiEventsWebSocket_PublishesCorrelatedActionLifecycle()
    {
        var button = new Button
        {
            AutomationId = "action-event-button",
            Text = "Run"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(button);
        harness.Client.MutationLeaseHolderKind = "mcp";
        var target = Assert.Single(await harness.Client.QueryAsync(automationId: button.AutomationId));
        using var socket = await ConnectUiEventsAsync(
            harness.Port,
            "ui-action-started",
            "ui-action-completed");

        using (harness.Client.UseActionContext(
            actionId: "action-test",
            frameId: "frame-test",
            coordinateSpace: "appLogical",
            x: 123,
            y: 45))
        {
            var result = await harness.Client.TapResultAsync(
                target.Id,
                target.CaptureEpoch,
                target.RegistryGeneration);
            Assert.True(result.Success);
        }

        using var started = await ReceiveUiEventAsync(
            socket,
            "ui-action-started",
            "action-test");
        var startData = started.RootElement.GetProperty("data");
        Assert.Equal("tap", startData.GetProperty("kind").GetString());
        Assert.Equal(target.Id, startData.GetProperty("elementId").GetString());
        Assert.Equal(123, startData.GetProperty("x").GetDouble());
        Assert.Equal(45, startData.GetProperty("y").GetDouble());
        Assert.Equal(target.CaptureEpoch, startData.GetProperty("captureEpoch").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(
            startData.GetProperty("diagnosticInputsRevision").GetString()));
        var correlation = startData.GetProperty("correlation");
        Assert.Equal("mcp", correlation.GetProperty("source").GetString());
        Assert.Equal("frame-test", correlation.GetProperty("frameId").GetString());
        Assert.Equal("appLogical", correlation.GetProperty("coordinateSpace").GetString());

        using var completed = await ReceiveUiEventAsync(
            socket,
            "ui-action-completed",
            "action-test");
        var completedData = completed.RootElement.GetProperty("data");
        Assert.True(completedData.GetProperty("success").GetBoolean());
        Assert.Equal("completed", completedData.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task PointerHeartbeat_DoesNotAdvanceDiagnosticInputsRevision()
    {
        using var harness = await SetPropertyTestHarness.CreateAsync(new Button
        {
            AutomationId = "heartbeat-button",
            Text = "Run"
        });

        var start = await harness.Client.StartPointerSessionAsync(new PointerSessionStartRequest
        {
            SessionId = "heartbeat-session",
            PointerId = 1,
            PointerType = "touch",
            CoordinateSpace = "windowLogical",
            X = 5,
            Y = 6,
            ElapsedMs = 0
        });
        Assert.True(start.Success);

        var before = await harness.Client.GetTreeSnapshotAsync(includeNative: false);
        Assert.NotNull(before);
        Assert.True((await harness.Client.MovePointerSessionAsync(
            "heartbeat-session",
            1,
            [])).Success);
        var after = await harness.Client.GetTreeSnapshotAsync(includeNative: false);
        Assert.NotNull(after);
        Assert.Equal(before.DiagnosticInputsRevision, after.DiagnosticInputsRevision);

        Assert.True((await harness.Client.CancelPointerSessionAsync(
            "heartbeat-session",
            "test-cleanup")).Success);
    }

    [Fact]
    public async Task UiEventsWebSocket_DoesNotPublishSensitiveActionPayloads()
    {
        using var harness = await SetPropertyTestHarness.CreateAsync(new Entry
        {
            AutomationId = "secret-entry"
        });
        harness.Client.MutationLeaseHolderKind = "mcp";
        using var socket = await ConnectUiEventsAsync(
            harness.Port,
            "ui-action-started",
            "ui-action-completed");

        const string secret = "do-not-publish-this-value";
        using (harness.Client.UseActionContext(actionId: "secret-action"))
        {
            var result = await harness.Client.FillResultAsync(
                "missing-element",
                secret,
                captureEpoch: null,
                registryGeneration: null);
            Assert.False(result.Success);
        }

        using var started = await ReceiveUiEventAsync(
            socket,
            "ui-action-started",
            "secret-action");
        using var completed = await ReceiveUiEventAsync(
            socket,
            "ui-action-completed",
            "secret-action");
        var lifecycleJson = started.RootElement.GetRawText()
            + completed.RootElement.GetRawText();
        Assert.DoesNotContain(secret, lifecycleJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\"", lifecycleJson, StringComparison.Ordinal);
        Assert.False(completed.RootElement
            .GetProperty("data")
            .GetProperty("success")
            .GetBoolean());
    }

    [Fact]
    public async Task UiEventsWebSocket_BatchPublishesOneLifecyclePerLogicalAction()
    {
        var button = new Button
        {
            AutomationId = "batch-action-button",
            Text = "Run"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(button);
        harness.Client.MutationLeaseHolderKind = "mcp";
        var target = Assert.Single(await harness.Client.QueryAsync(automationId: button.AutomationId));
        using var socket = await ConnectUiEventsAsync(
            harness.Port,
            "ui-action-started",
            "ui-action-completed");

        using (harness.Client.UseActionContext(actionId: "batch-container"))
        {
            var response = await harness.Client.BatchAsync(
                [
                    new JsonObject
                    {
                        ["action"] = "tap",
                        ["elementId"] = target.Id
                    },
                    new JsonObject
                    {
                        ["action"] = "tap",
                        ["elementId"] = target.Id
                    }
                ],
                continueOnError: false,
                captureEpoch: target.CaptureEpoch,
                registryGeneration: target.RegistryGeneration);
            Assert.True(response.GetProperty("success").GetBoolean());
        }

        var startedIds = new HashSet<string>(StringComparer.Ordinal);
        var completedIds = new HashSet<string>(StringComparer.Ordinal);
        while (startedIds.Count < 2 || completedIds.Count < 2)
        {
            using var message = await ReceiveUiEventAsync(socket);
            var type = message.RootElement.GetProperty("type").GetString();
            var id = message.RootElement
                .GetProperty("data")
                .GetProperty("correlation")
                .GetProperty("actionId")
                .GetString()!;
            if (type == "ui-action-started")
                startedIds.Add(id);
            else if (type == "ui-action-completed")
                completedIds.Add(id);
        }

        Assert.Equal(2, startedIds.Count);
        Assert.True(startedIds.SetEquals(completedIds));
        Assert.DoesNotContain("batch-container", startedIds);
    }

    [Fact]
    public async Task UiEventsWebSocket_ReplayPublishesDistinctReplayLifecycles()
    {
        var button = new Button
        {
            AutomationId = "replay-button",
            Text = "Run"
        };
        var entry = new Entry
        {
            AutomationId = "replay-entry"
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(button, entry);
        using var socket = await ConnectUiEventsAsync(
            harness.Port,
            "ui-action-started",
            "ui-action-completed");
        var flow = new MauiFlow
        {
            Name = "lifecycle",
            Steps =
            {
                new FlowStep
                {
                    Seq = 1,
                    Action = FlowActions.Tap,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = button.AutomationId }
                    }
                },
                new FlowStep
                {
                    Seq = 2,
                    Action = FlowActions.Fill,
                    Args = new FlowStepArgs
                    {
                        Selector = new FlowSelector { AutomationId = entry.AutomationId },
                        Text = "value"
                    }
                }
            }
        };

        using (harness.Client.UseActionContext(source: "replay"))
        {
            var report = await new FlowReplayer(
                harness.Client,
                pollTries: 1,
                pollGapMs: 0).ReplayAsync(flow);
            Assert.True(report.Ok, JsonSerializer.Serialize(report));
        }

        var startedIds = new HashSet<string>(StringComparer.Ordinal);
        var completedIds = new HashSet<string>(StringComparer.Ordinal);
        while (startedIds.Count < 2 || completedIds.Count < 2)
        {
            using var message = await ReceiveUiEventAsync(socket);
            var data = message.RootElement.GetProperty("data");
            Assert.Equal(
                "replay",
                data.GetProperty("correlation").GetProperty("source").GetString());
            Assert.True(data.TryGetProperty("captureEpoch", out var captureEpoch));
            Assert.True(captureEpoch.GetInt64() > 0);
            var id = data.GetProperty("correlation").GetProperty("actionId").GetString()!;
            if (message.RootElement.GetProperty("type").GetString() == "ui-action-started")
                startedIds.Add(id);
            else
                completedIds.Add(id);
        }

        Assert.Equal(2, startedIds.Count);
        Assert.True(startedIds.SetEquals(completedIds));
    }

    [Fact]
    public async Task SetPropertyAsync_UpdatesInheritedBindableProperty_ThroughAgentEndpoint()
    {
        var button = new Button
        {
            AutomationId = "set-property-isenabled-button",
            IsEnabled = true
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(button);
        var buttonId = await harness.GetElementIdAsync(button.AutomationId!);

        var success = await harness.Client.SetPropertyAsync(buttonId, nameof(VisualElement.IsEnabled), bool.FalseString);

        Assert.True(success);
        Assert.False(button.IsEnabled);
        Assert.Equal(bool.FalseString, await harness.Client.GetPropertyAsync(buttonId, nameof(VisualElement.IsEnabled)));
    }

    [Fact]
    public async Task SetPropertyAsync_FallsBackToClrSetter_WhenBindableFieldMapsDifferentProperty()
    {
        var control = new MismatchedBindablePropertyView
        {
            AutomationId = "set-property-fallback-view",
            StatusText = "Original"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(control);
        var controlId = await harness.GetElementIdAsync(control.AutomationId!);

        var success = await harness.Client.SetPropertyAsync(controlId, nameof(MismatchedBindablePropertyView.StatusText), "Updated");

        Assert.True(success);
        Assert.Equal("Updated", control.StatusText);
        Assert.Null(control.BackingText);
        Assert.Equal("Updated", await harness.Client.GetPropertyAsync(controlId, nameof(MismatchedBindablePropertyView.StatusText)));
    }

    [Fact]
    public async Task MutationObservation_UsesCoercedRuntimePropertyValue()
    {
        var control = new CoercingBindablePropertyView
        {
            AutomationId = "coercing-property-view"
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(control);
        var controlId = await harness.GetElementIdAsync(control.AutomationId!);
        Assert.True(await harness.Client.SetPropertyAsync(controlId, nameof(control.Score), "20"));

        var observation = await harness.Service.CreateMutationObservationAsync(new HttpRequest
        {
            Method = "PUT",
            Path = $"/api/v1/ui/elements/{controlId}/properties/{nameof(control.Score)}",
            RouteParams =
            {
                ["id"] = controlId,
                ["name"] = nameof(control.Score)
            },
            Body = "{\"value\":\"20\"}"
        });

        Assert.NotNull(observation);
        Assert.Equal("10", observation.Value);
    }

    private sealed class SetPropertyTestHarness : IDisposable
    {
        private readonly MauiDevFlowAgentService _service;

        public AgentClient Client { get; }
        public DevFlowAgentService Service => _service;
        public int Port => _service.Port;

        private SetPropertyTestHarness(MauiDevFlowAgentService service, AgentClient client)
        {
            _service = service;
            Client = client;
        }

        public static Task<SetPropertyTestHarness> CreateAsync(params View[] views)
            => CreateAsync(null, views);

        public static async Task<SetPropertyTestHarness> CreateAsync(IXamlSourceMapProvider? sourceMaps, params View[] views)
        {
            var app = new TestApplication(views);

            var service = new TestAgentService(new AgentOptions { Port = GetFreePort() });
            if (sourceMaps is not null)
                service.Walker.SourceMapProvider = sourceMaps;
            var client = new AgentClient("localhost", service.Port);

            service.StartServerOnly(new ImmediateDispatcher());
            service.BindApp(app);

            var status = await WaitForStatusAsync(client);
            Assert.NotNull(status);
            Assert.True(status!.Running);

            return new SetPropertyTestHarness(service, client);
        }

        public async Task<string> GetElementIdAsync(string automationId)
        {
            for (var i = 0; i < 10; i++)
            {
                var results = await Client.QueryAsync(automationId: automationId);
                var match = results.FirstOrDefault();
                if (match != null)
                    return match.Id;

                await Task.Delay(100);
            }

            throw new InvalidOperationException($"Could not find element with automation ID '{automationId}'.");
        }

        public void Dispose()
        {
            Client.Dispose();
            _service.Dispose();
        }

        private static async Task<AgentStatus?> WaitForStatusAsync(AgentClient client)
        {
            for (var i = 0; i < 10; i++)
            {
                var status = await client.GetStatusAsync();
                if (status != null)
                    return status;

                await Task.Delay(100);
            }

            return null;
        }

        public static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class TestAgentService(AgentOptions options) : MauiDevFlowAgentService(options)
    {
        public VisualTreeWalker Walker { get; private set; } = null!;

        protected override VisualTreeWalker CreateTreeWalker() => Walker = base.CreateTreeWalker();

        protected override PointerStreamSupport GetPointerStreamSupport()
            => new(
                Supported: true,
                Fidelity: "test",
                RequiresOptIn: false,
                Reason: null);

        protected override Task<PointerSessionBackendStartResult> StartPointerSessionBackendAsync(
            PointerSessionStartRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(PointerSessionBackendStartResult.Started(new object(), "test"));

        protected override Task<PointerSessionBackendResult> MovePointerSessionBackendAsync(
            object state,
            IReadOnlyList<PointerSessionSample> samples,
            CancellationToken cancellationToken)
            => Task.FromResult(PointerSessionBackendResult.Applied());

        protected override Task<PointerSessionBackendResult> EndPointerSessionBackendAsync(
            object state,
            PointerSessionSample sample,
            CancellationToken cancellationToken)
            => Task.FromResult(PointerSessionBackendResult.Applied());

        protected override Task<PointerSessionBackendResult> CancelPointerSessionBackendAsync(
            object state,
            string reason)
            => Task.FromResult(PointerSessionBackendResult.Applied());
    }

    private sealed class SourceMappedView : ContentView;

    private sealed class SiblingMapProvider(XamlSourceMap map) : IXamlSourceMapProvider
    {
        public XamlSourceMap? GetMap(string fullTypeName)
            => fullTypeName == typeof(SourceMappedView).FullName ? map : null;
    }

    private static async Task<MemoryStream> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("UI event stream closed before treeChange was received.");
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return message;
    }

    private static async Task<ClientWebSocket> ConnectUiEventsAsync(
        int port,
        params string[] events)
    {
        var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(
            new Uri($"ws://localhost:{port}/ws/v1/ui/events"),
            timeout.Token);
        var subscription = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = "subscribe",
            data = new { events }
        }));
        await socket.SendAsync(
            subscription,
            WebSocketMessageType.Text,
            true,
            timeout.Token);
        return socket;
    }

    private static async Task<JsonDocument> ReceiveUiEventAsync(
        ClientWebSocket socket,
        string? expectedType = null,
        string? expectedActionId = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using var message = await ReceiveMessageAsync(socket, timeout.Token);
            var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            var messageType = root.TryGetProperty("type", out var type)
                ? type.GetString()
                : null;
            var typeMatches = expectedType is null
                ? messageType is "ui-action-started" or "ui-action-completed"
                : messageType == expectedType;
            var actionMatches = expectedActionId is null
                || root.TryGetProperty("data", out var data)
                && data.TryGetProperty("correlation", out var correlation)
                && correlation.TryGetProperty("actionId", out var actionId)
                && actionId.GetString() == expectedActionId;
            if (typeMatches && actionMatches)
                return document;
            document.Dispose();
        }
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

    private sealed class MismatchedBindablePropertyView : ContentView
    {
        public static readonly BindableProperty BackingTextProperty =
            BindableProperty.Create(nameof(BackingText), typeof(string), typeof(MismatchedBindablePropertyView));

        // This intentionally mismatches the CLR property name to verify the agent
        // falls back to PropertyInfo.SetValue instead of writing the wrong property.
        public static readonly BindableProperty StatusTextProperty = BackingTextProperty;

        public string? BackingText
        {
            get => (string?)GetValue(BackingTextProperty);
            set => SetValue(BackingTextProperty, value);
        }

        public string? StatusText { get; set; }
    }

    private sealed class CoercingBindablePropertyView : ContentView
    {
        public static readonly BindableProperty ScoreProperty = BindableProperty.Create(
            nameof(Score),
            typeof(int),
            typeof(CoercingBindablePropertyView),
            defaultValue: 0,
            coerceValue: static (_, value) => Math.Min((int)value, 10));

        public int Score
        {
            get => (int)GetValue(ScoreProperty);
            set => SetValue(ScoreProperty, value);
        }
    }

    private sealed class TestApplication : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children;

        public TestApplication(IEnumerable<View> views)
        {
            _children = views.Cast<IVisualTreeElement>().ToArray();
        }

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
    }
}
