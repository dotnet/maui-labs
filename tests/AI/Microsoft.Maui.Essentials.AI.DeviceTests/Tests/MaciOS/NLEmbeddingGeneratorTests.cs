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
	[Fact]
	public void Dispose_WithOwnedEmbedding_DisposesEmbedding()
	{
		// When using default constructor, generator owns the embedding
		var generator = new NLEmbeddingGenerator();
		generator.Dispose();
		// No exception means success
	}

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
	[Fact]
	public void LanguageConstructor_WithEnglish_CreatesInstance()
	{
		var generator = new NLEmbeddingGenerator(NLLanguage.English);
		Assert.NotNull(generator);
	}

	[Fact]
	public void EmbeddingConstructor_WithValidEmbedding_CreatesInstance()
	{
		var embedding = NLEmbedding.GetSentenceEmbedding(NLLanguage.English);
		Assert.NotNull(embedding);

		var generator = new NLEmbeddingGenerator(embedding);
		Assert.NotNull(generator);
	}

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
