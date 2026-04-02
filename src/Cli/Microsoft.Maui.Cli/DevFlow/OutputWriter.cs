using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// Central output abstraction for consistent JSON/human output across all CLI commands.
/// JSON mode: raw data on stdout, structured errors on stderr.
/// Human mode: formatted text on stdout, plain errors on stderr.
/// </summary>
class DevFlowOutputWriter : IDevFlowOutputWriter
{
    /// <summary>
    /// Resolves whether JSON output mode is active.
    /// Priority: --no-json flag > --json flag > MAUIDEVFLOW_OUTPUT env var > TTY auto-detection.
    /// </summary>
    public bool ResolveJsonMode(bool jsonFlag, bool noJsonFlag)
    {
        if (noJsonFlag) return false;
        if (jsonFlag) return true;

        var envVar = Environment.GetEnvironmentVariable("MAUIDEVFLOW_OUTPUT");
        if (string.Equals(envVar, "json", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Console.IsOutputRedirected)
            return true;

        return false;
    }

    /// <summary>
    /// Write a successful result to stdout.
    /// In JSON mode, serializes the data. In human mode, calls the humanFormatter.
    /// </summary>
    public void WriteResult<T>(T data, bool json, Action<T>? humanFormatter = null)
    {
        if (json)
        {
            Console.WriteLine(CliJson.SerializeUntyped(data, indented: true));
        }
        else if (humanFormatter != null)
        {
            humanFormatter(data);
        }
        else
        {
            Console.WriteLine(CliJson.SerializeUntyped(data, indented: true));
        }
    }

    /// <summary>
    /// Write raw JSON string to stdout (for data already serialized or from HTTP responses).
    /// </summary>
    public void WriteRawJson(string jsonString)
    {
        Console.WriteLine(jsonString);
    }

    /// <summary>
    /// Write a <see cref="JsonElement"/> to stdout.
    /// In JSON mode, writes the element's raw JSON text. In human mode, writes indented JSON.
    /// </summary>
    public void WriteJsonElement(JsonElement element, bool json)
    {
        if (json)
        {
            Console.WriteLine(element.GetRawText());
        }
        else
        {
            Console.WriteLine(CliJson.PrettyPrint(element));
        }
    }

    /// <summary>
    /// Write a simple success action result (for tap, fill, clear, etc.).
    /// </summary>
    public void WriteActionResult(bool success, string action, string? elementId, bool json, string? humanMessage = null)
    {
        if (json)
        {
            Console.WriteLine(CliJson.SerializeUntyped(new JsonObject
            {
                ["success"] = success,
                ["action"] = action,
                ["elementId"] = elementId
            }, indented: false));
        }
        else
        {
            Console.WriteLine(humanMessage ?? (success ? $"{action}: {elementId}" : $"Failed to {action.ToLowerInvariant()}: {elementId}"));
        }
    }

    /// <summary>
    /// Write a structured error to stderr and set error state.
    /// In JSON mode, outputs structured error JSON. In human mode, plain text.
    /// </summary>
    public void WriteError(string message, bool json, string errorType = "RuntimeError",
        bool retryable = false, string[]? suggestions = null)
    {
        if (json)
        {
            var error = new JsonObject
            {
                ["error"] = message,
                ["type"] = errorType,
                ["retryable"] = retryable
            };
            if (suggestions is { Length: > 0 })
            {
                var suggestionArray = new JsonArray();
                foreach (var suggestion in suggestions)
                    suggestionArray.Add((JsonNode?)JsonValue.Create(suggestion));
                error["suggestions"] = suggestionArray;
            }

            Console.Error.WriteLine(CliJson.SerializeUntyped(error, indented: false));
        }
        else
        {
            Console.Error.WriteLine($"Error: {message}");
        }
    }

    /// <summary>
    /// Write a single JSONL line (for streaming commands).
    /// </summary>
    public void WriteJsonLine<T>(T data)
    {
        Console.WriteLine(CliJson.SerializeUntyped(data, indented: false));
    }

    /// <summary>
    /// Serialize an object to indented JSON string.
    /// </summary>
    public string FormatJson<T>(T data)
    {
        return CliJson.SerializeUntyped(data, indented: true);
    }
}
