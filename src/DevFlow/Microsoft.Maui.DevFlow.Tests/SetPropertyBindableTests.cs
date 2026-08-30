using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Properties;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;
using CorePropertyMutationResponse = Microsoft.Maui.DevFlow.Agent.Core.Properties.PropertyMutationResponse;
using CoreElementInfo = Microsoft.Maui.DevFlow.Agent.Core.ElementInfo;

namespace Microsoft.Maui.DevFlow.Tests;

public class SetPropertyBindableTests
{
    [Fact]
    public void SelectorObservationIndex_DuplicateStableIds_AreExcludedFromTopology()
    {
        var first = new CoreElementInfo { Id = "shared", Type = "Button" };
        var duplicate = new CoreElementInfo { Id = "shared", Type = "ContentView" };

        var known = DevFlowAgentService.IndexSelectorObservationElements([first, duplicate]);

        Assert.Empty(known);
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

        // ConnectAsync only proves the handshake completed. The agent registers the subscription
        // inside its WebSocket handler, which can still be scheduled, and PublishUiEvent only fans
        // out to already-registered subscriptions. The handler queues a lifecycle frame as its
        // first act after registering, so receiving any frame is the deterministic signal that a
        // mutation published from now on will reach this socket.
        using (await ReceiveMessageAsync(socket, setupTimeout.Token))
        {
        }

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
    public async Task SetPropertyAsync_UnknownClrSetter_IsBlockedUnlessExplicitlyAllowed()
    {
        var control = new MismatchedBindablePropertyView
        {
            AutomationId = "set-property-fallback-view",
            StatusText = "Original"
        };

        using var harness = await SetPropertyTestHarness.CreateAsync(control);
        var controlId = await harness.GetElementIdAsync(control.AutomationId!);

        var rejected = await harness.Client.SetPropertyDetailedAsync(
            controlId,
            nameof(MismatchedBindablePropertyView.StatusText),
            "Updated");

        Assert.False(rejected.Success);
        Assert.Equal("unknown", rejected.MutationSafety);
        Assert.Equal("Original", control.StatusText);

        var forced = await harness.Client.SetPropertyDetailedAsync(
            controlId,
            nameof(MismatchedBindablePropertyView.StatusText),
            "Updated",
            allowUnsafe: true);

        Assert.True(forced.Success);
        Assert.Equal("Updated", control.StatusText);
        Assert.Null(control.BackingText);
        Assert.Equal("Updated", await harness.Client.GetPropertyAsync(controlId, nameof(MismatchedBindablePropertyView.StatusText)));
    }

    [Fact]
    public async Task BoundProperty_IsReadOnlyByDefault_AndBindingRemainsActive()
    {
        var source = new BindingSource { Value = "Original" };
        var label = new Label
        {
            AutomationId = "bound-property-label",
            BindingContext = source
        };
        label.SetBinding(Label.TextProperty, nameof(BindingSource.Value), mode: BindingMode.OneWay);

        using var harness = await SetPropertyTestHarness.CreateAsync(label);
        var labelId = await harness.GetElementIdAsync(label.AutomationId!);
        var descriptors = await harness.Client.GetPropertyDescriptorSetAsync(labelId);
        var text = Assert.Single(descriptors!.Properties, property => property.Name == nameof(Label.Text));

        Assert.Equal("binding", text.ValueSource);
        Assert.Equal("bindingWouldBeReplaced", text.MutationSafety);
        Assert.False(text.Writable);
        Assert.True(text.ForceWritable);

        var rejected = await harness.Client.SetPropertyDetailedAsync(labelId, nameof(Label.Text), "Inspector");
        Assert.False(rejected.Success);
        Assert.Equal("bindingWouldBeReplaced", rejected.MutationSafety);
        Assert.Equal("Original", label.Text);

        var after = await harness.Client.GetPropertyDescriptorSetAsync(labelId);
        var textAfter = Assert.Single(after!.Properties, property => property.Name == nameof(Label.Text));
        Assert.Equal("binding", textAfter.ValueSource);
    }

    [Fact]
    public async Task DynamicResourceProperty_IsReadOnlyByDefault()
    {
        var label = new Label { AutomationId = "dynamic-resource-label" };
        label.SetDynamicResource(Label.TextColorProperty, "AccentColor");

        using var harness = await SetPropertyTestHarness.CreateAsync(label);
        var labelId = await harness.GetElementIdAsync(label.AutomationId!);
        var descriptors = await harness.Client.GetPropertyDescriptorSetAsync(labelId);
        var textColor = Assert.Single(
            descriptors!.Properties,
            property => property.Name == nameof(Label.TextColor));

        Assert.Equal("dynamicResource", textColor.ValueSource);
        Assert.Equal("dynamicResourceWouldBeReplaced", textColor.MutationSafety);
        Assert.False(textColor.Writable);

        var rejected = await harness.Client.SetPropertyDetailedAsync(
            labelId,
            nameof(Label.TextColor),
            "#ff0000");
        Assert.False(rejected.Success);
        Assert.Equal("dynamicResourceWouldBeReplaced", rejected.MutationSafety);
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

    [Fact]
    public async Task MutationObservation_RecordsMeasuredSelectorQualityAndPropertySource()
    {
        var first = new Button { AutomationId = "duplicate-action", Text = "First" };
        var second = new Button { AutomationId = "duplicate-action", Text = "Second" };
        using var harness = await SetPropertyTestHarness.CreateAsync(first, second);
        var firstId = (await harness.Client.QueryAsync(automationId: "duplicate-action"))[0].Id;

        var tapObservation = await harness.Service.CreateMutationObservationAsync(new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/tap",
            Body = JsonSerializer.Serialize(new { elementId = firstId })
        });

        Assert.NotNull(tapObservation);
        Assert.Equal(2, tapObservation.MatchCount);
        Assert.Equal("ambiguous", tapObservation.Quality);
        Assert.NotEmpty(tapObservation.FragilityReasons!);

        var propertyObservation = await harness.Service.CreateMutationObservationAsync(
            new HttpRequest
            {
                Method = "PUT",
                Path = $"/api/v1/ui/elements/{firstId}/properties/{nameof(Button.Text)}",
                RouteParams =
                {
                    ["id"] = firstId,
                    ["name"] = nameof(Button.Text)
                },
                Body = "{\"value\":\"Updated\"}"
            },
            HttpResponse.Json(new CorePropertyMutationResponse
            {
                Success = true,
                ValueSource = PropertyValueSources.Local,
                MutationSafety = PropertyMutationSafetyKinds.Safe
            }));

        Assert.NotNull(propertyObservation);
        Assert.Equal(PropertyValueSources.Local, propertyObservation.ValueSource);
    }

    [Fact]
    public async Task MutationObservation_SkipsNonInteractiveContainerTap()
    {
        var layout = new Grid { AutomationId = "layout" };
        using var harness = await SetPropertyTestHarness.CreateAsync(layout);
        var layoutId = await harness.GetElementIdAsync("layout");

        var observation = await harness.Service.CreateMutationObservationAsync(new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/tap",
            Body = JsonSerializer.Serialize(new { elementId = layoutId })
        });

        Assert.Null(observation);
    }

