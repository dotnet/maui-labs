using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed class MutationLeaseStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("youHold")]
    public bool YouHold { get; set; }

    [JsonPropertyName("heldByOther")]
    public bool HeldByOther { get; set; }

    [JsonPropertyName("leaseId")]
    public string? LeaseId { get; set; }

    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("holderKind")]
    public string? HolderKind { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("expiresInMs")]
    public long ExpiresInMs { get; set; }

    [JsonPropertyName("authorityEpoch")]
    public long AuthorityEpoch { get; set; }

    [JsonPropertyName("authority")]
    public string? Authority { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class MutationLeaseException : InvalidOperationException
{
    public MutationLeaseException(MutationLeaseStatus status)
        : base(status.HeldByOther
            ? $"Another DevFlow session is driving this app ({status.Label ?? status.HolderKind ?? "unknown holder"})."
            : status.Error ?? "Could not acquire the DevFlow mutation lease.")
    {
        Status = status;
    }

    public MutationLeaseStatus Status { get; }
}

public sealed class MutationRecordingObservation
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
    [JsonPropertyName("sensitive")]
    public bool Sensitive { get; set; }
    /// <summary>
    /// Value-free structural facts observed with the mutation. Older agents omit this field.
    /// </summary>
    [JsonPropertyName("selectorObservation")]
    public MauiSelectorObservation? SelectorObservation { get; set; }
}

public sealed class MutationRecordingStatus
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
