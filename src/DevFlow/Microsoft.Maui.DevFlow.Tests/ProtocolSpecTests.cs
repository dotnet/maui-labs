using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using YamlDotNet.Serialization;

namespace Microsoft.Maui.DevFlow.Tests;

public class ProtocolSpecTests
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();
    private static readonly YamlDotNet.Serialization.ISerializer JsonCompatibleYamlSerializer = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    private static readonly Lazy<string> SpecRoot = new(FindSpecRoot);

    [Fact]
    public async Task OpenApiYaml_CanBeParsedByOpenApiTooling()
    {
        var openApiPath = Path.Combine(SpecRoot.Value, "openapi.yaml");
        var openApiJson = ConvertYamlToJson(File.ReadAllText(openApiPath));

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(openApiJson));
        var ruleSet = new ValidationRuleSet(ValidationRuleSet.GetDefaultRuleSet());

        // Relative external references are validated separately so this test can focus on OpenAPI parsing diagnostics.
        ruleSet.Remove("OpenApiDocumentReferencesAreValid");

        var result = await OpenApiDocument.LoadAsync(
            stream,
            "json",
            new OpenApiReaderSettings { RuleSet = ruleSet },
            CancellationToken.None);

        var document = result.Document ?? throw new InvalidOperationException("OpenAPI parser did not return a document.");
        var diagnostic = result.Diagnostic ?? throw new InvalidOperationException("OpenAPI parser did not return diagnostics.");

        Assert.Equal(OpenApiSpecVersion.OpenApi3_1, diagnostic.SpecificationVersion);
        Assert.Empty(diagnostic.Errors);
        Assert.Empty(diagnostic.Warnings);
        Assert.Equal("DevFlow Agent Protocol", document.Info.Title);
        Assert.NotEmpty(document.Paths);
    }

    [Fact]
    public void ProtocolSpecFiles_AreValidYamlOrJson()
    {
        var failures = new List<string>();
        var specFiles = EnumerateSpecDocuments().ToList();

        Assert.NotEmpty(specFiles);

        foreach (var path in specFiles)
        {
            try
            {
                _ = LoadDocument(path);
            }
            catch (Exception ex) when (ex is JsonException or YamlDotNet.Core.YamlException or InvalidOperationException)
            {
                failures.Add($"{RelativeSpecPath(path)}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void ProtocolSpecReferences_TargetExistingDocumentsAndPointers()
    {
        var specFiles = EnumerateSpecDocuments().ToList();

        Assert.NotEmpty(specFiles);

        var documents = specFiles
            .ToDictionary(Path.GetFullPath, LoadDocument, StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        foreach (var (sourcePath, sourceDocument) in documents)
        {
            foreach (var reference in EnumerateReferences(sourceDocument))
            {
                var (targetPath, pointer) = ResolveReference(sourcePath, reference.Value);

                if (!documents.TryGetValue(targetPath, out var targetDocument))
                {
                    failures.Add(
                        $"{RelativeSpecPath(sourcePath)} {reference.JsonPath}: '{reference.Value}' targets missing file '{RelativeSpecPath(targetPath)}'.");
                    continue;
                }

                if (!PointerExists(targetDocument, pointer))
                {
                    failures.Add(
                        $"{RelativeSpecPath(sourcePath)} {reference.JsonPath}: '{reference.Value}' targets a missing JSON pointer.");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void PointerExists_HandlesEscapedSegmentsAndNullValues()
    {
        var document = JsonNode.Parse(
            """
            {
              "foo/bar": null,
              "~key": true,
              "%7E1": true,
              "items": [null]
            }
            """);

        Assert.True(PointerExists(document, "/foo~1bar"));
        Assert.True(PointerExists(document, "/~0key"));
        Assert.True(PointerExists(document, "/%7E1"));
        Assert.True(PointerExists(document, "/items/0"));
        Assert.False(PointerExists(document, "/missing"));
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

    private static IEnumerable<string> EnumerateSpecDocuments()
    {
        return Directory.EnumerateFiles(SpecRoot.Value, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
    }

    private static JsonNode LoadDocument(string path)
    {
        var text = File.ReadAllText(path);
        var json = IsYaml(path) ? ConvertYamlToJson(text) : text;

        return JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow })
            ?? throw new InvalidOperationException($"Unable to parse {RelativeSpecPath(path)}.");
    }

    private static string ConvertYamlToJson(string content)
    {
        var yamlObject = YamlDeserializer.Deserialize(new StringReader(content));
        return JsonCompatibleYamlSerializer.Serialize(yamlObject);
    }

    private static bool IsYaml(string path)
    {
        return path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<SpecReference> EnumerateReferences(JsonNode? node, string jsonPath = "$")
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var childPath = jsonPath == "$" ? $"$.{property.Key}" : $"{jsonPath}.{property.Key}";

                if (property.Key == "$ref" &&
                    property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var reference))
                {
                    yield return new SpecReference(childPath, reference);
                }

                foreach (var nestedReference in EnumerateReferences(property.Value, childPath))
                    yield return nestedReference;
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                foreach (var nestedReference in EnumerateReferences(array[i], $"{jsonPath}[{i}]"))
                    yield return nestedReference;
            }
        }
    }

    private static (string TargetPath, string Pointer) ResolveReference(string sourcePath, string reference)
    {
        var hashIndex = reference.IndexOf('#', StringComparison.Ordinal);
        var filePart = hashIndex >= 0 ? reference[..hashIndex] : reference;
        var pointer = hashIndex >= 0 ? reference[(hashIndex + 1)..] : string.Empty;
        var targetPath = string.IsNullOrEmpty(filePart)
            ? sourcePath
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, filePart));

        return (targetPath, pointer);
    }

    private static bool PointerExists(JsonNode? root, string pointer)
    {
        if (string.IsNullOrEmpty(pointer))
            return root is not null;

        if (!pointer.StartsWith('/'))
            return false;

        var current = root;
        foreach (var segment in pointer[1..].Split('/'))
        {
            var key = segment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(key, out current))
                    return false;
            }
            else if (current is JsonArray array)
            {
                if (!int.TryParse(key, out var index) || index < 0 || index >= array.Count)
                    return false;

                current = array[index];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static string RelativeSpecPath(string path)
    {
        return Path.GetRelativePath(SpecRoot.Value, path);
    }

    private sealed record SpecReference(string JsonPath, string Value);
}
