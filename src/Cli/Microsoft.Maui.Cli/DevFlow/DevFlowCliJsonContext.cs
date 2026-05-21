using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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
internal sealed partial class DevFlowCliJsonContext : JsonSerializerContext;
