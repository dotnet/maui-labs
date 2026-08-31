namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record AppleAppBundleInfo
{
    public required string BundleIdentifier { get; init; }
    public required string ExecutablePath { get; init; }
}

internal interface IAppleAppBundleInspector
{
    Task<AppleAppBundleInfo> InspectAsync(
        string appBundlePath,
        CancellationToken cancellationToken = default);
}

internal sealed class AppleAppBundleInspector : IAppleAppBundleInspector
{
    private const long MaximumPlistBytes = 1_048_576;
    private const int MaximumPlistValueCharacters = 4_096;
    private readonly IExecutionProcessRunner _processRunner;

    public AppleAppBundleInspector(IExecutionProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<AppleAppBundleInfo> InspectAsync(
        string appBundlePath,
        CancellationToken cancellationToken = default)
    {
        var bundlePath = Path.GetFullPath(appBundlePath);
        if (!Directory.Exists(bundlePath) ||
            !string.Equals(Path.GetExtension(bundlePath), ".app", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                "apple-app-bundle-invalid",
                "The resolved Apple artifact is not an existing .app bundle.");
        }
        ExecutionPathSafety.RejectReparsePoints(
            bundlePath,
            "apple-app-bundle-reparse-point",
            "The resolved Apple app bundle cannot contain or traverse a symbolic link or reparse point.");

        var plistPath = Path.Combine(bundlePath, "Info.plist");
        if (!File.Exists(plistPath))
            plistPath = Path.Combine(bundlePath, "Contents", "Info.plist");
        if (!File.Exists(plistPath))
        {
            throw FlowExecutionException.Invalid(
                "apple-info-plist-missing",
                "The resolved Apple app bundle does not contain Info.plist.");
        }
        ExecutionPathSafety.RejectReparsePoints(
            plistPath,
            "apple-info-plist-reparse-point",
            "The resolved Apple Info.plist cannot contain or traverse a symbolic link or reparse point.");
        if (new FileInfo(plistPath).Length is <= 0 or > MaximumPlistBytes)
        {
            throw FlowExecutionException.Invalid(
                "apple-info-plist-size-invalid",
                "The resolved Apple Info.plist exceeds the bounded size.");
        }

        var bundleIdentifier = await ReadPlistValueAsync(
            plistPath,
            "CFBundleIdentifier",
            cancellationToken).ConfigureAwait(false);
        var executableName = await ReadPlistValueAsync(
            plistPath,
            "CFBundleExecutable",
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(executableName, Path.GetFileName(executableName), StringComparison.Ordinal) ||
            executableName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw FlowExecutionException.Invalid(
                "apple-bundle-executable-invalid",
                "The Apple app bundle declares an invalid executable name.");
        }

        var candidates = new[]
        {
            Path.Combine(bundlePath, executableName),
            Path.Combine(bundlePath, "Contents", "MacOS", executableName),
        }
        .Distinct(StringComparer.Ordinal)
        .Where(File.Exists)
        .ToArray();
        if (candidates.Length != 1)
        {
            throw FlowExecutionException.Invalid(
                candidates.Length == 0
                    ? "apple-bundle-executable-missing"
                    : "apple-bundle-executable-ambiguous",
                candidates.Length == 0
                    ? "The Apple app bundle's exact executable is missing."
                    : "The Apple app bundle maps its executable identity to multiple files.");
        }
        ExecutionPathSafety.RejectReparsePoints(
            candidates[0],
            "apple-bundle-executable-reparse-point",
            "The resolved Apple executable cannot contain or traverse a symbolic link or reparse point.");

        return new AppleAppBundleInfo
        {
            BundleIdentifier = bundleIdentifier,
            ExecutablePath = Path.GetFullPath(candidates[0]),
        };
    }

    private async Task<string> ReadPlistValueAsync(
        string plistPath,
        string key,
        CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            "/usr/libexec/PlistBuddy",
            ["-c", $"Print :{key}", plistPath],
            workingDirectory: Path.GetDirectoryName(plistPath),
            timeout: TimeSpan.FromSeconds(15),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var lines = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!result.Success ||
            result.StandardOutput.Length > MaximumPlistValueCharacters ||
            result.StandardError.Length > MaximumPlistValueCharacters ||
            lines.Length != 1 ||
            string.IsNullOrWhiteSpace(lines[0]) ||
            lines[0].Length > MaximumPlistValueCharacters)
        {
            throw FlowExecutionException.Invalid(
                "apple-info-plist-invalid",
                $"The Apple app bundle does not expose a single valid {key} value.");
        }
        return lines[0];
    }
}
