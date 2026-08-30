using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class ExecutionManifestSchemaTests
{
    [Fact]
    public void DefaultSerialization_OmitsUnavailableFactsAndValidatesSchema()
    {
        var json = Encoding.UTF8.GetString(
            MauiTestExecutionManifestSerializer.SerializeToUtf8Bytes(
                new MauiTestExecutionManifest
                {
                    Host = new MauiTestExecutionHostFacts(),
                    Build = new MauiTestExecutionBuildFacts(),
                    Device = new MauiTestExecutionDeviceFacts(),
                    Lifecycle = new MauiTestExecutionLifecycleFacts(),
                    Outcome = new MauiTestExecutionOutcomeFacts(),
                }));

        ExecutionManifestSchemaValidator.AssertValid(json);
        using var document = JsonDocument.Parse(json);
        foreach (var propertyName in new[] { "host", "build", "device", "lifecycle", "outcome" })
            Assert.False(document.RootElement.TryGetProperty(propertyName, out _));
    }

    [Fact]
    public void EarlyFailureSerialization_OmitsUnavailableBuildAndDeviceAndValidatesSchema()
    {
        var json = Encoding.UTF8.GetString(
            MauiTestExecutionManifestSerializer.SerializeToUtf8Bytes(
                new MauiTestExecutionManifest
                {
                    ManifestId = "manifest-early-failure",
                    RunId = "run-early-failure",
                    Host = new MauiTestExecutionHostFacts
                    {
                        Os = "windows",
                        Architecture = "x64",
                        RunnerKind = "local-cli",
                    },
                    Build = new MauiTestExecutionBuildFacts(),
                    Lifecycle = new MauiTestExecutionLifecycleFacts
                    {
                        StartedAt = DateTimeOffset.UnixEpoch,
                        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
                        Stages =
                        [
                            new MauiTestExecutionLifecycleStage
                            {
                                Sequence = 1,
                                Name = "validate-request",
                                Status = "failed",
                                DetailCode = "project-path-missing",
                            },
                        ],
                    },
                    Outcome = new MauiTestExecutionOutcomeFacts
                    {
                        Status = "failed",
                        ExitCategory = "invalid-configuration",
                        Terminal = true,
                        Verified = false,
                    },
                    Artifacts =
                    [
                        new MauiTestExecutionArtifact
                        {
                            ArtifactId = "flow-run",
                            Role = "semantic-report",
                            Kind = "flow-run-report",
                            MediaType = "application/json",
                            Digest = new string('a', 64),
                            SizeBytes = 128,
                            RelativePath = "flow-run.json",
                            Redacted = true,
                        },
                    ],
                }));

        ExecutionManifestSchemaValidator.AssertValid(json);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("host", out _));
        Assert.False(document.RootElement.TryGetProperty("build", out _));
        Assert.False(document.RootElement.TryGetProperty("device", out _));
        Assert.True(document.RootElement.TryGetProperty("lifecycle", out _));
        Assert.True(document.RootElement.TryGetProperty("outcome", out _));
    }

    [Fact]
    public void Serialization_DropsInvalidLifecycleSequencesAndEmitsCanonicalRuntimeProfile()
    {
        var build = new MauiTestExecutionBuildFacts
        {
            TargetFramework = "net10.0-android",
        };
        var device = new MauiTestExecutionDeviceFacts
        {
            Platform = "android",
            RuntimeKind = "emulator",
            Architecture = "x64",
            ApiLevel = "35",
            OsVersion = "15",
            FormFactor = "phone",
        };
        var json = Encoding.UTF8.GetString(
            MauiTestExecutionManifestSerializer.SerializeToUtf8Bytes(
                new MauiTestExecutionManifest
                {
                    Build = build,
                    Device = device,
                    Lifecycle = new MauiTestExecutionLifecycleFacts
                    {
                        Stages =
                        [
                            new MauiTestExecutionLifecycleStage
                            {
                                Sequence = 0,
                                Name = "invalid",
                                Status = "failed",
                            },
                            new MauiTestExecutionLifecycleStage
                            {
                                Sequence = 1,
                                Name = "valid",
                                Status = "passed",
                            },
                            new MauiTestExecutionLifecycleStage
                            {
                                Sequence = 1,
                                Name = "duplicate",
                                Status = "failed",
                            },
                        ],
                    },
                }));

        ExecutionManifestSchemaValidator.AssertValid(json);
        using var document = JsonDocument.Parse(json);
        var stage = Assert.Single(
            document.RootElement.GetProperty("lifecycle").GetProperty("stages").EnumerateArray());
        Assert.Equal(1, stage.GetProperty("sequence").GetInt32());
        Assert.Equal("valid", stage.GetProperty("name").GetString());
        Assert.Matches(
            "^sha256:[0-9a-f]{64}$",
            MauiTestExecutionRuntimeProfile.CreateFingerprint(build, device)!);
    }
}

