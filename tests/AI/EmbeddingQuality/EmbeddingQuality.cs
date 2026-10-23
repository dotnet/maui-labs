using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.Essentials.AI.EmbeddingQuality;

internal sealed class Corpus
{
	public int SchemaVersion { get; set; }
	public List<Document> Documents { get; set; } = [];
	public List<Query> Queries { get; set; } = [];
	public List<LongDocument> LongDocuments { get; set; } = [];
	public List<Query> LongQueries { get; set; } = [];
	public string LongFiller { get; set; } = "";

	public static Corpus Parse(string json)
	{
		var corpus = JsonSerializer.Deserialize<Corpus>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidDataException("Missing corpus.");
		if (corpus.SchemaVersion != 1)
			throw new InvalidDataException("Unsupported corpus schema.");
		Validate(corpus.Documents, corpus.Queries);
		if (corpus.LongDocuments.Count == 0 || string.IsNullOrWhiteSpace(corpus.LongFiller))
			throw new InvalidDataException("Missing long documents or filler.");
		Validate(corpus.MatchedDocuments(35), corpus.LongQueries);
		return corpus;
	}

	public List<Document> MatchedMiddleDocuments() => MatchedDocuments(35);

	public List<Document> MatchedDocuments(int precedingSentences)
	{
		if (precedingSentences is < 0 or > 70)
			throw new ArgumentOutOfRangeException(nameof(precedingSentences));
		var filler = Normalize(LongFiller);
		return LongDocuments.Select(d =>
		{
			if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Needle) ||
				d.Lead < 0 || d.Tail < 0 || d.Lead + d.Tail != 70)
				throw new InvalidDataException("Invalid long document.");
			var lead = d.Position == "distractor" ? 35 : precedingSentences;
			var text = string.Join(" ", Enumerable.Repeat(filler, lead)
				.Append(Normalize(d.Needle)).Concat(Enumerable.Repeat(filler, 70 - lead)));
			return new Document { Id = d.Id, Text = text };
		}).ToList();
	}

	public static void Validate(IReadOnlyList<Document> documents, IReadOnlyList<Query> queries)
	{
		if (documents.Count == 0 || queries.Count == 0 ||
			documents.Any(d => string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Text)) ||
			queries.Any(q => string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Group) ||
				string.IsNullOrWhiteSpace(q.Text) || q.Relevant is null || q.Relevant.Count == 0 ||
				q.Relevant.Any(string.IsNullOrWhiteSpace)) ||
			documents.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != documents.Count ||
			queries.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != queries.Count)
			throw new InvalidDataException("Empty, duplicate or invalid corpus entries.");
		var ids = documents.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var query in queries)
			if (query.Relevant.Distinct(StringComparer.Ordinal).Count() != query.Relevant.Count ||
				query.Relevant.Any(id => !ids.Contains(id)))
				throw new InvalidDataException($"Invalid relevance labels for {query.Id}.");
	}

	public static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}

internal sealed class Document
{
	public string Id { get; set; } = "";
	public string Text { get; set; } = "";
}

internal sealed class LongDocument
{
	public string Id { get; set; } = "";
	public string Position { get; set; } = "";
	public int Lead { get; set; }
	public int Tail { get; set; }
	public string Needle { get; set; } = "";
}

internal sealed class Query
{
	public string Id { get; set; } = "";
	public string Group { get; set; } = "";
	public string Text { get; set; } = "";
	public List<string> Relevant { get; set; } = [];
}

