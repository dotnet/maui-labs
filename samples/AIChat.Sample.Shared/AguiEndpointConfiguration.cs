using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using AIChat.ClientServer.Sample.Shared;

namespace AIChat.Sample.Shared;

/// <summary>Configures the deliberately long-lived AG-UI transport without logging its bearer token.</summary>
public static class AguiEndpointConfiguration
{
    public const string HttpClientName = "agui-sample";

    public static void Configure(HttpClient client, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(configuration);
        var endpoint = new Uri(
            configuration["AGUI:Endpoint"] ?? "https://127.0.0.1:5018",
            UriKind.Absolute);
#if DEBUG
        ValidateEndpoint(endpoint, allowDebugCleartext: true);
#else
        ValidateEndpoint(endpoint, allowDebugCleartext: false);
#endif
        client.BaseAddress = endpoint;
        client.Timeout = Timeout.InfiniteTimeSpan;

        var apiKey = configuration["AGUI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public static string GetScenarioPath(string scenarioId)
    {
        if (!ScenarioIds.All.Any(scenario => string.Equals(scenario.Id, scenarioId, StringComparison.Ordinal)))
            throw new ArgumentOutOfRangeException(nameof(scenarioId), "The scenario is not exposed by the AG-UI sample server.");
        return "/" + scenarioId;
    }

    /// <summary>Rejects bearer-token cleartext transport except for deliberate Debug emulator loopback.</summary>
    public static void ValidateEndpoint(Uri endpoint, bool allowDebugCleartext)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme == Uri.UriSchemeHttps)
            return;
        if (endpoint.Scheme == Uri.UriSchemeHttp &&
            allowDebugCleartext &&
            (endpoint.IsLoopback || string.Equals(endpoint.Host, "10.0.2.2", StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException(
            "AGUI:Endpoint must use HTTPS. HTTP bearer transport is allowed only for Debug loopback or Android emulator 10.0.2.2.");
    }
}
