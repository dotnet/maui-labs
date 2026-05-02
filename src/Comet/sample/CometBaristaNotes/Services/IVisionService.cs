namespace CometBaristaNotes.Services;

public class VisionAnalysisResult
{
	public string Description { get; set; } = "";
	public string[] SuggestedActions { get; set; } = Array.Empty<string>();
	public double Confidence { get; set; }
}

public interface IVisionService
{
	Task<VisionAnalysisResult> AnalyzeImage(string imagePath);
	Task<VisionAnalysisResult> AnalyzeCoffeeSetup(string imagePath);
	bool IsAvailable { get; }
}
