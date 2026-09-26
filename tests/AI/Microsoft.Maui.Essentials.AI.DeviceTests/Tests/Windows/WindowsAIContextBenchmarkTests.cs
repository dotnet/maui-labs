#if WINDOWS && WINDOWS_AI_CONTEXT_BENCHMARK
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Storage;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Text;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Opt-in packaged benchmark; allow a longer DeviceRunners data timeout than the default 30 seconds.
/// </summary>
public sealed class WindowsAIContextBenchmarkTests(ITestOutputHelper output)
{
	private const string ShortSystemPrompt = "You are a concise assistant. Answer with only the requested word or code.";
	private const string BriefSystemPrompt =
		"You are a planning assistant. Include the project codename in every reply. " +
		"Answer in one brief sentence of no more than 20 words.";
	private const string ParagraphSystemPrompt =
		"You are a planning assistant. Include the project codename in every reply. " +
		"Answer in one coherent paragraph of 45 to 65 words.";
	private const int MaxIntakeNotes = 120;
	private static readonly string ReservedReply = string.Join(' ', Enumerable.Repeat(
		"MARIGOLD schedules supervised practice at accessible stations, rotates participants between tasks, " +
		"keeps the indoor weather plan ready, and checks available equipment before each session.", 4));

	private static readonly (string Prompt, string Expected)[] ShortTurns =
	[
		("Hi. Remember my first project code is COBALT. Reply HELLO.", "HELLO"),
		("Thanks. What was my first project code? Reply with only the code.", "COBALT"),
		("Remember my second project code is SAFFRON. Reply READY.", "READY"),
		("What was my first project code? Reply with only the code.", "COBALT"),
		("What was my second project code? Reply with only the code.", "SAFFRON"),
		("Remember my third project code is ORCHID. Reply READY.", "READY"),
		("What was my first project code? Reply with only the code.", "COBALT"),
		("What was my second project code? Reply with only the code.", "SAFFRON"),
		("What was my third project code? Reply with only the code.", "ORCHID"),
		("What was my second project code? Reply with only the code.", "SAFFRON"),
		("What was my first project code? Reply with only the code.", "COBALT"),
		("What was my third project code? Reply with only the code.", "ORCHID"),
	];

