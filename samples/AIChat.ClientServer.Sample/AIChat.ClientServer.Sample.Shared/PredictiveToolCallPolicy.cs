namespace AIChat.ClientServer.Sample.Shared;

/// <summary>Centralizes the no-parallel-call policy required by predictive proposal flows.</summary>
public static class PredictiveToolCallPolicy
{
    public static bool ForDirect(string scenarioId) =>
        !string.Equals(scenarioId, "predictive", StringComparison.Ordinal);

    public static bool ForAgui(string scenarioId) =>
        !string.Equals(scenarioId, ScenarioIds.PredictiveState, StringComparison.Ordinal);

    public static bool ForServerDocument() => false;
}
