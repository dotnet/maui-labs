using System.Text;
using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public static class AppiumSmokeArtifactPaths
{
    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.Ordinal)
    {
        "failure.txt",
        "page-source.xml",
        "appium-logs.txt",
        "screenshot.png",
    };

    public static string GetSafeRunDirectory(string artifactRoot, string runName)
    {
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            throw new ArgumentException("An artifact root is required.", nameof(artifactRoot));
        }

        if (string.IsNullOrWhiteSpace(runName)
            || runName is "." or ".."
            || runName.Contains('/') || runName.Contains('\\')
            || Path.GetFileName(runName) != runName
            || runName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Artifact run names must be a single safe path segment.", nameof(runName));
        }

        var root = Path.GetFullPath(artifactRoot);
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Artifact root must not be a filesystem root.", nameof(artifactRoot));
        }

        var result = Path.GetFullPath(Path.Combine(root, runName));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(rootWithSeparator, comparison))
        {
            throw new ArgumentException("Artifact path escapes the configured root.", nameof(runName));
        }

        return result;
    }

    public static string GetSafeArtifactPath(string runDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(runDirectory))
        {
            throw new ArgumentException("A run directory is required.", nameof(runDirectory));
        }

        if (!AllowedFileNames.Contains(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException("The artifact filename is not allowed.", nameof(fileName));
        }

        return Path.Combine(runDirectory, fileName);
    }
}

public static class AppiumSmokeRedactor
{
    private static readonly Regex SensitiveAttribute = new(
        @"(?<key>\b(?:password|passwd|pwd|token|secret|authorization|cookie|api[_-]?key)\b\s*=\s*[""'])(?<value>[^""']*)(?<quote>[""'])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveHeader = new(
        @"(?im)^(?<key>\s*(?:authorization|cookie|set-cookie)\s*:\s*)(?<value>[^\r\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SensitiveKeyValue = new(
        @"(?<key>\b(?:password|passwd|pwd|token|secret|authorization|cookie|api[_-]?key)\b\s*(?:=|:)\s*)(?<value>(""[^""]*""|'[^']*'|[^\s,;<>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var redacted = SensitiveAttribute.Replace(value, match =>
            $"{match.Groups["key"].Value}***{match.Groups["quote"].Value}");
        redacted = SensitiveHeader.Replace(redacted, match => $"{match.Groups["key"].Value}***");
        return SensitiveKeyValue.Replace(redacted, match => $"{match.Groups["key"].Value}***");
    }
}

public sealed class AppiumSmokeArtifactSink
{
    private const int MaxTextArtifactCharacters = 1_000_000;

    private AppiumSmokeArtifactSink(string runDirectory, AppiumSmokePlatform platform, bool captureScreenshots)
    {
        RunDirectory = runDirectory;
        Platform = platform;
        CaptureScreenshots = captureScreenshots;
    }

    public string RunDirectory { get; }

    public AppiumSmokePlatform Platform { get; }

    public bool CaptureScreenshots { get; }

    public static AppiumSmokeArtifactSink Create(AppiumSmokeOptions configuration)
    {
        var runName = $"smoke-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var runDirectory = AppiumSmokeArtifactPaths.GetSafeRunDirectory(configuration.ArtifactRoot, runName);
        Directory.CreateDirectory(runDirectory);
        return new AppiumSmokeArtifactSink(runDirectory, configuration.Platform, configuration.CaptureScreenshots);
    }

    public void CaptureFailure(IWebDriver? driver, Exception exception)
    {
        WriteText("failure.txt", exception.ToString());

        if (driver is null)
        {
            return;
        }

        CapturePageSource(driver);
        CaptureLogs(driver);
        CaptureScreenshot(driver);
    }

    private void CapturePageSource(IWebDriver driver)
    {
        try
        {
            WriteText("page-source.xml", driver.PageSource);
        }
        catch (Exception exception)
        {
            AppendCaptureFailure("page source", exception);
        }
    }

    private void CaptureLogs(IWebDriver driver)
    {
        try
        {
            var logs = driver.Manage().Logs;
            var requestedLogTypes = new HashSet<string>(logs.AvailableLogTypes, StringComparer.OrdinalIgnoreCase)
            {
                "server",
            };

            if (Platform == AppiumSmokePlatform.Android)
            {
                requestedLogTypes.Add("logcat");
            }
            else if (Platform == AppiumSmokePlatform.IOS)
            {
                requestedLogTypes.Add("syslog");
            }

            var output = new StringBuilder();
            foreach (var logType in requestedLogTypes)
            {
                try
                {
                    output.AppendLine($"[{logType}]");
                    foreach (var entry in logs.GetLog(logType))
                    {
                        output.AppendLine($"{entry.Timestamp:O} {entry.Level}: {entry.Message}");
                    }
                }
                catch (Exception exception)
                {
                    output.AppendLine($"{logType}: unavailable ({exception.GetType().Name})");
                }
            }

            WriteText("appium-logs.txt", output.ToString());
        }
        catch (Exception exception)
        {
            AppendCaptureFailure("Appium logs", exception);
        }
    }

    private void CaptureScreenshot(IWebDriver driver)
    {
        if (!CaptureScreenshots)
        {
            return;
        }

        try
        {
            if (driver is ITakesScreenshot screenshotDriver)
            {
                screenshotDriver.GetScreenshot().SaveAsFile(
                    AppiumSmokeArtifactPaths.GetSafeArtifactPath(RunDirectory, "screenshot.png"));
            }
        }
        catch (Exception exception)
        {
            AppendCaptureFailure("screenshot", exception);
        }
    }

    private void AppendCaptureFailure(string artifactName, Exception exception)
    {
        var failurePath = AppiumSmokeArtifactPaths.GetSafeArtifactPath(RunDirectory, "failure.txt");
        File.AppendAllText(
            failurePath,
            Environment.NewLine + AppiumSmokeRedactor.Redact($"{artifactName} capture failed: {exception.GetType().Name}") + Environment.NewLine);
    }

    private void WriteText(string fileName, string content)
    {
        var bounded = content.Length > MaxTextArtifactCharacters
            ? content[..MaxTextArtifactCharacters] + Environment.NewLine + "[truncated]"
            : content;
        File.WriteAllText(
            AppiumSmokeArtifactPaths.GetSafeArtifactPath(RunDirectory, fileName),
            AppiumSmokeRedactor.Redact(bounded));
    }
}
