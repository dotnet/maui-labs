#if WINDOWS && WINDOWS_AI_TOOL_EVALUATION
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;
using Microsoft.Windows.AI.Text;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Packaged, on-device observations, not model-accuracy assertions. Run with:
/// dotnet test tests\AI\Microsoft.Maui.Essentials.AI.DeviceTests\Microsoft.Maui.Essentials.AI.DeviceTests.csproj -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0 -p:TestingMode=XHarness -p:EnableWindowsAIToolEvaluation=true -p:DeviceRunnersDataTimeout=600 --filter WindowsAIToolCallingEvaluationTests
/// </summary>
public sealed class WindowsAIToolCallingEvaluationTests
{
	private readonly ITestOutputHelper _output;

	public WindowsAIToolCallingEvaluationTests(ITestOutputHelper output) => _output = output;

	private const int Repeats = 2;
	private const int MaxToolCallsPerTrial = 1;
	private static readonly TimeSpan TrialTimeout = TimeSpan.FromSeconds(45);
	private const string SystemPrompt = "Use the conversation and the available tools. Do not invent order statuses.";
	private const string KnownOrder = "ORD-204";
	private const string KnownStatus = "shipped";

	// The alternate transcript is an experimental prompt encoding, NOT a documented
	// Windows AI role format. Both arms use the documented structured-JSON API through
	// WindowsAIChatClient; no raw Phi/Aion template tokens are assumed.
	private static readonly EvaluationCase[] Cases =
	[
		new("greeting", "hello", ExpectedTool: null, ExpectedArgument: null, ExpectedAnswer: null),
		new("necessary_order", $"Look up the status of order {KnownOrder}.", "lookup_order", KnownOrder, KnownStatus),
		new("ambiguous_order", "Could you help me with my order?", ExpectedTool: null, ExpectedArgument: null,
			ExpectedAnswer: null, HasObjectiveToolExpectation: false),
		new("completed_tool_result", "Based on the tool result above, what is the status of the order? Do not look it up again.",
			ExpectedTool: null, ExpectedArgument: null, ExpectedAnswer: KnownStatus, HasCompletedToolResult: true),
		new("structured_final", $"Look up order {KnownOrder} and answer as JSON with orderId and status.",
			"lookup_order", KnownOrder, KnownStatus, StructuredFinal: true)
	];

	private static readonly JsonElement SelectionSchema = JsonDocument.Parse(
		"""{"type":"object","additionalProperties":false,"properties":{"tool_name":{"type":"string","enum":["lookup_order","get_weather","none"]}},"required":["tool_name"]}""").RootElement.Clone();

