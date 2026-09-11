namespace Browser.Essentials.Tests;

/// <summary>
/// A browser-run integration test. These publish the Blazor WebAssembly test app,
/// serve it from Kestrel, and drive it with Playwright (Chromium). Set
/// <c>BROWSER_ESSENTIALS_SKIP_TESTS=1</c> to skip them (e.g. environments where
/// Playwright browsers cannot be downloaded).
/// </summary>
public sealed class BrowserTestsFactAttribute : FactAttribute
{
	const string EnvVar = "BROWSER_ESSENTIALS_SKIP_TESTS";

	public BrowserTestsFactAttribute()
	{
		if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvVar)))
			Skip = $"Skipped because {EnvVar} is set.";
	}
}
