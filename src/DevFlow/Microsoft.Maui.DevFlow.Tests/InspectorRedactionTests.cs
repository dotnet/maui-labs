using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Unit tests for Inspector data-tab redaction. These surfaces expose app data
/// (network/logs/preferences) that can contain bearer tokens, cookies, API keys, and secrets in
/// query strings, so the server must redact by default before it ever reaches a host UI. These
/// tests pin that security boundary against regressions.
/// </summary>
public class InspectorRedactionTests
{
    [Theory]
    [InlineData("Authorization: Bearer abcDEF123456ghiJKL", "Authorization: Bearer <redacted>")]
    [InlineData("token=eyJhbGciOiJIUzI1.eyJzdWIiOiIxMjM0.SflKxwRJSMeKKF2QT4", "token=<jwt>")]
    [InlineData("{\"apiKey\":\"supersecret12345\"}", "{\"apiKey\":\"<redacted>\"}")]
    [InlineData("{\"access_token\":\"xyz\",\"foo\":\"bar\"}", "{\"access_token\":\"<redacted>\",\"foo\":\"bar\"}")]
    [InlineData("{\"refreshToken\":\"zzz\"}", "{\"refreshToken\":\"<redacted>\"}")]
    public void MaskSecrets_RedactsKnownSecretShapes(string input, string expected)
        => Assert.Equal(expected, InspectorServer.MaskSecrets(input));

    [Theory]
    [InlineData("{\"username\":\"alice\"}")]
    [InlineData("{\"count\":42,\"name\":\"home\"}")]
    [InlineData("plain log line with no secrets")]
    public void MaskSecrets_LeavesNonSecretsUnchanged(string input)
        => Assert.Equal(input, InspectorServer.MaskSecrets(input));

    [Fact]
    public void MaskUrlSecrets_MasksSecretQueryParamsButKeepsOthers()
    {
        var masked = InspectorServer.MaskUrlSecrets("https://api.example.com/cb?code=abc123&access_token=zzz999&foo=1&page=2");
        Assert.Contains("code=<redacted>", masked);
        Assert.Contains("access_token=<redacted>", masked);
        Assert.Contains("foo=1", masked);
        Assert.Contains("page=2", masked);
        Assert.DoesNotContain("abc123", masked);
        Assert.DoesNotContain("zzz999", masked);
    }

    [Fact]
    public void RedactHeaders_MasksSensitiveHeadersCaseInsensitively()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = new[] { "Bearer abc" },
            ["Cookie"] = new[] { "session=xyz" },
            ["X-Custom-Token"] = new[] { "t" },
            ["Content-Type"] = new[] { "application/json" },
            ["Accept"] = new[] { "*/*" },
        };

        InspectorServer.RedactHeaders(headers);

        Assert.Equal("<redacted>", headers["Authorization"][0]);
        Assert.Equal("<redacted>", headers["Cookie"][0]);
        Assert.Equal("<redacted>", headers["X-Custom-Token"][0]); // matches the "token" fragment
        Assert.Equal("application/json", headers["Content-Type"][0]);
        Assert.Equal("*/*", headers["Accept"][0]);
    }

    [Fact]
    public void RedactHeaders_NullIsSafe() => InspectorServer.RedactHeaders(null);

    [Theory]
    [InlineData("/api/logs", true)]
    [InlineData("/api/network", true)]
    [InlineData("/api/network/detail", true)]
    [InlineData("/api/preferences", true)]
    [InlineData("/api/device", true)]
    [InlineData("/api/sensors", true)]
    [InlineData("/api/geolocation", true)]
    [InlineData("/api/files/roots", true)]
    [InlineData("/api/files/list", true)]
    [InlineData("/api/source", true)]
    [InlineData("/api/tap", false)]
    [InlineData("/api/state", false)]
    [InlineData("/api/getProperty", false)]
    public void IsTokenGatedPath_GatesOnlyTheDataTabs(string path, bool gated)
        => Assert.Equal(gated, InspectorServer.IsTokenGatedPath(path));
}
