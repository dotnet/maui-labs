using System.Text.Json.Serialization;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.Models;
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
[JsonSerializable(typeof(DiagnoseResult))]
[JsonSerializable(typeof(DiagnoseCheckResult))]
[JsonSerializable(typeof(IReadOnlyList<DiagnoseCheckResult>))]
[JsonSerializable(typeof(List<DiagnoseCheckResult>))]
[JsonSerializable(typeof(RemediationResult))]
internal sealed partial class DevFlowCliJsonContext : JsonSerializerContext;
