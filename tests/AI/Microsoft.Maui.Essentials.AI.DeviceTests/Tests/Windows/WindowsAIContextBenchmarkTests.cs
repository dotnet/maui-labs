#if WINDOWS && WINDOWS_AI_CONTEXT_BENCHMARK
using System.Diagnostics;
using System.Globalization;
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
	private const int Trials = 3;
	private const string SystemPrompt = "You are a concise assistant. Answer with only the requested word or code.";

	private static readonly (string Prompt, string Expected)[] Turns =
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

	[Fact]
	[Trait(TestTraits.RequiresModel, TestTraits.True)]
	[Trait("Category", "Benchmark")]
	public async Task CompareFreshHistoryAndReusedContext_ReportsLatencyAndRecall()
	{
		Assert.Equal(AIFeatureReadyState.Ready, LanguageModel.GetReadyState());

		using var model = await LanguageModel.CreateAsync();
		var modelOptions = new LanguageModelOptions { Temperature = 0, TopK = 1 };
		var chatOptions = new ChatOptions { Instructions = SystemPrompt, Temperature = 0, TopK = 1 };

		using (var warmupContext = model.CreateContext(SystemPrompt))
		{
			var warmup = await model.GenerateResponseAsync(warmupContext, "Reply READY.", modelOptions);
			Assert.Equal(LanguageModelResponseStatus.Complete, warmup.Status);
		}

		var measurements = new List<Measurement>();
		for (var trial = 1; trial <= Trials; trial++)
		{
			Mode[] modes = [Mode.CurrentAdapter, Mode.ReusedContext, Mode.FreshHistory];

			for (var index = 0; index < modes.Length; index++)
			{
				var mode = modes[(index + trial - 1) % modes.Length];
				await RunConversationAsync(model, modelOptions, chatOptions, mode, trial, measurements);
			}
		}

		var lines = new List<string>
		{
			"mode\ttrial\tturn\tpromptChars\tresponseChars\ttotalMs\tgenerationMs\texpectedFound\tresponse"
		};
		lines.AddRange(measurements.Select(m =>
			$"{m.Mode}\t{m.Trial}\t{m.Turn}\t{m.PromptChars}\t{m.Response.Length}\t" +
			$"{m.TotalMs.ToString("F1", CultureInfo.InvariantCulture)}\t" +
			$"{m.GenerationMs.ToString("F1", CultureInfo.InvariantCulture)}\t" +
			$"{m.ExpectedFound}\t{m.Response.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t")}"));

		var path = Path.Combine(FileSystem.AppDataDirectory, "windows-ai-context-benchmark.tsv");
		await File.WriteAllLinesAsync(path, lines);

		foreach (var group in measurements.GroupBy(m => m.Mode))
		{
			var followUps = group.Where(m => m.Turn > 1).ToArray();
			var episodeTotals = group.GroupBy(m => m.Trial)
				.Select(trial => trial.Sum(m => m.TotalMs)).ToArray();
			output.WriteLine(
				$"{group.Key}: median follow-up {Median(followUps.Select(m => m.TotalMs)):F1} ms, " +
				$"median episode {Median(episodeTotals):F1} ms, " +
				$"expected answers {group.Count(m => m.ExpectedFound)}/{group.Count()}");
		}

		output.WriteLine($"Individual turns and responses: {path}");
	}

	private static async Task RunConversationAsync(
		LanguageModel model,
		LanguageModelOptions modelOptions,
		ChatOptions chatOptions,
		Mode mode,
		int trial,
		List<Measurement> measurements)
	{
		using var retainedContext = mode == Mode.ReusedContext ? model.CreateContext(SystemPrompt) : null;
		using var client = mode == Mode.CurrentAdapter ? new WindowsAIChatClient(model) : null;
		var history = new List<ChatMessage>();

		for (var turn = 0; turn < Turns.Length; turn++)
		{
			var (userText, expected) = Turns[turn];
			history.Add(new ChatMessage(ChatRole.User, userText));

			var started = Stopwatch.GetTimestamp();
			string response;
			string prompt;
			TimeSpan generation;

			if (client is not null)
			{
				prompt = SerializeHistory(history);
				var generating = Stopwatch.GetTimestamp();
				response = (await client.GetResponseAsync(history, chatOptions)).Text ?? string.Empty;
				generation = Stopwatch.GetElapsedTime(generating);
			}
			else
			{
				using var freshContext = mode == Mode.FreshHistory ? model.CreateContext(SystemPrompt) : null;
				var context = retainedContext ?? freshContext!;
				prompt = mode == Mode.ReusedContext ? userText : SerializeHistory(history);

				if (model.GetUsablePromptLength(context, prompt) < (ulong)prompt.Length)
					throw new InvalidOperationException($"Prompt did not fit for {mode}, trial {trial}, turn {turn + 1}.");

				var generating = Stopwatch.GetTimestamp();
				var result = await model.GenerateResponseAsync(context, prompt, modelOptions);
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
				response.Contains(expected, StringComparison.OrdinalIgnoreCase)));
		}
	}

	private static string SerializeHistory(IEnumerable<ChatMessage> history) =>
		string.Join(Environment.NewLine, history.Select(message =>
			$"{(message.Role == ChatRole.User ? "User" : "Assistant")}: {message.Text}"));

	private static double Median(IEnumerable<double> values)
	{
		var sorted = values.Order().ToArray();
		return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
	}

	private enum Mode { CurrentAdapter, ReusedContext, FreshHistory }

	private sealed record Measurement(
		Mode Mode, int Trial, int Turn, int PromptChars, string Response,
		double TotalMs, double GenerationMs, bool ExpectedFound);
}
#endif
