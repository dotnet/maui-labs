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

internal sealed class LayoutPolicyConcurrencyException(string message) : IOException(message);

internal static class LayoutDiagnosticsPolicyLoader
{
    private const string ProjectConfigFileName = ".mauidevflow";
    private const string UserPolicyFileName = "layout-diagnostics.json";
    private static readonly ConcurrentDictionary<string, object> ProjectPolicyGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mauidevflow",
            UserPolicyFileName);
        return File.Exists(path)
            ? ReadPolicyFile(path, nested: false)
            : new LayoutDiagnosticsPolicy();
    }

    public static string ResolveProjectConfigPath(string? startPath)
    {
        var startDirectory = ResolveStartDirectory(startPath);
        return FindProjectConfig(startDirectory)
            ?? Path.Combine(startDirectory, ProjectConfigFileName);
    }

    public static LayoutDiagnosticsPolicy UpdateProjectPolicy(
        string? startPath,
        Action<LayoutDiagnosticsPolicy> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var path = ResolveProjectConfigPath(startPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = ProjectPolicyGates.GetOrAdd(Path.GetFullPath(path), static _ => new object());

        lock (gate)
        {
            using var fileLock = AcquireProjectPolicyLock(path);
            var root = ReadProjectConfigRoot(path);
            var policy = ReadPolicy(root, path, nested: true);
            update(policy);
            root["layoutDiagnostics"] = JsonSerializer.SerializeToNode(
                policy,
                DevFlowCliJsonContext.Default.LayoutDiagnosticsPolicy);
            WriteAtomically(
                path,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return policy;
        }
    }

    public static string GetProjectPolicyDigest(string? startPath)
    {
        var path = ResolveProjectConfigPath(startPath);
        ValidateProjectPolicyPath(startPath, path);
        return File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
            : Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
    }

    public static LayoutDiagnosticsPolicy UpdateProjectPolicyCas(
        string? startPath,
        string expectedDigest,
        Action<LayoutDiagnosticsPolicy> update,
        string? expectedPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedDigest);
        ArgumentNullException.ThrowIfNull(update);
        var path = ResolveProjectConfigPath(startPath);
        ValidateProjectPolicyPath(startPath, path);
        // The digest alone cannot bind the target file: a missing policy and an empty one hash
        // identically, so a `.mauidevflow` created in a nearer ancestor between review and apply
        // would be written to while the digest check still passed. Bind to the reviewed path.
        if (expectedPath is not null &&
            !Path.GetFullPath(path).Equals(
                Path.GetFullPath(expectedPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new LayoutPolicyConcurrencyException(
                "The layout diagnostics policy file resolved to a different path than the one reviewed. " +
                "Rescan and review a new proposal.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var gate = ProjectPolicyGates.GetOrAdd(Path.GetFullPath(path), static _ => new object());

        lock (gate)
        {
            using var fileLock = AcquireProjectPolicyLock(path);
            ValidateProjectPolicyPath(startPath, path);
            var originalBytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
            var actualDigest = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
            if (!actualDigest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new LayoutPolicyConcurrencyException(
                    "The layout diagnostics policy changed after it was reviewed. Rescan and review a new proposal.");
            }

            var root = originalBytes.Length == 0
                ? new JsonObject()
                : JsonNode.Parse(originalBytes) as JsonObject
                    ?? throw new InvalidOperationException($"Expected a JSON object in '{path}'.");
            var policy = ReadPolicy(root, path, nested: true);
            update(policy);
            root["layoutDiagnostics"] = JsonSerializer.SerializeToNode(
                policy,
                DevFlowCliJsonContext.Default.LayoutDiagnosticsPolicy);
            ValidateProjectPolicyPath(startPath, path);
            var commitBytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
            var commitDigest = Convert.ToHexString(SHA256.HashData(commitBytes)).ToLowerInvariant();
            if (!commitDigest.Equals(expectedDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new LayoutPolicyConcurrencyException(
                    "The layout diagnostics policy changed while the approved update was being prepared.");
            }
            WriteAtomically(
                path,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return policy;
        }
    }

    private static JsonObject ReadProjectConfigRoot(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new InvalidOperationException($"Expected a JSON object in '{path}'.")
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in '{path}': {ex.Message}", ex);
        }
    }

    private static IDisposable AcquireProjectPolicyLock(string path)
    {
        var normalized = Path.GetFullPath(path).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        var mutex = new Mutex(initiallyOwned: false, $"MauiDevFlow.LayoutPolicy.{hash}");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                throw new IOException($"Timed out waiting to update layout diagnostics policy '{path}'.");
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
        if (Directory.Exists(startPath))
            return Path.GetFullPath(startPath);
        if (File.Exists(startPath) || Path.HasExtension(startPath))
            return Path.GetDirectoryName(Path.GetFullPath(startPath))
                ?? Environment.CurrentDirectory;
        return Path.GetFullPath(startPath);
    }

    private static void ValidateProjectPolicyPath(string? startPath, string policyPath)
    {
        var root = ResolveStartDirectory(startPath);
        var fullPolicy = Path.GetFullPath(policyPath);
        var expectedPolicy = Path.GetFullPath(
            FindProjectConfig(root) ?? Path.Combine(root, ProjectConfigFileName));
        if (!fullPolicy.Equals(
                expectedPolicy,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The layout diagnostics policy path does not match the resolved project policy target.");
        }

        // The search walks upward from the project directory, so the policy file sits at the start
        // directory or in one of its ancestors. Check exactly the segments the search covered and
        // stop there: continuing to the filesystem root would reject every write under a symlinked
        // ancestor such as macOS's `/var`, which says nothing about whether the target is safe.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var policyDirectory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(fullPolicy)!);
        for (var current = new DirectoryInfo(ResolveStartDirectory(startPath));
             current is not null;
             current = current.Parent)
        {
            if (current.Exists &&
                current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "The layout diagnostics policy path traverses a symbolic link or reparse point.");
            }
            if (Path.TrimEndingDirectorySeparator(current.FullName).Equals(policyDirectory, comparison))
                break;
        }
        if (File.Exists(fullPolicy) &&
            File.GetAttributes(fullPolicy).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "The layout diagnostics policy file is a symbolic link or reparse point.");
        }
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
        if (!nested)
            return ReadPolicyFileCore(path, nested);

        var fullPath = Path.GetFullPath(path);
        var gate = ProjectPolicyGates.GetOrAdd(fullPath, static _ => new object());
        lock (gate)
        {
            using var fileLock = AcquireProjectPolicyLock(fullPath);
            return ReadPolicyFileCore(fullPath, nested);
        }
    }

    private static LayoutDiagnosticsPolicy ReadPolicyFileCore(string path, bool nested)
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

    private static LayoutDiagnosticsPolicy ReadPolicy(JsonObject root, string path, bool nested)
    {
        using var document = JsonDocument.Parse(root.ToJsonString());
        return ReadPolicy(document.RootElement, path, nested);
    }

    private static LayoutDiagnosticsPolicy ReadPolicy(JsonElement root, string path, bool nested)
    {
        if (nested)
        {
            if (!root.TryGetProperty("layoutDiagnostics", out root))
                return new LayoutDiagnosticsPolicy();
        }

        try
        {
            return JsonSerializer.Deserialize(
                root,
                DevFlowCliJsonContext.Default.LayoutDiagnosticsPolicy)
                ?? new LayoutDiagnosticsPolicy();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid layout diagnostics policy JSON in '{path}': {ex.Message}",
                ex);
        }
    }

    private static void Merge(
        LayoutDiagnosticsPolicy destination,
        LayoutDiagnosticsPolicy source)
        => destination.Suppressions.AddRange(source.Suppressions);

    private sealed class MutexReleaser(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _mutex, null);
            if (current is null)
                return;
            try
            {
                current.ReleaseMutex();
            }
            finally
            {
                current.Dispose();
            }
        }
    }
}
