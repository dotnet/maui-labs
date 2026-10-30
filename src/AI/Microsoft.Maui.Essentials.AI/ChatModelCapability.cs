namespace Microsoft.Maui.Essentials.AI;

/// <summary>A capability exposed by an Essentials.AI chat provider through Microsoft.Extensions.AI.</summary>
public enum ChatModelCapability
{
	/// <summary>The provider supports text generation.</summary>
	Text,

	/// <summary>The provider supports streaming response updates.</summary>
	Streaming,

	/// <summary>The provider supports image input.</summary>
	ImageInput,

	/// <summary>The provider supports tool calling.</summary>
	ToolCalling,

	/// <summary>The provider supports schema-constrained structured output.</summary>
	StructuredOutput,

	/// <summary>The provider supplies native token usage metadata.</summary>
	TokenUsage,

	/// <summary>The provider honors Microsoft.Extensions.AI reasoning options.</summary>
	Reasoning,
}
