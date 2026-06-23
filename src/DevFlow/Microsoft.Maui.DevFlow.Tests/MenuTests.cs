using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class MenuTests
{
    [Fact]
    public async Task GetMenus_ReturnsMauiMenuBar_WithTitlesKeysAndModifiers()
    {
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(null));

        var menus = await harness.Client.GetMenusAsync();

        Assert.Equal(JsonValueKind.Object, menus.ValueKind);
        Assert.True(menus.GetProperty("mauiSupported").GetBoolean());

        var groups = menus.GetProperty("menuBar").GetProperty("items");
        Assert.Equal(JsonValueKind.Array, groups.ValueKind);

        var titles = groups.EnumerateArray().Select(g => g.GetProperty("title").GetString()).ToList();
        Assert.Contains("File", titles);
        Assert.Contains("Account", titles);

        var account = groups.EnumerateArray().First(g => g.GetProperty("title").GetString() == "Account");
        var logout = account.GetProperty("items").EnumerateArray().First(i => i.GetProperty("title").GetString() == "Log Out");
        Assert.Equal("l", logout.GetProperty("key").GetString());
        var mods = logout.GetProperty("modifiers").EnumerateArray().Select(m => m.GetString()).ToList();
        Assert.Contains("cmd", mods);
        Assert.Contains("shift", mods);
    }

    [Fact]
    public async Task GetMenus_IncludesNestedSubItems()
    {
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(null));

        var menus = await harness.Client.GetMenusAsync();
        var file = menus.GetProperty("menuBar").GetProperty("items").EnumerateArray()
            .First(g => g.GetProperty("title").GetString() == "File");

        var recent = file.GetProperty("items").EnumerateArray()
            .First(i => i.TryGetProperty("title", out var t) && t.GetString() == "Recent");
        Assert.True(recent.GetProperty("hasSubmenu").GetBoolean());
        var clear = recent.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("title").GetString() == "Clear");
        Assert.Equal("File/Recent/Clear", clear.GetProperty("path").GetString());
    }

    [Fact]
    public async Task InvokeMenu_ByPath_ExecutesCommand()
    {
        var loggedOut = false;
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(() => loggedOut = true));

        var result = await harness.Client.InvokeMenuAsync(path: "Account/Log Out");

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("maui", result.GetProperty("source").GetString());
        Assert.True(loggedOut);
    }

    [Fact]
    public async Task InvokeMenu_ByKeyAndModifiers_ExecutesCommand()
    {
        var loggedOut = false;
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(() => loggedOut = true));

        var result = await harness.Client.InvokeMenuAsync(key: "l", modifiers: "cmd+shift");

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.True(loggedOut);
    }

    [Fact]
    public async Task InvokeMenu_ByTitle_ExecutesCommand()
    {
        var loggedOut = false;
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(() => loggedOut = true));

        var result = await harness.Client.InvokeMenuAsync(title: "Log Out");

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.True(loggedOut);
    }

    [Fact]
    public async Task InvokeMenu_UnknownItem_ReturnsNotFound()
    {
        using var harness = await MenuTestHarness.CreateAsync(() => BuildMenuPage(null));

        var result = await harness.Client.InvokeMenuAsync(path: "Account/Does Not Exist");

        // Driver returns the error body; assert it did not report success.
        var success = result.ValueKind == JsonValueKind.Object &&
                      result.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        Assert.False(success);
    }

    [Fact]
    public async Task InvokeMenu_DisabledItem_DoesNotExecute()
    {
        var clicked = false;
        using var harness = await MenuTestHarness.CreateAsync(() =>
        {
            var page = new ContentPage();
            var bar = new MenuBarItem { Text = "Edit" };
            bar.Add(new MenuFlyoutItem { Text = "Paste", IsEnabled = false, Command = new RelayCommand(() => clicked = true) });
            page.MenuBarItems.Add(bar);
            return page;
        });

        var result = await harness.Client.InvokeMenuAsync(path: "Edit/Paste", target: "maui");
        var success = result.ValueKind == JsonValueKind.Object &&
                      result.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        Assert.False(success);
        Assert.False(clicked);
    }

    [Fact]
    public async Task MenusEndpoints_UseV1Paths_ViaMockListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var acceptTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer);
                var request = Encoding.UTF8.GetString(buffer, 0, read);

                if (request.Contains("GET /api/v1/ui/menus", StringComparison.Ordinal))
                {
                    var body = """
                    {
                      "platform": "macOS",
                      "mauiSupported": true,
                      "nativeSupported": true,
                      "menuBar": { "source": "maui", "items": [] },
                      "native": { "source": "appkit", "items": [ { "title": "File", "path": "File" } ] }
                    }
                    """;
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                    continue;
                }

                if (request.Contains("POST /api/v1/ui/menus/invoke", StringComparison.Ordinal))
                {
                    Assert.Contains("\"path\":\"File/Save\"", request);
                    var body = """{"success":true,"source":"appkit","title":"Save","path":"File/Save"}""";
                    var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
                    continue;
                }

                throw new InvalidOperationException($"Unexpected request: {request}");
            }
        });

        using var agentClient = new AgentClient("localhost", port);

        var menus = await agentClient.GetMenusAsync();
        Assert.True(menus.GetProperty("nativeSupported").GetBoolean());
        Assert.Equal("appkit", menus.GetProperty("native").GetProperty("source").GetString());

        var invoke = await agentClient.InvokeMenuAsync(path: "File/Save", target: "native");
        Assert.True(invoke.GetProperty("success").GetBoolean());
        Assert.Equal("Save", invoke.GetProperty("title").GetString());

        await acceptTask;
        listener.Stop();
    }

    private static ContentPage BuildMenuPage(Action? onLogout)
    {
        var page = new ContentPage();

        var file = new MenuBarItem { Text = "File" };
        file.Add(new MenuFlyoutItem { Text = "New" });
        file.Add(new MenuFlyoutSeparator());
        var recent = new MenuFlyoutSubItem { Text = "Recent" };
        recent.Add(new MenuFlyoutItem { Text = "Clear" });
        file.Add(recent);

        var account = new MenuBarItem { Text = "Account" };
        var logout = new MenuFlyoutItem { Text = "Log Out", Command = new RelayCommand(() => onLogout?.Invoke()) };
        logout.KeyboardAccelerators.Add(new KeyboardAccelerator
        {
            Key = "l",
            Modifiers = KeyboardAcceleratorModifiers.Cmd | KeyboardAcceleratorModifiers.Shift,
        });
        account.Add(logout);

        page.MenuBarItems.Add(file);
        page.MenuBarItems.Add(account);
        return page;
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }

    private sealed class MenuTestHarness : IDisposable
    {
        private readonly DevFlowAgentService _service;
        public AgentClient Client { get; }

        private MenuTestHarness(DevFlowAgentService service, AgentClient client)
        {
            _service = service;
            Client = client;
        }

        public static async Task<MenuTestHarness> CreateAsync(Func<Page> pageFactory)
        {
            var app = new Application();
            var service = new DevFlowAgentService(new AgentOptions { Port = GetFreePort() });
            var client = new AgentClient("localhost", service.Port);

            service.StartServerOnly(new ImmediateDispatcher());
            AddWindow(app, new Window(pageFactory()));
            service.BindApp(app);

            for (var i = 0; i < 20; i++)
            {
                var status = await client.GetStatusAsync();
                if (status != null)
                    return new MenuTestHarness(service, client);
                await Task.Delay(100);
            }

            throw new InvalidOperationException("Agent did not start in time");
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

        private static void AddWindow(Application app, Window window)
        {
            // Application.AddWindow is internal; at runtime the platform handler registers
            // windows. In a headless test we register the window directly so the agent can
            // enumerate Application.Windows.
            var method = typeof(Application).GetMethod(
                "AddWindow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(app, new object[] { window });
        }
    }

    private sealed class ImmediateDispatcher : IDispatcher
    {
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action) { action(); return true; }
        public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
        public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
    }

    private sealed class ImmediateDispatcherTimer : IDispatcherTimer
    {
        public bool IsRepeating { get; set; }
        public TimeSpan Interval { get; set; }
        public bool IsRunning { get; private set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => IsRunning = true;
        public void Stop() => IsRunning = false;
    }
}
