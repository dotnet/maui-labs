using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class AgentClientTests
{
    [Fact]
    public void Constructor_DefaultValues()
    {
        using var client = new AgentClient();
        Assert.Equal("http://localhost:9223", client.BaseUrl);
    }

    [Fact]
    public void Constructor_CustomHostAndPort()
    {
        using var client = new AgentClient("192.168.1.100", 8080);
        Assert.Equal("http://192.168.1.100:8080", client.BaseUrl);
    }

    [Fact]
    public async Task GetStatus_WhenAgentNotRunning_ReturnsNull()
    {
        using var client = new AgentClient("localhost", 19999);
        var status = await client.GetStatusAsync();
        Assert.Null(status);
    }

    [Fact]
    public async Task GetTree_WhenAgentNotRunning_ReturnsEmptyList()
    {
        using var client = new AgentClient("localhost", 19999);
        var tree = await client.GetTreeAsync();
        Assert.Empty(tree);
    }

    [Fact]
    public async Task GetStatus_WithTransientRetry_RetriesConnectionRefused()
    {
        using var reserved = new TcpListener(IPAddress.Loopback, 0);
        reserved.ExclusiveAddressUse = false;
        reserved.Start();
        var port = ((IPEndPoint)reserved.LocalEndpoint).Port;
        reserved.Stop();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Generous startup delay so the client is virtually guaranteed to make at
        // least one connection attempt (which fails with ECONNREFUSED) before the
        // listener is bound. We additionally assert elapsed wall time below to
        // prove the retry path actually fired.
        var startupDelay = TimeSpan.FromMilliseconds(500);

        var serverTask = Task.Run(async () =>
        {
            await Task.Delay(startupDelay, cts.Token);

            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.ExclusiveAddressUse = false;
            listener.Start();

            using var tcpClient = await listener.AcceptTcpClientAsync(cts.Token);
            using var stream = tcpClient.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAtLeastAsync(buffer, 1, throwOnEndOfStream: false, cancellationToken: cts.Token);
            Assert.True(read > 0);

            var body = """{"agent":{"name":"test","version":"1.0"},"device":{"platform":"Test"},"app":{"name":"Sample"},"running":true}""";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cts.Token);
        });

        var retryDelay = TimeSpan.FromMilliseconds(50);
        using var client = new AgentClient("localhost", port)
        {
            TransientFailureRetryCount = 20,
            TransientFailureRetryDelay = retryDelay,
        };

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var status = await client.GetStatusAsync();
            sw.Stop();

            Assert.NotNull(status);
            Assert.True(status!.Running);

            // If the retry path was not exercised, GetStatusAsync would have returned
            // either ~immediately (failure) or only after the very first successful
            // connect. Asserting elapsed >= one retry delay catches the regression
            // where the test passes without ever hitting the retry loop.
            Assert.True(
                sw.Elapsed >= retryDelay,
                $"Expected GetStatusAsync to take at least one retry delay ({retryDelay.TotalMilliseconds} ms) " +
                $"due to connection-refused retries, but completed in {sw.Elapsed.TotalMilliseconds:F1} ms.");
        }
        finally
        {
            // Guard against a missed accept hanging the test indefinitely.
            var completed = await Task.WhenAny(serverTask, Task.Delay(2000));
            if (completed == serverTask)
            {
                await serverTask;
            }
            else
            {
                cts.Cancel();
            }
        }
    }

    [Fact]
    public async Task Query_WhenAgentNotRunning_ReturnsEmptyList()
    {
        using var client = new AgentClient("localhost", 19999);
        var results = await client.QueryAsync(type: "Button");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Screenshot_WhenAgentNotRunning_ReturnsNull()
    {
        using var client = new AgentClient("localhost", 19999);
        var data = await client.ScreenshotAsync();
        Assert.Null(data);
    }

    [Fact]
    public async Task Tap_WhenAgentNotRunning_ReturnsFalse()
    {
        using var client = new AgentClient("localhost", 19999);
        var result = await client.TapAsync("test-id");
        Assert.False(result);
    }

    [Fact]
    public async Task Fill_WhenAgentNotRunning_ReturnsFalse()
    {
        using var client = new AgentClient("localhost", 19999);
        var result = await client.FillAsync("test-id", "hello");
        Assert.False(result);
    }

    [Fact]
    public async Task Clear_WhenAgentNotRunning_ReturnsFalse()
    {
        using var client = new AgentClient("localhost", 19999);
        var result = await client.ClearAsync("test-id");
        Assert.False(result);
    }

    [Fact]
    public async Task Focus_WhenAgentNotRunning_ReturnsFalse()
    {
        using var client = new AgentClient("localhost", 19999);
        var result = await client.FocusAsync("test-id");
        Assert.False(result);
    }
}

