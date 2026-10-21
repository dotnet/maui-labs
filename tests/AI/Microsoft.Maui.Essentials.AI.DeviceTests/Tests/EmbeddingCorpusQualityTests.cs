using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.EmbeddingQuality;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Trait(TestTraits.RequiresModel, TestTraits.True)]
[Trait("Suite", "EmbeddingCorpus")]
public sealed class EmbeddingCorpusQualityTests(ITestOutputHelper output)
{
#if IOS || MACCATALYST
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public async Task Apple_EnglishSentenceCorpus_AllRelevantDocumentsAppearInTopFive(int trial)
	{
		using var generator = new NLEmbeddingGenerator();
		await EvaluateAsync("apple", generator, trial);
	}
#endif

#if ENABLE_OPENAI_CLIENT
	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public async Task OpenAI_ConfiguredEmbeddingCorpus_AllRelevantDocumentsAppearInTopFive(int trial)
	{
		using var generator = new OpenAIEmbeddingGenerator();
		await EvaluateAsync("openai", generator, trial);
	}
#endif

	private async Task EvaluateAsync(string provider, IEmbeddingGenerator<string, Embedding<float>> generator, int trial)
	{
		var assembly = typeof(EmbeddingCorpusQualityTests).Assembly;
		static byte[] ReadResource(System.Reflection.Assembly assembly, string name)
		{
			using var stream = assembly.GetManifestResourceStream("EmbeddingQuality." + name)
				?? throw new InvalidDataException($"Missing embedded {name}.");
			using var memory = new MemoryStream();
			stream.CopyTo(memory);
			return memory.ToArray();
		}

		var corpusBytes = ReadResource(assembly, "corpus.json");
		var corpus = Corpus.Parse(System.Text.Encoding.UTF8.GetString(corpusBytes));
		var supplementBytes = ReadResource(assembly, "multi-relevant.json");
		var supplement = JsonSerializer.Deserialize<Supplement>(
			supplementBytes,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException("Missing multi-relevant queries.");
		if (supplement.SchemaVersion != 1)
			throw new InvalidDataException("Unexpected multi-relevant corpus schema.");
		Corpus.Validate(corpus.Documents, supplement.Queries);

		var longDocs = corpus.MatchedMiddleDocuments();
		var scenarios = new[]
		{
			new Scenario("short_whole", corpus.Documents, corpus.Queries, Scenario.Whole),
			new Scenario("short_multi", corpus.Documents, supplement.Queries, Scenario.Whole),
			new Scenario("matched_middle_whole", longDocs, corpus.LongQueries, Scenario.Whole),
			new Scenario("matched_middle_360", longDocs, corpus.LongQueries, Scenario.Playground360),
			new Scenario("matched_middle_sentence1", longDocs, corpus.LongQueries, Scenario.Sentences)
		};

		// One fresh set of embeddings per independent trial; reuse identical text only inside that trial.
		var texts = scenarios.SelectMany(s => s.Documents.SelectMany(d => s.Chunk(d.Text)))
			.Concat(scenarios.SelectMany(s => s.Queries.Select(q => Corpus.Normalize(q.Text))))
			.Distinct(StringComparer.Ordinal).ToArray();
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
		var vectors = await EmbeddingBatcher.RunAsync(texts, async (batch, token) =>
		{
			var generated = await generator.GenerateAsync(batch, cancellationToken: token);
			return generated.Select(e => e.Vector.ToArray()).ToArray();
		}, timeout.Token);

		var model = provider == "apple" ? "english-sentence" : "configured-openai-embedding";
#if IOS || MACCATALYST
		if (generator is NLEmbeddingGenerator apple)
			model += $"-revision-{((IEmbeddingGenerator<string, Embedding<float>>)apple).GetService<NaturalLanguage.NLEmbedding>()?.Revision}";
#endif
		output.WriteLine($"CORPUS provider={provider} model={model} trial={trial} sha256={Convert.ToHexString(SHA256.HashData(corpusBytes)).ToLowerInvariant()} supplementSha256={Convert.ToHexString(SHA256.HashData(supplementBytes)).ToLowerInvariant()} inputs={texts.Length} dimension={vectors.First().Value.Length}");
		var failures = new List<string>();
		foreach (var scenario in scenarios)
		{
			var docs = scenario.Documents.ToDictionary(d => d.Id,
				d => (IReadOnlyList<float[]>)scenario.Chunk(d.Text).Select(t => vectors[t]).ToArray(),
				StringComparer.Ordinal);
			var queries = scenario.Queries.ToDictionary(q => q.Id,
				q => vectors[Corpus.Normalize(q.Text)], StringComparer.Ordinal);
			var result = EmbeddingScorer.Score(scenario.Documents, scenario.Queries, docs, queries);
			output.WriteLine($"QUALITY provider={provider} scenario={scenario.Name} trial={trial} n={result.Rankings.Count} " +
				$"recall={Format(result.Recall)} hit={Format(result.Hit)} precision={Format(result.Precision)} mrr={result.Mrr:F4}");
			foreach (var row in result.Rankings)
				output.WriteLine($"QUERY provider={provider} scenario={scenario.Name} trial={trial} id={row.Id} group={row.Group} " +
					$"ranks={string.Join(",", row.Relevant.Select(id => $"{id}:{row.Rank(id)?.ToString() ?? "missing"}"))} " +
					$"top5={string.Join(",", row.Ranked.Take(5))}");
			if (!result.AllRelevantInTop5)
				failures.Add($"{scenario.Name}: {string.Join(", ", result.Rankings
					.Where(row => row.Relevant.Any(id => row.Rank(id) is not int rank || rank > 5))
					.Select(row => row.Id))}");
		}
		Assert.True(failures.Count == 0, $"100% all-relevant Recall@5 target missed: {string.Join("; ", failures)}");
	}

	private static string Format(IEnumerable<double> values) =>
		string.Join(",", values.Select(v => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));

	private sealed class Supplement
	{
		public int SchemaVersion { get; set; }
		public List<Query> Queries { get; set; } = [];
	}
}
