// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Providers.Android;

public partial class SdkManager
{
    public async Task<List<SdkPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        var (installed, _) = await ListPackagesAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileCommandLineToolsAsync(installed, cancellationToken).ConfigureAwait(false);
        return installed;
    }

    public async Task<List<SdkPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken = default)
    {
        var (_, available) = await ListPackagesAsync(cancellationToken).ConfigureAwait(false);
        return available;
    }

    async Task<(List<SdkPackage> Installed, List<SdkPackage> Available)> ListPackagesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (sdkPath, jdkPath) = SyncPaths();
        var sdkManagerPath = EnsureAvailable();
        var startInfo = CreatePackageListStartInfo(sdkManagerPath, sdkPath, jdkPath);

        // The upstream ListAsync parser only accepts the pre-23 pipe-delimited table.
        var result = await ProcessRunner.RunAsync(
            startInfo,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            throw new InvalidOperationException(
                $"SDK package inventory failed (exit code {result.ExitCode}).{Environment.NewLine}" +
                $"stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}");

        try
        {
            return ParsePackageList(result.StandardOutput);
        }
        catch (FormatException error)
        {
            throw new FormatException(
                $"Could not parse SDK package inventory from '{sdkManagerPath}': {error.Message}{Environment.NewLine}" +
                $"stdout: {result.StandardOutput}{Environment.NewLine}stderr: {result.StandardError}", error);
        }
    }

    internal static ProcessStartInfo CreatePackageListStartInfo(string sdkManagerPath, string? sdkPath, string? jdkPath, bool? isWindows = null)
    {
        var startInfo = new ProcessStartInfo { FileName = sdkManagerPath };
        foreach (var variable in AndroidEnvironment.BuildEnvironmentVariables(sdkPath, jdkPath))
            startInfo.Environment[variable.Key] = variable.Value;
        if ((isWindows ?? OperatingSystem.IsWindows()) && sdkManagerPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "cmd.exe";
            // cmd.exe does not use argv quoting. Expand paths once inside quotes, with delayed expansion disabled.
            startInfo.Arguments = "/d /s /v:off /c \"\"%MAUI_SDK_MANAGER_PATH%\" --list \"--sdk_root=%ANDROID_HOME%\"\"";
            startInfo.Environment["MAUI_SDK_MANAGER_PATH"] = sdkManagerPath;
        }
        else
        {
            startInfo.ArgumentList.Add("--list");
            startInfo.ArgumentList.Add($"--sdk_root={sdkPath}");
        }
        return startInfo;
    }

    static (List<SdkPackage> Installed, List<SdkPackage> Available) ParsePackageList(string output)
    {
        var installed = new List<SdkPackage>();
        var available = new List<SdkPackage>();
        List<SdkPackage>? section = null;
        var foundSection = false;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith("Installed packages:", StringComparison.OrdinalIgnoreCase))
            {
                section = installed;
                foundSection = true;
                continue;
            }
            if (trimmed.EndsWith("Available packages:", StringComparison.OrdinalIgnoreCase))
            {
                section = available;
                foundSection = true;
                continue;
            }
            if (trimmed.EndsWith("Available updates:", StringComparison.OrdinalIgnoreCase))
            {
                section = null;
                continue;
            }
            if (section is null || trimmed.Length == 0 ||
                trimmed.StartsWith("Path", StringComparison.Ordinal) || trimmed.StartsWith("---", StringComparison.Ordinal))
                continue;

            var columns = trimmed.Split('|');
            string packagePath;
            string version;
            string? description;
            if (columns.Length >= 2)
            {
                packagePath = columns[0].Trim();
                version = columns[1].Trim();
                description = columns.Length > 2 ? columns[2].Trim() : null;
            }
            else
            {
                var match = PackageRowRegex().Match(trimmed);
                if (!match.Success)
                    throw new FormatException($"Unrecognized package row: {trimmed}");
                packagePath = match.Groups["path"].Value;
                version = match.Groups["version"].Value;
                description = match.Groups["description"].Value.Trim();
            }
            if (packagePath.Length == 0 || version.Length == 0)
                throw new FormatException($"Incomplete package row: {trimmed}");

            section.Add(new SdkPackage
            {
                Path = packagePath.Replace('/', ';').Replace('\\', ';'),
                Version = version,
                Description = description,
                IsInstalled = section == installed
            });
        }
        if (!foundSection)
            throw new FormatException("No installed or available package section was found.");
        return (installed, available);
    }

    async Task ReconcileCommandLineToolsAsync(List<SdkPackage> installed, CancellationToken cancellationToken)
    {
        var sdkPath = _getSdkPath();
        if (string.IsNullOrEmpty(sdkPath))
            return;
        var toolsRoot = Path.Combine(sdkPath, "cmdline-tools");
        if (!Directory.Exists(toolsRoot))
            return;

        var toolName = OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager";
        foreach (var directory in Directory.EnumerateDirectories(toolsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packagePath = $"cmdline-tools;{Path.GetFileName(directory)}";
            if (!File.Exists(Path.Combine(directory, "bin", toolName)))
                continue;

            // Package inventories can omit tools or report a revision that disagrees with the executable's metadata.
            var propertiesPath = Path.Combine(directory, "source.properties");
            var properties = File.Exists(propertiesPath)
                ? await File.ReadAllTextAsync(propertiesPath, cancellationToken).ConfigureAwait(false)
                : string.Empty;
            var revision = PackageRevisionRegex().Match(properties).Groups[1].Value.Trim();
            var installedIndex = installed.FindIndex(package => string.Equals(package.Path, packagePath, StringComparison.OrdinalIgnoreCase));
            if (installedIndex >= 0)
            {
                if (revision.Length > 0)
                    installed[installedIndex] = installed[installedIndex] with { Version = revision };
                continue;
            }
            installed.Add(new SdkPackage
            {
                Path = packagePath,
                Version = revision,
                Description = "Android SDK Command-line Tools",
                IsInstalled = true
            });
        }
    }

    [GeneratedRegex(@"^(?<path>\S+)[ \t]+(?<version>(?:\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z.-]+)?|unknown)(?:[ \t]+(?:alpha|beta|rc)[ \t]*\d+)?)(?:[ \t]+->[ \t]+\S+)?[ \t]+(?<description>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PackageRowRegex();

    [GeneratedRegex(@"^Pkg\.Revision[ \t]*=[ \t]*([^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex PackageRevisionRegex();
}
