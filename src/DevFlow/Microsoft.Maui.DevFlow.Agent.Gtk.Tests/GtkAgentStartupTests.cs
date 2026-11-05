using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.DevFlow.Agent.Gtk.Tests;

public class GtkAgentStartupTests
{
    [Fact]
    public void ConfigureStartup_FirstWindowStartsSynchronously_SubsequentWindowsDoNot()
    {
        var builder = MauiApp.CreateBuilder();
        var starts = 0;
        var startupThread = 0;
        GtkAgentServiceExtensions.ConfigureStartup(builder, () =>
        {
            starts++;
            startupThread = Environment.CurrentManagedThreadId;
        });

        using var app = builder.Build();
        var lifecycle = app.Services.GetRequiredService<ILifecycleEventService>();
        Assert.Equal(0, starts);

        lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!));
        Assert.Equal(1, starts);
        Assert.Equal(Environment.CurrentManagedThreadId, startupThread);

        lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!));
        Assert.Equal(1, starts);
    }

    [Fact]
    public void ConfigureStartup_FailedStartupIsReported_AndCanRetry()
    {
        var builder = MauiApp.CreateBuilder();
        var starts = 0;
        GtkAgentServiceExtensions.ConfigureStartup(builder, () =>
        {
            if (++starts == 1)
                throw new InvalidOperationException("Startup failed");
        });

        using var app = builder.Build();
        var lifecycle = app.Services.GetRequiredService<ILifecycleEventService>();
        Assert.Throws<InvalidOperationException>(() =>
            lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!)));

        lifecycle.InvokeEvents<GtkWindowCreated>(nameof(GtkWindowCreated), callback => callback(null!));
        Assert.Equal(2, starts);
    }

    [Fact]
    public void StartDevFlowAgent_UninitializedGtkHost_ReportsStartupOrder()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new TestGtkApplication().StartDevFlowAgent());
        Assert.Contains("OnStarted", exception.Message);
    }

    [Fact]
    public void StartDevFlowAgent_NullApplication_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((Application)null!).StartDevFlowAgent());
        Assert.Throws<ArgumentNullException>(() => ((GtkMauiApplication)null!).StartDevFlowAgent());
    }

    private sealed class TestGtkApplication : GtkMauiApplication
    {
        protected override MauiApp CreateMauiApp() => throw new NotSupportedException();
    }
}
