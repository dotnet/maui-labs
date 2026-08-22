namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// Generates, validates, corrects once, caches, and safely falls back to the standard layout.
/// </summary>
public sealed class AdaptiveSurfaceComposer(
    IAdaptiveLayoutGenerator generator,
    ComponentLayoutValidator validator,
    IAdaptiveLayoutCache cache)
{
    public async Task<AdaptiveCompositionResult> ComposeAsync(
        AdaptiveSurfaceContext context,
        AdaptiveSurfaceSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);
        session.ThrowIfDisposed();
        if (session.IsSuspended)
            throw new InvalidOperationException("A suspended adaptive surface cannot compose a layout.");

        var generation = session.BeginGeneration();
        var current = session.CurrentLayout;
        var expectedLayoutId = current?.LayoutId ?? $"{context.SurfaceInstanceId}-layout";
        var expectedRevision = current?.Revision + 1 ?? 1;
        var cacheKey = AdaptiveLayoutCacheKey.Create(context);
        if ((current is null || session.IsStandardLayout) && cache.TryGet(cacheKey, out var cached))
        {
            var normalizedCached = cached with
            {
                LayoutId = expectedLayoutId,
                Revision = expectedRevision,
                Surface = context.Surface.Surface,
            };
            var cachedValidation = validator.Validate(
                normalizedCached,
                context,
                expectedLayoutId: expectedLayoutId,
                expectedRevision: expectedRevision);
            if (cachedValidation.IsValid)
            {
                return new(
                    normalizedCached,
                    AdaptiveCompositionSource.Cache,
                    cachedValidation,
                    CorrectionCount: 0,
                    Duration: TimeSpan.Zero,
                    InputTokens: null,
                    OutputTokens: null,
                    generation);
            }
        }

        var totalDuration = TimeSpan.Zero;
        long? inputTokens = null;
        long? outputTokens = null;
        ComponentLayoutDocument? invalidLayout = null;
        string? correction = null;

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var generated = await generator.GenerateAsync(
                    new(
                        context,
                        session.StandardLayout,
                        current,
                        invalidLayout,
                        expectedLayoutId,
                        expectedRevision,
                        correction),
                    cancellationToken).ConfigureAwait(false);
                EnsureCurrent(session, generation, cancellationToken);
                totalDuration += generated.Duration;
                inputTokens = Sum(inputTokens, generated.InputTokens);
                outputTokens = Sum(outputTokens, generated.OutputTokens);

                if (generated.Layout is not null)
                {
                    var validation = validator.Validate(
                        generated.Layout,
                        context,
                        current,
                        expectedLayoutId,
                        expectedRevision);
                    if (validation.IsValid)
                    {
                        cache.Set(cacheKey, generated.Layout);
                        return new(
                            generated.Layout,
                            attempt == 0 ? AdaptiveCompositionSource.Generated : AdaptiveCompositionSource.Corrected,
                            validation,
                            attempt,
                            totalDuration,
                            inputTokens,
                            outputTokens,
                            generation);
                    }

                    invalidLayout = generated.Layout;
                    correction = ComponentLayoutValidationErrorFormatter.Format(validation);
                }
                else
                {
                    correction = ComponentLayoutValidationErrorFormatter.Format(
                        new ComponentLayoutValidationResult(
                        [
                            new(
                                "empty_model_response",
                                "$",
                                "The model did not return a component layout document."),
                        ]));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Generation failures intentionally fall through to the validated standard layout.
        }

        EnsureCurrent(session, generation, cancellationToken);
        if (current is not null)
        {
            var currentValidation = validator.Validate(current, context);
            if (currentValidation.IsValid)
            {
                return new(
                    current,
                    AdaptiveCompositionSource.CurrentLayout,
                    currentValidation,
                    CorrectionCount: invalidLayout is null ? 0 : 1,
                    totalDuration,
                    inputTokens,
                    outputTokens,
                    generation);
            }
        }

        var standard = session.StandardLayout with
        {
            LayoutId = expectedLayoutId,
            Revision = expectedRevision,
            Surface = context.Surface.Surface,
        };
        var standardValidation = validator.Validate(
            standard,
            context,
            current,
            expectedLayoutId,
            expectedRevision);
        if (!standardValidation.IsValid)
        {
            throw new InvalidOperationException(
                "The standard adaptive layout is invalid: " +
                ComponentLayoutValidationErrorFormatter.Format(standardValidation));
        }

        return new(
            standard,
            AdaptiveCompositionSource.StandardLayout,
            standardValidation,
            CorrectionCount: invalidLayout is null ? 0 : 1,
            totalDuration,
            inputTokens,
            outputTokens,
            generation);
    }

    private static long? Sum(long? left, long? right)
        => left is null && right is null ? null : left.GetValueOrDefault() + right.GetValueOrDefault();

    private static void EnsureCurrent(
        AdaptiveSurfaceSession session,
        long generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsCurrentGeneration(generation))
            throw new OperationCanceledException("A newer adaptive layout generation superseded this request.");
    }
}
