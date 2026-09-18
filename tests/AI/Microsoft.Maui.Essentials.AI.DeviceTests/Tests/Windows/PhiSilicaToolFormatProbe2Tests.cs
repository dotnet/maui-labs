#if WINDOWS
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Second probe round: validates the fixes suggested by <see cref="PhiSilicaToolFormatProbeTests"/>
/// and explores multi-step tool calling.
/// </summary>
/// <remarks>
/// Round one showed the model reliably selects the right tool and fills arguments correctly, but
/// invents property names for the free-text branch (it produced <c>body</c>, <c>response</c> and
/// <c>message</c> instead of the declared <c>text</c>). The declared schema did not set
/// <c>additionalProperties: false</c>, so constrained decoding permitted the invented keys. These
/// probes check whether closing the schema fixes that, and how far multi-step calling can be pushed.
/// </remarks>
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class PhiSilicaToolFormatProbe2Tests
{
	private const string ReportFileName = "PhiSilicaProbeReport2.md";

	private readonly StringBuilder _report = new();

	[Fact(Skip = "Diagnostic probe. Remove the Skip to re-run when the OS model changes.")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Probe_ClosedSchemasAndChaining_WritesReport()
	{
		_report.AppendLine("# Phi Silica tool-calling probe, round 2");
		_report.AppendLine();
		_report.AppendLine($"Generated: {DateTimeOffset.Now:u}");
		_report.AppendLine();

		try
		{
			await ProbeClosedSchemaAsync();
			await ProbeRequiredTextAsync();
			await ProbeTwoPhaseAsync();
			await ProbeMultiStepChainAsync();
			await ProbeParallelCallsAsync();
			await ProbePerToolArgumentSchemaAsync();
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

	/// <summary>Does <c>additionalProperties: false</c> stop the model inventing property names?</summary>
	private async Task ProbeClosedSchemaAsync()
	{
		Section("1. Closed schema (additionalProperties: false)");

		var closed = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_weather","get_time"]},"arguments":{"type":"object"},"text":{"type":"string"}},"required":["type"]}
			""");

		const string system =
			"You are a helpful assistant with access to tools.\n" +
			"Available tools:\n" +
			"- get_weather: Gets the current weather. Parameters: city (string)\n" +
			"- get_time: Gets the current time. Parameters: city (string)\n" +
			"If a tool is needed set type to tool_call. If you can answer, set type to text and put the answer in the text property.";

		// Round one produced "body" here instead of "text".
		await RecordStructuredAsync("closed-text-branch", closed, system, "Write me a haiku about rain.");

		// Round one produced "response" here instead of "text".
		await RecordStructuredAsync("closed-followup-answer", closed, system,
			"User: What is the weather in Paris?\n" +
			"Assistant: [Tool call: get_weather({\"city\":\"Paris\"})]\n" +
			"[Tool result: Sunny, 22C in Paris]\n" +
			"Now answer the user.");

		// Confirm the tool branch still works with the schema closed.
		await RecordStructuredAsync("closed-tool-branch", closed, system, "What is the weather in Cape Town?");
	}

	/// <summary>Does an enum-only closed schema round-trip reliably?</summary>
	private async Task ProbeRequiredTextAsync()
	{
		Section("2. Enum value extraction (closed schema)");

		var open = ParseSchema("""
			{"type":"object","properties":{"fruit":{"type":"string","enum":["Apple","Banana","Cherry"]}},"required":["fruit"]}
			""");

		var closed = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"fruit":{"type":"string","enum":["Apple","Banana","Cherry"]}},"required":["fruit"]}
			""");

		await RecordStructuredAsync("enum-open", open,
			system: "Pick the fruit the user is describing.",
			user: "Which fruit is long, yellow and peels?");

		await RecordStructuredAsync("enum-closed", closed,
			system: "Pick the fruit the user is describing.",
			user: "Which fruit is long, yellow and peels?");
	}

	/// <summary>
	/// Two-phase calling: pick the tool with one constrained call, then fill that tool's real
	/// parameter schema with a second call.
	/// </summary>
	private async Task ProbeTwoPhaseAsync()
	{
		Section("3. Two-phase selection then arguments");

		var selection = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"tool_name":{"type":"string","enum":["get_weather","get_time","get_stock_price","none"]}},"required":["tool_name"]}
			""");

		const string system =
			"Decide which tool answers the user's question.\n" +
			"- get_weather: current weather for a city\n" +
			"- get_time: current time for a city\n" +
			"- get_stock_price: price for a stock symbol\n" +
			"Choose none if no tool is needed.";

		await RecordStructuredAsync("phase1-weather", selection, system, "What is the weather in Paris?");
		await RecordStructuredAsync("phase1-none", selection, system, "Write me a haiku about rain.");

		// Phase two uses the tool's own parameter schema verbatim.
		var weatherArgs = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"city":{"type":"string"}},"required":["city"]}
			""");

		await RecordStructuredAsync("phase2-weather-args", weatherArgs,
			system: "Extract the arguments for get_weather (gets the current weather for a city).",
			user: "What is the weather in Paris?");
	}

	/// <summary>Can the model chain three dependent calls when fed results one at a time?</summary>
	private async Task ProbeMultiStepChainAsync()
	{
		Section("4. Multi-step chaining");

		var schema = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"type":{"type":"string","enum":["tool_call","text"]},"tool_name":{"type":"string","enum":["get_user_profile","get_orders","get_order_details"]},"arguments":{"type":"object"},"text":{"type":"string"}},"required":["type"]}
			""");

		const string system =
			"You are a helpful assistant with access to tools. Call ONE tool at a time.\n" +
			"Available tools:\n" +
			"- get_user_profile: Gets the user profile. Parameters: none\n" +
			"- get_orders: Lists orders for a user. Parameters: user_id (string)\n" +
			"- get_order_details: Gets one order. Parameters: order_id (string)\n" +
			"If you still need data set type to tool_call. When you can answer set type to text.";

		await RecordStructuredAsync("chain-step1", schema, system,
			"How much did I spend on my most recent order?");

		await RecordStructuredAsync("chain-step2", schema, system,
			"How much did I spend on my most recent order?\n" +
			"[Tool call: get_user_profile({})]\n" +
			"[Tool result: {\"user_id\":\"u-42\",\"name\":\"Sam\"}]\n" +
			"Continue.");

		await RecordStructuredAsync("chain-step3", schema, system,
			"How much did I spend on my most recent order?\n" +
			"[Tool call: get_user_profile({})]\n" +
			"[Tool result: {\"user_id\":\"u-42\",\"name\":\"Sam\"}]\n" +
			"[Tool call: get_orders({\"user_id\":\"u-42\"})]\n" +
			"[Tool result: [{\"order_id\":\"o-9\",\"date\":\"2026-07-01\"}]]\n" +
			"Continue.");

		await RecordStructuredAsync("chain-step4-answer", schema, system,
			"How much did I spend on my most recent order?\n" +
			"[Tool call: get_user_profile({})]\n" +
			"[Tool result: {\"user_id\":\"u-42\",\"name\":\"Sam\"}]\n" +
			"[Tool call: get_orders({\"user_id\":\"u-42\"})]\n" +
			"[Tool result: [{\"order_id\":\"o-9\",\"date\":\"2026-07-01\"}]]\n" +
			"[Tool call: get_order_details({\"order_id\":\"o-9\"})]\n" +
			"[Tool result: {\"order_id\":\"o-9\",\"total\":\"$42.50\"}]\n" +
			"Continue.");
	}

	/// <summary>Can the model emit several calls at once as an array?</summary>
	private async Task ProbeParallelCallsAsync()
	{
		Section("5. Parallel calls in one response");

		var schema = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"tool_calls":{"type":"array","items":{"type":"object","additionalProperties":false,"properties":{"tool_name":{"type":"string","enum":["get_weather","get_time"]},"arguments":{"type":"object"}},"required":["tool_name","arguments"]}}},"required":["tool_calls"]}
			""");

		const string system =
			"Emit every tool call needed to answer the question.\n" +
			"- get_weather: current weather. Parameters: city (string)\n" +
			"- get_time: current time. Parameters: city (string)";

		await RecordStructuredAsync("parallel-two-cities", schema, system,
			"What is the weather in Paris and in Tokyo?");

		await RecordStructuredAsync("parallel-two-tools", schema, system,
			"What is the weather and the time in Paris?");
	}

	/// <summary>Does giving each tool its real parameter schema improve argument quality?</summary>
	private async Task ProbePerToolArgumentSchemaAsync()
	{
		Section("6. Per-tool argument schemas via anyOf");

		var anyOf = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"tool_name":{"type":"string","enum":["search_landmarks"]},"arguments":{"type":"object","additionalProperties":false,"properties":{"continent":{"type":"string","enum":["Africa","Asia","Europe","NorthAmerica","SouthAmerica","Oceania","Antarctica"]},"pointOfInterest":{"type":"string"}},"required":["continent","pointOfInterest"]}},"required":["tool_name","arguments"]}
			""");

		// Mirrors the shape of the failing device test, which omitted a required argument.
		await RecordStructuredAsync("required-args-enforced", anyOf,
			system: "Available tool: search_landmarks(continent, pointOfInterest). " +
				"continent must be one of the listed values. pointOfInterest describes what to look for.",
			user: "What are the landmarks in Africa?");
	}

	// ═══════════════════════════════════════════════════════════
	// HARNESS
	// ═══════════════════════════════════════════════════════════

	private void Section(string title)
	{
		_report.AppendLine($"## {title}");
		_report.AppendLine();
	}

	private async Task RecordStructuredAsync(string label, JsonElement schema, string system, string user)
	{
		_report.AppendLine($"### {label}");
		_report.AppendLine();
		_report.AppendLine("System:");
		_report.AppendLine("```");
		_report.AppendLine(system);
		_report.AppendLine("```");
		_report.AppendLine("User:");
		_report.AppendLine("```");
		_report.AppendLine(user);
		_report.AppendLine("```");
		_report.AppendLine("Schema:");
		_report.AppendLine("```json");
		_report.AppendLine(schema.GetRawText());
		_report.AppendLine("```");

		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, system),
			new(ChatRole.User, user)
		};

		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "probe", "probe schema")
		};

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

		Assert.True(written, "Could not write the probe report:\n" + contents);
	}
}
#endif
