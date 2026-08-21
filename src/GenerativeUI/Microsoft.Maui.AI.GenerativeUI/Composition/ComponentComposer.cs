using System.Text.Json.Nodes;
using Microsoft.Maui.AI.GenerativeUI.Binding;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public enum CompositionPlanSource
{
    Model,
    Corrected,
    Fallback,
}

public sealed record ComponentCompositionRequest(
    string Intent,
    string Scaffold,
    string DataContract,
    string DataPath,
    string Title);

public sealed record ComponentCompositionResult(
    CompositionPlan Plan,
    CompositionPlanSource Source,
    int CorrectionCount,
    CompositionValidationResult Validation,
    TimeSpan ModelLatency,
    long? InputTokens,
    long? OutputTokens,
    CompositionValidationResult? RejectedModelValidation = null);

public sealed class ComponentComposer(
    IComponentPlanGenerator generator,
    ComponentCandidateResolver candidateResolver,
    CompositionPlanValidator validator,
    CompositionSessionState session,
    IEnumerable<ICompositionFallbackPlanFactory> fallbackFactories)
{
    public async Task<ComponentCompositionResult> ComposeAsync(
        ComponentCompositionRequest request,
        UiObject stateRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stateRoot);

        var data = UiObjectPath.ResolveDotted(stateRoot, request.DataPath)
            ?? throw new InvalidOperationException($"Composition dataPath '{request.DataPath}' was not found.");
        var candidates = candidateResolver.Resolve(stateRoot, request.DataContract, request.DataPath);
        if (candidates.Count == 0)
            throw new InvalidOperationException("No registered components are compatible with the active data.");

        var currentPlan = session.CurrentPlan is { } current &&
                          string.Equals(current.Scaffold, request.Scaffold, StringComparison.OrdinalIgnoreCase)
            ? current
            : null;
        var planId = currentPlan?.PlanId ?? $"composition-{Guid.NewGuid():N}";
        var revision = currentPlan?.Revision + 1 ?? 1;
        var generationRequest = new CompositionPlanGenerationRequest(
            request.Intent,
            request.Scaffold,
            request.DataContract,
            request.DataPath,
            request.Title,
            planId,
            revision,
            UiObjectBuilder.ToJson(data),
            candidates,
            currentPlan);

        var first = await generator.GenerateAsync(generationRequest, cancellationToken).ConfigureAwait(false);
        var firstValidation = Validate(first.Plan, request.Scaffold, candidates, currentPlan, planId, revision);
        if (first.Plan is not null && firstValidation.IsValid)
        {
            return new ComponentCompositionResult(
                first.Plan,
                CompositionPlanSource.Model,
                CorrectionCount: 0,
                firstValidation,
                first.Latency,
                first.InputTokens,
                first.OutputTokens);
        }

        var correction = CompositionValidationErrorFormatter.Format(firstValidation);
        var retryRequest = generationRequest with
        {
            InvalidPlan = first.Plan,
            CorrectionErrors = correction,
        };
        var retry = await generator.GenerateAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        var retryValidation = Validate(retry.Plan, request.Scaffold, candidates, currentPlan, planId, revision);
        if (retry.Plan is not null && retryValidation.IsValid)
        {
            return new ComponentCompositionResult(
                retry.Plan,
                CompositionPlanSource.Corrected,
                CorrectionCount: 1,
                retryValidation,
                first.Latency + retry.Latency,
                Sum(first.InputTokens, retry.InputTokens),
                Sum(first.OutputTokens, retry.OutputTokens),
                firstValidation);
        }

        var fallbackFactory = fallbackFactories.FirstOrDefault(factory =>
            string.Equals(factory.Scaffold, request.Scaffold, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"No deterministic fallback is registered for scaffold '{request.Scaffold}'.");
        var fallback = fallbackFactory.CreateFallback(new(
            request.Scaffold,
            request.DataPath,
            request.Title,
            planId,
            revision,
            currentPlan));
        var fallbackValidation = validator.Validate(
            fallback,
            request.Scaffold,
            candidates,
            currentPlan,
            currentPlan is null ? planId : null,
            currentPlan is null ? revision : null);
        if (!fallbackValidation.IsValid)
        {
            throw new InvalidOperationException(
                $"Deterministic composition fallback is invalid: {CompositionValidationErrorFormatter.Format(fallbackValidation)}");
        }

        return new ComponentCompositionResult(
            fallback,
            CompositionPlanSource.Fallback,
            CorrectionCount: 1,
            fallbackValidation,
            first.Latency + retry.Latency,
            Sum(first.InputTokens, retry.InputTokens),
            Sum(first.OutputTokens, retry.OutputTokens),
            retryValidation);
    }

    private static long? Sum(long? first, long? second)
        => first is null && second is null
            ? null
            : first.GetValueOrDefault() + second.GetValueOrDefault();

    private CompositionValidationResult Validate(
        CompositionPlan? plan,
        string scaffold,
        IReadOnlyList<ResolvedComponentCandidate> candidates,
        CompositionPlan? currentPlan,
        string planId,
        int revision)
    {
        if (plan is null)
        {
            return new(
            [
                new(
                    "missing_model_plan",
                    "$",
                    "The model did not return a composition plan."),
            ]);
        }

        return validator.Validate(
            plan,
            scaffold,
            candidates,
            currentPlan,
            currentPlan is null ? planId : null,
            currentPlan is null ? revision : null);
    }
}