internal sealed record Scenario(string Name, IReadOnlyList<Document> Documents,
	IReadOnlyList<Query> Queries, Func<string, IReadOnlyList<string>> Chunk)
{
	public static IReadOnlyList<string> Whole(string text) => [Corpus.Normalize(text)];

	public static IReadOnlyList<string> Sentences(string text)
	{
		var normalized = Corpus.Normalize(text);
		return Regex.Matches(normalized, @"[^.!?]+[.!?]?")
			.Select(m => m.Value.Trim()).Where(s => s.Length != 0).ToArray();
	}

	public static IReadOnlyList<string> Playground360(string text)
	{
		var source = Corpus.Normalize(text);
		var chunks = new List<string>();
		var offset = 0;
		while (offset < source.Length)
		{
			var count = Math.Min(360, source.Length - offset);
			var end = offset + count;
			if (end < source.Length)
			{
				var space = source.LastIndexOf(' ', end - 1, count);
				if (space > offset + count / 2)
					end = space;
				// Never split a UTF-16 surrogate pair when the fixed limit hits one.
				else if (char.IsHighSurrogate(source[end - 1]) && char.IsLowSurrogate(source[end]))
					end--;
			}
			var chunk = source[offset..end].Trim();
			if (chunk.Length != 0)
				chunks.Add(chunk);
			offset = end;
			while (offset < source.Length && source[offset] == ' ')
				offset++;
		}
		return chunks;
	}
}

internal sealed record QueryRanking(string Id, string Group, IReadOnlyList<string> Relevant,
	IReadOnlyList<string> Ranked)
{
	public int? Rank(string id)
	{
		for (var i = 0; i < Ranked.Count; i++)
			if (Ranked[i] == id)
				return i + 1;
		return null;
	}

	public int? FirstRelevantRank => Relevant.Select(Rank).Where(r => r.HasValue)
		.Select(r => r!.Value).Order().Cast<int?>().FirstOrDefault();
}

internal sealed class QualityResult
{
	public required IReadOnlyList<QueryRanking> Rankings { get; init; }
	public required double[] Recall { get; init; }
	public required double[] Hit { get; init; }
	public required double[] Precision { get; init; }
	public required double Mrr { get; init; }
	public bool AllRelevantInTop5 => Rankings.All(row =>
		row.Relevant.All(id => row.Rank(id) is int rank && rank <= 5));
}

internal static class EvidenceCoverage
{
	public static bool ContainsRequiredSpans(string chunk, IReadOnlyList<string> spans)
	{
		if (spans.Count == 0 || spans.Any(string.IsNullOrWhiteSpace))
			throw new InvalidDataException("Evidence needs all answer-essential source spans.");
		return spans.All(span => chunk.Contains(span, StringComparison.Ordinal));
	}
}

internal sealed class RankBaseline
{
	public int SchemaVersion { get; set; }
	public string CorpusFingerprint { get; set; } = "";
	public string ModelKey { get; set; } = "";
	public Dictionary<string, RankExpectation> Expectations { get; set; } = [];
}

internal sealed class RankExpectation
{
	public Dictionary<string, int> DocumentRanks { get; set; } = [];
	public int? EvidenceRank { get; set; }

	public IEnumerable<string> Regressions(QueryRanking ranking, int? evidenceRank)
	{
		if (!DocumentRanks.Keys.Order(StringComparer.Ordinal)
			.SequenceEqual(ranking.Relevant.Order(StringComparer.Ordinal)))
			throw new InvalidDataException("Baseline relevance labels differ from the corpus.");
		foreach (var (documentId, ceiling) in DocumentRanks)
		{
			if (ceiling <= 0)
				throw new InvalidDataException("Baseline document ranks must be positive.");
			if (ranking.Rank(documentId) is not int actual || actual > ceiling)
				yield return $"{documentId}: expected rank <= {ceiling}, actual {ranking.Rank(documentId)?.ToString() ?? "missing"}";
		}
		if (EvidenceRank is <= 0)
			throw new InvalidDataException("Baseline evidence rank must be positive or null.");
		if (EvidenceRank is int expected && (evidenceRank is null || evidenceRank > expected))
			yield return $"supporting passage: expected rank <= {expected}, actual {evidenceRank?.ToString() ?? "missing"}";
	}
}

