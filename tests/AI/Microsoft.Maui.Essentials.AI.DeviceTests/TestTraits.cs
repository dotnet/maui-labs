namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Constants for xUnit test traits used across the test suite.
/// Use with <c>[Trait(TestTraits.RequiresModel, TestTraits.True)]</c>.
/// </summary>
public static class TestTraits
{
	public const string True = "true";

	/// <summary>
	/// Indicates the test requires an AI model, either on the device or at a configured cloud endpoint.
	/// Tests with this trait are excluded on CI runners that lack the required model support.
	/// </summary>
	public const string RequiresModel = "RequiresModel";
}
