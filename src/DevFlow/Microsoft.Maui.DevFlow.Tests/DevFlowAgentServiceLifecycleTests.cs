using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowAgentServiceLifecycleTests
{
    [Fact]
    public async Task StartServerOnly_AllowsLateAppBinding()
    {
        var port = GetFreePort();
        using var service = new DevFlowAgentService(new AgentOptions { Port = port });
        using var client = new AgentClient("localhost", port);

        service.StartServerOnly(new ImmediateDispatcher());

        var beforeBind = await WaitForStatusAsync(client);
        Assert.NotNull(beforeBind);
        Assert.False(beforeBind!.Running);
        Assert.Equal("unknown", beforeBind.AppName);

        var app = new Application();
        service.BindApp(app);

        var afterBind = await WaitForStatusAsync(client);
        Assert.NotNull(afterBind);
        Assert.True(afterBind!.Running);
        Assert.Equal(app.GetType().Assembly.GetName().Name, afterBind.AppName);
    }

    private static async Task<AgentStatus?> WaitForStatusAsync(AgentClient client)
    {
        for (int i = 0; i < 10; i++)
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
}
