using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI;

internal static class FoundationModelsReasoningOptions
{
	internal static string? GetLevel(ReasoningOptions? options, bool isCoreAI)
	{
		// Unsupported system controls must not change the OS26/default system request.
		if (!isCoreAI || options is null)
			return null;

		if (options.Output == ReasoningOutput.Summary)
			throw new NotSupportedException("Core AI supplies full reasoning, not summaries. Use ReasoningOutput.Full or None.");
		if (options.Output is not null && options.Output != ReasoningOutput.Full && options.Output != ReasoningOutput.None)
			throw new NotSupportedException($"Unsupported Core AI reasoning output: {options.Output}.");

		var effort = options.Effort;
		if (effort is null) return null;
		if (effort == ReasoningEffort.None) return "none";
		if (effort == ReasoningEffort.Low) return "light";
		if (effort == ReasoningEffort.Medium) return "moderate";
		if (effort == ReasoningEffort.High || effort == ReasoningEffort.ExtraHigh) return "deep";
		throw new NotSupportedException($"Unsupported Core AI reasoning effort: {effort}.");
	}
}