    [Fact]
    public async Task MutationObservation_RecordsContainerWithTapGesture()
    {
        var layout = new Grid { AutomationId = "tappable-layout" };
        layout.GestureRecognizers.Add(new TapGestureRecognizer());
        using var harness = await SetPropertyTestHarness.CreateAsync(layout);
        var layoutId = await harness.GetElementIdAsync("tappable-layout");

        var observation = await harness.Service.CreateMutationObservationAsync(new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/tap",
            Body = JsonSerializer.Serialize(new { elementId = layoutId })
        });

        Assert.NotNull(observation);
        Assert.Equal("tappable-layout", observation.AutomationId);
    }

    [Fact]
    public async Task MutationObservation_NeverCarriesPasswordEntryText()
    {
        var entry = new Entry
        {
            AutomationId = "PasswordEntry",
            IsPassword = true,
            Text = "super-secret-value"
        };
        using var harness = await SetPropertyTestHarness.CreateAsync(entry);
        var entryId = await harness.GetElementIdAsync(entry.AutomationId);

        var observation = await harness.Service.CreateMutationObservationAsync(new HttpRequest
        {
            Method = "POST",
            Path = "/api/v1/ui/actions/fill",
            Body = JsonSerializer.Serialize(new { elementId = entryId, text = "super-secret-value" })
        });

        Assert.NotNull(observation);
        Assert.True(observation.Sensitive);
        Assert.Null(observation.Value);
    }

    private sealed class SetPropertyTestHarness : IDisposable
    {
        private readonly DevFlowAgentService _service;

        public AgentClient Client { get; }
        public DevFlowAgentService Service => _service;
        public int Port => _service.Port;

        private SetPropertyTestHarness(DevFlowAgentService service, AgentClient client)
        {
            _service = service;
            Client = client;
        }

        public static async Task<SetPropertyTestHarness> CreateAsync(params View[] views)
        {
            var app = new TestApplication(views);

            var service = new DevFlowAgentService(new AgentOptions { Port = GetFreePort() });
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

        private static int GetFreePort() => TestPorts.Reserve();
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

    private sealed class BindingSource : INotifyPropertyChanged
    {
        private string _value = "";

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                    return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
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
