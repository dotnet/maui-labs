#if IOS || MACCATALYST || MACOS
using Foundation;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

/// <summary>
/// Checks whether Apple Intelligence (FoundationModels) is available on the current device.
/// Performs a one-time probe by attempting a minimal model call.
/// </summary>
internal static class AppleIntelligenceAvailability
{
	private static bool? _isAvailable;

	/// <summary>
	/// Gets whether Apple Intelligence is available on this device.
	/// Returns false if the device doesn't support it or models aren't downloaded.
	/// </summary>
	public static bool IsAvailable
	{
		get
		{
			_isAvailable ??= CheckAvailability();
			return _isAvailable.Value;
		}
	}

	private static bool CheckAvailability()
	{
		try
		{
			// Try to instantiate the chat client and make a minimal call.
			// If Apple Intelligence isn't available, it throws immediately.
			var client = new AppleIntelligenceChatClient();
			var messages = new List<Microsoft.Extensions.AI.ChatMessage>
			{
				new(Microsoft.Extensions.AI.ChatRole.User, "test")
			};
			client.GetResponseAsync(messages).GetAwaiter().GetResult();
			return true;
		}
		catch (NSErrorException ex) when (ex.Message.Contains("does not support Apple Intelligence"))
		{
			return false;
		}
		catch
		{
			// Any other error means the API is at least reachable
			return true;
		}
	}

	/// <summary>
	/// Skips the current test if Apple Intelligence is not available.
	/// Uses xUnit v2's dynamic skip token mechanism.
	/// </summary>
	public static void SkipIfUnavailable()
	{
		if (!IsAvailable)
		{
			throw new Exception(
				"$XunitDynamicSkip$Apple Intelligence is not available on this device.");
		}
	}
}
#endif
