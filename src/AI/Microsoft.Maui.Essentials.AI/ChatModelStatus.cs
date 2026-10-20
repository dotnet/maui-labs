namespace Microsoft.Maui.Essentials.AI;

/// <summary>An immutable model-status snapshot containing availability and effective provider capabilities.</summary>
/// <remarks>Snapshots use reference identity, not value equality.</remarks>
public sealed class ChatModelStatus
{
	internal ChatModelStatus(
		ChatModelAvailability availability,
		string? unavailableReason,
		IEnumerable<ChatModelCapability>? capabilities)
	{
		if (availability is ChatModelAvailability.Available && unavailableReason is not null)
			throw new ArgumentException("An available model cannot have an unavailable reason.", nameof(unavailableReason));
		if (unavailableReason is not null)
			ArgumentException.ThrowIfNullOrWhiteSpace(unavailableReason);
		Availability = availability;
		UnavailableReason = unavailableReason;
		Capabilities = capabilities is null ? null : Array.AsReadOnly(capabilities.ToArray());
	}

	/// <summary>The model's availability at the time of the snapshot, not a readiness guarantee.</summary>
	public ChatModelAvailability Availability { get; }

	/// <summary>
	/// The provider-native unavailable reason, or <see langword="null"/> when unavailable details are not
	/// known. This is always <see langword="null"/> when <see cref="Availability"/> is
	/// <see cref="ChatModelAvailability.Available"/>.
	/// </summary>
	public string? UnavailableReason { get; }

	/// <summary>
	/// Effective capabilities exposed by the provider, copied into a read-only collection.
	/// <see langword="null"/> means unknown or not advertised; an empty collection means known none.
	/// Capabilities do not guarantee current model availability.
	/// </summary>
	public IReadOnlyCollection<ChatModelCapability>? Capabilities { get; }
}
