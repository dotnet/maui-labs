using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

#pragma warning disable MCPEXP001 // Extension negotiation is intentionally the subject of this compatibility spike.

public class McpAppsCompatibilitySpikeTests
{
    private const string UiExtension = "io.modelcontextprotocol/ui";
    private const string AppResourceUri = "ui://maui/phase-0";
    private const string AppMimeType = "text/html;profile=mcp-app";

    [Fact]
    public void CentrallyPinnedPackage_IsTheTestedVersion()
    {
        var repositoryRoot = GetRepositoryRoot();
        var versions = XDocument.Load(Path.Combine(repositoryRoot, "eng", "Versions.props"));
        var packages = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Packages.props"));

        Assert.Equal(
            "1.1.0",
            versions.Descendants("ModelContextProtocolVersion").Single().Value);
        Assert.Equal(
            "$(ModelContextProtocolVersion)",
            packages.Descendants("PackageVersion")
                .Single(element => (string?)element.Attribute("Include") == "ModelContextProtocol")
                .Attribute("Version")?.Value);
        Assert.Equal(new Version(1, 1, 0, 0), typeof(McpServerTool).Assembly.GetName().Version);
        Assert.Equal(new Version(1, 1, 0, 0), typeof(CallToolResult).Assembly.GetName().Version);
    }

    [Fact]
    public void TypedToolResult_CanAdvertiseStructuredContentAndRetainTextFallback()
    {
        var tool = McpServerTool.Create((Func<PhaseZeroViewModel>)CreateViewModel);
        var result = CreateToolResult();

        Assert.NotNull(tool.ProtocolTool.OutputSchema);
        Assert.Equal("object", tool.ProtocolTool.OutputSchema.Value.GetProperty("type").GetString());

        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            "Phase 0 is ready.",
            document.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal(
            "Phase 0",
            document.RootElement.GetProperty("structuredContent").GetProperty("title").GetString());
    }

