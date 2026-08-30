using Microsoft.Maui.DevFlow.Agent.Core;

namespace DevFlow.Sample;

/// <summary>
/// The app's opt-in surface for agent-led repair validation.
/// </summary>
/// <remarks>
/// A repair validation is fenced to one agent instance, so the reset that precedes it must not
/// restart the process. An external <c>pm clear</c> would, which is why the reset an app wants to
/// support has to live inside the app. This action re-establishes the same seed the app starts
/// with, in process, leaving the DevFlow agent registration and its instance id untouched.
/// </remarks>
public static class TodoResetAction
{
    /// <summary>The only seed this app can establish, named so a caller cannot ask for another.</summary>
    public const string IntegrationSeedId = "todo-integration-seed-v1";

    /// <summary>
    /// Restores the app's seeded todo list and clears the durable ledger.
    /// </summary>
    /// <param name="seedId">
    /// The seed the caller expects. Null means "whatever this app seeds by default". Any other value
    /// is refused rather than silently treated as the default, so a caller can never believe a seed
    /// was applied that this app has never heard of.
    /// </param>
    [DevFlowAction(
        "devflow-reset",
        Description = "Reset the todo list and ledger to the app's seeded state, in process.")]
    public static string ResetTodos(string? seedId = null)
    {
        var requested = seedId?.Trim();
        if (!string.IsNullOrEmpty(requested) &&
            !string.Equals(requested, IntegrationSeedId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unknown seed '{requested}'. This app can only establish '{IntegrationSeedId}'.",
                nameof(seedId));
        }

        var services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The app service provider is not available yet.");
        var todos = services.GetService(typeof(TodoService)) as TodoService
            ?? throw new InvalidOperationException("TodoService is not registered.");

        todos.ResetToIntegrationSeed();
        return IntegrationSeedId;
    }
}
