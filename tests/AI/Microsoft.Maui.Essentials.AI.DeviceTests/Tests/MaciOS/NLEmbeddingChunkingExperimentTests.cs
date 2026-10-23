#if IOS || MACCATALYST

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI.EmbeddingQuality;
using NaturalLanguage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Trait(TestTraits.RequiresModel, TestTraits.True)]
[Trait("Suite", "EmbeddingChunking")]
public sealed class NLEmbeddingChunkingExperimentTests(ITestOutputHelper output)
{
	private static readonly (string Name, DocumentChunkingOptions Options)[] Policies =
	[
		("whole", new(DocumentChunkingMode.WholeDocument)),
		("fixed120", new(DocumentChunkingMode.FixedCharacters, 120)),
		("fixed180", new(DocumentChunkingMode.FixedCharacters, 180)),
		("fixed240", new(DocumentChunkingMode.FixedCharacters, 240)),
		("fixed360", new(DocumentChunkingMode.FixedCharacters, 360)),
		("fixed720", new(DocumentChunkingMode.FixedCharacters, 720)),
		("fixed180_overlap45", new(DocumentChunkingMode.FixedCharacters, 180, 45)),
		("fixed360_overlap90", new(DocumentChunkingMode.FixedCharacters, 360, 90)),
		("fixed720_overlap180", new(DocumentChunkingMode.FixedCharacters, 720, 180)),
		("sentence1", new(DocumentChunkingMode.Sentences, 1)),
		("sentence2", new(DocumentChunkingMode.Sentences, 2)),
		("sentence3", new(DocumentChunkingMode.Sentences, 3)),
		("sentence2_overlap1", new(DocumentChunkingMode.Sentences, 2, 1)),
		("sentence3_overlap1", new(DocumentChunkingMode.Sentences, 3, 1))
	];

