using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class LayoutDiagnosticsPolicy
{
    [JsonPropertyName("suppressions")]
    public List<LayoutSuppression> Suppressions { get; set; } = [];
}

internal static class LayoutDiagnosticsPolicyLoader
{
    private const string ProjectConfigFileName = ".mauidevflow";
    private const string UserPolicyFileName = "layout-diagnostics.json";
    private static readonly ConcurrentDictionary<string, object> ProjectPolicyGates =
        new(StringComparer.OrdinalIgnoreCase);

    public static LayoutDiagnosticsPolicy Load(
        string? startPath = null,
        bool includeUserPolicy = true)
    {
        var policy = new LayoutDiagnosticsPolicy();
        if (includeUserPolicy)
            Merge(policy, LoadUserPolicy());
        Merge(policy, LoadProjectPolicy(startPath));

        return policy;
    }

    public static LayoutDiagnosticsPolicy LoadProjectPolicy(string? startPath)
    {
        var projectConfig = FindProjectConfig(ResolveStartDirectory(startPath));
        return projectConfig is null
            ? new LayoutDiagnosticsPolicy()
            : ReadPolicyFile(projectConfig, nested: true);
    }

    public static LayoutDiagnosticsPolicy LoadUserPolicy()
    {
        var userPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mauidevflow",
            UserPolicyFileName);
        return File.Exists(userPath)
            ? ReadPolicyFile(userPath, nested: false)
            : new LayoutDiagnosticsPolicy();
    }

    public static string ResolveProjectConfigPath(string? startPath)
    {
        var startDirectory = ResolveStartDirectory(startPath);
        return FindProjectConfig(startDirectory)
            ?? Path.Combine(startDirectory, ProjectConfigFileName);
    }

    public static void SaveProjectPolicy(
        string? startPath,
        LayoutDiagnosticsPolicy policy)
        => WriteProjectPolicy(startPath, _ => policy);

    public static LayoutDiagnosticsPolicy UpdateProjectPolicy(
        string? startPath,
        Action<LayoutDiagnosticsPolicy> update)
        => WriteProjectPolicy(
            startPath,
            policy =>
            {
                update(policy);
                return policy;
            });

    private static LayoutDiagnosticsPolicy WriteProjectPolicy(
        string? startPath,
        Func<LayoutDiagnosticsPolicy, LayoutDiagnosticsPolicy> update)
    {
        var path = ResolveProjectConfigPath(startPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = ProjectPolicyGates.GetOrAdd(
            Path.GetFullPath(path),
            static _ => new object());

        lock (gate)
        {
            using var fileLock = AcquireProjectPolicyLock(path);
            var root = ReadProjectConfigRoot(path);
            var current = ReadPolicy(root, path, nested: true);
            var updated = update(current);

            root["layoutDiagnostics"] = JsonSerializer.SerializeToNode(
                updated,
                DevFlowCliJsonContext.Default.LayoutDiagnosticsPolicy);
            WriteAtomically(
                path,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return updated;
        }
    }

    private static JsonObject ReadProjectConfigRoot(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidOperationException(
                        $"Expected a JSON object in '{path}'.")
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid JSON in '{path}': {ex.Message}",
                ex);
        }
    }

    private static IDisposable AcquireProjectPolicyLock(string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)))[..24];
        var mutex = new Mutex(
            initiallyOwned: false,
            $"MauiDevFlow.LayoutPolicy.{hash}");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                throw new IOException(
                    $"Timed out waiting to update layout diagnostics policy '{path}'.");
        }
        catch (AbandonedMutexException)
        {
        }
        return new MutexReleaser(mutex);
    }

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (true)
            {
                try
                {
                    File.Move(temporaryPath, path, overwrite: true);
                    break;
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(25);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string ResolveStartDirectory(string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
            return Environment.CurrentDirectory;
        if (File.Exists(startPath) || Path.HasExtension(startPath))
            return Path.GetDirectoryName(Path.GetFullPath(startPath))
                ?? Environment.CurrentDirectory;
        return Path.GetFullPath(startPath);
    }

    private static string? FindProjectConfig(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ProjectConfigFileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static LayoutDiagnosticsPolicy ReadPolicyFile(string path, bool nested)
    {
        if (nested)
        {
            var fullPath = Path.GetFullPath(path);
            var gate = ProjectPolicyGates.GetOrAdd(
                fullPath,
                static _ => new object());
            lock (gate)
            {
                using var fileLock = AcquireProjectPolicyLock(fullPath);
                return ReadPolicyFileCore(fullPath, nested);
            }
        }

        return ReadPolicyFileCore(path, nested);
    }

    private static LayoutDiagnosticsPolicy ReadPolicyFileCore(
        string path,
        bool nested)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return ReadPolicy(document.RootElement, path, nested);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid layout diagnostics policy JSON in '{path}': {ex.Message}",
                ex);
        }
    }

    private static LayoutDiagnosticsPolicy ReadPolicy(
        JsonObject root,
        string path,
        bool nested)
    {
        try
        {
            using var document = JsonDocument.Parse(root.ToJsonString());
            return ReadPolicy(document.RootElement, path, nested);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid layout diagnostics policy JSON in '{path}': {ex.Message}",
                ex);
        }
    }

    private static LayoutDiagnosticsPolicy ReadPolicy(
        JsonElement root,
        string path,
        bool nested)
    {
        if (nested)
        {
            if (!root.TryGetProperty("layoutDiagnostics", out root))
                return new LayoutDiagnosticsPolicy();
        }

        return JsonSerializer.Deserialize(
            root,
            DevFlowCliJsonContext.Default.LayoutDiagnosticsPolicy)
            ?? new LayoutDiagnosticsPolicy();
    }

    private static void Merge(
        LayoutDiagnosticsPolicy destination,
        LayoutDiagnosticsPolicy source)
    {
        destination.Suppressions.AddRange(source.Suppressions);
    }

    private sealed class MutexReleaser(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var mutexToRelease = Interlocked.Exchange(
                ref _mutex,
                null);
            if (mutexToRelease is null)
                return;
            try
            {
                mutexToRelease.ReleaseMutex();
            }
            finally
            {
                mutexToRelease.Dispose();
            }
        }
    }
}

