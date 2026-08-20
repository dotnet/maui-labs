using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class MutationRecordingRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "status";
    [JsonPropertyName("leaseId")]
    public string? LeaseId { get; set; }
    [JsonPropertyName("recordingId")]
    public string? RecordingId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("app")]
    public string? App { get; set; }
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
    [JsonPropertyName("preconditions")]
    public string? Preconditions { get; set; }
    [JsonPropertyName("observation")]
    public MutationObservation? Observation { get; set; }
}

internal sealed class MutationRecordingStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }
    [JsonPropertyName("recording")]
    public bool Recording { get; set; }
    [JsonPropertyName("recordingId")]
    public string? RecordingId { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("steps")]
    public int Steps { get; set; }
    [JsonPropertyName("seq")]
    public int? Seq { get; set; }
    [JsonPropertyName("fragile")]
    public bool Fragile { get; set; }
    [JsonPropertyName("empty")]
    public bool Empty { get; set; }
    [JsonPropertyName("markdown")]
    public string? Markdown { get; set; }
    [JsonPropertyName("warnings")]
    public string[]? Warnings { get; set; }
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

internal sealed class MutationObservation
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
    [JsonPropertyName("automationId")]
    public string? AutomationId { get; set; }
    [JsonPropertyName("text")]
    public string? Text { get; set; }
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    [JsonPropertyName("index")]
    public int? Index { get; set; }
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    [JsonPropertyName("value")]
    public string? Value { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("dx")]
    public double? Dx { get; set; }
    [JsonPropertyName("dy")]
    public double? Dy { get; set; }
    [JsonPropertyName("itemIndex")]
    public int? ItemIndex { get; set; }
    [JsonPropertyName("position")]
    public string? Position { get; set; }
    [JsonPropertyName("page")]
    public string? Page { get; set; }
    [JsonPropertyName("navigated")]
    public bool Navigated { get; set; }
    [JsonPropertyName("assertsJson")]
    public string? AssertsJson { get; set; }
}

internal sealed class MutationRecordingTracker
{
    private readonly object _gate = new();
    private bool _active;
    private string? _recordingId;

    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    public string? RecordingId
    {
        get { lock (_gate) return _recordingId; }
    }

    public void Update(MutationRecordingStatus? status)
    {
        if (status?.Ok != true)
            return;
        lock (_gate)
        {
            _active = status.Recording;
            _recordingId = status.Recording ? status.RecordingId : null;
        }
    }
}
