namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Constants for xUnit test traits used across the test suite.
/// Use with <c>[Trait(TestTraits.RequiresModel, TestTraits.True)]</c>.
/// </summary>
public static class TestTraits
{
	public const string True = "true";

	/// <summary>
	/// Indicates the test requires a language model to be available on the device.
	/// Tests with this trait are excluded on CI runners that lack model support.
	/// </summary>
	public const string RequiresModel = "RequiresModel";
}
