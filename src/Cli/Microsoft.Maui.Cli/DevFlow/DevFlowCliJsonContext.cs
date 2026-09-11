using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Flows;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(CommandDescription))]
[JsonSerializable(typeof(List<CommandDescription>))]
[JsonSerializable(typeof(AgentStatus))]
[JsonSerializable(typeof(ElementInfo))]
[JsonSerializable(typeof(List<ElementInfo>))]
[JsonSerializable(typeof(NetworkRequest))]
[JsonSerializable(typeof(List<NetworkRequest>))]
[JsonSerializable(typeof(ThemeResult))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(List<AgentRegistration>))]
[JsonSerializable(typeof(AgentRegistration[]))]
[JsonSerializable(typeof(BrokerState))]
[JsonSerializable(typeof(RegistrationMessage))]
[JsonSerializable(typeof(AndroidDevFlowForwardingReport))]
[JsonSerializable(typeof(AndroidDevFlowDevice[]))]
[JsonSerializable(typeof(AndroidDevFlowPortForward[]))]
[JsonSerializable(typeof(ExtensionDescriptor))]
[JsonSerializable(typeof(ExtensionToolInfo))]
[JsonSerializable(typeof(ExtensionToolAnnotationsInfo))]
[JsonSerializable(typeof(Dictionary<string, ExtensionDescriptor>))]
[JsonSerializable(typeof(LayoutInspectionResult))]
[JsonSerializable(typeof(LayoutFinding))]
[JsonSerializable(typeof(CompactLayoutDiagnosticsResult))]
[JsonSerializable(typeof(LayoutDiagnosticsPolicy))]
[JsonSerializable(typeof(LayoutDiagnosticsDelta))]
[JsonSerializable(typeof(InspectorServer.InspectorDiagnosticRequest))]
[JsonSerializable(typeof(FlowObservation))]
[JsonSerializable(typeof(BrokerFlowResult))]
[JsonSerializable(typeof(MutationRecordingStatus))]
[JsonSerializable(typeof(MauiFlow))]
[JsonSerializable(typeof(FlowReplayReport))]
[JsonSerializable(typeof(List<FlowAssert>))]
[JsonSerializable(typeof(FlowTools.FlowFileSummary))]
[JsonSerializable(typeof(List<FlowTools.FlowFileSummary>))]
[JsonSerializable(typeof(FlowRecordTools.ActiveRecordingSummary))]
[JsonSerializable(typeof(List<FlowRecordTools.ActiveRecordingSummary>))]
[JsonSerializable(typeof(InspectorAlertResult))]
[JsonSerializable(typeof(AlertInfo))]
internal sealed partial class DevFlowCliJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(MauiFlow))]
[JsonSerializable(typeof(FlowReplayReport))]
[JsonSerializable(typeof(InspectorAlertResult))]
[JsonSerializable(typeof(NetworkRequest))]
[JsonSerializable(typeof(List<NetworkRequest>))]
internal sealed partial class DevFlowCliJsonPreserveNullContext : JsonSerializerContext;