	private static readonly JsonElement FinalSchema = JsonDocument.Parse(
		"""{"type":"object","additionalProperties":false,"properties":{"orderId":{"type":"string"},"status":{"type":"string"}},"required":["orderId","status"]}""").RootElement.Clone();

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task StructuredSelectionProbe_CompletesWithinBoundedTime()
	{
		RequireReadyModel();
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
		var native = new WindowsAIChatClient();
		var quarantineNative = false;
		var timer = Stopwatch.StartNew();
		try
		{
			var response = await native.GetResponseAsync(
				[new(ChatRole.System, "Decide whether to call a function. For a greeting, choose none."),
					new(ChatRole.User, "hello")],
				CreatePhaseOptions(SelectionSchema, "tool_selection", "temperature_zero"),
				timeout.Token).WaitAsync(TimeSpan.FromSeconds(70));
			Emit("structured_probe", new { ElapsedMilliseconds = timer.ElapsedMilliseconds, Response = response.Text });
			Assert.False(string.IsNullOrWhiteSpace(response.Text));
		}
		catch (Exception ex)
		{
			if (ex is TimeoutException or OperationCanceledException)
				quarantineNative = true;
			Emit("structured_probe_error", new { ElapsedMilliseconds = timer.ElapsedMilliseconds, Error = ex.ToString() });
			throw;
		}
		finally
		{
			if (!quarantineNative)
				((IDisposable)native).Dispose();
		}
	}

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	public async Task PairedStructuredToolTrials_ReportAllCasesWithoutJudgingModelAccuracy()
	{
		RequireReadyModel();
		var trials = new List<Trial>();
		var native = new WindowsAIChatClient();
		var quarantineNative = false;
		Emit("configuration", new
		{
			Repeats,
			MaxToolCallsPerTrial,
			Cases = Cases.Select(c => c.Id).ToArray(),
			ActualWindowsVersion = Environment.OSVersion.Version.ToString(),
			ReferenceEnvironment = new
			{
				Processor = "AMD Ryzen AI 7 PRO 350",
				WindowsBuild = 26200,
				PhiSilicaComponentUpdate = "KB5124881",
				ComponentVersion = "1.2608.951.0",
				InstalledDate = "2026-09-18",
				Scope = "Creator-verified benchmark host; not automatically validated against the current test machine."
			},
			ActiveModelAndExecutionProviderVersion = "not exposed by LanguageModel; the active model may change (including to Aion)",
			Documentation = "https://learn.microsoft.com/windows/ai/apis/language-model-best-practices",
			Comparison = "The same adapter, tools, instructions and schema-constrained calls on both arms; only the native history encoding changes from the parent's plain labels to a JSON transcript. Persistent context cannot be used with constrained JSON.",
			ModelLifecycle = "One WindowsAIChatClient is reused for all sequential trials and quarantined after an unconfirmed timeout.",
			TrialTimeoutSeconds = TrialTimeout.TotalSeconds,
			SamplingCaveat = "Production adapter pins selection and arguments at T=0/TopP=1/TopK=1. A test-only inner proxy clears those options for provider-default trials in both arms; T=0 determinism is documented only for the same hardware and execution-provider version."
		});

		try
		{
			// Alternate each pair instead of running one arm's entire batch first.
			foreach (var sampling in new[] { "temperature_zero", "provider_defaults" })
			foreach (var testCase in Cases)
			for (var repeat = 0; repeat < Repeats; repeat++)
			{
				var arms = repeat % 2 == 0
					? new[] { "plain_history", "json_history" }
					: new[] { "json_history", "plain_history" };

				for (var order = 0; order < arms.Length; order++)
				{
					var trial = await RunTrialAsync(
						testCase, sampling, repeat, arms[order], order, native);
					trials.Add(trial);
					Emit("trial", trial);
					if (trial.Status == "timed_out")
					{
						quarantineNative = true;
						throw new TimeoutException(
							$"Evaluation stopped after a timed-out trial: " +
							$"{trial.Case}/{trial.Sampling}/{trial.Arm}: {trial.Error}");
					}
				}
			}
		}
		finally
		{
			if (!quarantineNative)
				((IDisposable)native).Dispose();
		}

		var expected = Cases.Length * Repeats * 2 * 2;
		Assert.Equal(expected, trials.Count);
		Assert.Equal(expected, trials.Select(t => (t.Case, t.Sampling, t.Repeat, t.Arm)).Distinct().Count());
		Assert.All(trials, trial =>
		{
			Assert.False(string.IsNullOrWhiteSpace(trial.Case));
			Assert.False(string.IsNullOrWhiteSpace(trial.Arm));
			Assert.True(trial.ElapsedMilliseconds >= 0);
			Assert.True(trial.Requests > 0 || trial.Error is not null);
			Assert.True(trial.NativeRequests > 0 || trial.Error is not null);
			Assert.InRange(trial.PhaseOverrides, 0, trial.PhaseRequests);
			Assert.Equal(0, trial.SamplingInvariantFailures);
			Assert.Equal(trial.Sampling == "provider_defaults" ? trial.PhaseRequests : 0, trial.PhaseOverrides);
			Assert.True(trial.ToolCalls.Count <= MaxToolCallsPerTrial + 1);
			Assert.False(string.IsNullOrWhiteSpace(trial.Status));
		});

		foreach (var group in trials.GroupBy(t => (t.Arm, t.Sampling)))
		{
			Emit("summary", new
			{
				group.Key.Arm,
				group.Key.Sampling,
				Trials = group.Count(),
				Completed = group.Count(t => t.Status == "complete"),
				Errors = group.Count(t => t.Error is not null),
				ContextFailures = group.Count(t => t.ContextFailure),
				SelectedTools = group.Sum(t => t.ToolCalls.Count),
				WrongTools = group.Sum(t => t.WrongTools),
				UnnecessaryTools = group.Sum(t => t.UnnecessaryTools),
				RepeatedTools = group.Sum(t => t.RepeatedTools),
				MissedRequiredTools = group.Count(t =>
					Cases.Single(c => c.Id == t.Case).ExpectedTool is { } expected &&
					t.ToolCalls.All(call => call.Name != expected)),
				CorrectArguments = group.Sum(t => t.CorrectArguments),
				IncorrectArguments = group.Sum(t => t.IncorrectArguments),
				FinalAnswersPresent = group.Count(t => t.FinalAnswerPresent),
				FinalAnswersChecked = group.Count(t => t.FinalAnswerCorrect is not null),
				FinalAnswersCorrect = group.Count(t => t.FinalAnswerCorrect == true),
				NativeRequests = group.Sum(t => t.NativeRequests),
				ConstrainedPhaseRequests = group.Sum(t => t.PhaseRequests),
				AdapterPhaseOverrides = group.Sum(t => t.PhaseOverrides),
				ElapsedMilliseconds = group.Sum(t => t.ElapsedMilliseconds)
			});
			Assert.Equal(Cases.Length * Repeats, group.Count());
		}
	}

