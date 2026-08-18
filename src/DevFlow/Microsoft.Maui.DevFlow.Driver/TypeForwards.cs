using System.Runtime.CompilerServices;
using Microsoft.Maui.DevFlow.Driver;

// The DevFlow wire protocol — AgentClient, the element/protocol DTOs and their serialization —
// moved to the portable Microsoft.Maui.DevFlow.Client assembly so .NET Framework harnesses can
// share it (dotnet/maui-labs#427). The namespace was deliberately left alone so source stays
// compatible; these forwards keep already-compiled consumers of Microsoft.Maui.DevFlow.Driver
// binding successfully as well.

[assembly: TypeForwardedTo(typeof(ActionResult))]
[assembly: TypeForwardedTo(typeof(AgentCapabilities))]
[assembly: TypeForwardedTo(typeof(AgentCapabilitiesResponse))]
[assembly: TypeForwardedTo(typeof(AgentClient))]
[assembly: TypeForwardedTo(typeof(AgentDescriptor))]
[assembly: TypeForwardedTo(typeof(AgentStatus))]
[assembly: TypeForwardedTo(typeof(AppDescriptor))]
[assembly: TypeForwardedTo(typeof(BoundsInfo))]
[assembly: TypeForwardedTo(typeof(DevFlowTheme))]
[assembly: TypeForwardedTo(typeof(DevFlowThemeJsonConverter))]
[assembly: TypeForwardedTo(typeof(DeviceDescriptor))]
[assembly: TypeForwardedTo(typeof(ElementInfo))]
[assembly: TypeForwardedTo(typeof(ElementNativeViewInfo))]
[assembly: TypeForwardedTo(typeof(ElementStateInfo))]
[assembly: TypeForwardedTo(typeof(ElementStyleInfo))]
[assembly: TypeForwardedTo(typeof(ExtensionDescriptor))]
[assembly: TypeForwardedTo(typeof(ExtensionsMarker))]
[assembly: TypeForwardedTo(typeof(ExtensionToolAnnotationsInfo))]
[assembly: TypeForwardedTo(typeof(ExtensionToolInfo))]
[assembly: TypeForwardedTo(typeof(InvokeResult))]
[assembly: TypeForwardedTo(typeof(NetworkRequest))]
[assembly: TypeForwardedTo(typeof(NotSupportedByAgentException))]
[assembly: TypeForwardedTo(typeof(ProfilerBatch))]
[assembly: TypeForwardedTo(typeof(ProfilerCapabilities))]
[assembly: TypeForwardedTo(typeof(ProfilerHotspot))]
[assembly: TypeForwardedTo(typeof(ProfilerMarker))]
[assembly: TypeForwardedTo(typeof(ProfilerSample))]
[assembly: TypeForwardedTo(typeof(ProfilerSessionInfo))]
[assembly: TypeForwardedTo(typeof(ProfilerSpan))]
[assembly: TypeForwardedTo(typeof(RecordingState))]
[assembly: TypeForwardedTo(typeof(RecordingStateManager))]
[assembly: TypeForwardedTo(typeof(ScreenshotResult))]
[assembly: TypeForwardedTo(typeof(ThemeExtensions))]
[assembly: TypeForwardedTo(typeof(ThemeResult))]
[assembly: TypeForwardedTo(typeof(ThemeSetScope))]
[assembly: TypeForwardedTo(typeof(ThemeSetScopeJsonConverter))]
[assembly: TypeForwardedTo(typeof(UiReadResult))]
