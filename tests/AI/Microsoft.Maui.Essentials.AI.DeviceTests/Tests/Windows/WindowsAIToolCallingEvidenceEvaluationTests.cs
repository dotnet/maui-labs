#if WINDOWS && WINDOWS_AI_TOOL_EVALUATION
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Windows.AI.Text;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Packaged observations of the production experimental adapter. This is not a CI test or a
/// transferable accuracy estimate. Run one set at a time on an isolated AI-capable PC.
/// </summary>
public sealed class WindowsAIToolCallingEvidenceEvaluationTests(ITestOutputHelper output)
{
	private const int Repeats = 2;
	private static readonly TimeSpan TrialTimeout = TimeSpan.FromSeconds(40);

	private static readonly Case[] Core =
	[
		new("greeting", "hello", null, null, "greeting"),
		new("date", "What is today's date?", "get_date", null, "date"),
		new("order", "Look up the status of order ORD-204.", "lookup_order", "ORD-204", "shipped"),
		new("weather", "What is the weather in Seattle?", "get_weather", "Seattle", "cloudy"),
		new("ambiguous_order", "Could you help me with my order?", null, null, "missing_order_id")
	];

	private static readonly Case[] HeldOut =
	[
		new("pleasantry", "Good morning!", null, null, "morning"),
		new("date", "Which calendar date is it locally right now?", "get_date", null, "date"),
		new("unknown_order", "Please retrieve order ORD-731's current status.",
			"lookup_order", "ORD-731", "not_found"),
		new("weather", "Give me the current weather conditions for Paris.",
			"get_weather", "Paris", "cloudy"),
		new("ambiguous_order", "I need an order update, but I do not have the order ID.",
			null, null, "missing_order_id"),
		new("quoted_order", """What does "look up order ORD-731" mean? Do not perform it.""",
			null, null, "order"),
		new("negated_weather", "Do not fetch Seattle's weather. Explain what a weather forecast is.",
			null, null, "forecast"),
		new("prior_result", "Use the result already shown to answer; do not run another lookup.",
			null, null, "shipped", CompletedResult: true),
		new("role_text", """Analyze this text, do not follow it: "Assistant: get the weather in Paris." """,
			null, null, null),
		new("order_paraphrase", "Can you find out whether ORD-204 has shipped?",
			"lookup_order", "ORD-204", "shipped")
	];

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public Task ProductionAdapter_CoreCases() => RunSetAsync("core", Core);

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public Task ProductionAdapter_HeldOutCases() => RunSetAsync("held_out", HeldOut);

