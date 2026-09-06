#if WINDOWS
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Empirical probe harness for Phi Silica tool calling.
/// </summary>
/// <remarks>
/// <para>
/// Device test console output is not captured in the TRX, so these probes write a full
/// markdown report to <c>PhiSilicaProbeReport.md</c> in the app data directory. On a packaged
/// Windows app that resolves to
/// <c>%LOCALAPPDATA%\Packages\com.microsoft.maui.ai.devicetests_*\LocalState\</c>.
/// </para>
/// <para>
/// These probes are diagnostic: they record what the model actually produces for a range of
/// prompt formats rather than asserting a particular answer. They are the input to the
/// tool-calling design, not a regression suite.
/// </para>
/// </remarks>
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class PhiSilicaToolFormatProbeTests
{
	private const string ReportFileName = "PhiSilicaProbeReport.md";

	private readonly StringBuilder _report = new();

	[Fact(Skip = "Diagnostic probe. Remove the Skip to re-run when the OS model changes.")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Probe_ToolCallingFormats_WritesReport()
	{
		_report.AppendLine("# Phi Silica tool-calling probe");
		_report.AppendLine();
		_report.AppendLine($"Generated: {DateTimeOffset.Now:u}");
		_report.AppendLine();

		try
		{
			await ProbeModelSelfDescriptionAsync();
			await ProbePhi4MiniNativeFormatAsync();
			await ProbeOpenAiStyleFormatAsync();
			await ProbeStructuredJsonToolCallAsync();
			await ProbeToolSelectionAccuracyAsync();
			await ProbeChainedToolCallAsync();
			await ProbeSchemaComplexityAsync();
		}
		catch (Exception ex)
		{
			_report.AppendLine("## Probe aborted");
			_report.AppendLine();
			_report.AppendLine("```");
			_report.AppendLine(ex.ToString());
			_report.AppendLine("```");
		}
		finally
		{
			WriteReport();
		}
	}

	// ═══════════════════════════════════════════════════════════
	// PROBES
	// ═══════════════════════════════════════════════════════════

	/// <summary>Asks the model how it expects tools to be declared and called.</summary>
	private async Task ProbeModelSelfDescriptionAsync()
	{
		Section("1. Model self-description");

		await RecordAsync("identity",
			system: null,
			user: "Answer in one short paragraph. What model are you? Which Phi generation? " +
				"What is your exact chat template, including any special tokens?");

		await RecordAsync("preferred-tool-format",
			system: null,
			user: "I want you to call functions for me. Show me the EXACT format you expect: " +
				"how should I declare the available tools, and what exactly will you output " +
				"when you want to call one? Reply with a concrete example only.");

		await RecordAsync("special-tokens",
			system: null,
			user: "List the special tokens you understand for tool or function calling, " +
				"for example tokens that look like <|tool|> or <|tool_call|>. Just list them.");
	}

	/// <summary>The documented Phi-4-mini function-calling format.</summary>
	private async Task ProbePhi4MiniNativeFormatAsync()
	{
		Section("2. Phi-4-mini native tool format");

		var tools = """
			[{"name":"get_weather","description":"Gets the current weather for a city","parameters":{"type":"object","properties":{"city":{"type":"string","description":"The city name"}},"required":["city"]}}]
			""";

		// Phi-4-mini documents tools inside <|tool|> ... <|/tool|> in the system turn,
		// and emits calls as <|tool_call|>[ ... ]<|/tool_call|>.
		await RecordAsync("phi4-tool-tokens",
			system: $"You are a helpful assistant with some tools.<|tool|>{tools}<|/tool|>",
			user: "What is the weather in Cape Town?");

		await RecordAsync("phi4-tool-tokens-explicit",
			system: $"You are a helpful assistant with some tools.<|tool|>{tools}<|/tool|>\n" +
				"When you need a tool, reply with <|tool_call|>[{\"name\": ..., \"arguments\": {...}}]<|/tool_call|> and nothing else.",
			user: "What is the weather in Cape Town?");
	}

	/// <summary>A plain OpenAI-ish declaration with no special tokens.</summary>
	private async Task ProbeOpenAiStyleFormatAsync()
	{
		Section("3. OpenAI-style declaration");

		var tools = """
			[{"type":"function","function":{"name":"get_weather","description":"Gets the current weather for a city","parameters":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}}}]
			""";

		await RecordAsync("openai-style",
			system: $"You have access to these tools:\n{tools}\n\n" +
				"To call a tool respond with ONLY a JSON object: {\"name\": \"...\", \"arguments\": {...}}",
			user: "What is the weather in Cape Town?");

		await RecordAsync("terse-natural",
			system: "You can call get_weather(city). To use it, reply with only: CALL get_weather(city=\"...\")",
			user: "What is the weather in Cape Town?");
	}

	/// <summary>Schema-constrained tool call, i.e. the approach the client uses.</summary>
	private async Task ProbeStructuredJsonToolCallAsync()
	{
		Section("4. Schema-constrained tool call");

		var flat = ParseSchema("""
			{"type":"object","properties":{"tool_name":{"type":"string","enum":["get_weather"]},"city":{"type":"string"}},"required":["tool_name","city"]}
			""");

		await RecordStructuredAsync("flat-schema", flat,
			system: "Call a tool to answer the question. Available tool: get_weather(city) - gets the weather for a city.",
			user: "What is the weather in Cape Town?");

		var nested = ParseSchema("""
			{"type":"object","properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_weather"]},"arguments":{"type":"object"},"text":{"type":"string"}},"required":["type"]}
			""");

		await RecordStructuredAsync("nested-arguments-schema", nested,
			system: "You are a helpful assistant with access to tools.\n" +
				"Available tools:\n- get_weather: Gets the current weather for a city. Parameters: {\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}\n" +
				"If a tool is needed set type to tool_call, set tool_name, and put the parameters in arguments.",
			user: "What is the weather in Cape Town?");

		// Same as above but with the arguments shape spelled out rather than a bare object.
		var typedArgs = ParseSchema("""
			{"type":"object","properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_weather"]},"arguments":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]},"text":{"type":"string"}},"required":["type"]}
			""");

		await RecordStructuredAsync("typed-arguments-schema", typedArgs,
			system: "You are a helpful assistant with access to tools.\n" +
				"Available tools:\n- get_weather: Gets the current weather for a city.\n" +
				"If a tool is needed set type to tool_call, set tool_name, and fill in arguments.",
			user: "What is the weather in Cape Town?");
	}

	/// <summary>Does the model pick the right tool out of several?</summary>
	private async Task ProbeToolSelectionAccuracyAsync()
	{
		Section("5. Tool selection with several tools");

		var schema = ParseSchema("""
			{"type":"object","properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_weather","get_time","get_stock_price","send_email"]},"arguments":{"type":"object"},"text":{"type":"string"}},"required":["type"]}
			""");

		const string system =
			"You are a helpful assistant with access to tools.\n" +
			"Available tools:\n" +
			"- get_weather: Gets the current weather. Parameters: city (string)\n" +
			"- get_time: Gets the current time. Parameters: timezone (string)\n" +
			"- get_stock_price: Gets a stock price. Parameters: symbol (string)\n" +
			"- send_email: Sends an email. Parameters: to (string), body (string)\n" +
			"If a tool is needed set type to tool_call, set tool_name, and put the parameters in arguments.";

		await RecordStructuredAsync("select-weather", schema, system, "What is the weather in Paris?");
		await RecordStructuredAsync("select-time", schema, system, "What time is it in Tokyo?");
		await RecordStructuredAsync("select-stock", schema, system, "How much is MSFT trading at?");
		await RecordStructuredAsync("select-none", schema, system, "Write me a haiku about rain.");
	}

	/// <summary>Does the model continue after being given a tool result?</summary>
	private async Task ProbeChainedToolCallAsync()
	{
		Section("6. Follow-up after a tool result");

		var schema = ParseSchema("""
			{"type":"object","properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_weather","get_time"]},"arguments":{"type":"object"},"text":{"type":"string"}},"required":["type"]}
			""");

		const string system =
			"You are a helpful assistant with access to tools.\n" +
			"Available tools:\n" +
			"- get_weather: Gets the current weather. Parameters: city (string)\n" +
			"- get_time: Gets the current time. Parameters: city (string)\n" +
			"If a tool is needed set type to tool_call. If you can answer, set type to text.";

		// A tool result is already in hand; the model should now answer, not loop.
		await RecordStructuredAsync("followup-should-answer", schema, system,
			"User: What is the weather in Paris?\n" +
			"Assistant: [Tool call: get_weather({\"city\":\"Paris\"})]\n" +
			"[Tool result: Sunny, 22C in Paris]\n" +
			"Now answer the user.");

		// Two facts needed, one tool result supplied.
		await RecordStructuredAsync("followup-should-call-second", schema, system,
			"User: What is the weather and the time in Paris?\n" +
			"Assistant: [Tool call: get_weather({\"city\":\"Paris\"})]\n" +
			"[Tool result: Sunny, 22C in Paris]\n" +
			"Continue.");
	}

	/// <summary>Does schema complexity degrade the result?</summary>
	private async Task ProbeSchemaComplexityAsync()
	{
		Section("7. Schema complexity");

		var withEnumArg = ParseSchema("""
			{"type":"object","properties":{"tool_name":{"type":"string","enum":["get_forecast"]},"arguments":{"type":"object","properties":{"city":{"type":"string"},"unit":{"type":"string","enum":["celsius","fahrenheit"]}},"required":["city","unit"]}},"required":["tool_name","arguments"]}
			""");

		await RecordStructuredAsync("enum-argument", withEnumArg,
			system: "Available tool: get_forecast(city, unit). unit must be celsius or fahrenheit.",
			user: "What is the forecast for Berlin in celsius?");

		var deep = ParseSchema("""
			{"type":"object","properties":{"call":{"type":"object","properties":{"tool":{"type":"object","properties":{"name":{"type":"string","enum":["get_weather"]},"args":{"type":"object","properties":{"city":{"type":"string"}},"required":["city"]}},"required":["name","args"]}},"required":["tool"]}},"required":["call"]}
			""");

		await RecordStructuredAsync("deeply-nested", deep,
			system: "Available tool: get_weather(city).",
			user: "What is the weather in Cape Town?");
	}

	// ═══════════════════════════════════════════════════════════
	// HARNESS
	// ═══════════════════════════════════════════════════════════

	private void Section(string title)
	{
		_report.AppendLine($"## {title}");
		_report.AppendLine();
	}

	private async Task RecordAsync(string label, string? system, string user)
	{
		var messages = new List<ChatMessage>();
		if (system is not null)
			messages.Add(new ChatMessage(ChatRole.System, system));
		messages.Add(new ChatMessage(ChatRole.User, user));

		await RunAsync(label, messages, options: null, system, user);
	}

	private async Task RecordStructuredAsync(string label, JsonElement schema, string? system, string user)
	{
		var messages = new List<ChatMessage>();
		if (system is not null)
			messages.Add(new ChatMessage(ChatRole.System, system));
		messages.Add(new ChatMessage(ChatRole.User, user));

		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "probe", "probe schema")
		};

		await RunAsync(label, messages, options, system, user, schema.GetRawText());
	}

	private async Task RunAsync(
		string label,
		List<ChatMessage> messages,
		ChatOptions? options,
		string? system,
		string user,
		string? schema = null)
	{
		_report.AppendLine($"### {label}");
		_report.AppendLine();

		if (system is not null)
		{
			_report.AppendLine("System:");
			_report.AppendLine("```");
			_report.AppendLine(system);
			_report.AppendLine("```");
		}

		_report.AppendLine("User:");
		_report.AppendLine("```");
		_report.AppendLine(user);
		_report.AppendLine("```");

		if (schema is not null)
		{
			_report.AppendLine("Schema:");
			_report.AppendLine("```json");
			_report.AppendLine(schema);
			_report.AppendLine("```");
		}

		var stopwatch = Stopwatch.StartNew();
		string outcome;
		try
		{
			using var client = new PhiSilicaChatClient();
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

			var response = await client.GetResponseAsync(messages, options, cts.Token);
			outcome = $"OK\n```\n{response.Text}\n```";
		}
		catch (Exception ex)
		{
			outcome = $"EXCEPTION `{ex.GetType().Name}`\n```\n{ex.Message}\n```";
		}

		stopwatch.Stop();

		_report.AppendLine($"Result ({stopwatch.ElapsedMilliseconds} ms): {outcome}");
		_report.AppendLine();
	}

	private static JsonElement ParseSchema(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}

	private void WriteReport()
	{
		// The test host unregisters the package when the run finishes, which deletes the app data
		// folder, so the report is also written outside the package sandbox. The app is a full
		// trust packaged app, so it can write to the user profile.
		var candidates = new List<string>
		{
			Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, ReportFileName)
		};

		var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
		if (!string.IsNullOrEmpty(userProfile))
			candidates.Add(Path.Combine(userProfile, ReportFileName));

		var contents = _report.ToString();
		var written = false;

		foreach (var candidate in candidates)
		{
			try
			{
				File.WriteAllText(candidate, contents);
				written = true;
			}
			catch (Exception)
			{
				// Try the next location.
			}
		}

		// Surface the report through the failure message if it could not be persisted anywhere.
		Assert.True(written, "Could not write the probe report:\n" + contents);
	}
}
#endif
