#if WINDOWS
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Third probe round: why schema-constrained generation reports
/// <c>ResponseInvalidJson</c> for the schemas produced by <see cref="AIFunctionFactory"/>.
/// </summary>
/// <remarks>
/// The device tests that still fail use trivial tools such as <c>(string username) =&gt; ...</c>,
/// so the failure is unlikely to be schema complexity. These probes dump the schema
/// <see cref="AIFunctionFactory"/> actually generates and try progressively more sanitised
/// variants of it against the model.
/// </remarks>
[Trait(TestTraits.RequiresModel, TestTraits.True)]
public class PhiSilicaSchemaSanitizationProbeTests
{
	private const string ReportFileName = "PhiSilicaProbeReport3.md";

	private readonly StringBuilder _report = new();

	[Fact(Skip = "Diagnostic probe. Remove the Skip to re-run when the OS model changes.")]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task Probe_FunctionFactorySchemas_WritesReport()
	{
		_report.AppendLine("# Phi Silica schema sanitisation probe");
		_report.AppendLine();
		_report.AppendLine($"Generated: {DateTimeOffset.Now:u}");
		_report.AppendLine();

		try
		{
			var profileTool = AIFunctionFactory.Create(
				(string username) => "{\"userId\": \"U12345\", \"name\": \"John Doe\"}",
				name: "GetUserProfile",
				description: "Looks up a user profile by username. Returns userId and name.");

			await ProbeToolAsync(profileTool, "What are the recent orders for username 'johndoe'?");

			var weatherTool = AIFunctionFactory.Create(
				(string location, string unit) => "Sunny",
				name: "GetWeather",
				description: "Gets the weather for a location.");

			await ProbeToolAsync(weatherTool, "What is the weather in Paris in celsius?");
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

	private async Task ProbeToolAsync(AIFunction tool, string question)
	{
		_report.AppendLine($"## {tool.Name}");
		_report.AppendLine();
		_report.AppendLine("Raw schema from AIFunctionFactory:");
		_report.AppendLine("```json");
		_report.AppendLine(tool.JsonSchema.GetRawText());
		_report.AppendLine("```");
		_report.AppendLine();

		var instructions = $"Work out the arguments for the {tool.Name} tool ({tool.Description}).";

		await RunAsync("raw", tool.JsonSchema, instructions, question);
		await RunAsync("closed", Close(tool.JsonSchema), instructions, question);
		await RunAsync("stripped", Strip(tool.JsonSchema, close: false), instructions, question);
		await RunAsync("stripped+closed", Strip(tool.JsonSchema, close: true), instructions, question);
	}

	/// <summary>Adds <c>additionalProperties: false</c> to every object, as the client does.</summary>
	private static JsonElement Close(JsonElement schema)
	{
		var node = JsonNode.Parse(schema.GetRawText())!;
		CloseNode(node);
		return ToElement(node);
	}

	private static void CloseNode(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				if (obj.TryGetPropertyValue("type", out var type) &&
					type?.GetValueKind() == JsonValueKind.String &&
					type.GetValue<string>() == "object")
				{
					obj["additionalProperties"] = false;
				}

				foreach (var property in obj.ToList())
					CloseNode(property.Value);
				break;

			case JsonArray array:
				foreach (var item in array)
					CloseNode(item);
				break;
		}
	}

	/// <summary>Removes annotation keywords that carry no structural meaning.</summary>
	private static JsonElement Strip(JsonElement schema, bool close)
	{
		var node = JsonNode.Parse(schema.GetRawText())!;
		StripNode(node);

		if (close)
			CloseNode(node);

		return ToElement(node);
	}

	private static void StripNode(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject obj:
				foreach (var keyword in new[] { "$schema", "title", "description", "default" })
					obj.Remove(keyword);

				foreach (var property in obj.ToList())
					StripNode(property.Value);
				break;

			case JsonArray array:
				foreach (var item in array)
					StripNode(item);
				break;
		}
	}

	private async Task RunAsync(string label, JsonElement schema, string instructions, string question)
	{
		_report.AppendLine($"### {label}");
		_report.AppendLine();
		_report.AppendLine("```json");
		_report.AppendLine(schema.GetRawText());
		_report.AppendLine("```");

		var messages = new List<ChatMessage>
		{
			new(ChatRole.System, instructions),
			new(ChatRole.User, question)
		};

		var options = new ChatOptions
		{
			ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, "args", "tool arguments")
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

	private static JsonElement ToElement(JsonNode node)
	{
		using var document = JsonDocument.Parse(node.ToJsonString());
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
