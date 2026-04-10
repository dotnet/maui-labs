using System.Text.Json.Serialization;
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
[JsonSerializable(typeof(AgentRegistration))]
[JsonSerializable(typeof(List<AgentRegistration>))]
[JsonSerializable(typeof(AgentRegistration[]))]
[JsonSerializable(typeof(BrokerState))]
[JsonSerializable(typeof(RegistrationMessage))]
internal sealed partial class DevFlowCliJsonContext : JsonSerializerContext;