internal static class EmbeddingBatcher
{
	public static async Task<Dictionary<string, float[]>> RunAsync(
		IReadOnlyList<string> texts,
		Func<string[], CancellationToken, Task<IReadOnlyList<float[]>>> generate,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(texts);
		ArgumentNullException.ThrowIfNull(generate);
		if (texts.Count == 0 || texts.Any(string.IsNullOrWhiteSpace) ||
			texts.Distinct(StringComparer.Ordinal).Count() != texts.Count)
			throw new InvalidDataException("Embedding inputs must be nonempty and unique.");

		var vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
		foreach (var batch in texts.Chunk(16))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var generated = await generate(batch, cancellationToken);
			if (generated is null || generated.Count != batch.Length)
				throw new InvalidDataException("Embedding provider returned the wrong batch size.");
			for (var i = 0; i < batch.Length; i++)
				vectors.Add(batch[i], generated[i]);
		}
		return vectors;
	}
}

internal static class EmbeddingScorer
{
	public static QualityResult Score(
		IReadOnlyList<Document> documents, IReadOnlyList<Query> queries,
		IReadOnlyDictionary<string, IReadOnlyList<float[]>> documentVectors,
		IReadOnlyDictionary<string, float[]> queryVectors)
	{
		Corpus.Validate(documents, queries);
		if (documentVectors.Count != documents.Count || queryVectors.Count != queries.Count)
			throw new InvalidDataException("Missing or extra vectors.");
		int? dimension = null;
		void ValidateVector(float[]? vector)
		{
			if (vector is null || vector.Length == 0 || vector.Any(v => !float.IsFinite(v)))
				throw new InvalidDataException("Empty or nonfinite vector.");
			if (dimension is not null && dimension != vector.Length)
				throw new InvalidDataException("Embedding dimension mismatch.");
			dimension = vector.Length;
			if (vector.All(v => v == 0))
				throw new InvalidDataException("Zero vector.");
		}
		foreach (var document in documents)
		{
			if (!documentVectors.TryGetValue(document.Id, out var vectors) ||
				vectors is null || vectors.Count == 0)
				throw new InvalidDataException($"Missing chunks for {document.Id}.");
			foreach (var vector in vectors)
				ValidateVector(vector);
		}
		foreach (var query in queries)
		{
			if (!queryVectors.TryGetValue(query.Id, out var vector))
				throw new InvalidDataException($"Missing query vector for {query.Id}.");
			ValidateVector(vector);
		}

		var rankings = queries.Select(query => new QueryRanking(query.Id, query.Group,
			query.Relevant, documents.Select(d => (d.Id, Score: documentVectors[d.Id]
				.Max(v => Cosine(queryVectors[query.Id], v))))
				.OrderByDescending(pair => pair.Score)
				.ThenBy(pair => pair.Id, StringComparer.Ordinal).Select(pair => pair.Id).ToArray())).ToArray();
		var recall = new double[5];
		var hit = new double[5];
		var precision = new double[5];
		foreach (var ranking in rankings)
		{
			for (var k = 1; k <= 5; k++)
			{
				var found = ranking.Relevant.Count(id => ranking.Rank(id) is int rank && rank <= k);
				recall[k - 1] += (double)found / ranking.Relevant.Count / rankings.Length;
				hit[k - 1] += (found > 0 ? 1d : 0d) / rankings.Length;
				precision[k - 1] += (double)found / k / rankings.Length;
			}
		}
		return new QualityResult
		{
			Rankings = rankings, Recall = recall, Hit = hit, Precision = precision,
			Mrr = rankings.Average(r => r.FirstRelevantRank is int rank ? 1d / rank : 0d)
		};
	}

	public static double Cosine(float[] a, float[] b)
	{
		if (a.Length == 0 || a.Length != b.Length)
			throw new InvalidDataException("Empty or incompatible cosine vectors.");
		double dot = 0, aa = 0, bb = 0;
		for (var i = 0; i < a.Length; i++)
		{
			dot += (double)a[i] * b[i];
			aa += (double)a[i] * a[i];
			bb += (double)b[i] * b[i];
		}
		var result = dot / Math.Sqrt(aa * bb);
		if (!double.IsFinite(result))
			throw new InvalidDataException("Invalid cosine score.");
		return result;
	}
}
