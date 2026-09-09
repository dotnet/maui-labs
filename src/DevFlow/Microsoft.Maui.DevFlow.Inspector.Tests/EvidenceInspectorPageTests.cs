using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

[Trait("Category", "Integration")]
public class EvidenceInspectorPageTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devflow-evidence-browser-{Guid.NewGuid():N}");
    private EvidenceAgentFixture _agent = null!;
    private InspectorServer _inspector = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _agent = new EvidenceAgentFixture();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var embedToken = Guid.NewGuid().ToString("N");
        _inspector = new InspectorServer(port, "127.0.0.1", _agent.Port, embedToken);
        _inspector.Start();
        _url = $"http://127.0.0.1:{port}/?embed={embedToken}";
        _playwright = await Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        _context = await _browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.DisposeAsync();
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_inspector is not null)
        {
            await _inspector.StopAsync();
            _inspector.Dispose();
        }
        if (_agent is not null) await _agent.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }

    [EvidenceBrowserTheory]
    [InlineData(1440, 900, "light", 13)]
    [InlineData(1440, 900, "dark", 13)]
    [InlineData(375, 812, "light", 13)]
    [InlineData(320, 568, "dark", 13)]
    [InlineData(1024, 320, "dark", 13)]
    [InlineData(800, 600, "light", 26)]
    public async Task Preview_IsUsableAcrossHostSizesThemesAndLargeText(int width, int height, string theme, int fontSize)
    {
        await _page.SetViewportSizeAsync(width, height);
        await _page.GotoAsync(_url);
        await _page.EvaluateAsync("([theme, size]) => { document.documentElement.dataset.theme = theme; document.documentElement.style.setProperty('--df-font-size', `${size}px`); }",
            new object[] { theme, fontSize });
        await OpenEvidenceAsync();
        var dialog = _page.Locator("dialog.df-evidence-dialog");
        await Expect(dialog).ToContainTextAsync("430 \u00d7 760 DIP");
        await Expect(dialog).ToContainTextAsync("1920 \u00d7 1080 px");
        await Expect(dialog.Locator("dd").Filter(new() { HasText = "dark" })).ToBeVisibleAsync();
        await Expect(dialog.Locator("#df-evidence-screenshot")).Not.ToBeCheckedAsync();
        var bounds = await dialog.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.InRange(bounds.X, 0, width);
        Assert.InRange(bounds.Y, 0, height);
        Assert.True(bounds.X + bounds.Width <= width + 1);
        Assert.True(bounds.Y + bounds.Height <= height + 1);
        Assert.True(await dialog.Locator(".df-evidence-content")
            .EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        Assert.Equal(theme == "light" ? "rgb(255, 255, 255)" : "rgb(30, 30, 30)",
            await dialog.EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        var artifacts = Environment.GetEnvironmentVariable("EVIDENCE_TEST_ARTIFACTS");
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            await _page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"evidence-overview-{theme}-{width}x{height}-font{fontSize}.png") });
        }
        await dialog.Locator(".df-evidence-gaps summary").ClickAsync();
        await Expect(dialog).ToContainTextAsync("fontScale");
        await Expect(dialog).ToContainTextAsync("permissions");
        await dialog.Locator("#df-evidence-screenshot").ScrollIntoViewIfNeededAsync();
        await Expect(dialog.Locator("#df-evidence-screenshot")).ToBeVisibleAsync();
        if (!string.IsNullOrWhiteSpace(artifacts))
        {
            Directory.CreateDirectory(artifacts);
            await _page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"evidence-{theme}-{width}x{height}-font{fontSize}.png") });
        }
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
    }

    [EvidenceBrowserFact]
    public async Task Attachments_RequireARefreshedPreviewAndExplicitFinalConfirmation()
    {
        await _page.GotoAsync(_url);
        await LoadWorkflowAsync();
        await OpenEvidenceAsync();
        await _page.Locator("#df-evidence-screenshot").CheckAsync();
        await _page.Locator("#df-evidence-workflow").CheckAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Review attachments", Exact = true }).ClickAsync();
        var final = _page.GetByRole(AriaRole.Button, new() { Name = "Confirm and download", Exact = true });
        await Expect(final).ToBeVisibleAsync();
        await Expect(_page.Locator("dialog")).ToContainTextAsync("screenshot.png");
        await Expect(_page.Locator("dialog")).ToContainTextAsync("workflow.md");
        var download = await _page.RunAndWaitForDownloadAsync(() => final.ClickAsync());
        var path = Path.Combine(_root, download.SuggestedFilename);
        await download.SaveAsAsync(path);
        using var archive = ZipFile.OpenRead(path);
        Assert.NotNull(archive.GetEntry("screenshot.png"));
        using var workflow = new StreamReader(archive.GetEntry("workflow.md")!.Open());
        var text = await workflow.ReadToEndAsync();
        Assert.DoesNotContain("private-typed-value", text, StringComparison.Ordinal);
        Assert.Contains("OrderConfirmation", text, StringComparison.Ordinal);
        using var environment = JsonDocument.Parse(archive.GetEntry("environment.json")!.Open());
        Assert.Equal(430, environment.RootElement.GetProperty("viewport").GetProperty("width").GetDouble());
        Assert.Equal("42", environment.RootElement.GetProperty("app").GetProperty("build").GetString());
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Evidence bundle downloaded");
        await OpenEvidenceAsync();
        await Expect(_page.Locator("#df-evidence-screenshot")).Not.ToBeCheckedAsync();
        await Expect(_page.Locator("#df-evidence-workflow")).Not.ToBeCheckedAsync();
    }

    [EvidenceBrowserFact]
    public async Task CancelAndEscape_DoNotCaptureAndKeepKeyboardFocusInsideTheDialog()
    {
        var captures = 0;
        _page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/evidence/capture", StringComparison.Ordinal))
                Interlocked.Increment(ref captures);
        };
        await _page.GotoAsync(_url);
        await OpenEvidenceAsync();
        await _page.Locator("#df-evidence-screenshot").CheckAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Review attachments", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        Assert.Equal(0, captures);
        await OpenEvidenceAsync();
        await Expect(_page.Locator("#df-evidence-screenshot")).Not.ToBeCheckedAsync();
        for (var index = 0; index < 8; index++)
        {
            await _page.Keyboard.PressAsync("Tab");
            Assert.True(await _page.EvaluateAsync<bool>("() => document.querySelector('dialog').contains(document.activeElement)"),
                $"Tab {index}: {await _page.EvaluateAsync<string>("() => document.activeElement?.outerHTML")}");
        }
        await _page.Keyboard.PressAsync("Escape");
        await Expect(_page.Locator("dialog")).ToHaveCountAsync(0);
        Assert.Equal(0, captures);
    }

    [EvidenceBrowserTheory]
    [InlineData(".github\\extensions\\maui-devflow-canvas\\shell.mjs")]
    [InlineData("src\\DevFlow\\js\\vscode-inspector\\src\\extension.ts")]
    public async Task SandboxedHosts_AllowConfirmedEvidenceDownloadsWithoutNavigationPermissions(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? sourcePath = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) { sourcePath = candidate; break; }
            directory = directory.Parent;
        }
        Assert.NotNull(sourcePath);
        var source = File.ReadAllText(sourcePath);
        var sandbox = Regex.Match(source, "sandbox=\"([^\"]+)\"").Groups[1].Value;
        var permission = Regex.Match(source, "frame\\.sandbox\\.add\\('([^']+)'\\)").Groups[1].Value;
        Assert.Equal("allow-downloads", permission);
        Assert.DoesNotContain("allow-top-navigation", sandbox, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-popups", sandbox, StringComparison.Ordinal);
        await _page.RouteAsync("http://evidence-host.invalid/", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = $$"""
                <!doctype html><html><body style="margin:0">
                <iframe id="frame" sandbox="{{sandbox}}" style="width:100vw;height:100vh;border:0"></iframe>
                <script>const frame = document.getElementById('frame'); frame.sandbox.add('{{permission}}'); frame.src = '{{_url}}';</script>
                </body></html>
                """,
        }));
        await _page.GotoAsync("http://evidence-host.invalid/");
        var frame = _page.FrameLocator("#frame");
        var evidence = frame.Locator("#df-evidence");
        await Expect(evidence).ToBeVisibleAsync();
        await evidence.ClickAsync();
        var download = await _page.RunAndWaitForDownloadAsync(() =>
            frame.GetByRole(AriaRole.Button, new() { Name = "Download bundle", Exact = true }).ClickAsync());
        var path = Path.Combine(_root, download.SuggestedFilename);
        await download.SaveAsAsync(path);
        using var archive = ZipFile.OpenRead(path);
        Assert.NotNull(archive.GetEntry("environment.json"));
        Assert.Null(archive.GetEntry("screenshot.png"));
    }

    private async Task OpenEvidenceAsync()
    {
        var button = _page.Locator("#df-evidence");
        if (!await button.IsVisibleAsync())
            await _page.Locator("#df-more").ClickAsync();
        await button.ClickAsync();
        await Expect(_page.Locator("dialog.df-evidence-dialog")).ToBeVisibleAsync();
    }

    private async Task LoadWorkflowAsync()
    {
        const string markdown = """
            # Order confirmation
            ```json maui-test
            {"schema":1,"name":"order","steps":[{"seq":1,"action":"assert","target":{"automationId":"OrderConfirmation"},"asserts":[{"kind":"propEquals","selector":{"automationId":"OrderConfirmation"},"name":"Text","expected":"private-typed-value","verify":true}]}]}
            ```
            """;
        await _page.Locator("#df-workflow-file-input").SetInputFilesAsync(new FilePayload
        {
            Name = "order.md",
            MimeType = "text/markdown",
            Buffer = Encoding.UTF8.GetBytes(markdown),
        });
        await Expect(_page.Locator("#df-timeline")).ToBeVisibleAsync();
    }
}

public sealed class EvidenceBrowserFactAttribute : FactAttribute
{
    public EvidenceBrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVFLOW_EVIDENCE_BROWSER_TESTS") != "1")
            Skip = "Set DEVFLOW_EVIDENCE_BROWSER_TESTS=1 to run isolated Chromium evidence checks.";
    }
}

public sealed class EvidenceBrowserTheoryAttribute : TheoryAttribute
{
    public EvidenceBrowserTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVFLOW_EVIDENCE_BROWSER_TESTS") != "1")
            Skip = "Set DEVFLOW_EVIDENCE_BROWSER_TESTS=1 to run isolated Chromium evidence checks.";
    }
}