internal static class ExecutionManifestSchemaValidator
{
    public static void AssertValid(string json)
    {
        using var instance = JsonDocument.Parse(json);
        using var schema = JsonDocument.Parse(File.ReadAllText(SchemaPath()));
        var errors = new List<string>();

        Validate(instance.RootElement, schema.RootElement, schema.RootElement, "$", errors);

        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static void Validate(
        JsonElement instance,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        List<string> errors)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Validate(instance, ResolveReference(rootSchema, reference.GetString()!), rootSchema, path, errors);
            return;
        }

        if (schema.TryGetProperty("const", out var constant) &&
            !string.Equals(instance.GetRawText(), constant.GetRawText(), StringComparison.Ordinal))
        {
            errors.Add($"{path}: expected const {constant.GetRawText()}, got {instance.GetRawText()}.");
        }

        if (schema.TryGetProperty("type", out var type))
        {
            var expectedType = type.GetString();
            var typeMatches = expectedType switch
            {
                "object" => instance.ValueKind == JsonValueKind.Object,
                "array" => instance.ValueKind == JsonValueKind.Array,
                "string" => instance.ValueKind == JsonValueKind.String,
                "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
                "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => true,
            };
            if (!typeMatches)
            {
                errors.Add($"{path}: expected JSON type {expectedType}, got {instance.ValueKind}.");
                return;
            }
        }

        if (instance.ValueKind == JsonValueKind.String)
        {
            var value = instance.GetString()!;
            if (schema.TryGetProperty("pattern", out var pattern) &&
                !Regex.IsMatch(value, pattern.GetString()!, RegexOptions.CultureInvariant))
            {
                errors.Add($"{path}: value does not match pattern {pattern.GetString()}.");
            }
            if (schema.TryGetProperty("format", out var format) &&
                format.GetString() == "date-time" &&
                !DateTimeOffset.TryParse(value, out _))
            {
                errors.Add($"{path}: value is not an RFC 3339 date-time.");
            }
        }

        if (instance.ValueKind == JsonValueKind.Number &&
            schema.TryGetProperty("minimum", out var minimum) &&
            instance.TryGetInt64(out var integerValue) &&
            integerValue < minimum.GetInt64())
        {
            errors.Add($"{path}: value {integerValue} is below minimum {minimum.GetInt64()}.");
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var propertyName in required.EnumerateArray().Select(item => item.GetString()!))
                {
                    if (!instance.TryGetProperty(propertyName, out _))
                        errors.Add($"{path}: required property '{propertyName}' is absent.");
                }
            }

            if (schema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in instance.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out var propertySchema))
                    {
                        Validate(
                            property.Value,
                            propertySchema,
                            rootSchema,
                            $"{path}.{property.Name}",
                            errors);
                    }
                }
            }
        }

        if (instance.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("maxItems", out var maxItems) &&
                instance.GetArrayLength() > maxItems.GetInt32())
            {
                errors.Add($"{path}: array exceeds maxItems {maxItems.GetInt32()}.");
            }
            if (schema.TryGetProperty("items", out var itemSchema))
            {
                var index = 0;
                foreach (var item in instance.EnumerateArray())
                {
                    Validate(item, itemSchema, rootSchema, $"{path}[{index}]", errors);
                    index++;
                }
            }
        }
    }

    private static JsonElement ResolveReference(JsonElement rootSchema, string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Only local JSON Schema references are supported: {reference}");

        var current = rootSchema;
        foreach (var encodedSegment in reference[2..].Split('/'))
        {
            var segment = encodedSegment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current.GetProperty(segment);
        }
        return current;
    }

    private static string SchemaPath()
        => Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "DevFlow",
            "spec",
            "schemas",
            "maui-test-execution-manifest-v1.json");

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