    [Fact]
    public void NegotiatedSurface_UsesCurrentNestedUiShapes()
    {
        var surface = CreateSurface(CreateClientCapabilities(AppMimeType));

        Assert.True(surface.IsNegotiated);
        Assert.NotNull(surface.Resource);
        Assert.NotNull(surface.ResourceTemplate);

        var toolJson = JsonSerializer.Serialize(
            surface.Tool.ProtocolTool,
            McpJsonUtilities.DefaultOptions);
        using var toolDocument = JsonDocument.Parse(toolJson);
        var toolUi = toolDocument.RootElement.GetProperty("_meta").GetProperty("ui");
        Assert.Equal(AppResourceUri, toolUi.GetProperty("resourceUri").GetString());
        Assert.Equal(
            ["model", "app"],
            toolUi.GetProperty("visibility").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.False(toolDocument.RootElement.GetProperty("_meta").TryGetProperty("ui/resourceUri", out _));

        Assert.Equal(AppResourceUri, surface.Resource!.ProtocolResource!.Uri);
        Assert.Equal(AppMimeType, surface.Resource.ProtocolResource.MimeType);
        Assert.Equal(
            "ui://maui/phase-0/{theme}",
            surface.ResourceTemplate!.ProtocolResourceTemplate.UriTemplate);
    }

    [Fact]
    public void ResourceContent_UsesNestedUiCspAndRenderingMetadata()
    {
        var resource = CreateResourceContents();
        var json = JsonSerializer.Serialize<ResourceContents>(
            resource,
            McpJsonUtilities.DefaultOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var ui = root.GetProperty("_meta").GetProperty("ui");
        var csp = ui.GetProperty("csp");

        Assert.Equal(AppMimeType, root.GetProperty("mimeType").GetString());
        Assert.Equal(
            "https://api.example.test",
            csp.GetProperty("connectDomains")[0].GetString());
        Assert.Equal(
            "https://cdn.example.test",
            csp.GetProperty("resourceDomains")[0].GetString());
        Assert.True(ui.GetProperty("prefersBorder").GetBoolean());
        Assert.False(root.GetProperty("_meta").TryGetProperty("connectDomains", out _));
        Assert.False(root.GetProperty("_meta").TryGetProperty("prefersBorder", out _));
    }

    [Fact]
    public void Negotiation_SerializesExactExtensionAndFallsBackWhenUnsupported()
    {
        var negotiating = CreateClientCapabilities(AppMimeType);
        var absent = new ClientCapabilities();
        var wrongMime = CreateClientCapabilities("text/plain");

        var json = JsonSerializer.Serialize(negotiating, McpJsonUtilities.DefaultOptions);
        using var document = JsonDocument.Parse(json);
        var uiSettings = document.RootElement
            .GetProperty("extensions")
            .GetProperty(UiExtension);
        Assert.Equal(AppMimeType, uiSettings.GetProperty("mimeTypes")[0].GetString());

        var roundTripped = JsonSerializer.Deserialize<ClientCapabilities>(
            json,
            McpJsonUtilities.DefaultOptions)!;
        var negotiatedSurface = CreateSurface(roundTripped);
        var absentSurface = CreateSurface(absent);
        var wrongMimeSurface = CreateSurface(wrongMime);

        Assert.True(negotiatedSurface.IsNegotiated);
        Assert.False(absentSurface.IsNegotiated);
        Assert.False(wrongMimeSurface.IsNegotiated);
        Assert.Null(absentSurface.Tool.ProtocolTool.Meta);
        Assert.Null(absentSurface.Resource);
        Assert.Null(absentSurface.ResourceTemplate);
        Assert.Null(wrongMimeSurface.Tool.ProtocolTool.Meta);
        Assert.Null(wrongMimeSurface.Resource);
        Assert.Null(wrongMimeSurface.ResourceTemplate);

        Assert.Equal(
            SerializeToolResult(negotiatedSurface.Result),
            SerializeToolResult(absentSurface.Result));
        Assert.Equal(
            SerializeToolResult(negotiatedSurface.Result),
            SerializeToolResult(wrongMimeSurface.Result));
    }

    [Fact]
    public void ProductionCompactView_IsSelfContainedAndReadOnly()
    {
        var html = McpAppResources.CompactView();

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("textContent", html, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArtifactTool_InvalidKind_IsReadOnlyStructuredFailure()
    {
        var result = await ArtifactTools.Inspect(
            session: null!,
            file: "artifact.bin",
            kind: "unknown");
        var json = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

        using var document = JsonDocument.Parse(json);
        Assert.Contains("Specify kind=", document.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.False(document.RootElement.GetProperty("structuredContent").GetProperty("ok").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("_meta", out _));
    }

    [Fact]
    public void FullProfile_AdvertisesArtifactInspection()
    {
        Assert.Contains(
            "maui_artifact_inspect",
            McpServerHost.GetToolInventory(McpServerProfile.Full));
    }

    private static PhaseZeroSurface CreateSurface(ClientCapabilities clientCapabilities)
    {
        var negotiated = SupportsMcpApps(clientCapabilities);
        var tool = McpServerTool.Create(
            (Func<PhaseZeroViewModel>)CreateViewModel,
            new McpServerToolCreateOptions
            {
                Meta = negotiated
                    ? ToJsonObject(new UiMeta<McpUiToolMeta>(
                        new(AppResourceUri, ["model", "app"])))
                    : null
            });

        if (!negotiated)
            return new(false, tool, null, null, CreateToolResult());

        var resource = McpServerResource.Create(
            (Func<string>)RenderApp,
            new McpServerResourceCreateOptions
            {
                Meta = ToJsonObject(new UiMeta<McpUiResourceMeta>(
                    new(
                        new(
                            ["https://api.example.test"],
                            ["https://cdn.example.test"]),
                        true)))
            });
        var resourceTemplate = McpServerResource.Create(
            (Func<string, string>)RenderThemedApp);

        return new(true, tool, resource, resourceTemplate, CreateToolResult());
    }

    private static ClientCapabilities CreateClientCapabilities(string mimeType)
        => new()
        {
            Extensions = new Dictionary<string, object>
            {
                [UiExtension] = new McpUiClientCapabilities([mimeType])
            }
        };

    private static bool SupportsMcpApps(ClientCapabilities capabilities)
    {
        if (capabilities.Extensions is null ||
            !capabilities.Extensions.TryGetValue(UiExtension, out var settings))
        {
            return false;
        }

        var typed = JsonSerializer.Deserialize<McpUiClientCapabilities>(
            JsonSerializer.Serialize(settings, McpJsonUtilities.DefaultOptions),
            McpJsonUtilities.DefaultOptions);
        return typed?.MimeTypes.Contains(AppMimeType, StringComparer.Ordinal) == true;
    }

    private static CallToolResult CreateToolResult()
        => new()
        {
            Content = [new TextContentBlock { Text = "Phase 0 is ready." }],
            StructuredContent = JsonSerializer.SerializeToElement(
                CreateViewModel(),
                McpJsonUtilities.DefaultOptions)
        };

    private static TextResourceContents CreateResourceContents()
        => new()
        {
            Uri = AppResourceUri,
            MimeType = AppMimeType,
            Text = "<!doctype html><title>Phase 0</title>",
            Meta = ToJsonObject(new UiMeta<McpUiResourceMeta>(
                new(
                    new(
                        ["https://api.example.test"],
                        ["https://cdn.example.test"]),
                    true)))
        };

    private static JsonObject ToJsonObject<T>(T value)
        => JsonSerializer.SerializeToNode(value, McpJsonUtilities.DefaultOptions)!.AsObject();

    private static string SerializeToolResult(CallToolResult result)
        => JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

    [McpServerTool(
        Name = "phase_zero_view_model",
        UseStructuredContent = true)]
    private static PhaseZeroViewModel CreateViewModel()
        => new("Phase 0", true);

    [McpServerResource(
        UriTemplate = AppResourceUri,
        Name = "phase_zero_app",
        MimeType = AppMimeType)]
    private static string RenderApp()
        => "<!doctype html><title>Phase 0</title>";

    [McpServerResource(
        UriTemplate = "ui://maui/phase-0/{theme}",
        Name = "phase_zero_themed_app",
        MimeType = AppMimeType)]
    private static string RenderThemedApp(
        [Description("Theme requested by the MCP host")] string theme)
        => $"<!doctype html><body data-theme=\"{theme}\"></body>";

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the maui-labs repository root.");
    }

    private sealed record PhaseZeroSurface(
        bool IsNegotiated,
        McpServerTool Tool,
        McpServerResource? Resource,
        McpServerResource? ResourceTemplate,
        CallToolResult Result);

    private sealed record PhaseZeroViewModel(string Title, bool Ready);

    private sealed record UiMeta<T>(T Ui);

    private sealed record McpUiClientCapabilities(string[] MimeTypes);

    private sealed record McpUiToolMeta(string ResourceUri, string[] Visibility);

    private sealed record McpUiResourceMeta(McpUiResourceCsp Csp, bool PrefersBorder);

    private sealed record McpUiResourceCsp(
        string[] ConnectDomains,
        string[] ResourceDomains);
}

#pragma warning restore MCPEXP001