public class AppDriverFactoryTests
{
    [Theory]
    [InlineData("maccatalyst", typeof(MacCatalystAppDriver))]
    [InlineData("mac", typeof(MacCatalystAppDriver))]
    [InlineData("catalyst", typeof(MacCatalystAppDriver))]
    [InlineData("android", typeof(AndroidAppDriver))]
    [InlineData("ios", typeof(iOSSimulatorAppDriver))]
    [InlineData("iossimulator", typeof(iOSSimulatorAppDriver))]
    [InlineData("windows", typeof(WindowsAppDriver))]
    [InlineData("win", typeof(WindowsAppDriver))]
    [InlineData("winui", typeof(WindowsAppDriver))]
    public void Create_ReturnsCorrectDriverType(string platform, Type expectedType)
    {
        using var driver = AppDriverFactory.Create(platform);
        Assert.IsType(expectedType, driver);
    }

    [Fact]
    public void Create_UnknownPlatform_Throws()
    {
        Assert.Throws<ArgumentException>(() => AppDriverFactory.Create("unknown"));
    }
}

public class MacCatalystAppDriverTests
{
    [Fact]
    public void Platform_ReturnsMacCatalyst()
    {
        using var driver = new MacCatalystAppDriver();
        Assert.Equal("MacCatalyst", driver.Platform);
    }

    [Fact]
    public async Task ConnectAsync_NoAgent_ThrowsInvalidOperation()
    {
        using var driver = new MacCatalystAppDriver();
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.ConnectAsync("localhost", 19999));
    }

    [Fact]
    public async Task GetTree_BeforeConnect_ThrowsInvalidOperation()
    {
        using var driver = new MacCatalystAppDriver();
        await Assert.ThrowsAsync<InvalidOperationException>(() => driver.GetTreeAsync());
    }
}

public class AndroidAppDriverTests
{
    [Fact]
    public void Platform_ReturnsAndroid()
    {
        using var driver = new AndroidAppDriver();
        Assert.Equal("Android", driver.Platform);
    }
}

public class iOSSimulatorAppDriverTests
{
    [Fact]
    public void Platform_ReturnsiOSSimulator()
    {
        using var driver = new iOSSimulatorAppDriver();
        Assert.Equal("iOSSimulator", driver.Platform);
    }
}

public class WindowsAppDriverTests
{
    [Fact]
    public void Platform_ReturnsWindows()
    {
        using var driver = new WindowsAppDriver();
        Assert.Equal("Windows", driver.Platform);
    }
}

public class ElementInfoTests
{
    [Fact]
    public void DefaultValues()
    {
        var info = new ElementInfo();
        Assert.Equal(string.Empty, info.Id);
        Assert.Null(info.ParentId);
        Assert.Equal(string.Empty, info.Type);
        Assert.Equal("maui", info.Framework);
        Assert.Null(info.AutomationId);
        Assert.Null(info.Text);
        Assert.False(info.IsVisible);
        Assert.False(info.IsEnabled);
        Assert.False(info.IsFocused);
        Assert.NotNull(info.State);
        Assert.False(info.State.Displayed);
        Assert.Null(info.Bounds);
        Assert.Null(info.Children);
    }

