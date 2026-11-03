namespace Microsoft.Maui.Essentials.AI;

/// <summary>Describes the current availability of a chat model.</summary>
public enum ChatModelAvailability
{
	/// <summary>The model is ready for requests.</summary>
	/// <remarks>
	/// <para>Apple: <c>available</c>.</para>
	/// <para>Android: <c>AVAILABLE (3)</c>.</para>
	/// <para>Windows: <c>Ready</c>.</para>
	/// </remarks>
	Available,

	/// <summary>The model is temporarily not ready.</summary>
	/// <remarks>
	/// <para>Apple: <c>modelNotReady</c>.</para>
	/// <para>Android: <c>DOWNLOADABLE (1)</c>.</para>
	/// <para>Windows: <c>NotReady</c>.</para>
	/// </remarks>
	NotReady,

	/// <summary>The current device or system cannot support the model.</summary>
	/// <remarks>
	/// <para>Apple: <c>deviceNotEligible</c>.</para>
	/// <para>Android: not mapped; <c>UNAVAILABLE</c> does not distinguish unsupported devices from missing configuration.</para>
	/// <para>Windows: <c>NotSupportedOnCurrentSystem</c> or <c>NotCompatibleWithSystemHardware</c>.</para>
	/// </remarks>
	NotSupported,

	/// <summary>The model or its system feature is disabled.</summary>
	/// <remarks>
	/// <para>Apple: <c>appleIntelligenceNotEnabled</c>.</para>
	/// <para>Android: not mapped.</para>
	/// <para>Windows: <c>DisabledByUser</c>.</para>
	/// </remarks>
	Disabled,

	/// <summary>Application or system requirements have not been met.</summary>
	/// <remarks>
	/// <para>Apple: not mapped.</para>
	/// <para>Android: not mapped.</para>
	/// <para>Windows: <c>CapabilityMissing</c> or <c>OSUpdateNeeded</c>.</para>
	/// </remarks>
	RequirementsNotMet,

	/// <summary>The provider reported an unrecognized availability state.</summary>
	/// <remarks>
	/// <para>Apple: unrecognized availability or unavailable reason.</para>
	/// <para>Android: unrecognized <c>FeatureStatus</c> value.</para>
	/// <para>Windows: unrecognized <c>AIFeatureReadyState</c> value.</para>
	/// </remarks>
	Unknown,

	/// <summary>The provider reports that the model is unavailable. See the status reason for details.</summary>
	/// <remarks>
	/// <para>Apple: not mapped; known unavailable reasons use the more specific states above.</para>
	/// <para>Android: <c>UNAVAILABLE (0)</c>.</para>
	/// <para>Windows: not mapped; known reasons use the more specific states above.</para>
	/// </remarks>
	Unavailable,

	/// <summary>The model's assets are being downloaded.</summary>
	/// <remarks>
	/// <para>Apple: not mapped; downloading is not reported separately from <c>modelNotReady</c>.</para>
	/// <para>Android: <c>DOWNLOADING (2)</c>.</para>
	/// <para>Windows: not mapped; downloading is not reported separately from <c>NotReady</c>.</para>
	/// </remarks>
	Downloading,
}
