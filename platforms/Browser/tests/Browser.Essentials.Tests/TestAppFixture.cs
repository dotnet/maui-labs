using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Browser.Essentials.Tests;

public sealed record InBrowserTestResult(string Name, bool Passed, string? Error);

/// <summary>
/// Publishes Browser.Essentials.TestApp, serves the static output from Kestrel,
/// loads it in headless Chromium via Playwright, and captures the in-browser
/// test suite results rendered into the DOM.
/// </summary>
public sealed class TestAppFixture : IAsyncLifetime
{
	WebApplication? server;
	IPlaywright? playwright;
	IBrowser? browser;

	public IPage Page { get; private set; } = null!;

	public IReadOnlyList<InBrowserTestResult> Results { get; private set; } = [];

	public async Task InitializeAsync()
	{
		var testAppDirectory = FindTestAppDirectory();
		var publishDirectory = Path.Combine(Path.GetTempPath(), "browser-essentials-tests", Guid.NewGuid().ToString("N"));
		PublishTestApp(testAppDirectory, publishDirectory);

		var url = await StartServerAsync(Path.Combine(publishDirectory, "wwwroot"));

		InstallPlaywrightChromium();
		playwright = await Playwright.CreateAsync();
		browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
		var context = await browser.NewContextAsync(new()
		{
			Permissions = ["clipboard-read", "clipboard-write", "geolocation"],
			Geolocation = new Geolocation { Latitude = 35.6895f, Longitude = 139.6917f, Accuracy = 10 },
		});
		Page = await context.NewPageAsync();
		await Page.GotoAsync(url);
		var resultsElement = await Page.WaitForSelectorAsync(
			"#test-results[data-done='true']",
			new() { Timeout = 180_000, State = WaitForSelectorState.Attached });

		var json = await resultsElement!.TextContentAsync() ?? "[]";
		Results = JsonSerializer.Deserialize<List<InBrowserTestResult>>(json)
			?? throw new InvalidOperationException("Could not parse in-browser test results.");
	}

	public async Task DisposeAsync()
	{
		if (browser is not null)
			await browser.DisposeAsync();
		playwright?.Dispose();
		if (server is not null)
			await server.DisposeAsync();
	}

	static string FindTestAppDirectory()
	{
		// Arcade puts test output under <repo>/artifacts/bin/..., so walk up to the
		// repo root (MauiLabs.slnx) rather than expecting to pass through platforms/Browser.
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null
			&& !File.Exists(Path.Combine(directory.FullName, "Browser.slnx"))
			&& !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
		{
			directory = directory.Parent;
		}
		if (directory is null)
			throw new InvalidOperationException("Could not locate the repo root from the test output path.");
		if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
			directory = new DirectoryInfo(Path.Combine(directory.FullName, "platforms", "Browser"));
		return Path.Combine(directory.FullName, "tests", "Browser.Essentials.TestApp");
	}

	static void PublishTestApp(string projectDirectory, string outputDirectory)
	{
		var startInfo = new ProcessStartInfo("dotnet",
			$"publish \"{projectDirectory}\" -c Release -o \"{outputDirectory}\" --nologo")
		{
			WorkingDirectory = projectDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Failed to start dotnet publish.");
		var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();
		if (process.ExitCode != 0)
			throw new InvalidOperationException($"dotnet publish of the test app failed:\n{output}");
	}

	async Task<string> StartServerAsync(string webRoot)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = webRoot });
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		server = builder.Build();

		var fileProvider = new PhysicalFileProvider(webRoot);
		server.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
		server.UseStaticFiles(new StaticFileOptions
		{
			FileProvider = fileProvider,
			ServeUnknownFileTypes = true,
			DefaultContentType = "application/octet-stream",
		});

		await server.StartAsync();
		return server.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!.Addresses.First();
	}

	static void InstallPlaywrightChromium()
	{
		var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
		if (exitCode != 0)
			throw new InvalidOperationException(
				"Playwright could not install Chromium. Set BROWSER_ESSENTIALS_SKIP_TESTS=1 to skip these tests in environments without browser downloads.");
	}
}
