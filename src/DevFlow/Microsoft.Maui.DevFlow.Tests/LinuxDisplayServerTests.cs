using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class LinuxDisplayServerTests : IDisposable
{
    private readonly string? _origSessionType;
    private readonly string? _origWaylandDisplay;
    private readonly string? _origDisplay;

    public LinuxDisplayServerTests()
    {
        _origSessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        _origWaylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        _origDisplay = Environment.GetEnvironmentVariable("DISPLAY");
    }

    public void Dispose()
    {
        SetEnv("XDG_SESSION_TYPE", _origSessionType);
        SetEnv("WAYLAND_DISPLAY", _origWaylandDisplay);
        SetEnv("DISPLAY", _origDisplay);
    }

    private static void SetEnv(string name, string? value)
    {
        if (value == null)
            Environment.SetEnvironmentVariable(name, null);
        else
            Environment.SetEnvironmentVariable(name, value);
    }

    // ── IsWayland / IsX11 detection ──

    [Fact]
    public void IsWayland_WhenXdgSessionTypeIsWayland_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.True(LinuxDisplayServer.IsWayland);
        Assert.False(LinuxDisplayServer.IsX11);
    }

    [Fact]
    public void IsWayland_WhenXdgSessionTypeIsWayland_CaseInsensitive()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "Wayland");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.True(LinuxDisplayServer.IsWayland);
    }

    [Fact]
    public void IsWayland_WhenWaylandDisplayIsSet_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", null);
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-0");

        Assert.True(LinuxDisplayServer.IsWayland);
        Assert.False(LinuxDisplayServer.IsX11);
    }

    [Fact]
    public void IsX11_WhenSessionTypeIsX11_ReturnsTrue()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.True(LinuxDisplayServer.IsX11);
        Assert.False(LinuxDisplayServer.IsWayland);
    }

    [Fact]
    public void IsX11_WhenNoSessionVarsSet_DefaultsToX11()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", null);
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.True(LinuxDisplayServer.IsX11);
        Assert.False(LinuxDisplayServer.IsWayland);
    }

    // ── Ffmpeg capture format ──

    [Fact]
    public void GetFfmpegCaptureFormat_OnWayland_ReturnsPipewire()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.Equal("pipewire", LinuxDisplayServer.GetFfmpegCaptureFormat());
    }

    [Fact]
    public void GetFfmpegCaptureFormat_OnX11_ReturnsX11grab()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.Equal("x11grab", LinuxDisplayServer.GetFfmpegCaptureFormat());
    }

    // ── Default capture display ──

    [Fact]
    public void GetDefaultCaptureDisplay_OnWayland_ReturnsZero()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

        Assert.Equal("0", LinuxDisplayServer.GetDefaultCaptureDisplay());
    }

    [Fact]
    public void GetDefaultCaptureDisplay_OnX11_ReturnsDisplayEnvVar()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        Environment.SetEnvironmentVariable("DISPLAY", ":1");

        Assert.Equal(":1", LinuxDisplayServer.GetDefaultCaptureDisplay());
    }

    [Fact]
    public void GetDefaultCaptureDisplay_OnX11_DefaultsToColonZero()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        Environment.SetEnvironmentVariable("DISPLAY", null);

        Assert.Equal(":0", LinuxDisplayServer.GetDefaultCaptureDisplay());
    }

    // ── IsYdotooldRunning ──

    [Fact]
    public void IsYdotooldRunning_DoesNotThrow()
    {
        // This should return a boolean without throwing, regardless of
        // whether ydotoold is actually running on the test machine.
        var result = LinuxDisplayServer.IsYdotooldRunning();
        Assert.IsType<bool>(result);
    }
}
