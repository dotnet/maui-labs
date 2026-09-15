using System.Text.Json.Serialization;

namespace AIChat.ClientServer.Sample.Shared;

[JsonSerializable(typeof(WeatherInfo))]
[JsonSerializable(typeof(Plan))]
[JsonSerializable(typeof(PlanStep))]
[JsonSerializable(typeof(PlanStepStatus))]
[JsonSerializable(typeof(PlanStepStatus?))]
[JsonSerializable(typeof(JsonPatchOperation))]
[JsonSerializable(typeof(List<JsonPatchOperation>))]
[JsonSerializable(typeof(Recipe))]
[JsonSerializable(typeof(Ingredient))]
[JsonSerializable(typeof(RecipeResponse))]
[JsonSerializable(typeof(DocumentState))]
[JsonSerializable(typeof(ClientAgentState))]
[JsonSerializable(typeof(DocumentProposal))]
[JsonSerializable(typeof(DocumentProposalSnapshot))]
[JsonSerializable(typeof(ScenarioDescriptor))]
[JsonSerializable(typeof(List<ScenarioDescriptor>))]
[JsonSerializable(typeof(ClientToolDeclaration))]
[JsonSerializable(typeof(List<ClientToolDeclaration>))]
public sealed partial class SampleSerializerContext : JsonSerializerContext;