internal static class LayoutDiagnosticsSuppressionMatcher
{
    public static bool Matches(
        LayoutSuppression suppression,
        LayoutFinding finding)
    {
        if (suppression.RuleId is not null
            && !suppression.RuleId.Equals(finding.RuleId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.ElementId is not null
            && !suppression.ElementId.Equals(finding.Element.Id, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.AutomationId is not null
            && !suppression.AutomationId.Equals(finding.Element.AutomationId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.ElementType is not null
            && !suppression.ElementType.Equals(finding.Element.Type, StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.Fingerprint is not null
            && !suppression.Fingerprint.Equals(
                string.IsNullOrWhiteSpace(finding.SuppressionKey)
                    ? finding.Id
                    : finding.SuppressionKey,
                StringComparison.OrdinalIgnoreCase)
            && !suppression.Fingerprint.Equals(
                finding.Id,
                StringComparison.OrdinalIgnoreCase))
            return false;
        if (suppression.SourceFile is not null
            && !SourcePathMatches(suppression.SourceFile, finding.Element.SourceFile))
            return false;
        if (suppression.SourceLineStart is not null)
        {
            if (finding.Element.SourceLine is not { } line)
                return false;
            var end = suppression.SourceLineEnd ?? suppression.SourceLineStart.Value;
            if (line < suppression.SourceLineStart.Value || line > end)
                return false;
        }
        if (suppression.RelatedElementId is not null
            && !finding.RelatedElements.Any(related =>
                suppression.RelatedElementId.Equals(
                    related.Element.Id,
                    StringComparison.OrdinalIgnoreCase)))
            return false;
        if (suppression.RelatedAutomationId is not null
            && !finding.RelatedElements.Any(related =>
                suppression.RelatedAutomationId.Equals(
                    related.Element.AutomationId,
                    StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static bool SourcePathMatches(string requested, string? actual)
    {
        if (actual is null)
            return false;
        var normalizedRequested = requested.Replace('\\', '/').TrimStart('/');
        var normalizedActual = actual.Replace('\\', '/');
        return normalizedActual.Equals(
                normalizedRequested,
                StringComparison.OrdinalIgnoreCase)
            || normalizedActual.EndsWith(
                "/" + normalizedRequested,
                StringComparison.OrdinalIgnoreCase);
    }
}
