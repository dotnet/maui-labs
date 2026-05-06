namespace CometBaristaNotes.Services;

public class MockVisionService : IVisionService
{
	public bool IsAvailable => true;

	public async Task<VisionAnalysisResult> AnalyzeImage(string imagePath)
	{
		await Task.Delay(1500);

		return new VisionAnalysisResult
		{
			Description = "Image analysis complete. The photo appears to show a coffee-related setup "
				+ "with equipment and accessories arranged on a counter surface.",
			SuggestedActions = new[]
			{
				"Tag this image with your current shot record",
				"Add a note about the setup configuration",
				"Compare with previous setup photos"
			},
			Confidence = 0.85
		};
	}

	public async Task<VisionAnalysisResult> AnalyzeCoffeeSetup(string imagePath)
	{
		await Task.Delay(1500);

		return new VisionAnalysisResult
		{
			Description = "Detected: espresso machine, portafilter, grinder, and tamper. "
				+ "The setup appears well-organized with the grinder positioned for efficient workflow. "
				+ "Puck screen visible — good practice for even water distribution.",
			SuggestedActions = new[]
			{
				"Ensure the grinder is calibrated for your current beans",
				"Check portafilter basket for wear or buildup",
				"Consider adding a dosing funnel to reduce mess",
				"Clean the group head before your next session"
			},
			Confidence = 0.92
		};
	}
}
