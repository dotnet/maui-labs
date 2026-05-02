#if ENABLE_OPENAI_CLIENT

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public class OpenAIEmbeddingGenerator : DelegatingEmbeddingGenerator<string, Embedding<float>>
{
	public OpenAIEmbeddingGenerator()
		: base(IPlatformApplication.Current!.Services.GetRequiredService<OpenAI.Embeddings.EmbeddingClient>().AsIEmbeddingGenerator())
	{
	}
}
public class OpenAIEmbeddingGeneratorCancellationTests : EmbeddingGeneratorCancellationTestsBase<OpenAIEmbeddingGenerator>
{
}
public class OpenAIEmbeddingGeneratorConcurrencyTests : EmbeddingGeneratorConcurrencyTestsBase<OpenAIEmbeddingGenerator>
{
}
public class OpenAIEmbeddingGeneratorDisposalTests : EmbeddingGeneratorDisposalTestsBase<OpenAIEmbeddingGenerator>
{
}
public class OpenAIEmbeddingGeneratorGenerateTests : EmbeddingGeneratorGenerateTestsBase<OpenAIEmbeddingGenerator>
{
}
public class OpenAIEmbeddingGeneratorGetServiceTests : EmbeddingGeneratorGetServiceTestsBase<OpenAIEmbeddingGenerator>
{
	protected override string ExpectedProviderName => "openai";
	protected override string ExpectedDefaultModelId => "text-embedding-3-small";
}
public class OpenAIEmbeddingGeneratorInstantiationTests : EmbeddingGeneratorInstantiationTestsBase<OpenAIEmbeddingGenerator>
{
}
public class OpenAIEmbeddingGeneratorSimilarityTests : EmbeddingGeneratorSimilarityTestsBase<OpenAIEmbeddingGenerator>
{
}

#endif
