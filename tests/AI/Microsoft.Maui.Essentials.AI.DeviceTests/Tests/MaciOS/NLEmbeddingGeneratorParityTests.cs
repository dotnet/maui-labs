#if IOS || MACCATALYST

using Microsoft.Extensions.AI;
using NaturalLanguage;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class NLEmbeddingGeneratorParityTests(ITestOutputHelper output)
{
	[Theory]
	[InlineData("The bicycle tire needs a patch.")]
	[InlineData("HTTP 429 means the server is limiting requests.")]
	public async Task SentenceGenerator_RepresentativeInput_EqualsNativeVector(string text)
	{
		using var generator = new NLEmbeddingGenerator();
		IEmbeddingGenerator<string, Embedding<float>> service = generator;
		var native = service.GetService<NLEmbedding>();
		Assert.NotNull(native);
		var expected = native.GetVector(text);
		Assert.NotNull(expected);

		var actual = (await generator.GenerateAsync([text]))[0].Vector.ToArray();
		Assert.Equal(expected, actual);
		AssertValidVector(actual, native.Dimension);
		output.WriteLine($"sentence language={native.Language} revision={native.Revision} dimension={native.Dimension}");
	}

	[Fact]
	public async Task SentenceGenerator_LongInput_EqualsNativeVector()
	{
		var text = string.Join(" ", Enumerable.Repeat(
			"The office keeps routine records in a cabinet near the door.", 80))
			+ " The greenhouse irrigation valve needs a replacement gasket.";
		using var generator = new NLEmbeddingGenerator(NLLanguage.English);
		var native = ((IEmbeddingGenerator<string, Embedding<float>>)generator).GetService<NLEmbedding>();
		Assert.NotNull(native);
		var expected = native.GetVector(text);
		Assert.NotNull(expected);
		var actual = (await generator.GenerateAsync([text]))[0].Vector.ToArray();

		Assert.Equal(expected, actual);
		AssertValidVector(actual, native.Dimension);
		output.WriteLine($"long sentence UTF16={text.Length} revision={native.Revision} dimension={native.Dimension}");
	}

	[Fact]
	public async Task CustomWordModel_ValidAndMissingInputs_TrackNativeVector()
	{
		using var native = NLEmbedding.GetWordEmbedding(NLLanguage.English);
		Assert.NotNull(native);
		using var generator = new NLEmbeddingGenerator(native);
		var service = (IEmbeddingGenerator<string, Embedding<float>>)generator;
		Assert.Same(native, service.GetService<NLEmbedding>());
		var metadata = service.GetService<EmbeddingGeneratorMetadata>();
		Assert.NotNull(metadata);
		Assert.Equal("apple", metadata.ProviderName);
		Assert.Equal("natural-language", metadata.DefaultModelId);
		output.WriteLine($"word language={native.Language} revision={native.Revision} dimension={native.Dimension}");

		foreach (var text in new[] { "coffee", "coffee coffee", "unknownblorfquizzlet" })
		{
			var expected = native.GetVector(text);
			var actual = (await generator.GenerateAsync([text]))[0].Vector.ToArray();
			output.WriteLine($"word input={text}, nativeNil={expected is null}, vectorLength={actual.Length}");
			if (expected is null)
				Assert.Empty(actual); // Current contract: nil is represented as an empty vector.
			else
			{
				Assert.Equal(expected, actual);
				AssertValidVector(actual, native.Dimension);
			}
		}
	}

	[Fact]
	public async Task SentenceGenerator_RepeatedInput_ProducesIdenticalFiniteNonzeroVectors()
	{
		using var generator = new NLEmbeddingGenerator();
		var native = ((IEmbeddingGenerator<string, Embedding<float>>)generator).GetService<NLEmbedding>();
		Assert.NotNull(native);
		var vectors = await generator.GenerateAsync(["Find the unpaid invoice.", "Find the unpaid invoice."]);
		var first = vectors[0].Vector.ToArray();
		var second = vectors[1].Vector.ToArray();
		AssertValidVector(first, native.Dimension);
		Assert.Equal(first, second);
	}

	private static void AssertValidVector(float[] vector, nuint dimension)
	{
		Assert.Equal((int)dimension, vector.Length);
		Assert.All(vector, component => Assert.True(float.IsFinite(component)));
		Assert.Contains(vector, component => component != 0);
	}
}

#endif
