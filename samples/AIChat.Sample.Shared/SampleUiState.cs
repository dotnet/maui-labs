using System.Text.Json;
using AIChat.ClientServer.Sample.Shared;

namespace AIChat.Sample.Shared;

/// <summary>
/// Typed sample state rendered beside chat. This deliberately accepts only the small patch surface
/// emitted by the sample server; it is not a general JSON Pointer implementation.
/// </summary>
public sealed class SampleUiState
{
    public WeatherInfo? Weather { get; private set; }
    public Plan Plan { get; private set; } = new();
    public Recipe? Recipe { get; private set; }
    public DocumentState Document { get; private set; } = new();
    public DocumentProposal? PendingDocument { get; private set; }
    /// <summary>Gets whether a document proposal is awaiting an explicit decision.</summary>
    public bool HasPendingDocument => PendingDocument is not null;
    public event Action? Changed;

    public void Reset()
    {
        Weather = null;
        Plan = new Plan();
        Recipe = null;
        Document = new DocumentState();
        PendingDocument = null;
        Changed?.Invoke();
    }

    public bool TryApplySnapshot(JsonElement snapshot)
    {
        if (snapshot.ValueKind != JsonValueKind.Object)
            return false;

        var applied = false;
        if (snapshot.TryGetProperty("weather", out var weather))
        {
            Weather = weather.Deserialize(SampleSerializerContext.Default.WeatherInfo);
            applied = true;
        }
        if (snapshot.TryGetProperty("plan", out var plan))
        {
            Plan = plan.Deserialize(SampleSerializerContext.Default.Plan) ?? new Plan();
            applied = true;
        }
        if (snapshot.TryGetProperty("recipe", out var recipe))
        {
            Recipe = recipe.Deserialize(SampleSerializerContext.Default.Recipe);
            applied = true;
        }
        if (snapshot.TryGetProperty("proposal", out var proposal))
            return TryApplyDocumentProposal(proposal);
        if (snapshot.TryGetProperty("document", out var document))
        {
            Document = document.Deserialize(SampleSerializerContext.Default.DocumentState) ?? new DocumentState();
            applied = true;
        }

        // AG-UI result mappers may emit the bare result instead of a named state envelope.
        if (!applied && snapshot.TryGetProperty("steps", out _))
        {
            Plan = snapshot.Deserialize(SampleSerializerContext.Default.Plan) ?? new Plan();
            applied = true;
        }
        if (!applied && snapshot.TryGetProperty("ingredients", out _))
        {
            Recipe = snapshot.Deserialize(SampleSerializerContext.Default.Recipe);
            applied = true;
        }
        if (!applied && snapshot.TryGetProperty("temperature", out _))
        {
            Weather = snapshot.Deserialize(SampleSerializerContext.Default.WeatherInfo);
            applied = true;
        }
        if (applied)
            Changed?.Invoke();
        return applied;
    }

    public bool TryApplyDelta(JsonElement delta)
    {
        if (delta.ValueKind == JsonValueKind.Object)
            return TryApplyPatch(delta);

        if (delta.ValueKind != JsonValueKind.Array)
            return false;

        var applied = false;
        foreach (var operation in delta.EnumerateArray())
            applied |= TryApplyPatch(operation);
        return applied;
    }

    public bool TryApplyDocumentProposal(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return false;
        if (arguments.TryGetProperty("proposal", out var wrapped))
            arguments = wrapped;
        if (arguments.TryGetProperty("document", out var document))
            arguments = JsonSerializer.SerializeToElement(
                new DocumentProposal
                {
                    Document = document.Deserialize(SampleSerializerContext.Default.DocumentState) ?? new DocumentState(),
                },
                SampleSerializerContext.Default.DocumentProposal);
        else if (arguments.TryGetProperty("content", out _))
            arguments = JsonSerializer.SerializeToElement(
                new DocumentProposal
                {
                    Document = arguments.Deserialize(SampleSerializerContext.Default.DocumentState) ?? new DocumentState(),
                },
                SampleSerializerContext.Default.DocumentProposal);

        var proposal = arguments.Deserialize(SampleSerializerContext.Default.DocumentProposal);
        if (proposal is null)
            return false;

        PendingDocument = proposal;
        Changed?.Invoke();
        return true;
    }

    public void AcceptDocumentProposal()
    {
        if (PendingDocument is not null)
            Document = PendingDocument.Document;
        PendingDocument = null;
        Changed?.Invoke();
    }

    public void RejectDocumentProposal()
    {
        PendingDocument = null;
        Changed?.Invoke();
    }

    private bool TryApplyPatch(JsonElement operation)
    {
        if (operation.ValueKind != JsonValueKind.Object
            || !operation.TryGetProperty("op", out var op)
            || !operation.TryGetProperty("path", out var path)
            || !string.Equals(op.GetString(), "replace", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.GetString()?.Split('/', StringSplitOptions.None);
        if (segments is not ["", "steps", var indexText, "status"]
            || !int.TryParse(indexText, out var index)
            || index < 0
            || index >= Plan.Steps.Count
            || !operation.TryGetProperty("value", out var value)
            || !Enum.TryParse<PlanStepStatus>(value.GetString(), ignoreCase: true, out var status))
        {
            return false;
        }

        Plan.Steps[index].Status = status;
        Changed?.Invoke();
        return true;
    }
}
