using System.Text.Json;
using Spectre.Console;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// Abstraction over DevFlow output formatting, enabling DI and testability.
/// </summary>
public interface IDevFlowOutputWriter
{
	bool ResolveJsonMode(bool jsonFlag, bool noJsonFlag);
	void WriteResult<T>(T data, bool json, Action<T>? humanFormatter = null);
	void WriteResult<T>(T data, bool json, Action<T, IAnsiConsole> humanFormatter);
	void WriteRawJson(string jsonString);
	void WriteJsonElement(JsonElement element, bool json);
	void WriteActionResult(bool success, string action, string? elementId, bool json, string? humanMessage = null);
	void WriteError(string message, bool json, string errorType = "RuntimeError",
		bool retryable = false, string[]? suggestions = null);
	void WriteJsonLine<T>(T data);
	string FormatJson<T>(T data);
}
