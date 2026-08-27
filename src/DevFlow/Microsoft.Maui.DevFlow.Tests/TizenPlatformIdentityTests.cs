using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Agent-, driver- and schema-side coverage for Tizen platform identity. The Tizen backend itself
/// lives in Redth/Maui.Tizen; what this repository owns is the contract that lets such an agent
/// report <c>Tizen</c> truthfully.
/// </summary>
/// <remarks>
/// Several tests mutate the <c>DEVFLOW_PLATFORM</c> environment variable, which is process-wide.
/// The collection keeps them from running in parallel with any other environment-mutating class.
/// </remarks>
[Collection("EnvironmentVariables")]
public class TizenPlatformIdentityTests
{
    // ── Agent-side detection ──────────────────────────────────────────────

    [Fact]
    public void DetectName_HonoursTheDevFlowPlatformOverride()
    {
        // The escape hatch for an out-of-tree agent on a platform detection reads incorrectly: it
        // must win even when detection succeeds, because the failure mode it addresses is a
        // confidently wrong answer (a Tizen host detecting as Linux), not detection giving up.
        Assert.Equal("Tizen", DetectNameWithOverride("Tizen"));
        Assert.Equal(DevFlowPlatform.Tizen, DevFlowPlatform.Normalize(DetectNameWithOverride("Tizen")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DetectName_IgnoresAnEmptyOverride(string overrideValue)
        => Assert.Equal(ExpectedHostPlatformName(), DetectNameWithOverride(overrideValue));

    [Theory]
    // The override is reported verbatim to clients and serialized into JSON, so a value that is
    // not a plausible platform name must be ignored rather than placed on the wire.
    [InlineData("Tizen\"},\"evil\":{\"")]
    [InlineData("Tizen\nX-Injected: 1")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Tizen/../../etc/passwd")]
    [InlineData("ThisPlatformNameIsFarTooLongToBeLegitimate")]
    public void DetectName_RejectsAnImplausibleOverrideAndFallsBackToDetection(string overrideValue)
        => Assert.Equal(ExpectedHostPlatformName(), DetectNameWithOverride(overrideValue));

    [Theory]
    [InlineData("Tizen")]
    [InlineData("Tizen 8.0")]
    [InlineData("my-custom_platform.1")]
    public void DetectName_AcceptsAPlausibleOverride(string overrideValue)
        => Assert.Equal(overrideValue, DetectNameWithOverride(overrideValue));

    private static string DetectNameWithOverride(string? overrideValue)
    {
        var original = Environment.GetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, overrideValue);
            return DevFlowRuntimePlatform.DetectName();
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevFlowRuntimePlatform.OverrideEnvironmentVariable, original);
        }
    }

    [Fact]
    public void DetectName_ReportsTheHostPlatformAndNotTizen()
    {
        Assert.False(DevFlowRuntimePlatform.IsTizen);
        Assert.Equal(ExpectedHostPlatformName(), DevFlowRuntimePlatform.DetectName());
    }

    [Fact]
    public void DetectName_WindowsNameIsSelectableForTheUiFacingAgent()
    {
        // The UI agent reports "WinUI" and host bootstrap reports "Windows"; both must normalize
        // to the same canonical identifier so a filter written against either one works.
        Assert.Equal(DevFlowPlatform.Windows, DevFlowPlatform.Normalize("WinUI"));
        Assert.Equal(DevFlowPlatform.Windows, DevFlowPlatform.Normalize("Windows"));

        if (OperatingSystem.IsWindows())
            Assert.Equal("WinUI", DevFlowRuntimePlatform.DetectName(windowsName: "WinUI"));
    }

    private static string ExpectedHostPlatformName()
    {
        if (OperatingSystem.IsWindows()) return "Windows";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    // ── Driver factory ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Tizen")]
    [InlineData("tizen")]
    [InlineData("tizen-nui")]
    [InlineData("macOS")]
    public void AppDriverFactory_RecognizedPlatformsWithoutAHostDriverFailExplicitly(string platform)
    {
        Assert.False(AppDriverFactory.HasLocalDriver(platform));

        var exception = Assert.Throws<PlatformNotSupportedException>(() => AppDriverFactory.Create(platform));

        // The point of the change: an explicit "recognized, but host-side driving is unavailable"
        // instead of the old "Unknown platform" that made these look unsupported outright.
        Assert.Contains(DevFlowPlatform.Normalize(platform), exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown platform", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("maccatalyst")]
    [InlineData("mac")]
    [InlineData("catalyst")]
    [InlineData("android")]
    [InlineData("ios")]
    [InlineData("iossimulator")]
    [InlineData("windows")]
    [InlineData("win")]
    [InlineData("winui")]
    [InlineData("wpf")]
    [InlineData("linux")]
    [InlineData("gtk")]
    public void AppDriverFactory_ExistingPlatformAliasesStillResolve(string platform)
    {
        Assert.True(AppDriverFactory.HasLocalDriver(platform));

        using var driver = AppDriverFactory.Create(platform);
        Assert.NotNull(driver);
    }

    [Fact]
    public void AppDriverFactory_UnknownPlatformStillThrowsArgumentException()
    {
        Assert.False(AppDriverFactory.HasLocalDriver("nintendo64"));

        var exception = Assert.Throws<ArgumentException>(() => AppDriverFactory.Create("nintendo64"));

        // The "Supported:" list must name only platforms Create can actually construct, not every
        // identity DevFlowPlatform recognizes.
        Assert.DoesNotContain(DevFlowPlatform.Tizen, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(DevFlowPlatform.MacOS, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(DevFlowPlatform.Android, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Protocol schema ───────────────────────────────────────────────────

    [Fact]
    public void AgentStatusSchema_PlatformEnumIsAdditiveAndIncludesTizen()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(FindSpecRoot(), "schemas", "agent-status.json")))!;
        var values = schema["properties"]!["device"]!["properties"]!["platform"]!["enum"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        Assert.Contains(DevFlowPlatform.Tizen, values);

        // Additive only: dropping a previously published identifier would break shipped clients.
        foreach (var expected in new[] { "ios", "android", "maccatalyst", "windows", "linux", "macos" })
            Assert.Contains(expected, values);

        // Every identifier the schema advertises must be one the client can normalize to itself.
        foreach (var value in values)
        {
            Assert.Equal(value, DevFlowPlatform.Normalize(value));
            Assert.True(DevFlowPlatform.IsKnown(value), value);
        }

        // …and vice versa, so the schema and the client cannot drift apart.
        Assert.Equal(
            DevFlowPlatform.KnownIds.OrderBy(id => id, StringComparer.Ordinal),
            values.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void AgentStatusExample_ValidatesAgainstTheNestedDevicePlatformSchema()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(FindSpecRoot(), "schemas", "agent-status.json")))!.AsObject();
        var examplePath = Path.Combine(FindSpecRoot(), "examples", "agent-status-response.json");
        using var document = JsonDocument.Parse(File.ReadAllText(examplePath));

        AssertSchemaValid(document.RootElement, schema);

        var platform = document.RootElement.GetProperty("device").GetProperty("platform").GetString();
        Assert.False(string.IsNullOrWhiteSpace(platform));
        Assert.True(DevFlowPlatform.IsKnown(platform), platform);
    }

    [Fact]
    public void AgentStatusSerialization_ValidatesAgainstTheProtocolSchema()
    {
        var status = new AgentStatus
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            Agent = new AgentDescriptor
            {
                Name = "Microsoft.Maui.DevFlow.Agent",
                Version = "0.1.0-preview.13",
                Framework = ".NET MAUI",
                FrameworkId = "maui",
                FrameworkVersion = "10.0.0",
                UiFramework = "maui-controls",
            },
            Device = new DeviceDescriptor
            {
                Platform = DevFlowPlatform.Tizen,
                DeviceType = "Virtual",
                Idiom = "tv",
                DisplayDensity = 1,
                WindowCount = 1,
                WindowWidth = 1920,
                WindowHeight = 1080,
            },
            App = new AppDescriptor
            {
                Name = "TizenSample",
                Version = "1.0.0",
                Build = "1",
                PackageId = "org.tizen.sample",
                ProcessId = 1234,
            },
            Capabilities = new AgentCapabilities
            {
                Ui = true,
                Screenshots = true,
                Network = true,
                Logs = true,
            },
            Extensions = new ExtensionsMarker
            {
                Count = 0,
                Hash = new string('0', 64),
            },
            Running = true,
            Route = "//home",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(status));
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(FindSpecRoot(), "schemas", "agent-status.json")))!.AsObject();

        AssertSchemaValid(document.RootElement, schema);
        Assert.Equal(
            DevFlowPlatform.Tizen,
            document.RootElement.GetProperty("device").GetProperty("platform").GetString());
        Assert.False(document.RootElement.TryGetProperty("platform", out _));
    }

    private static void AssertSchemaValid(JsonElement value, JsonObject schema, string path = "$")
    {
        var type = schema["type"]?.GetValue<string>();
        switch (type)
        {
            case "object":
                Assert.True(value.ValueKind == JsonValueKind.Object, $"{path} must be an object.");

                if (schema["required"] is JsonArray required)
                {
                    foreach (var requiredProperty in required)
                    {
                        var name = requiredProperty!.GetValue<string>();
                        Assert.True(value.TryGetProperty(name, out _), $"{path}.{name} is required.");
                    }
                }

                if (schema["properties"] is JsonObject properties)
                {
                    foreach (var property in properties)
                    {
                        if (value.TryGetProperty(property.Key, out var propertyValue))
                            AssertSchemaValid(propertyValue, property.Value!.AsObject(), $"{path}.{property.Key}");
                    }
                }
                break;

            case "string":
                Assert.True(value.ValueKind == JsonValueKind.String, $"{path} must be a string.");
                if (schema["enum"] is JsonArray allowed)
                {
                    var actual = value.GetString();
                    Assert.Contains(allowed, item => item!.GetValue<string>() == actual);
                }
                break;

            case "boolean":
                Assert.True(
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    $"{path} must be a boolean.");
                break;

            case "integer":
                Assert.True(value.TryGetInt64(out var integer), $"{path} must be an integer.");
                if (schema["minimum"] is JsonValue integerMinimum)
                    Assert.True(integer >= integerMinimum.GetValue<long>(), $"{path} is below its minimum.");
                break;

            case "number":
                Assert.True(value.TryGetDouble(out var number), $"{path} must be a number.");
                if (schema["minimum"] is JsonValue numberMinimum)
                    Assert.True(number >= numberMinimum.GetValue<double>(), $"{path} is below its minimum.");
                break;
        }
    }

    private static string FindSpecRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "DevFlow", "spec");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find docs/DevFlow/spec from the test output directory.");
    }
}