	[Fact]
	public async Task Apple_ChunkingSweep_MatchesMeasuredRankCeilingsWithoutRepeatedRunDrift()
	{
		var assembly = typeof(NLEmbeddingChunkingExperimentTests).Assembly;
		using var corpusStream = assembly.GetManifestResourceStream("EmbeddingQuality.corpus.json")
			?? throw new InvalidDataException("Missing frozen embedding corpus.");
		using var corpusMemory = new MemoryStream();
		await corpusStream.CopyToAsync(corpusMemory);
		var corpusBytes = corpusMemory.ToArray();
		var corpus = Corpus.Parse(Encoding.UTF8.GetString(corpusBytes));
		using var queryStream = assembly.GetManifestResourceStream("EmbeddingQuality.playground-queries.json")
			?? throw new InvalidDataException("Missing playground relevance labels.");
		var labels = await JsonSerializer.DeserializeAsync<SampleLabels>(queryStream,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException("Invalid playground relevance labels.");
		Assert.Equal(2, labels.SchemaVersion);
		var samples = EmbeddingSampleDocuments.All.Select(d => new Document { Id = d.Id, Text = d.Text }).ToArray();
		var sampleQueries = labels.Queries.Select(q => new Query
		{
			Id = q.Id, Group = q.Group, Text = q.Text, Relevant = [q.DocumentId]
		}).ToArray();
		Corpus.Validate(samples, sampleQueries);
		foreach (var label in labels.Queries)
		{
			Assert.NotEmpty(label.Evidence);
			foreach (var span in label.Evidence)
			{
				Assert.False(string.IsNullOrWhiteSpace(span));
				Assert.Contains(span, samples.Single(d => d.Id == label.DocumentId).Text, StringComparison.Ordinal);
			}
		}
		var scenarios = new[]
		{
			("short", (IReadOnlyList<Document>)corpus.Documents, (IReadOnlyList<Query>)corpus.Queries),
			("matched_early", corpus.MatchedDocuments(0), corpus.LongQueries),
			("matched_middle", corpus.MatchedDocuments(35), corpus.LongQueries),
			("matched_late", corpus.MatchedDocuments(70), corpus.LongQueries),
			("playground", samples, sampleQueries)
		};
		var prepared = (from policy in Policies
			from scenario in scenarios
			select new
			{
				Policy = policy.Name, policy.Options, Name = scenario.Item1,
				Documents = scenario.Item2, Queries = scenario.Item3,
				Chunks = scenario.Item2.ToDictionary(d => d.Id,
					d => DocumentChunker.Chunk(d.Text, policy.Options), StringComparer.Ordinal)
			}).ToArray();
		var texts = prepared.SelectMany(s => s.Chunks.Values.SelectMany(c => c))
			.Concat(scenarios.SelectMany(s => s.Item3.Select(q => Corpus.Normalize(q.Text))))
			.Distinct(StringComparer.Ordinal).ToArray();
		var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
			JsonSerializer.Serialize(new { Corpus = Convert.ToHexString(SHA256.HashData(corpusBytes)),
				Samples = EmbeddingSampleDocuments.All, Labels = labels,
				Policies = Policies.Select(p => new { p.Name, p.Options }) })))).ToLowerInvariant();
		using var baselineStream = assembly.GetManifestResourceStream("EmbeddingQuality.apple-rank-baseline.json")
			?? throw new InvalidDataException("Missing reviewed Apple rank baseline.");
		var baseline = await JsonSerializer.DeserializeAsync<RankBaseline>(baselineStream)
			?? throw new InvalidDataException("Invalid Apple rank baseline.");
		Assert.Equal(1, baseline.SchemaVersion);
		Assert.Equal(baseline.CorpusFingerprint, fingerprint);
		Assert.Equal(prepared.Sum(s => s.Queries.Count), baseline.Expectations.Count);
		var regressions = new List<string>();
		string? firstRanks = null;
		using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));

		for (var trial = 1; trial <= 3; trial++)
		{
			using var generator = new NLEmbeddingGenerator(NLLanguage.English);
			var native = ((IEmbeddingGenerator<string, Embedding<float>>)generator).GetService<NLEmbedding>();
			Assert.NotNull(native);
			Assert.Equal(baseline.ModelKey, $"sentence:en:{native.Revision}:{native.Dimension}");
			var elapsed = Stopwatch.StartNew();
			var vectors = await EmbeddingBatcher.RunAsync(texts, async (batch, token) =>
			{
				var generated = await Task.Run(() => generator.GenerateAsync(batch, cancellationToken: token), token);
				return generated.Select(e => e.Vector.ToArray()).ToArray();
			}, timeout.Token);
			Assert.All(vectors.Values, vector => Assert.Equal((int)native.Dimension, vector.Length));
			var signatures = new List<string>();
			foreach (var scenario in prepared)
			{
				var documentVectors = scenario.Chunks.ToDictionary(p => p.Key,
					p => (IReadOnlyList<float[]>)p.Value.Select(t => vectors[t]).ToArray(), StringComparer.Ordinal);
				var queryVectors = scenario.Queries.ToDictionary(q => q.Id,
					q => vectors[Corpus.Normalize(q.Text)], StringComparer.Ordinal);
				var result = EmbeddingScorer.Score(scenario.Documents, scenario.Queries, documentVectors, queryVectors);
				var observations = scenario.Queries.Select(query =>
				{
					var ranking = result.Rankings.Single(r => r.Id == query.Id);
					var label = scenario.Name == "playground" ? labels.Queries.Single(q => q.Id == query.Id) : null;
					var chunks = scenario.Chunks.SelectMany(pair => pair.Value.Select((text, index) => new
					{
						DocumentId = pair.Key, Index = index + 1,
						Score = EmbeddingScorer.Cosine(queryVectors[query.Id], vectors[text]),
						ContainsRequiredSpans = label is not null && pair.Key == label.DocumentId &&
							EvidenceCoverage.ContainsRequiredSpans(text, label.Evidence)
					})).OrderByDescending(c => c.Score).ThenBy(c => c.DocumentId, StringComparer.Ordinal)
						.ThenBy(c => c.Index).ToArray();
					int? evidenceRank = label is null ? null : Array.FindIndex(chunks, c => c.ContainsRequiredSpans) + 1;
					if (evidenceRank == 0)
						evidenceRank = null;
					var key = $"{scenario.Policy}/{scenario.Name}/{query.Id}";
					Assert.True(baseline.Expectations.TryGetValue(key, out var expectation), $"Missing baseline for {key}.");
					regressions.AddRange(expectation!.Regressions(ranking, evidenceRank)
						.Select(message => $"trial {trial}, {key}: {message}"));
					signatures.Add($"{scenario.Policy}/{scenario.Name}/{query.Id}:" +
						$"{string.Join(",", ranking.Ranked)}:evidence={evidenceRank?.ToString() ?? "missing"}");
					return new
					{
						query.Id, query.Group, query.Text,
						RelevantRanks = query.Relevant.Select(id => new { DocumentId = id, Rank = ranking.Rank(id) }),
						RankedDocuments = ranking.Ranked, EvidenceRank = evidenceRank,
						EvidenceCovered = label is null ? (bool?)null : chunks.Any(c => c.ContainsRequiredSpans),
						TopChunks = label is null ? null : chunks.Take(5).ToArray()
					};
				}).ToArray();
				output.WriteLine("APPLE_CHUNKING " + JsonSerializer.Serialize(new
				{
					SchemaVersion = 2, CorpusFingerprint = fingerprint, Trial = trial,
					Platform = DeviceInfo.Current.Platform.ToString(), OSVersion = Environment.OSVersion.Version.ToString(),
					Model = new { Kind = "sentence", ConfiguredLanguage = "en", NativeLanguage = $"{native.Language}",
						Revision = (ulong)native.Revision, Dimensions = (ulong)native.Dimension },
					scenario.Policy, Mode = scenario.Options.Mode.ToString(), scenario.Options.Size, scenario.Options.Overlap,
					Scenario = scenario.Name, Documents = scenario.Documents.Count,
					Chunks = scenario.Chunks.Sum(p => p.Value.Count),
					DistinctChunks = scenario.Chunks.Values.SelectMany(c => c).Distinct(StringComparer.Ordinal).Count(),
					IndexedCharacters = scenario.Chunks.Values.SelectMany(c => c).Sum(c => c.Length),
					result.Recall, result.Hit, result.Precision, result.Mrr, Observations = observations
				}));
			}
			var ranks = string.Join("\n", signatures);
			if (firstRanks is null)
				firstRanks = ranks;
			else
				Assert.Equal(firstRanks, ranks);
			output.WriteLine($"Trial {trial}: generated {vectors.Count} unique inputs in {elapsed.Elapsed.TotalSeconds:F1}s. " +
				"Deduplication is within a trial only. Ranking reproducibility is not a retrieval-quality guarantee.");
		}
		Assert.True(regressions.Count == 0, string.Join("\n", regressions));
	}

	private sealed class SampleLabels
	{
		public int SchemaVersion { get; set; }
		public List<SampleQuery> Queries { get; set; } = [];
	}

	private sealed class SampleQuery
	{
		public string Id { get; set; } = "";
		public string Group { get; set; } = "";
		public string Text { get; set; } = "";
		public string DocumentId { get; set; } = "";
		public string[] Evidence { get; set; } = [];
	}
}

#endif
