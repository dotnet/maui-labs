#if WINDOWS
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Fourth probe round: how the selection prompt should be worded so the model keeps going when a
/// request needs more than one tool.
/// </summary>
/// <remarks>
/// Rounds one to three established the two-phase design. The one weak spot left is chaining across
/// different tools: given "the weather and the time", the model calls the weather tool, sees a
/// result, and stops. The wording used for the selection phase looks like the cause, since it
/// invites the model to stop as soon as any result is present. These probes compare candidate
/// phrasings on exactly that follow-up turn.
/// </remarks>
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class PhiSilicaFollowUpPromptProbeTests
{
	private const string ReportFileName = "PhiSilicaProbeReport4.md";

	/// <summary>The tools offered in every probe.</summary>
	private const string ToolList =
		"- get_weather: Gets the current weather for a city.\n" +
		"- get_time: Gets the current time for a city.\n";

	/// <summary>A first turn that has already called the weather tool, leaving the time outstanding.</summary>
	private const string FollowUpConversation =
		"What is the weather and the time in Paris?\n" +
		"[Tool call: get_weather({\"city\":\"Paris\"})]\n" +
		"[Tool result: Sunny, 22C in Paris]";

	/// <summary>A first turn whose single question is already fully answered.</summary>
	private const string SatisfiedConversation =
		"What is the weather in Paris?\n" +
		"[Tool call: get_weather({\"city\":\"Paris\"})]\n" +
		"[Tool result: Sunny, 22C in Paris]";

	/// <summary>A first turn that has already fetched one city, leaving a second city outstanding.</summary>
	private const string SecondCityConversation =
		"What is the weather in Paris and in Tokyo?\n" +
		"[Tool call: get_weather({\"city\":\"Paris\"})]\n" +
		"[Tool result: Sunny, 22C in Paris]";

	private readonly StringBuilder _report = new();

	[Fact(Skip = "Diagnostic probe. Remove the Skip to re-run when the OS model changes.")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Probe_SelectionPromptWording_WritesReport()
	{
		_report.AppendLine("# Phi Silica follow-up selection wording probe");
		_report.AppendLine();
		_report.AppendLine($"Generated: {DateTimeOffset.Now:u}");
		_report.AppendLine();
		_report.AppendLine("Each wording is asked twice: once where a second tool is still needed");
		_report.AppendLine("(want `get_time`), and once where the request is already satisfied (want `none`).");
		_report.AppendLine("A good wording gets both right, not just the first.");
		_report.AppendLine();

		try
		{
			// The wording in use today, as the baseline to beat.
			await CompareAsync(
				"baseline",
				"Decide which tool is needed to answer the user's request.\n\n" + ToolList +
				"\nChoose none if no tool is needed, or if the tool results above already answer the request.");

			// Makes stopping the exception rather than the default.
			await CompareAsync(
				"every-part",
				"Decide which tool to call next.\n\n" + ToolList +
				"\nThe user's request may need more than one tool. Check every part of it.\n" +
				"If any part has not been answered yet, choose the tool that answers that part.\n" +
				"Choose none only when every part of the request has been answered.");

			// Frames the turn as an explicit outstanding-work check.
			await CompareAsync(
				"whats-left",
				"Some tools have already run and their results are shown above.\n\n" +
				"Available tools:\n" + ToolList +
				"\nWork out what the user asked for that is still missing, and choose the tool that provides it.\n" +
				"Choose none if nothing is missing.");

			// Names the completed calls, so "already done" does not have to be inferred.
			await CompareAsync(
				"names-completed",
				"Decide which tool to call next.\n\n" +
				"Available tools:\n" + ToolList +
				"\nAlready called: get_weather.\n" +
				"Do not repeat a call that has already been made. If the user asked for something that the\n" +
				"completed calls do not cover, choose the tool that covers it. Otherwise choose none.");

			// Tests whether an explicit worked example helps or over-fits.
			await CompareAsync(
				"with-example",
				"Decide which tool to call next.\n\n" + ToolList +
				"\nA request such as \"the weather and the time\" needs two tools, so after the weather tool\n" +
				"has run the time tool is still needed.\n" +
				"Choose none only when every part of the request has been answered.");
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

	/// <summary>Runs one wording against both the unfinished and the finished conversation.</summary>
	private async Task CompareAsync(string label, string instructions)
	{
		_report.AppendLine($"## {label}");
		_report.AppendLine();
		_report.AppendLine("Instructions:");
		_report.AppendLine("```");
		_report.AppendLine(instructions);
		_report.AppendLine("```");
		_report.AppendLine();

		var continues = await SelectAsync(instructions, FollowUpConversation);
		var stops = await SelectAsync(instructions, SatisfiedConversation);
		var secondCity = await SelectAsync(instructions, SecondCityConversation);

		_report.AppendLine($"| Case | Want | Got | |");
		_report.AppendLine($"|---|---|---|---|");
		_report.AppendLine($"| second tool still needed | `get_time` | `{continues}` | {Mark(continues == "get_time")} |");
		_report.AppendLine($"| request already answered | `none` | `{stops}` | {Mark(stops == "none")} |");
		_report.AppendLine($"| same tool, second subject | `get_weather` | `{secondCity}` | {Mark(secondCity == "get_weather")} |");
		_report.AppendLine();
	}

	private static string Mark(bool ok) => ok ? "pass" : "FAIL";

	/// <summary>Runs the selection phase exactly as the client does, and returns the chosen name.</summary>
	private async Task<string> SelectAsync(string instructions, string conversation)
	{
		var schema = ParseSchema("""
			{"type":"object","additionalProperties":false,"properties":{"tool_name":{"type":"string","enum":["get_weather","get_time","none"]}},"required":["tool_name"]}
			""");

		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, instructions),
			new(ChatRole.User, conversation)
		};

		// Match the client: deterministic sampling for a classification question.
		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "tool_selection"),
			Temperature = 0f,
			TopP = 1f,
			TopK = 1
		};

		var stopwatch = Stopwatch.StartNew();
		try
		{
			using var client = new PhiSilicaChatClient();
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

			var response = await client.GetResponseAsync(messages, options, cts.Token);

			using var document = JsonDocument.Parse(response.Text);

			return document.RootElement.TryGetProperty("tool_name", out var name)
				? name.GetString() ?? "(null)"
				: "(missing)";
		}
		catch (Exception ex)
		{
			return $"({ex.GetType().Name})";
		}
		finally
		{
			stopwatch.Stop();
		}
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
