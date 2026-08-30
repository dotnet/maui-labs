namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Result of a one-shot assertion check used by authoring hosts.</summary>
public sealed class MauiFlowAssertionVerification
{
    public bool? Passed { get; init; }
    public bool ObservationOnly { get; init; }
    public bool Skipped { get; init; }
    public int MatchCount { get; init; }
    public string? Quality { get; init; }
    public string? Actual { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Uses the same strict target resolution and scalar comparison rules as <see cref="MauiFlowRunner"/>
/// without driving the application. It is intended for optional authoring-time feedback only.
/// </summary>
public static class MauiFlowAssertionVerifier
{
    public static async Task<MauiFlowAssertionVerification> VerifyAsync(
        IMauiFlowDriver driver,
        FlowAssert assertion,
        int pollTries = 1,
        int pollGapMs = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(assertion);

        if (!assertion.Verify || string.Equals(assertion.Kind, "pageChanged", StringComparison.Ordinal))
        {
            return new MauiFlowAssertionVerification
            {
                ObservationOnly = true,
                Skipped = true,
            };
        }

        var tries = Math.Max(1, pollTries);
        FlowTargetResolution? lastResolution = null;
        string? actual = null;
        for (var attempt = 0; attempt < tries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (assertion.Kind is "propEquals" or "exists" or "notExists")
                {
                    var resolution = await new FlowActionabilityEngine(driver, 1, 0)
                        .ResolveAsync(assertion.Selector, cancellationToken)
                        .ConfigureAwait(false);
                    lastResolution = resolution;
                    if (assertion.Kind == "notExists" &&
                        !resolution.Ok &&
                        string.Equals(resolution.Kind, FlowFailureKinds.NotFound, StringComparison.Ordinal))
                    {
                        return new MauiFlowAssertionVerification
                        {
                            Passed = true,
                            MatchCount = 0,
                        };
                    }
                    if (resolution.Ok)
                    {
                        if (assertion.Kind == "exists")
                        {
                            return new MauiFlowAssertionVerification
                            {
                                Passed = true,
                                MatchCount = resolution.MatchCount,
                                Quality = resolution.Quality,
                            };
                        }

                        if (assertion.Kind == "notExists")
                            continue;

                        actual = await driver.GetPropertyAsync(
                            resolution.Element!.Id,
                            string.IsNullOrWhiteSpace(assertion.Name) ? "Text" : assertion.Name)
                            .ConfigureAwait(false);
                        if (FlowReplayer.PropertyValuesEqual(actual, assertion.Expected))
                        {
                            return new MauiFlowAssertionVerification
                            {
                                Passed = true,
                                MatchCount = resolution.MatchCount,
                                Quality = resolution.Quality,
                                Actual = actual,
                            };
                        }
                    }
                    else if (string.Equals(resolution.Kind, FlowFailureKinds.Ambiguous, StringComparison.Ordinal))
                    {
                        return new MauiFlowAssertionVerification
                        {
                            Passed = false,
                            MatchCount = resolution.MatchCount,
                            Error = resolution.Error,
                        };
                    }
                }
                else if (assertion.Kind == "routeIs")
                {
                    actual = (await driver.GetStatusAsync().ConfigureAwait(false))?.Route;
                    if (string.Equals(actual, assertion.Expected, StringComparison.Ordinal))
                    {
                        return new MauiFlowAssertionVerification
                        {
                            Passed = true,
                            Actual = actual,
                        };
                    }
                }
                else
                {
                    return new MauiFlowAssertionVerification
                    {
                        Skipped = true,
                        Error = $"Unsupported assertion kind '{assertion.Kind}'.",
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // The runner retries observed assertion state without exposing transport details.
            }

            if (attempt < tries - 1)
                await Task.Delay(Math.Max(0, pollGapMs), cancellationToken).ConfigureAwait(false);
        }

        return new MauiFlowAssertionVerification
        {
            Passed = false,
            MatchCount = lastResolution?.MatchCount ?? 0,
            Quality = lastResolution?.Quality,
            Actual = actual,
            Error = lastResolution?.Error,
        };
    }
}
