using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Microsoft.Maui.Platforms.Browser.Essentials;

/// <summary>
/// Entry point for the browser Essentials implementations. The JS interop module must be
/// imported once before any Essentials API is used — call <see cref="InitializeAsync"/>
/// (and await it) during app startup.
/// </summary>
[SupportedOSPlatform("browser")]
public static class BrowserEssentials
{
	internal const string ModuleName = "BrowserEssentials";

	static Task? initTask;

	/// <summary>
	/// Imports the interop JavaScript module. Idempotent; subsequent calls return the same task.
	/// </summary>
	/// <param name="moduleUrl">
	/// Optional URL of the BrowserEssentials.js module. When null (the default), the module
	/// embedded in this assembly is imported via a data: URL. Pass an explicit URL when the
	/// app's Content-Security-Policy disallows data: script sources — copy
	/// BrowserEssentials.js into the app's static assets and point this at it.
	/// </param>
	public static Task InitializeAsync(string? moduleUrl = null) =>
		initTask ??= InitializeCoreAsync(moduleUrl);

	static async Task InitializeCoreAsync(string? moduleUrl)
	{
		if (!OperatingSystem.IsBrowser())
			throw new PlatformNotSupportedException("Microsoft.Maui.Platforms.Browser.Essentials only runs on the browser (WebAssembly) platform.");

		if (moduleUrl is null)
		{
			using var stream = typeof(BrowserEssentials).Assembly.GetManifestResourceStream("BrowserEssentials.js")
				?? throw new InvalidOperationException("Embedded resource 'BrowserEssentials.js' not found.");
			using var memory = new MemoryStream();
			await stream.CopyToAsync(memory).ConfigureAwait(false);
			moduleUrl = "data:text/javascript;base64," + Convert.ToBase64String(memory.ToArray());
		}

		await JSHost.ImportAsync(ModuleName, moduleUrl).ConfigureAwait(false);
	}

	/// <summary>True once <see cref="InitializeAsync"/> has completed.</summary>
	public static bool IsInitialized => initTask is { IsCompletedSuccessfully: true };

	/// <summary>
	/// Throws when the module has not been imported yet. Used by synchronous API surfaces
	/// that cannot await initialization themselves.
	/// </summary>
	internal static void EnsureInitialized()
	{
		if (!IsInitialized)
			throw new InvalidOperationException(
				"Browser Essentials is not initialized. Call 'await BrowserEssentials.InitializeAsync()' during app startup before using Essentials APIs.");
	}

	/// <summary>
	/// Awaited by asynchronous API surfaces — starts initialization on demand if the app
	/// did not call <see cref="InitializeAsync"/> explicitly.
	/// </summary>
	internal static Task WhenInitializedAsync() => InitializeAsync();
}