    [Fact]
    public void Serialization_RoundTrips()
    {
        var info = new ElementInfo
        {
            Id = "btn1",
            Type = "Button",
            FullType = "Microsoft.Maui.Controls.Button",
            Framework = "maui",
            AutomationId = "SubmitBtn",
            Text = "Submit",
            IsVisible = true,
            IsEnabled = true,
            StyleClass = ["primary-button"],
            NativeType = "UIButton",
            NativeProperties = new Dictionary<string, string?> { ["title"] = "Submit" },
            Bounds = new BoundsInfo { X = 10, Y = 20, Width = 100, Height = 44 }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(info);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ElementInfo>(json);

        Assert.NotNull(deserialized);
        Assert.Contains("\"framework\":\"maui\"", json);
        Assert.Contains("\"role\":\"button\"", json);
        Assert.Contains("\"traits\":", json);
        Assert.Contains("\"state\":", json);
        Assert.Contains("\"style\":", json);
        Assert.Contains("\"nativeView\":", json);
        Assert.Equal("btn1", deserialized.Id);
        Assert.Equal("Button", deserialized.Type);
        Assert.Equal("maui", deserialized.Framework);
        Assert.Equal("SubmitBtn", deserialized.AutomationId);
        Assert.Equal("Submit", deserialized.Text);
        Assert.True(deserialized.IsVisible);
        Assert.NotNull(deserialized.State);
        Assert.True(deserialized.State.Displayed);
        Assert.False(deserialized.State.Selected);
        Assert.Equal("button", deserialized.Role);
        Assert.NotNull(deserialized.Traits);
        Assert.Contains("interactive", deserialized.Traits!);
        Assert.NotNull(deserialized.Style);
        Assert.Contains("primary-button", deserialized.Style!.Classes!);
        Assert.NotNull(deserialized.NativeView);
        Assert.Equal("UIButton", deserialized.NativeView!.Type);
        Assert.NotNull(deserialized.Bounds);
        Assert.Equal(100, deserialized.Bounds.Width);
    }

    [Fact]
    public void Deserialization_MapsSpecStyleAndStateShape()
    {
        var json = """
        {
          "id":"btn1",
          "type":"Button",
          "fullType":"Microsoft.Maui.Controls.Button",
          "framework":"maui",
          "text":"Submit",
          "state":{"displayed":true,"enabled":true,"selected":false,"focused":false,"opacity":1.0},
          "style":{"classes":["primary-button"]},
          "nativeView":{"type":"UIButton","properties":{"title":"Submit"}},
          "bounds":{"x":10,"y":20,"width":100,"height":44}
        }
        """;

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<ElementInfo>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsVisible);
        Assert.True(deserialized.IsEnabled);
        Assert.Equal("maui", deserialized.Framework);
        Assert.NotNull(deserialized.StyleClass);
        Assert.Contains("primary-button", deserialized.StyleClass);
        Assert.Equal("UIButton", deserialized.NativeType);
        Assert.NotNull(deserialized.NativeProperties);
        Assert.Equal("Submit", deserialized.NativeProperties["title"]);
    }
}

public class AgentStatusTests
{
    [Fact]
    public void Deserialization_Works()
    {
        var json = """{"agent":{"name":"Microsoft.Maui.DevFlow.Agent","version":"1.0.0","framework":".NET MAUI","frameworkVersion":"10.0"},"device":{"platform":"MacCatalyst","deviceType":"Virtual","idiom":"Desktop"},"app":{"name":"SampleMauiApp"},"running":true}""";
        var status = System.Text.Json.JsonSerializer.Deserialize<AgentStatus>(json);

        Assert.NotNull(status);
        Assert.Equal("Microsoft.Maui.DevFlow.Agent", status.Agent?.Name);
        Assert.Equal("1.0.0", status.Version);
        Assert.Equal("MacCatalyst", status.Platform);
        Assert.True(status.Running);
    }
}
