using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Marks a test as a "live" inspector integration test that requires:
///   - a running DevFlow broker,
///   - a connected MAUI app the broker can address, and
///   - the Playwright browsers installed.
///
/// Live tests are skipped by default — CI does not bring up these prerequisites
/// — and only run when the <c>MAUI_INSPECTOR_LIVE_TESTS</c> environment variable
/// is set to a non-empty value. Local developers can run them with:
///
/// <code>
/// MAUI_INSPECTOR_LIVE_TESTS=1 dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Inspector.Tests/
/// </code>
///
/// Optionally set <c>INSPECTOR_URL</c> to point at a non-default broker, e.g.
/// <c>http://localhost:19223/inspector/myapp/</c>.
/// </summary>
public sealed class LiveInspectorFactAttribute : FactAttribute
{
    private const string EnvVar = "MAUI_INSPECTOR_LIVE_TESTS";

    public LiveInspectorFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"Live inspector integration test; set {EnvVar}=1 (and INSPECTOR_URL if needed) to enable.";
        }
    }
}
