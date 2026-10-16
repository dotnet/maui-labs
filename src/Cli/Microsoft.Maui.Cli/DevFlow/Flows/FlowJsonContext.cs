using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

[JsonSerializable(typeof(MauiFlow))]
internal sealed partial class FlowJsonContext : JsonSerializerContext;
