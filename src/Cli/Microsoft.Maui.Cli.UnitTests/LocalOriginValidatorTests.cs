using Microsoft.Maui.Cli.DevFlow.Inspector;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class LocalOriginValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:19223")]
    [InlineData("http://LOCALHOST:80")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("https://localhost:443")]
    public void IsAllowed_ReturnsTrue_ForLoopbackOrAbsentOrigin(string? origin)
    {
        Assert.True(LocalOriginValidator.IsAllowed(origin));
    }

    [Theory]
    [InlineData("null")] // browsers send this for file:// pages, sandboxed iframes, data: URLs
    [InlineData("http://evil.com")]
    [InlineData("https://attacker.example/")]
    [InlineData("http://localhost.evil.com")] // not actually localhost
    [InlineData("http://127.0.0.1.attacker.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,foo")]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost")]
    public void IsAllowed_ReturnsFalse_ForNonLoopbackOrigin(string origin)
    {
        Assert.False(LocalOriginValidator.IsAllowed(origin));
    }

    // ── Port-validated overload ────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 9000)] // absent origin always allowed (non-browser callers)
    [InlineData("", 9000)]
    [InlineData("http://localhost:9000", 9000)]
    [InlineData("http://127.0.0.1:9000", 9000)]
    [InlineData("http://[::1]:9000", 9000)]
    [InlineData("https://localhost:9000", 9000)]
    public void IsAllowed_WithExpectedPort_PermitsMatchingLoopbackOrigin(string? origin, int expectedPort)
    {
        Assert.True(LocalOriginValidator.IsAllowed(origin, expectedPort));
    }

    [Theory]
    [InlineData("http://localhost:3000", 9000)] // legit-looking dev server on a different port
    [InlineData("http://127.0.0.1:8080", 9000)]
    [InlineData("http://localhost", 9000)] // defaults to :80, not the broker port
    [InlineData("https://localhost", 9000)] // defaults to :443
    [InlineData("http://localhost:9001", 9000)] // off-by-one
    [InlineData("null", 9000)]
    [InlineData("http://evil.com:9000", 9000)] // matching port, wrong host
    public void IsAllowed_WithExpectedPort_RejectsMismatchedOrInvalidOrigin(string origin, int expectedPort)
    {
        Assert.False(LocalOriginValidator.IsAllowed(origin, expectedPort));
    }

    [Theory]
    [InlineData("http://localhost:3000", 0)] // expectedPort=0 disables the port check
    [InlineData("http://localhost", 0)]
    public void IsAllowed_WithZeroExpectedPort_FallsBackToHostOnlyCheck(string origin, int expectedPort)
    {
        Assert.True(LocalOriginValidator.IsAllowed(origin, expectedPort));
    }
}
