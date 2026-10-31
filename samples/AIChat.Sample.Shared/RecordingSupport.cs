using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Recording;

namespace AIChat.Sample.Shared;

/// <summary>
/// Removes provider-owned raw payloads before strict recording. It intentionally preserves every
/// semantic M.E.AI field and never attempts generic provider serialization.
/// </summary>
public sealed class RecordingRawNormalizerChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return Normalize(update);
        }
    }

    public static ChatResponseUpdate Normalize(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var clone = update.Clone();
        clone.RawRepresentation = null;
        foreach (var content in clone.Contents)
            content.RawRepresentation = null;
        return clone;
    }
}

/// <summary>Derives a distinct recording destination for each scenario without using source-relative defaults.</summary>
public static class RecordingDestination
{
    public static string? ForScenario(string? configuredPath, string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return null;
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("A scenario identifier is required.", nameof(scenarioId));

        var fullPath = Path.GetFullPath(configuredPath);
        if (Path.HasExtension(fullPath))
        {
            var directory = Path.GetDirectoryName(fullPath)!;
            var stem = Path.GetFileNameWithoutExtension(fullPath);
            return Path.Combine(directory, $"{stem}.{scenarioId}.recording.json");
        }

        return Path.Combine(fullPath, $"{scenarioId}.recording.json");
    }
}

/// <summary>Persists sample recordings without exposing a partially written fixture at its final path.</summary>
public static class SampleRecordingStore
{
    public static void SaveAtomic(ChatRecording recording, string path) =>
        SaveAtomic(recording, path, ChatRecordingStore.Save);

    /// <summary>
    /// Writes through a caller-supplied serializer. This overload exists to verify failure handling
    /// without replacing an existing fixture or requiring a source-tree fixture destination.
    /// </summary>
    public static void SaveAtomic(
        ChatRecording recording,
        string path,
        Action<ChatRecording, string> save)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(save);

        var fullPath = Path.GetFullPath(path);
        var destinationDirectory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(destinationDirectory);
        var stagingDirectory = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        var temporaryPath = Path.Combine(stagingDirectory, Path.GetFileName(fullPath));
        var stagedBlobDirectory = temporaryPath + ".blobs";
        var finalBlobDirectory = fullPath + ".blobs";
        var promotedBlobs = new List<string>();
        var committed = false;

        Directory.CreateDirectory(stagingDirectory);
        try
        {
            save(recording, temporaryPath);

            if (Directory.Exists(stagedBlobDirectory))
            {
                foreach (var stagedBlob in Directory.EnumerateFiles(
                    stagedBlobDirectory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingDirectory, stagedBlob);
                    var finalBlob = Path.Combine(destinationDirectory, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(finalBlob)!);
                    if (File.Exists(finalBlob))
                    {
                        File.Delete(stagedBlob);
                    }
                    else
                    {
                        File.Move(stagedBlob, finalBlob);
                        promotedBlobs.Add(finalBlob);
                    }
                }
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            committed = true;

            var referencedBlobs = GetReferencedBlobPaths(recording, destinationDirectory);
            if (Directory.Exists(finalBlobDirectory))
            {
                foreach (var blob in Directory.EnumerateFiles(
                    finalBlobDirectory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    if (!referencedBlobs.Contains(Path.GetFullPath(blob)))
                        File.Delete(blob);
                }

                DeleteEmptyDirectories(finalBlobDirectory);
            }
        }
        finally
        {
            if (!committed)
            {
                foreach (var promotedBlob in promotedBlobs)
                {
                    if (File.Exists(promotedBlob))
                        File.Delete(promotedBlob);
                }

                if (Directory.Exists(finalBlobDirectory))
                    DeleteEmptyDirectories(finalBlobDirectory);
            }

            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static HashSet<string> GetReferencedBlobPaths(
        ChatRecording recording,
        string destinationDirectory)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var interaction in recording.Interactions)
        {
            CollectBlobPaths(interaction.Request, destinationDirectory, paths);
            foreach (var update in interaction.Updates)
                CollectBlobPaths(update.Value, destinationDirectory, paths);
        }

        return paths;
    }

    private static void CollectBlobPaths(
        System.Text.Json.Nodes.JsonNode? node,
        string destinationDirectory,
        ISet<string> paths)
    {
        if (node is System.Text.Json.Nodes.JsonObject obj)
        {
            if (obj["blob"] is System.Text.Json.Nodes.JsonObject blob &&
                blob["path"]?.GetValue<string>() is { } relativePath)
            {
                paths.Add(Path.GetFullPath(Path.Combine(destinationDirectory, relativePath)));
            }

            foreach (var property in obj)
                CollectBlobPaths(property.Value, destinationDirectory, paths);
        }
        else if (node is System.Text.Json.Nodes.JsonArray array)
        {
            foreach (var item in array)
                CollectBlobPaths(item, destinationDirectory, paths);
        }
    }

    private static void DeleteEmptyDirectories(string directory)
    {
        foreach (var child in Directory.EnumerateDirectories(directory))
            DeleteEmptyDirectories(child);

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}
