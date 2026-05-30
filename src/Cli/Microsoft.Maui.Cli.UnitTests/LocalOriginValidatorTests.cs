using Microsoft.Maui.Cli.DevFlow.Inspector;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class LocalOriginValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
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
}