	private static async Task<Trial> RunTrialAsync(
		EvaluationCase testCase, string sampling, int repeat, string arm, int order,
		IChatClient native)
	{
		var trial = new Trial
		{
			Case = testCase.Id, Sampling = sampling, Repeat = repeat, Arm = arm, Order = order,
			// The test-only proxy clears the adapter's pinned values for default-sampling trials.
			SelectionSampling = sampling == "temperature_zero" ? "T=0,TopP=1,TopK=1" : "WindowsAI defaults (T=.9,TopP=.9,TopK=40)",
			FinalSampling = sampling == "temperature_zero" ? "T=0,TopP=1,TopK=1" : "WindowsAI defaults (T=.9,TopP=.9,TopK=40)"
		};
		var stopwatch = Stopwatch.StartNew();
		using var timeout = new CancellationTokenSource(TrialTimeout);
		try
		{
			var adapter = new WindowsAIToolCallingClient(
				new SamplingOverrideClient(native, trial, jsonHistory: arm == "json_history"));
			var tools = CreateTools();
			var messages = CreateMessages(testCase);
			var options = CreateOptions(testCase, sampling, tools);

			// One completed tool call maximum, then one follow-up decision. If
			// the model asks for another call, record it but do not execute it.
			for (var turn = 0; turn <= MaxToolCallsPerTrial; turn++)
			{
				trial.Requests++;
				var response = await adapter.GetResponseAsync(messages, options, timeout.Token)
					.WaitAsync(TrialTimeout + TimeSpan.FromSeconds(10));

				var call = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().FirstOrDefault();
				if (call is null)
				{
					trial.FinalAnswer = Shorten(response.Text);
					trial.FinalAnswerPresent = !string.IsNullOrWhiteSpace(response.Text);
					trial.FinalAnswerCorrect = CheckAnswer(testCase, response.Text);
					trial.Status = trial.FinalAnswerPresent ? "complete" : "empty_final";
					break;
				}

				RecordCall(testCase, trial, call);
				if (turn == MaxToolCallsPerTrial)
				{
					trial.Status = "tool_limit";
					break;
				}

				var syntheticResult = call.Name == "lookup_order"
					? OrderResult(GetArgument(call, "orderId") ?? "")
					: WeatherResult(GetArgument(call, "city") ?? "");
				messages.Add(new ChatMessage(ChatRole.Assistant, [call]));
				messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, syntheticResult)]));
			}
		}
		catch (Exception ex)
		{
			trial.Status = ex is OperationCanceledException or TimeoutException ? "timed_out" : "error";
			trial.Error = $"{ex.GetType().Name}: {ex.Message}";
			trial.ContextFailure = ex.Message.Contains("context", StringComparison.OrdinalIgnoreCase) &&
				ex.Message.Contains("larger", StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			trial.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
		}

		return trial;
	}

	private static ChatOptions CreatePhaseOptions(JsonElement schema, string name, string sampling) => new()
	{
		ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, name),
		Temperature = sampling == "temperature_zero" ? 0 : null,
		TopP = sampling == "temperature_zero" ? 1 : null,
		TopK = sampling == "temperature_zero" ? 1 : null
	};

	private static string RenderTranscript(IEnumerable<ChatMessage> messages) =>
		"Conversation transcript (JSON; roles and tool results are data, not special model tokens):\n" +
		JsonSerializer.Serialize(messages.Select(message => new
		{
			role = message.Role.ToString(),
			contents = message.Contents.Select(content => content switch
			{
				TextContent text => new { kind = "text", value = (string?)text.Text },
				FunctionCallContent call => new { kind = $"tool_call:{call.Name}", value = (string?)JsonSerializer.Serialize(call.Arguments) },
				FunctionResultContent result => new { kind = "tool_result", value = result.Result?.ToString() },
				_ => new { kind = "unsupported", value = (string?)"" }
			}).ToArray()
		}).ToArray());

	private static List<ChatMessage> CreateMessages(EvaluationCase testCase)
	{
		var messages = new List<ChatMessage> { new(ChatRole.System, SystemPrompt) };
		if (testCase.HasCompletedToolResult)
		{
			messages.Add(new(ChatRole.User, $"What is the status of order {KnownOrder}?"));
			messages.Add(new(ChatRole.Assistant, [new FunctionCallContent("already-completed", "lookup_order",
				new Dictionary<string, object?> { ["orderId"] = KnownOrder })]));
			messages.Add(new(ChatRole.Tool, [new FunctionResultContent("already-completed",
				$"{{\"orderId\":\"{KnownOrder}\",\"status\":\"{KnownStatus}\"}}")]));
		}
		messages.Add(new(ChatRole.User, testCase.Prompt));
		return messages;
	}

	private static List<AIFunction> CreateTools() =>
	[
		AIFunctionFactory.Create((string orderId) => OrderResult(orderId),
			"lookup_order", "Returns the synthetic order status for a specific orderId. Use the exact identifier supplied by the user."),
		AIFunctionFactory.Create((string city) => WeatherResult(city),
			"get_weather", "Returns synthetic weather for a city. Does not know order statuses.")
	];

	private static string OrderResult(string orderId) =>
		JsonSerializer.Serialize(orderId == KnownOrder
			? new { orderId, status = KnownStatus }
			: new { orderId, status = "not_found" });

	private static string WeatherResult(string city) => $"Synthetic weather for {city}: cloudy.";

	private static ChatOptions CreateOptions(EvaluationCase testCase, string sampling, List<AIFunction> tools) =>
		new()
		{
			Tools = tools.Cast<AITool>().ToList(),
			AllowMultipleToolCalls = testCase.HasCompletedToolResult,
			Temperature = sampling == "temperature_zero" ? 0 : null,
			TopP = sampling == "temperature_zero" ? 1 : null,
			TopK = sampling == "temperature_zero" ? 1 : null,
			ResponseFormat = testCase.StructuredFinal
				? ChatResponseFormat.ForJsonSchema(FinalSchema, "order_status")
				: null
		};

	private static void RecordCall(EvaluationCase testCase, Trial trial, FunctionCallContent call)
	{
		var argument = GetArgument(call, call.Name == "lookup_order" ? "orderId" : "city");
		trial.ToolCalls.Add(new ToolObservation(call.Name, argument));
		if (testCase.HasObjectiveToolExpectation)
		{
			if (testCase.ExpectedTool is null)
				trial.UnnecessaryTools++;
			else if (call.Name != testCase.ExpectedTool)
				trial.WrongTools++;
			else if (string.Equals(argument, testCase.ExpectedArgument, StringComparison.OrdinalIgnoreCase))
				trial.CorrectArguments++;
			else
				trial.IncorrectArguments++;
		}
		if (trial.ToolCalls.Take(trial.ToolCalls.Count - 1).Any(c =>
			c.Name == call.Name && c.Argument == argument))
			trial.RepeatedTools++;
		if (testCase.HasCompletedToolResult && call.Name == "lookup_order" &&
			argument == KnownOrder)
			trial.RepeatedTools++;
	}

	private static string? GetArgument(FunctionCallContent call, string name) =>
		call.Arguments?.TryGetValue(name, out var value) == true
			? value is JsonElement json ? json.ToString() : value?.ToString()
			: null;

	private static bool? CheckAnswer(EvaluationCase testCase, string? answer)
	{
		if (testCase.ExpectedAnswer is null)
			return null;
		if (!testCase.StructuredFinal)
			return answer?.Contains(testCase.ExpectedAnswer, StringComparison.OrdinalIgnoreCase) == true;
		try
		{
			using var document = JsonDocument.Parse(answer ?? "");
			return document.RootElement.GetProperty("orderId").GetString() == KnownOrder &&
				string.Equals(document.RootElement.GetProperty("status").GetString(),
					testCase.ExpectedAnswer, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
		{
			return false;
		}
	}

	private static void RequireReadyModel()
	{
		Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100),
			"Windows AI evaluation requires Windows build 26100 or newer.");
		Assert.Equal(Microsoft.Windows.AI.AIFeatureReadyState.Ready, LanguageModel.GetReadyState());
	}

	private static string? Shorten(string? value) => value is { Length: > 512 } ? value[..512] : value;
	private void Emit(string kind, object value) =>
		_output.WriteLine("TOOL_AB:" + JsonSerializer.Serialize(new { kind, value }));

	private sealed record EvaluationCase(
		string Id, string Prompt, string? ExpectedTool, string? ExpectedArgument, string? ExpectedAnswer,
		bool HasObjectiveToolExpectation = true, bool HasCompletedToolResult = false, bool StructuredFinal = false);

	private sealed record ToolObservation(string Name, string? Argument);

	private sealed class SamplingOverrideClient : DelegatingChatClient
	{
		private readonly Trial _trial;
		private readonly bool _jsonHistory;

		public SamplingOverrideClient(IChatClient inner, Trial trial, bool jsonHistory) : base(inner)
		{
			_trial = trial;
			_jsonHistory = jsonHistory;
		}

		public override Task<ChatResponse> GetResponseAsync(
			IEnumerable<ChatMessage> messages, ChatOptions? options = null,
			CancellationToken cancellationToken = default)
		{
			var request = messages as IReadOnlyList<ChatMessage> ?? [.. messages];
			var first = request.FirstOrDefault();
			var instruction = first?.Role == ChatRole.System ? first.Text : null;
			var isPhase =
				instruction?.StartsWith(
					"Decide what the latest actual user message requests.",
					StringComparison.Ordinal) == true ||
				instruction?.StartsWith(
					"Extract the required ",
					StringComparison.Ordinal) == true ||
				instruction?.StartsWith(
					"Work out the arguments for the ",
					StringComparison.Ordinal) == true;

			_trial.NativeRequests++;
			if (_jsonHistory)
			{
				var system = first?.Role == ChatRole.System ? first : null;
				request = system is null
					? [new(ChatRole.User, RenderTranscript(request))]
					: [new(ChatRole.System, system.Text), new(ChatRole.User, RenderTranscript(request.Skip(1)))];
			}
			if (!isPhase)
				return base.GetResponseAsync(request, options, cancellationToken);

			_trial.PhaseRequests++;
			if (options?.ResponseFormat is not ChatResponseFormatJson)
				throw new InvalidOperationException("Expected a constrained selection or argument request.");
			if (options.Temperature != 0 || options.TopP != 1 || options.TopK != 1)
			{
				_trial.SamplingInvariantFailures++;
				throw new InvalidOperationException("The adapter no longer pins constrained phases to T=0/TopP=1/TopK=1.");
			}
			if (_trial.Sampling != "provider_defaults")
				return base.GetResponseAsync(request, options, cancellationToken);

			var defaults = options.Clone();
			defaults.Temperature = null;
			defaults.TopP = null;
			defaults.TopK = null;
			_trial.PhaseOverrides++;
			return base.GetResponseAsync(request, defaults, cancellationToken);
		}
	}

	private sealed class Trial
	{
		public string Case { get; init; } = "";
		public string Sampling { get; init; } = "";
		public int Repeat { get; init; }
		public string Arm { get; init; } = "";
		public int Order { get; init; }
		public string SelectionSampling { get; init; } = "";
		public string FinalSampling { get; init; } = "";
		// Adapter counts logical turns here; NativeRequests counts actual inner
		// calls for both arms, including the adapter's selection/argument phases.
		public int Requests { get; set; }
		public int NativeRequests { get; set; }
		public int PhaseRequests { get; set; }
		public int PhaseOverrides { get; set; }
		public int SamplingInvariantFailures { get; set; }
		public List<ToolObservation> ToolCalls { get; } = [];
		public int WrongTools { get; set; }
		public int UnnecessaryTools { get; set; }
		public int RepeatedTools { get; set; }
		public int CorrectArguments { get; set; }
		public int IncorrectArguments { get; set; }
		public string? FinalAnswer { get; set; }
		public bool FinalAnswerPresent { get; set; }
		public bool? FinalAnswerCorrect { get; set; }
		public string Status { get; set; } = "";
		public string? Error { get; set; }
		public bool ContextFailure { get; set; }
		public long ElapsedMilliseconds { get; set; }
	}

}
#endif
