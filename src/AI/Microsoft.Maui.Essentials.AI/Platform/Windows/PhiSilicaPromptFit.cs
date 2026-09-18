using System.Runtime.Versioning;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Describes how much of a prompt fits in the model's context window.
/// </summary>
/// <param name="PromptLength">The length, in characters, of the flattened prompt.</param>
/// <param name="UsableLength">
/// The number of leading characters of the prompt that fit in the remaining context window.
/// </param>
[SupportedOSPlatform("windows10.0.26100.0")]
public readonly record struct PhiSilicaPromptFit(long PromptLength, long UsableLength)
{
	/// <summary>Whether the whole prompt fits in the remaining context window.</summary>
	public bool Fits => UsableLength >= PromptLength;

	/// <summary>The number of characters that do not fit.</summary>
	public long OverflowLength => Math.Max(0, PromptLength - UsableLength);
}
