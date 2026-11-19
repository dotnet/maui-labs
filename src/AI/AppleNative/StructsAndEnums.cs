using System;
using ObjCRuntime;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleIntelligenceModelAvailabilityNative
{
	public const string Available = "available";
	public const string DeviceNotEligible = "deviceNotEligible";
	public const string AppleIntelligenceNotEnabled = "appleIntelligenceNotEnabled";
	public const string ModelNotReady = "modelNotReady";
}

[Flags]
internal enum AppleIntelligenceModelCapabilitiesNative : ulong
{
	None = 0,
	Vision = 1UL << 0,
	GuidedGeneration = 1UL << 1,
	Reasoning = 1UL << 2,
	ToolCalling = 1UL << 3,
}

[Native]
internal enum ToolCallingModeNative : long
{
	Allowed = 0,
	Disallowed = 1,
	Required = 2
}

[Native]
internal enum ChatClientError : long
{
	EmptyMessages = 1,
	InvalidRole = 2,
	InvalidContent = 3,
	Cancelled = 4
}

[Native]
internal enum ChatRoleNative : long
{
	User = 1,
	Assistant = 2,
	System = 3,
	Tool = 4,
}

[Native]
internal enum ResponseUpdateTypeNative : long
{
	Content = 0,
	ToolCall = 1,
	ToolResult = 2
}
