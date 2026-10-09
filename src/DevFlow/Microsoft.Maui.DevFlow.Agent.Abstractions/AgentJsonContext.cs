using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Agent.Core.Profiling;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    internal static JsonSerializerContext JsonContext => AgentJsonContext.Default;
    internal static JsonSerializerContext CollectionJsonContext => AgentCollectionJsonContext.Default;

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(byte))]
    [JsonSerializable(typeof(sbyte))]
    [JsonSerializable(typeof(short))]
    [JsonSerializable(typeof(ushort))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(uint))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(ulong))]
    [JsonSerializable(typeof(float))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(decimal))]
    [JsonSerializable(typeof(char))]
    [JsonSerializable(typeof(Half))]
    [JsonSerializable(typeof(Int128))]
    [JsonSerializable(typeof(UInt128))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(DateOnly))]
    [JsonSerializable(typeof(TimeOnly))]
    [JsonSerializable(typeof(TimeSpan))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(Uri))]
    [JsonSerializable(typeof(Version))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(JsonDocument))]
    [JsonSerializable(typeof(JsonNode))]
    [JsonSerializable(typeof(JsonObject))]
    [JsonSerializable(typeof(JsonArray))]
    [JsonSerializable(typeof(JsonValue))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    [JsonSerializable(typeof(object[]))]
    [JsonSerializable(typeof(ElementInfo))]
    [JsonSerializable(typeof(FileLogEntry))]
    [JsonSerializable(typeof(NetworkRequestEntry))]
    [JsonSerializable(typeof(ProfilerSessionInfo))]
    [JsonSerializable(typeof(ProfilerSample))]
    [JsonSerializable(typeof(ProfilerMarker))]
    [JsonSerializable(typeof(ProfilerBatch))]
    [JsonSerializable(typeof(ProfilerSpan))]
    [JsonSerializable(typeof(ProfilerHotspot))]
    [JsonSerializable(typeof(StartProfilerRequest))]
    [JsonSerializable(typeof(PublishProfilerMarkerRequest))]
    [JsonSerializable(typeof(PublishProfilerSpanRequest))]
    [JsonSerializable(typeof(ProfilerCapabilities))]
    [JsonSerializable(typeof(MutationLeaseRequest))]
    [JsonSerializable(typeof(MutationLeaseStatus))]
    [JsonSerializable(typeof(MutationRecordingRequest))]
    [JsonSerializable(typeof(MutationRecordingStatus))]
    [JsonSerializable(typeof(MutationObservation))]
    [JsonSerializable(typeof(BrokerRegistration.RegistrationResponse))]
    [JsonSerializable(typeof(CaptureBoundRequest))]
    [JsonSerializable(typeof(ActionRequest))]
    [JsonSerializable(typeof(FillRequest))]
    [JsonSerializable(typeof(NavigateRequest))]
    [JsonSerializable(typeof(ResizeRequest))]
    [JsonSerializable(typeof(ScrollRequest))]
    [JsonSerializable(typeof(KeyActionRequest))]
    [JsonSerializable(typeof(GestureActionRequest))]
    [JsonSerializable(typeof(BatchRequest))]
    [JsonSerializable(typeof(SetPropertyRequest))]
    [JsonSerializable(typeof(InvokeActionRequest))]
    [JsonSerializable(typeof(WebViewDomQueryRequest))]
    [JsonSerializable(typeof(WebViewNavigateRequest))]
    [JsonSerializable(typeof(WebViewInputClickRequest))]
    [JsonSerializable(typeof(WebViewInputFillRequest))]
    [JsonSerializable(typeof(WebViewInputTextRequest))]
    [JsonSerializable(typeof(PreferenceSetRequest))]
    [JsonSerializable(typeof(ThemeSetRequest))]
    [JsonSerializable(typeof(SecureStorageSetRequest))]
    [JsonSerializable(typeof(FileUploadRequest))]
    [JsonSerializable(typeof(JobRunRequest))]
    private partial class AgentJsonContext : JsonSerializerContext;

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(ReadOnlyDictionary<string, object>))]
    [JsonSerializable(typeof(IEnumerable))]
    [JsonSerializable(typeof(IDictionary))]
    [JsonSerializable(typeof(IDictionary<string, object>))]
    [JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
    private partial class AgentCollectionJsonContext : JsonSerializerContext;
}
