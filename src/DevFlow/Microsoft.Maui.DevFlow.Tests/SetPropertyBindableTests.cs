using System.Net;
using System.Net.Sockets;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class SetPropertyBindableTests
{
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

    private sealed class SetPropertyTestHarness : IDisposable
    {
        private readonly DevFlowAgentService _service;

        public AgentClient Client { get; }

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

        private static int GetFreePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
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