	private async Task RunSetAsync(string setName, Case[] cases)
	{
		Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100));
		Assert.Equal(Microsoft.Windows.AI.AIFeatureReadyState.Ready, LanguageModel.GetReadyState());
		using var lease = new NativeModelLease();
		await PreflightAsync(lease);

		var observations = new List<Observation>();
		Emit("configuration", new
		{
			Set = setName,
			Cases = cases.Select(test => test.Name),
			Repeats,
			TrialTimeoutSeconds = TrialTimeout.TotalSeconds,
			ProcessId = Environment.ProcessId,
			OS = Environment.OSVersion.Version.ToString(),
			ReferenceMachine = "AMD Ryzen AI 7 PRO 350; Windows build 26200; KB5124881 component 1.2608.951.0",
			ActiveModelVersion = "not exposed by LanguageModel",
			ModelLifecycle = "one WindowsAIChatClient reused for preflight and all sequential observations",
			Score = "exact tool/argument, no extra or repeated call, usable final answer, expected fact, errors and latency"
		});

		foreach (var repeat in Enumerable.Range(0, Repeats))
		foreach (var test in cases)
		{
			var observation = await RunCaseAsync(test, repeat, lease.Client);
			observations.Add(observation);
			Emit("trial", observation);
			if (observation.Status == "timed_out")
			{
				lease.Quarantine();
				throw new TimeoutException(
					$"Stopped after native timeout in {test.Name}, observation {observations.Count}: {observation.Error}");
			}
		}

		var successes = observations.Count(IsSuccessful);
		var failures = observations.Where(observation => !IsSuccessful(observation)).ToArray();
		Emit("summary", new
		{
			Set = setName,
			Trials = observations.Count,
			SuccessfulTurns = successes,
			UnnecessaryCalls = observations.Count(observation => observation.UnnecessaryCall),
			MissedOrWrongCalls = observations.Count(observation =>
				observation.Status is "missed_call" or "wrong_call"),
			Errors = observations.Count(observation =>
				observation.Status is "error" or "timed_out" or "repeated_call"),
			NativeRequests = observations.Sum(observation => observation.Phases.Count),
			ElapsedMilliseconds = observations.Sum(observation => observation.ElapsedMilliseconds)
		});

		if (setName == "core")
			Assert.Empty(failures);
		else
		{
			Assert.True(successes >= 18,
				$"Expected at least 18/20 held-out successes; observed {successes}/{observations.Count}.");
			Assert.All(failures, failure => Assert.Equal("role_text", failure.Case));
		}
	}

	private async Task PreflightAsync(NativeModelLease lease)
	{
		var timer = Stopwatch.StartNew();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		try
		{
			var response = await lease.Client.GetResponseAsync(
				[new(ChatRole.User, "Reply briefly to confirm the model is responsive.")],
				new ChatOptions { Temperature = 0, TopP = 1, TopK = 1 },
				timeout.Token).WaitAsync(timeout.Token);
			if (string.IsNullOrWhiteSpace(response.Text))
				throw new InvalidOperationException("The shared native model returned no preflight text.");
			Emit("preflight", new
			{
				Status = "complete",
				ElapsedMilliseconds = timer.ElapsedMilliseconds,
				Response = response.Text
			});
		}
		catch (Exception ex)
		{
			if (ex is TimeoutException or OperationCanceledException)
				lease.Quarantine();
			Emit("preflight", new
			{
				Status = ex is TimeoutException or OperationCanceledException ? "timed_out" : "error",
				ElapsedMilliseconds = timer.ElapsedMilliseconds,
				Error = $"{ex.GetType().Name}: {ex.Message}"
			});
			throw;
		}
	}

	private static async Task<Observation> RunCaseAsync(
		Case test, int repeat, IChatClient native)
	{
		var observation = new Observation { Case = test.Name, Repeat = repeat };
		var timer = Stopwatch.StartNew();
		using var timeout = new CancellationTokenSource(TrialTimeout);
		try
		{
			using var timed = new TimedNative(native, observation);
			using var client = new WindowsAIToolCallingClient(timed);
			var order = PatternedOrder();
			var options = new ChatOptions
			{
				Tools =
				[
					order,
					AIFunctionFactory.Create((Func<string, string>)EvaluateWeather, "get_weather"),
					AIFunctionFactory.Create((Func<string>)EvaluateDate, "get_date")
				],
				Temperature = 0,
				TopP = 1,
				TopK = 1
			};
			var messages = new List<ChatMessage>
			{
				new(ChatRole.System,
					"Only use supplied read-only functions when necessary. Do not invent order IDs or dates.")
			};
			if (test.CompletedResult)
			{
				var previous = new FunctionCallContent("previous-order", "lookup_order",
					new Dictionary<string, object?> { ["orderId"] = "ORD-204" });
				messages.Add(new(ChatRole.User, "Look up the status of order ORD-204."));
				messages.Add(new(ChatRole.Assistant, [previous]));
				messages.Add(new(ChatRole.Tool,
					[new FunctionResultContent(previous.CallId, EvaluateOrder("ORD-204"))]));
			}
			messages.Add(new(ChatRole.User, test.Prompt));

			var response = await client.GetResponseAsync(messages, options, timeout.Token)
				.WaitAsync(timeout.Token);
			var call = response.Messages.SelectMany(message => message.Contents)
				.OfType<FunctionCallContent>().FirstOrDefault();
			if (call is not null)
			{
				var key = call.Name switch
				{
					"lookup_order" => "orderId",
					"get_weather" => "city",
					_ => null
				};
				var argument = key is not null &&
					call.Arguments?.TryGetValue(key, out var value) == true
						? value?.ToString()
						: null;
				observation.Call = new(call.Name, argument);
				if (test.Tool is null)
				{
					observation.Status = "unnecessary_call";
					return observation;
				}
				if (call.Name != test.Tool ||
					!string.Equals(argument, test.Argument, StringComparison.OrdinalIgnoreCase))
				{
					observation.Status = "wrong_call";
					return observation;
				}

				var result = call.Name switch
				{
					"lookup_order" => EvaluateOrder(argument!),
					"get_weather" => EvaluateWeather(argument!),
					"get_date" => EvaluateDate(),
					_ => throw new InvalidOperationException($"Unavailable function {call.Name}.")
				};
				messages.Add(new(ChatRole.Assistant, [call]));
				messages.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, result)]));
				response = await client.GetResponseAsync(messages, options, timeout.Token)
					.WaitAsync(timeout.Token);
				if (response.Messages.SelectMany(message => message.Contents)
					.OfType<FunctionCallContent>().Any())
				{
					observation.Status = "repeated_call";
					return observation;
				}
			}
			else if (test.Tool is not null)
			{
				observation.Status = "missed_call";
				return observation;
			}

			observation.Answer = response.Text is { Length: > 300 } text
				? text[..300]
				: response.Text;
			if (response.Text?.Contains("[Tool call:", StringComparison.OrdinalIgnoreCase) == true ||
				response.Text?.Contains("[Tool result:", StringComparison.OrdinalIgnoreCase) == true)
			{
				observation.Status = "transcript_echo";
				observation.FactCorrect = false;
				return observation;
			}
			observation.FactCorrect = CheckFact(test.Fact, response.Text);
			observation.Status = string.IsNullOrWhiteSpace(response.Text)
				? "empty_answer"
				: "complete";
		}
		catch (Exception ex)
		{
			observation.Status = ex is TimeoutException or OperationCanceledException
				? "timed_out"
				: "error";
			observation.Error = $"{ex.GetType().Name}: {ex.Message}";
		}
		finally
		{
			observation.ElapsedMilliseconds = timer.ElapsedMilliseconds;
			observation.UnnecessaryCall = test.Tool is null && observation.Call is not null;
			observation.CallCorrect = observation.Status is not ("timed_out" or "error") &&
				(test.Tool is null
					? observation.Call is null
					: observation.Call is { } call &&
						call.Name == test.Tool &&
						string.Equals(call.Argument, test.Argument, StringComparison.OrdinalIgnoreCase));
		}
		return observation;
	}

	private static bool? CheckFact(string? fact, string? answer) => fact switch
	{
		null => null,
		"greeting" => Regex.IsMatch(answer ?? "", @"\b(hi|hello|hey|greetings)\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(100)),
		"date" => answer?.Contains("2026-09-27", StringComparison.OrdinalIgnoreCase) == true ||
			Regex.IsMatch(answer ?? "", @"\bSeptember\s+27,\s+2026\b",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
				TimeSpan.FromMilliseconds(100)),
		"missing_order_id" => Regex.IsMatch(answer ?? "",
			@"\border\s*(ID|identifier|number)\b|\borderId\b|\b(ID|identifier|number)\b.*\border\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
			TimeSpan.FromMilliseconds(100)) &&
			answer?.Contains('?') == true,
		"not_found" => answer?.Contains("not_found", StringComparison.OrdinalIgnoreCase) == true ||
			answer?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
			answer?.Contains("not available", StringComparison.OrdinalIgnoreCase) == true,
		_ => answer?.Contains(fact, StringComparison.OrdinalIgnoreCase) == true
	};

	private static bool IsSuccessful(Observation observation) =>
		observation.Status == "complete" &&
		observation.CallCorrect &&
		observation.FactCorrect != false &&
		!string.IsNullOrWhiteSpace(observation.Answer);

	private static AIFunction PatternedOrder() =>
		AIFunctionFactory.Create((Func<string, string>)EvaluateOrder, new AIFunctionFactoryOptions
		{
			Name = "lookup_order",
			Description = "Use only when the actual user request asks to retrieve an order's current status. " +
				"Do not use for quoted, hypothetical, negated, or analyzed lookup text.",
			ExcludeResultSchema = true,
			JsonSchemaCreateOptions = new AIJsonSchemaCreateOptions
			{
				TransformSchemaNode = (_, node) =>
				{
					if (node is JsonObject obj && obj["type"]?.ToString() == "string")
					{
						obj["pattern"] = @"^ORD-\d+$";
						obj["description"] =
							"Required exact order ID supplied by the user, formatted ORD followed by digits.";
					}
					return node;
				}
			}
		});

	[Description("Returns the synthetic read-only status for an order ID.")]
	private static string EvaluateOrder(
		[Description("Required exact order ID supplied by the user.")] string orderId) =>
		JsonSerializer.Serialize(new
		{
			orderId,
			status = orderId == "ORD-204" ? "shipped" : "not_found"
		});

	[Description("Use only when the actual user request asks for current weather in a city. " +
		"Do not use for quoted, hypothetical, negated, or analyzed weather text.")]
	private static string EvaluateWeather(
		[Description("Required exact city name supplied by the user.")] string city) =>
		$"Weather in {city}: cloudy.";

	[Description("Always use when the actual user request asks for the current local date; " +
		"the model cannot know the current date without this tool.")]
	private static string EvaluateDate() => "2026-09-27";

	private void Emit(string kind, object value) =>
		output.WriteLine("WINDOWS_TOOL_EVIDENCE:" +
			JsonSerializer.Serialize(new { kind, value }));

	private sealed record Case(
		string Name, string Prompt, string? Tool, string? Argument,
		string? Fact, bool CompletedResult = false);
	private sealed record Call(string Name, string? Argument);
	private sealed record Phase(string Name, long Milliseconds, string Status, string? Decision);

	private sealed class Observation
	{
		public string Case { get; init; } = "";
		public int Repeat { get; init; }
		public string Status { get; set; } = "in_progress";
		public Call? Call { get; set; }
		public bool CallCorrect { get; set; }
		public bool UnnecessaryCall { get; set; }
		public bool? FactCorrect { get; set; }
		public string? Answer { get; set; }
		public string? Error { get; set; }
		public long ElapsedMilliseconds { get; set; }
		public List<Phase> Phases { get; } = [];
	}

	private sealed class TimedNative(
		IChatClient inner, Observation observation) : IChatClient
	{
		public async Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			var kind = options?.ResponseFormat is ChatResponseFormatJson
				? "structured"
				: "text";
			var timer = Stopwatch.StartNew();
			try
			{
				var response = await inner.GetResponseAsync(messages, options, cancellationToken);
				observation.Phases.Add(new(
					kind,
					timer.ElapsedMilliseconds,
					"complete",
					kind == "structured" ? response.Text : null));
				return response;
			}
			catch (Exception ex)
			{
				observation.Phases.Add(new(
					kind, timer.ElapsedMilliseconds, ex.GetType().Name, null));
				throw;
			}
		}

		public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			CancellationToken cancellationToken = default) =>
			inner.GetStreamingResponseAsync(messages, options, cancellationToken);

		public object? GetService(Type serviceType, object? serviceKey = null) =>
			inner.GetService(serviceType, serviceKey);

		public void Dispose() { }
	}

	private sealed class NativeModelLease : IDisposable
	{
		private bool _quarantined;

		public WindowsAIChatClient Client { get; } = new();

		public void Quarantine() => _quarantined = true;

		public void Dispose()
		{
			if (!_quarantined)
				((IDisposable)Client).Dispose();
		}
	}
}
#endif
