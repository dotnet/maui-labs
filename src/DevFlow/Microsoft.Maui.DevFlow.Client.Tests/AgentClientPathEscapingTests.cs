using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Several APIs put caller-supplied values into a <em>path segment</em> — file paths, preference and
/// secure-storage keys, action names, sensor ids. Those are percent-encoded so a value containing
/// <c>/</c> stays one segment, and the agent routes on the encoded form. .NET Framework's
/// <see cref="Uri"/> has historically canonicalised paths (unescaping <c>%2F</c> and collapsing dot
/// segments), which would silently re-route these requests on the portable target only. These tests
/// assert the exact request target on both target families so the two cannot diverge.
/// </summary>
public class AgentClientPathEscapingTests
{
    [Fact]
    public async Task DownloadFileAsync_KeepsASlashInThePathEncoded()
    {
        var request = await CaptureAsync(client => client.DownloadFileAsync("logs/app.txt"));

        Assert.Equal("/api/v1/storage/files/logs%2Fapp.txt", request.Path);
    }

    [Fact]
    public async Task UploadFileAsync_KeepsASlashInThePathEncoded()
    {
        var request = await CaptureAsync(client => client.UploadFileAsync("logs/app.txt", "aGk="));

        Assert.Equal("/api/v1/storage/files/logs%2Fapp.txt", request.Path);
    }

    [Fact]
    public async Task DeleteFileAsync_KeepsASlashInThePathEncoded()
    {
        var request = await CaptureAsync(client => client.DeleteFileAsync("logs/app.txt"));

        Assert.Equal("/api/v1/storage/files/logs%2Fapp.txt", request.Path);
    }

    [Fact]
    public async Task DownloadFileAsync_DoesNotCollapseDotSegments()
    {
        // A traversal-looking path must reach the agent verbatim so the agent — which owns the
        // storage-root sandbox — is the one that rejects it, rather than the client quietly
        // rewriting it into a different, possibly valid, path.
        var request = await CaptureAsync(client => client.DownloadFileAsync("logs/../secrets.txt"));

        Assert.Equal("/api/v1/storage/files/logs%2F..%2Fsecrets.txt", request.Path);
    }

    [Fact]
    public async Task GetPreferenceAsync_KeepsASlashInTheKeyEncoded()
    {
        var request = await CaptureAsync(client => client.GetPreferenceAsync("group/setting"));

        Assert.Equal("/api/v1/storage/preferences/group%2Fsetting", request.Path);
    }

    [Fact]
    public async Task SetPreferenceAsync_KeepsASlashInTheKeyEncoded()
    {
        var request = await CaptureAsync(client => client.SetPreferenceAsync("group/setting", "value"));

        Assert.Equal("PUT", request.Method);
        Assert.Equal("/api/v1/storage/preferences/group%2Fsetting", request.Path);
    }

    [Fact]
    public async Task GetSecureStorageAsync_KeepsASlashInTheKeyEncoded()
    {
        var request = await CaptureAsync(client => client.GetSecureStorageAsync("tokens/refresh"));

        Assert.Equal("/api/v1/storage/secure/tokens%2Frefresh", request.Path);
    }

    [Fact]
    public async Task InvokeActionAsync_KeepsASlashInTheActionNameEncoded()
    {
        var request = await CaptureAsync(client => client.InvokeActionAsync("Area/Reset"));

        Assert.Equal("/api/v1/invoke/actions/Area%2FReset", request.Path);
    }

    [Fact]
    public async Task StartSensorAsync_KeepsTheEncodedSegmentBeforeTheActionSuffix()
    {
        var request = await CaptureAsync(client => client.StartSensorAsync("orientation/raw"));

        Assert.Equal("/api/v1/device/sensors/orientation%2Fraw/start", request.Path);
    }

    [Fact]
    public async Task DownloadFileAsync_EncodesSpacesAndOtherReservedCharacters()
    {
        var request = await CaptureAsync(client => client.DownloadFileAsync("my logs/a+b&c.txt"));

        Assert.Equal("/api/v1/storage/files/my%20logs%2Fa%2Bb%26c.txt", request.Path);
        Assert.Equal(string.Empty, request.Query);
    }

    [Fact]
    public async Task DownloadFileAsync_KeepsTheRootQueryStringSeparateFromTheEncodedPath()
    {
        var request = await CaptureAsync(client => client.DownloadFileAsync("logs/app.txt", root: "cache"));

        Assert.Equal("/api/v1/storage/files/logs%2Fapp.txt", request.Path);
        Assert.Equal("root=cache", request.Query);
    }

    private static async Task<FakeAgent.RecordedRequest> CaptureAsync(Func<AgentClient, Task> call)
    {
        using var agent = FakeAgent.StartJson("""{"success":true}""");
        using var client = new AgentClient("localhost", agent.Port);

        await call(client);

        return Assert.Single(agent.Requests);
    }
}
