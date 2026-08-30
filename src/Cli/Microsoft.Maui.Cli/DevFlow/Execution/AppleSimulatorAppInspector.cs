using System.Text.Json;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record AppleSimulatorAppState
{
    public bool Installed { get; init; }
    public bool Running { get; init; }
}

internal interface IAppleSimulatorAppInspector
{
    Task WaitForBootReadinessAsync(
        string simulatorUdid,
        CancellationToken cancellationToken = default);

    Task<AppleSimulatorAppState> InspectAsync(
        string simulatorUdid,
        string bundleIdentifier,
        CancellationToken cancellationToken = default);
}

internal sealed class AppleSimulatorAppInspector : IAppleSimulatorAppInspector
{
    private const int MaximumListAppsCharacters = 8 * 1024 * 1024;
    private const int MaximumLaunchctlCharacters = 8 * 1024 * 1024;
    private readonly IExecutionProcessRunner _processRunner;
    private readonly IExecutionStandardInputProcessRunner _standardInputRunner;

    public AppleSimulatorAppInspector(
        IExecutionProcessRunner processRunner,
        IExecutionStandardInputProcessRunner standardInputRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _standardInputRunner = standardInputRunner ?? throw new ArgumentNullException(nameof(standardInputRunner));
    }

    public async Task WaitForBootReadinessAsync(
        string simulatorUdid,
        CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "/usr/bin/xcrun",
            ["simctl", "bootstatus", simulatorUdid, "-b"],
            timeout: TimeSpan.FromSeconds(90),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-boot-readiness-failed",
                "The exact iOS Simulator did not become boot-ready before the bounded timeout.");
        }
    }

    public async Task<AppleSimulatorAppState> InspectAsync(
        string simulatorUdid,
        string bundleIdentifier,
        CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync(
            "/usr/bin/xcrun",
            ["simctl", "listapps", simulatorUdid],
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-installation-query-failed",
                "The exact iOS Simulator app installation state could not be determined.");
        }
        if (result.StandardOutput.Length > MaximumListAppsCharacters)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-installation-query-too-large",
                "The exact iOS Simulator app installation query exceeded the bounded response size.");
        }

        var json = result.StandardOutput.Trim();
        if (!LooksLikeJsonObject(json))
        {
            var conversion = await _standardInputRunner.RunAsync(
                "/usr/bin/plutil",
                ["-convert", "json", "-o", "-", "-"],
                result.StandardOutput,
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
            if (!conversion.Success ||
                conversion.StandardOutput.Length > MaximumListAppsCharacters)
            {
                throw FlowExecutionException.Infrastructure(
                    "ios-simulator-installation-query-invalid",
                    "The exact iOS Simulator returned invalid app installation metadata.");
            }
            json = conversion.StandardOutput.Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw FlowExecutionException.Infrastructure(
                    "ios-simulator-installation-query-invalid",
                    "The exact iOS Simulator returned invalid app installation metadata.");
            }
            var installed = document.RootElement.TryGetProperty(bundleIdentifier, out _);
            var running = await IsRunningAsync(
                simulatorUdid,
                bundleIdentifier,
                cancellationToken).ConfigureAwait(false);
            return new AppleSimulatorAppState
            {
                Installed = installed,
                Running = running,
            };
        }
        catch (JsonException ex)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-installation-query-invalid",
                "The exact iOS Simulator returned invalid app installation metadata.",
                ex);
        }
    }

    private async Task<bool> IsRunningAsync(
        string simulatorUdid,
        string bundleIdentifier,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "/usr/bin/xcrun",
            ["simctl", "spawn", simulatorUdid, "launchctl", "list"],
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-running-query-failed",
                "The exact iOS Simulator app running state could not be determined.");
        }
        if (result.StandardOutput.Length > MaximumLaunchctlCharacters)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-running-query-too-large",
                "The exact iOS Simulator app running-state query exceeded the bounded response size.");
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static fields => fields.Length >= 3)
            .Select(static fields => fields[^1])
            .Any(label => ContainsExactBundleIdentifier(label, bundleIdentifier));
    }

    private static bool ContainsExactBundleIdentifier(string label, string bundleIdentifier)
    {
        var index = label.IndexOf(bundleIdentifier, StringComparison.Ordinal);
        while (index >= 0)
        {
            var beforeIsIdentifier = index > 0 && IsBundleIdentifierCharacter(label[index - 1]);
            var afterIndex = index + bundleIdentifier.Length;
            var afterIsIdentifier = afterIndex < label.Length && IsBundleIdentifierCharacter(label[afterIndex]);
            if (!beforeIsIdentifier && !afterIsIdentifier)
                return true;
            index = label.IndexOf(bundleIdentifier, index + 1, StringComparison.Ordinal);
        }
        return false;
    }

    private static bool IsBundleIdentifierCharacter(char value)
        => char.IsAsciiLetterOrDigit(value) || value is '.' or '-';

    private static bool LooksLikeJsonObject(string value)
    {
        if (!value.StartsWith('{') || !value.EndsWith('}'))
            return false;
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
