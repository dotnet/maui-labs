#if IOS || MACCATALYST

using Microsoft.Extensions.AI;
using NaturalLanguage;
using Xunit;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class NLEmbeddingGeneratorCancellationTests : EmbeddingGeneratorCancellationTestsBase<NLEmbeddingGenerator>
{
}

public class NLEmbeddingGeneratorConcurrencyTests : EmbeddingGeneratorConcurrencyTestsBase<NLEmbeddingGenerator>
{
}

public class NLEmbeddingGeneratorDisposalTests : EmbeddingGeneratorDisposalTestsBase<NLEmbeddingGenerator>
{
	// A caller-provided NLEmbedding is borrowed; the shared generator interface has no native-resource ownership contract.
	[Fact]
	public void Dispose_WithProvidedEmbedding_DoesNotDisposeEmbedding()
	{
		var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
		Assert.NotNull(embedding);

		var generator = new NLEmbeddingGenerator(embedding);
		generator.Dispose();

		// Embedding should still be usable
		var vector = embedding.GetVector("test");
		Assert.NotNull(vector);
	}

	// AsIEmbeddingGenerator adapts Apple's NLEmbedding, a native extension entry point unavailable on other providers.
	[Fact]
	public void AsIEmbeddingGenerator_CreatesGeneratorFromNLEmbedding()
	{
		var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
		Assert.NotNull(embedding);

		var generator = embedding.AsIEmbeddingGenerator();

		Assert.NotNull(generator);
		Assert.IsType<NLEmbeddingGenerator>(generator);
	}
}

public class NLEmbeddingGeneratorGenerateTests : EmbeddingGeneratorGenerateTestsBase<NLEmbeddingGenerator>
{
}

public class NLEmbeddingGeneratorGetServiceTests : EmbeddingGeneratorGetServiceTestsBase<NLEmbeddingGenerator>
{
	protected override string ExpectedProviderName => "apple";
	protected override string ExpectedDefaultModelId => "natural-language";

	// Only this adapter exposes NaturalLanguage.NLEmbedding through GetService; common service checks already live in the base.
	[Fact]
	public void GetService_ReturnsUnderlyingNLEmbedding()
	{
		IEmbeddingGenerator<string, Embedding<float>> generator = new NLEmbeddingGenerator();
		var embedding = generator.GetService<NLEmbedding>();

		Assert.NotNull(embedding);
	}
}

public class NLEmbeddingGeneratorInstantiationTests : EmbeddingGeneratorInstantiationTestsBase<NLEmbeddingGenerator>
{
	// Exercises the NLLanguage constructor rather than the shared parameterless generator constructor.
	[Fact]
	public void LanguageConstructor_WithEnglish_CreatesInstance()
	{
		var generator = new NLEmbeddingGenerator(NLLanguage.English);
		Assert.NotNull(generator);
	}

	// Exercises injection of an existing NaturalLanguage.NLEmbedding; other generators do not accept this native resource.
	[Fact]
	public void EmbeddingConstructor_WithValidEmbedding_CreatesInstance()
	{
		var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
		Assert.NotNull(embedding);

		var generator = new NLEmbeddingGenerator(embedding);
		Assert.NotNull(generator);
	}

	// Null validation belongs to the NLEmbedding-specific constructor, not the common generator API.
	[Fact]
	public void EmbeddingConstructor_WithNull_ThrowsArgumentNullException()
	{
		Assert.Throws<ArgumentNullException>(() => new NLEmbeddingGenerator((NLEmbedding)null!));
	}
}

public class NLEmbeddingGeneratorSimilarityTests : EmbeddingGeneratorSimilarityTestsBase<NLEmbeddingGenerator>
{
}

#endif
