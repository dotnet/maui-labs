namespace Microsoft.Maui.Essentials.AI;

/// <summary>Describes the current availability of a chat model.</summary>
public enum ChatModelAvailability
{
	/// <summary>The model is ready for requests.</summary>
	Available,

	/// <summary>The model is temporarily not ready.</summary>
	NotReady,

	/// <summary>The current device or system cannot support the model.</summary>
	NotSupported,

	/// <summary>The model or its system feature is disabled.</summary>
	Disabled,

	/// <summary>Application or system requirements have not been met.</summary>
	RequirementsNotMet,

	/// <summary>The provider reported an unavailable state with no known shared mapping.</summary>
	Unknown,
}