	private static readonly (string Prompt, string Expected)[] SubstantialTurns =
	[
		(
			"Our fictional community workshop, codenamed MARIGOLD, has twelve volunteer instructors and a room " +
			"that holds at most twenty-four participants. We have four Saturday sessions to teach basic bicycle " +
			"repairs. Each session lasts ninety minutes, and we want beginners to leave knowing how to check brakes, " +
			"inflate tires safely, and diagnose a slipping chain. Two instructors can lead demonstrations while " +
			"the others supervise small practice groups. Supplies arrive only before the first session; there is " +
			"no time to buy replacements during the series. Propose an initial teaching plan that respects the " +
			"room limit and identifies what should be practiced first.",
			"MARIGOLD"
		),
		(
			"An update from the venue: the practice space is accessible only by a narrow ramp, and all " +
			"participants must be able to reach each station without carrying a bicycle up stairs. Two of " +
			"the twelve instructors now have to leave halfway through each session. In the second session, " +
			"a forecasted storm may prevent us from using the outdoor demonstration area, so every activity " +
			"needs an indoor alternative. We still want hands-on practice and cannot exceed the earlier room " +
			"capacity. Revise the plan to handle these accessibility, staffing, and weather constraints.",
			"MARIGOLD"
		),
		(
			"The safety coordinator has limited each station to one repair stand and requires direct supervision " +
			"whenever someone adjusts a brake. We have four stands total but only two tire pumps. We also promised " +
			"the venue that no lubricant will be used indoors, even if weather forces a demonstration inside. " +
			"Participants are a mix of adults and teenagers, and each should perform at least one practical task " +
			"rather than watching every demonstration. Explain the most important changes to the workshop " +
			"rotation while preserving all the earlier constraints.",
			"MARIGOLD"
		),
		(
			"Budget review: one instructor suggests buying three additional repair stands, but the remaining " +
			"funds can cover either those stands or a set of accessible floor-level tools, not both. A partner " +
			"offers to lend two more tire pumps at no cost. The venue will still limit attendance as previously " +
			"agreed, the number of instructors has not improved, and supervised brake work remains mandatory. " +
			"Choose which purchase improves the session more, explain the tradeoff briefly, and retain the " +
			"indoor contingency without relying on lubricant.",
			"MARIGOLD"
		),
		(
			"Before registration opens, the organizers need one final recommendation they can hand to the " +
			"instructors. Recap the most important schedule, staffing, accessibility, equipment, weather, " +
			"and safety decisions from this conversation. Do not invent more volunteers or a larger room, " +
			"and distinguish equipment we own from the items the partner merely lends. Include the original " +
			"project codename from the first message so the organizers can identify the correct plan.",
			"MARIGOLD"
		),
	];

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	[Trait("Category", "Benchmark")]
	public Task CompareFreshHistoryAndReusedContext_ReportsLatencyAndRecall() =>
		RunBenchmarkAsync("short", ShortSystemPrompt, ShortTurns, trials: 3);

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	[Trait("Category", "Benchmark")]
	public Task CompareSubstantialPromptsAndBriefReplies_ReportsCosts() =>
		RunBenchmarkAsync("substantial-brief", BriefSystemPrompt, SubstantialTurns, trials: 2);

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	[Trait("Category", "Benchmark")]
	public Task CompareSubstantialPromptsAndParagraphReplies_ReportsCosts() =>
		RunBenchmarkAsync("substantial-paragraph", ParagraphSystemPrompt, SubstantialTurns, trials: 2);

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	[Trait("Category", "Benchmark")]
	public async Task CompareNearContextLimitAcrossFiveTurns_ReportsCosts()
	{
		Assert.Equal(AIFeatureReadyState.Ready, LanguageModel.GetReadyState());
		using var model = await LanguageModel.CreateAsync();
		using var context = model.CreateContext(ParagraphSystemPrompt);

		var lower = 0;
		var upper = MaxIntakeNotes;
		while (lower < upper)
		{
			var middle = lower + (upper - lower + 1) / 2;
			var transcript = CapacityTranscript(BuildNearLimitTurns(middle));
			if (model.GetUsablePromptLength(context, transcript) == (ulong)transcript.Length)
				lower = middle;
			else
				upper = middle - 1;
		}

		if (lower == MaxIntakeNotes)
			throw new InvalidOperationException("Increase MaxIntakeNotes to measure this model's context boundary.");

		var turns = BuildNearLimitTurns(lower);
		var nextTranscript = CapacityTranscript(BuildNearLimitTurns(lower + 1));
		output.WriteLine(
			$"Near-limit probe: {lower} intake notes across five turns, " +
			$"{CapacityTranscript(turns).Length} UTF-16 characters including five reserved replies. " +
			$"One additional note fits {model.GetUsablePromptLength(context, nextTranscript)}/{nextTranscript.Length} characters. " +
			"GetUsablePromptLength is a character-index fit check, not a token counter.");
		await RunBenchmarkAsync("near-limit", ParagraphSystemPrompt, turns, trials: 1);
	}

	private static (string Prompt, string Expected)[] BuildNearLimitTurns(int noteCount)
	{
		var turns = new (string Prompt, string Expected)[SubstantialTurns.Length];
		for (var turn = 0; turn < turns.Length; turn++)
		{
			var notes = Enumerable.Range(0, noteCount)
				.Where(index => index % (turns.Length - 1) == turn)
				.Select(index =>
					$"Intake note {index + 1:D3}: A participant with a {new[] { "low-step", "cargo", "folding", "hybrid" }[index % 4]} " +
					$"bicycle needs {new[] { "a floor-level tool tray", "a supervised brake check", "a seated tire inspection", "a chain diagnostic" }[index % 4]}. " +
					"Record the need for the indoor rotation without adding instructors or exceeding the room capacity.");
			turns[turn] = (SubstantialTurns[turn].Prompt + Environment.NewLine + string.Join(" ", notes),
				SubstantialTurns[turn].Expected);
		}
		return turns;
	}

