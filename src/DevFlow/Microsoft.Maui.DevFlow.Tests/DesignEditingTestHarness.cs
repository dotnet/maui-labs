using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>A MAUI agent bound to an in-memory app whose root children are the given views.</summary>
internal sealed class DesignEditingTestHarness : IDisposable
{
    private readonly MauiDevFlowAgentService _service;

    public AgentClient Client { get; }
    public MauiDevFlowAgentService Service => _service;
    public Application App { get; }
    public int Port => _service.Port;

    private DesignEditingTestHarness(MauiDevFlowAgentService service, AgentClient client, Application app)
    {
        _service = service;
        Client = client;
        App = app;
    }

    public static Task<DesignEditingTestHarness> CreateAsync(params View[] views)
        => StartAsync(new TestApplication(views));

    /// <summary>
    /// An agent bound to an app with a real window hosting <paramref name="views"/>, so each
    /// element's <c>Window</c> and the window's <see cref="IVisualDiagnosticsOverlay"/> exist the
    /// way they do in a running app. Highlight and pick mode need the overlay.
    /// </summary>
    public static Task<DesignEditingTestHarness> CreateWithWindowAsync(params View[] views)
    {
        var root = new VerticalStackLayout();
        foreach (var view in views)
            root.Children.Add(view);

        var app = new Application();
        typeof(Application)
            .GetMethod("AddWindow", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(app, [new Window(new ContentPage { Content = root })]);
        return StartAsync(app);
    }

    private static async Task<DesignEditingTestHarness> StartAsync(Application app)
    {
        var service = new MauiDevFlowAgentService(new AgentOptions { Port = GetFreePort() });
        var client = new AgentClient("localhost", service.Port);

        service.StartServerOnly(new ImmediateDispatcher());
        service.BindApp(app);

        for (var i = 0; i < 10 && await client.GetStatusAsync() is null; i++)
            await Task.Delay(100);

        return new DesignEditingTestHarness(service, client, app);
    }

    public async Task<string> GetElementIdAsync(string automationId)
    {
        for (var i = 0; i < 10; i++)
        {
            var match = (await Client.QueryAsync(automationId: automationId)).FirstOrDefault();
            if (match != null)
                return match.Id;

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"Could not find element with automation ID '{automationId}'.");
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds. <paramref name="background"/> is the work expected
    /// to make it hold: when it finishes first, it is awaited so its own failure surfaces instead of a
    /// timeout that says nothing about why.
    /// </summary>
    public static async Task WaitForAsync(Func<bool> condition, string description, Task? background = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (background is { IsCompleted: true })
            {
                await background;
                if (!condition())
                    throw new InvalidOperationException($"Waited for {description}, but the work that should have caused it finished first.");

                return;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Timed out after 10 seconds waiting for {description}.");

            await Task.Delay(20);
        }
    }

    public async Task<ClientWebSocket> SubscribeUiEventsAsync(params string[] events)
    {
        var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.ConnectAsync(new Uri($"ws://localhost:{Port}/ws/v1/ui/events"), timeout.Token);
        var subscription = JsonSerializer.Serialize(new { type = "subscribe", data = new { events } });
        await socket.SendAsync(Encoding.UTF8.GetBytes(subscription), WebSocketMessageType.Text, true, timeout.Token);
        return socket;
    }

    /// <summary>Reads UI events until one of <paramref name="type"/> matches <paramref name="predicate"/>.</summary>
    public static async Task<JsonElement> ReceiveEventAsync(ClientWebSocket socket, string type, Func<JsonElement, bool>? predicate = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[8192];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new InvalidOperationException($"UI event stream closed before '{type}' was received.");
                message.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            using var document = JsonDocument.Parse(message.ToArray());
            var root = document.RootElement;
            if (root.TryGetProperty("type", out var eventType)
                && eventType.GetString() == type
                && (predicate is null || predicate(root.GetProperty("data"))))
            {
                return root.GetProperty("data").Clone();
            }
        }
    }

    public void Dispose()
    {
        Client.Dispose();
        _service.Dispose();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TestApplication(IEnumerable<View> views) : Application, IVisualTreeElement
    {
        private readonly IReadOnlyList<IVisualTreeElement> _children = views.Cast<IVisualTreeElement>().ToArray();

        IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren() => _children;

        IVisualTreeElement? IVisualTreeElement.GetVisualParent() => null;
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

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;
    }
}
