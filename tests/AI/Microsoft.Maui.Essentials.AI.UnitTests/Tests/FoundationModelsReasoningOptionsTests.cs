using Microsoft.Extensions.AI;

namespace Microsoft.Maui.Essentials.AI.UnitTests;

public class FoundationModelsReasoningOptionsTests
{
	[Fact]
	public void CoreAI_StandardEfforts_MapOnlyActualNativeLevels()
	{
		Assert.Null(FoundationModelsReasoningOptions.GetLevel(null, true));
		Assert.Null(FoundationModelsReasoningOptions.GetLevel(new() { Output = ReasoningOutput.Full }, true));
		foreach (var (effort, level) in new[]
		{
			(ReasoningEffort.None, "none"), (ReasoningEffort.Low, "light"),
			(ReasoningEffort.Medium, "moderate"), (ReasoningEffort.High, "deep"),
			(ReasoningEffort.ExtraHigh, "deep"),
		})
			Assert.Equal(level, FoundationModelsReasoningOptions.GetLevel(new() { Effort = effort }, true));
	}

	[Fact]
	public void CoreAI_OutputNone_DoesNotDisableComputation()
	{
		Assert.Null(FoundationModelsReasoningOptions.GetLevel(new() { Output = ReasoningOutput.None }, true));
		Assert.Equal("moderate", FoundationModelsReasoningOptions.GetLevel(
			new() { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.None }, true));
	}

	[Fact]
	public void CoreAI_Summary_IsExplicitlyUnsupported()
	{
		Assert.Throws<NotSupportedException>(() =>
			FoundationModelsReasoningOptions.GetLevel(new() { Output = ReasoningOutput.Summary }, true));
	}

	[Fact]
	public void System_UnsupportedReasoningControls_PreserveProviderDefault()
	{
		Assert.Null(FoundationModelsReasoningOptions.GetLevel(
			new() { Effort = ReasoningEffort.High, Output = ReasoningOutput.Summary }, false));
	}
}