	private static string CapacityTranscript((string Prompt, string Expected)[] turns) =>
		string.Join(Environment.NewLine, turns.Select(turn =>
			$"User: {turn.Prompt}{Environment.NewLine}Assistant: {ReservedReply}"));

	private async Task RunBenchmarkAsync(
		string scenario,
		string systemPrompt,
		(string Prompt, string Expected)[] turns,
		int trials)
	{
		Assert.Equal(AIFeatureReadyState.Ready, LanguageModel.GetReadyState());

		using var model = await LanguageModel.CreateAsync();
		var modelOptions = new LanguageModelOptions { Temperature = 0, TopK = 1 };
		var chatOptions = new ChatOptions { Instructions = systemPrompt, Temperature = 0, TopK = 1 };

		using (var warmupContext = model.CreateContext(systemPrompt))
		{
			var warmup = await model.GenerateResponseAsync(warmupContext, "Reply READY.", modelOptions);
			Assert.Equal(LanguageModelResponseStatus.Complete, warmup.Status);
		}

		var measurements = new List<Measurement>();
		for (var trial = 1; trial <= trials; trial++)
		{
			Mode[] modes = [Mode.CurrentAdapter, Mode.ReusedContext, Mode.FreshHistory];

			for (var index = 0; index < modes.Length; index++)
			{
				var mode = modes[(index + trial - 1) % modes.Length];
				await RunConversationAsync(model, modelOptions, chatOptions, systemPrompt, turns, mode, trial, measurements);
			}
		}

		var lines = new List<string>
		{
			"mode\ttrial\tturn\tpromptChars\tresponseChars\tresponseWords\ttotalMs\tgenerationMs\tfirstChunkMs\texpectedFound\tresponse"
		};
		lines.AddRange(measurements.Select(m =>
			$"{m.Mode}\t{m.Trial}\t{m.Turn}\t{m.PromptChars}\t{m.Response.Length}\t{CountWords(m.Response)}\t" +
			$"{m.TotalMs.ToString("F1", CultureInfo.InvariantCulture)}\t" +
			$"{m.GenerationMs.ToString("F1", CultureInfo.InvariantCulture)}\t" +
			$"{m.FirstChunkMs?.ToString("F1", CultureInfo.InvariantCulture)}\t" +
			$"{m.ExpectedFound}\t{m.Response.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}"));

		var path = Path.Combine(FileSystem.AppDataDirectory, $"windows-ai-context-benchmark-{scenario}.tsv");
		await File.WriteAllLinesAsync(path, lines);

		foreach (var group in measurements.GroupBy(m => m.Mode))
		{
			var followUps = group.Where(m => m.Turn > 1).ToArray();
			var episodeTotals = group.GroupBy(m => m.Trial)
				.Select(trial => trial.Sum(m => m.TotalMs)).ToArray();
			var firstChunks = group.Where(m => m.FirstChunkMs.HasValue)
				.Select(m => m.FirstChunkMs!.Value).ToArray();
			output.WriteLine(
				$"{group.Key}: median follow-up {Median(followUps.Select(m => m.TotalMs)):F1} ms, " +
				$"median episode {Median(episodeTotals):F1} ms, " +
				$"median first chunk {(firstChunks.Length > 0 ? $"{Median(firstChunks):F1} ms" : "not reported")}, " +
				$"median response {Median(group.Select(m => (double)CountWords(m.Response))):F0} words, " +
				$"expected answers {group.Count(m => m.ExpectedFound)}/{group.Count()}");
		}

		output.WriteLine($"{scenario} individual turns and responses: {path}");
	}

