using Microsoft.Maui.Essentials.AI.EmbeddingQuality;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class EmbeddingQualityTests
{
	private static readonly Document[] Documents =
	[
		new() { Id = "a", Text = "First" },
		new() { Id = "b", Text = "Second" },
		new() { Id = "c", Text = "Third" },
		new() { Id = "d", Text = "Fourth" },
		new() { Id = "e", Text = "Fifth" },
		new() { Id = "f", Text = "Sixth" }
	];

	private static readonly Query[] Queries =
	[
		new() { Id = "q", Group = "multi", Text = "Question", Relevant = ["a", "f"] }
	];

	private static Dictionary<string, IReadOnlyList<float[]>> Vectors() => new()
	{
		["a"] = [[1f, 0f]],
		["b"] = [[0.9f, 0.1f]],
		["c"] = [[0.8f, 0.2f]],
		["d"] = [[0.7f, 0.3f]],
		["e"] = [[0.6f, 0.4f]],
		["f"] = [[0f, 1f]]
	};

	private static Dictionary<string, float[]> QueryVectors() => new() { ["q"] = [1f, 0f] };

	[Fact]
	public void Evidence_AnswerWithoutNamedLocation_DoesNotCountAsSupportingPassage()
	{
		string[] spans = ["Willow Loop", "flat walking trail", "suitable for wheelchairs"];
		const string location = "The Willow Loop is a flat walking trail around the freshwater lagoon.";
		const string details = "Its wooden boardwalk is suitable for wheelchairs.";
		Assert.False(EvidenceCoverage.ContainsRequiredSpans(location, spans));
		Assert.False(EvidenceCoverage.ContainsRequiredSpans(details, spans));
		Assert.True(EvidenceCoverage.ContainsRequiredSpans(location + " " + details, spans));
		Assert.Throws<InvalidDataException>(() => EvidenceCoverage.ContainsRequiredSpans(details, []));
		Assert.Throws<InvalidDataException>(() => EvidenceCoverage.ContainsRequiredSpans(details, [""]));
	}

	[Fact]
	public void RankCeiling_OneWorseQueryCannotBeOffsetByAnotherImprovement()
	{
		var baseline = new RankExpectation { DocumentRanks = new() { ["answer"] = 2 }, EvidenceRank = 3 };
		var improved = new QueryRanking("q1", "group", ["answer"], ["answer", "other"]);
		var regressed = new QueryRanking("q2", "group", ["answer"], ["other", "another", "answer"]);
		Assert.Empty(baseline.Regressions(improved, 1));
		Assert.Equal(2, baseline.Regressions(regressed, 4).Count());
		Assert.Single(baseline.Regressions(improved, null));
		Assert.Throws<InvalidDataException>(() =>
			baseline.Regressions(new("q", "group", ["unknown"], ["unknown"]), 1).ToArray());
	}

	[Fact]
	public void Score_MultipleLabels_SeparatesRecallHitPrecisionAndMrr()
	{
		var result = EmbeddingScorer.Score(Documents, Queries, Vectors(), QueryVectors());
		Assert.Equal(["a", "b", "c", "d", "e", "f"], result.Rankings[0].Ranked);
		Assert.Equal(0.5, result.Recall[0]);
		Assert.Equal(1, result.Hit[0]);
		Assert.Equal(1, result.Precision[0]);
		Assert.Equal(0.5, result.Recall[4]);
		Assert.Equal(0.2, result.Precision[4], 10);
		Assert.Equal(1, result.Mrr);
		Assert.False(result.AllRelevantInTop5);
		Assert.Equal(6, result.Rankings[0].Rank("f"));
	}

	[Fact]
	public void Score_MaxChunkCosineAndOrdinalTieBreak_RanksBothLabelsAtThreshold()
	{
		var vectors = Vectors();
		vectors["f"] = [[0f, 1f], [1f, 0f]];
		var result = EmbeddingScorer.Score(Documents, Queries, vectors, QueryVectors());
		Assert.Equal(["a", "f"], result.Rankings[0].Ranked.Take(2));
		Assert.Equal(1, result.Recall[4]);
		Assert.True(result.AllRelevantInTop5);
		Assert.Equal(0.4, result.Precision[4], 10);
	}

	[Fact]
	public void Score_SecondRelevantAtRankFive_PassesButAtRankSixFails()
	{
		var vectors = Vectors();
		vectors["e"] = [[0f, 1f]];
		vectors["f"] = [[0.1f, 0.9f]];
		var result = EmbeddingScorer.Score(Documents, Queries, vectors, QueryVectors());
		Assert.Equal(5, result.Rankings[0].Rank("f"));
		Assert.True(result.AllRelevantInTop5);
		Assert.Equal(1, result.Recall[4], 10);
		Assert.Equal(0.5, result.Recall[3]);
		Assert.False(EmbeddingScorer.Score(Documents, Queries, Vectors(), QueryVectors()).AllRelevantInTop5);
	}

	[Theory]
	[InlineData("empty")]
	[InlineData("zero")]
	[InlineData("nan")]
	[InlineData("infinite")]
	[InlineData("dimension")]
	[InlineData("missing")]
	public void Score_InvalidVectors_Throws(string kind)
	{
		var vectors = Vectors();
		switch (kind)
		{
			case "empty": vectors["a"] = [[]]; break;
			case "zero": vectors["a"] = [[0f, 0f]]; break;
			case "nan": vectors["a"] = [[float.NaN, 1f]]; break;
			case "infinite": vectors["a"] = [[float.PositiveInfinity, 1f]]; break;
			case "dimension": vectors["a"] = [[1f]]; break;
			case "missing": vectors.Remove("a"); break;
		}
		Assert.Throws<InvalidDataException>(() => EmbeddingScorer.Score(Documents, Queries, vectors, QueryVectors()));
	}

	[Fact]
	public void Score_MissingQueryVector_Throws()
	{
		Assert.Throws<InvalidDataException>(() => EmbeddingScorer.Score(Documents, Queries, Vectors(),
			new Dictionary<string, float[]>()));
	}

	[Theory]
	[InlineData("[]")]
	[InlineData("[\"unknown\"]")]
	[InlineData("[\"a\",\"a\"]")]
	public void Corpus_InvalidRelevance_Throws(string labels)
	{
		var json = $$"""{"schemaVersion":1,"documents":[{"id":"a","text":"text"}],"queries":[{"id":"q","group":"g","text":"query","relevant":{{labels}}}],"longDocuments":[{"id":"l","needle":"needle","lead":35,"tail":35}],"longQueries":[{"id":"lq","group":"g","text":"query","relevant":["l"]}],"longFiller":"filler."}""";
		Assert.Throws<InvalidDataException>(() => Corpus.Parse(json));
	}

	[Fact]
	public void Corpus_DuplicateIds_Throws()
	{
		var documents = new[] { Documents[0], Documents[0] };
		Assert.Throws<InvalidDataException>(() => Corpus.Validate(documents, Queries));
	}

	[Fact]
	public void Chunking_Playground360AndSentences_AreDeterministic()
	{
		var text = string.Join(" ", Enumerable.Repeat("A sentence. ", 40));
		var chunks = Scenario.Playground360(text);
		Assert.All(chunks, chunk => Assert.InRange(chunk.Length, 1, 360));
		Assert.Equal(Corpus.Normalize(text), string.Join(" ", chunks));
		Assert.Equal(40, Scenario.Sentences(text).Count);
	}

	[Fact]
	public void EmbeddedCorpus_HasFrozenIdentityAndValidLabels()
	{
		using var stream = typeof(EmbeddingQualityTests).Assembly.GetManifestResourceStream("EmbeddingQuality.corpus.json");
		Assert.NotNull(stream);
		using var reader = new StreamReader(stream);
		var corpus = Corpus.Parse(reader.ReadToEnd());
		Assert.Equal(24, corpus.Documents.Count);
		Assert.Equal(24, corpus.Queries.Count);
		Assert.Equal(9, corpus.MatchedMiddleDocuments().Count);
		Assert.Equal(6, corpus.LongQueries.Count);
	}

	[Fact]
	public void MatchedPositions_KeepNeedlesAndDistractorsButMoveOnlyTargets()
	{
		using var stream = typeof(EmbeddingQualityTests).Assembly.GetManifestResourceStream("EmbeddingQuality.corpus.json")!;
		using var reader = new StreamReader(stream);
		var corpus = Corpus.Parse(reader.ReadToEnd());
		var early = corpus.MatchedDocuments(0);
		var middle = corpus.MatchedDocuments(35);
		var late = corpus.MatchedDocuments(70);
		foreach (var source in corpus.LongDocuments)
		{
			var first = early.Single(d => d.Id == source.Id).Text;
			var last = late.Single(d => d.Id == source.Id).Text;
			if (source.Position == "distractor")
			{
				Assert.Equal(first, middle.Single(d => d.Id == source.Id).Text);
				Assert.Equal(first, last);
			}
			else
			{
				Assert.StartsWith(source.Needle, first);
				Assert.EndsWith(source.Needle, last);
			}
			Assert.Equal(first.Length, last.Length);
		}
		Assert.Throws<ArgumentOutOfRangeException>(() => corpus.MatchedDocuments(71));
	}

	[Fact]
	public async Task Batcher_SeventeenInputs_UsesTwoBatchesAndAssociatesVectors()
	{
		var sizes = new List<int>();
		var inputs = Enumerable.Range(0, 17).Select(i => $"text-{i}").ToArray();
		var actual = await EmbeddingBatcher.RunAsync(inputs, (batch, token) =>
		{
			sizes.Add(batch.Length);
			Assert.False(token.IsCancellationRequested);
			return Task.FromResult<IReadOnlyList<float[]>>(
				batch.Select(text => new[] { (float)int.Parse(text[5..]) }).ToArray());
		}, CancellationToken.None);
		Assert.Equal([16, 1], sizes);
		Assert.Equal(17, actual.Count);
		Assert.Equal(16f, actual["text-16"][0]);
	}

	[Fact]
	public async Task Batcher_CancellationAndWrongCount_DoNotPassSilently()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var called = false;
		Task<IReadOnlyList<float[]>> Generate(string[] batch, CancellationToken token)
		{
			called = true;
			return Task.FromResult<IReadOnlyList<float[]>>([]);
		}
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			EmbeddingBatcher.RunAsync(["one"], Generate, cancellation.Token));
		Assert.False(called);
		await Assert.ThrowsAsync<InvalidDataException>(() =>
			EmbeddingBatcher.RunAsync(["one"], Generate, CancellationToken.None));
	}
}