	private static async Task RunConversationAsync(
		LanguageModel model,
		LanguageModelOptions modelOptions,
		ChatOptions chatOptions,
		string systemPrompt,
		(string Prompt, string Expected)[] turns,
		Mode mode,
		int trial,
		List<Measurement> measurements)
	{
		using var retainedContext = mode == Mode.ReusedContext ? model.CreateContext(systemPrompt) : null;
		using var client = mode == Mode.CurrentAdapter ? new WindowsAIChatClient(model) : null;
		var history = new List<ChatMessage>();

		for (var turn = 0; turn < turns.Length; turn++)
		{
			var (userText, expected) = turns[turn];
			history.Add(new ChatMessage(ChatRole.User, userText));

			var started = Stopwatch.GetTimestamp();
			string response;
			string prompt;
			TimeSpan generation;
			long generating;
			long firstChunkAt = 0;

			if (client is not null)
			{
				prompt = SerializeHistory(history);
				generating = Stopwatch.GetTimestamp();
				var text = new StringBuilder();
				await foreach (var update in client.GetStreamingResponseAsync(history, chatOptions))
				{
					if (update.Text is { Length: > 0 } chunk)
					{
						Interlocked.CompareExchange(ref firstChunkAt, Stopwatch.GetTimestamp(), 0);
						text.Append(chunk);
					}
				}
				response = text.ToString();
				generation = Stopwatch.GetElapsedTime(generating);
			}
			else
			{
				using var freshContext = mode == Mode.FreshHistory ? model.CreateContext(systemPrompt) : null;
				var context = retainedContext ?? freshContext!;
				prompt = mode == Mode.ReusedContext ? userText : SerializeHistory(history);

				if (model.GetUsablePromptLength(context, prompt) < (ulong)prompt.Length)
					throw new InvalidOperationException($"Prompt did not fit for {mode}, trial {trial}, turn {turn + 1}.");

				generating = Stopwatch.GetTimestamp();
				var operation = model.GenerateResponseAsync(context, prompt, modelOptions);
				operation.Progress = (_, chunk) =>
				{
					if (!string.IsNullOrEmpty(chunk))
						Interlocked.CompareExchange(ref firstChunkAt, Stopwatch.GetTimestamp(), 0);
				};
				var result = await operation;
				generation = Stopwatch.GetElapsedTime(generating);

				if (result.Status != LanguageModelResponseStatus.Complete)
					throw new InvalidOperationException(
						$"{mode}, trial {trial}, turn {turn + 1}: {result.Status}", result.ExtendedError);

				response = result.Text ?? string.Empty;
			}

			history.Add(new ChatMessage(ChatRole.Assistant, response));
			measurements.Add(new Measurement(
				mode, trial, turn + 1, prompt.Length, response,
				Stopwatch.GetElapsedTime(started).TotalMilliseconds,
				generation.TotalMilliseconds,
				firstChunkAt == 0 ? null : Stopwatch.GetElapsedTime(generating, firstChunkAt).TotalMilliseconds,
				response.Contains(expected, StringComparison.OrdinalIgnoreCase)));
		}
	}

	private static string SerializeHistory(IEnumerable<ChatMessage> history) =>
		string.Join(Environment.NewLine, history.Select(message =>
			$"{(message.Role == ChatRole.User ? "User" : "Assistant")}: {message.Text}"));

	private static int CountWords(string text) =>
		text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

	private static double Median(IEnumerable<double> values)
	{
		var sorted = values.Order().ToArray();
		return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
	}

	private enum Mode { CurrentAdapter, ReusedContext, FreshHistory }

	private sealed record Measurement(
		Mode Mode, int Trial, int Turn, int PromptChars, string Response,
		double TotalMs, double GenerationMs, double? FirstChunkMs, bool ExpectedFound);
}
#endif
