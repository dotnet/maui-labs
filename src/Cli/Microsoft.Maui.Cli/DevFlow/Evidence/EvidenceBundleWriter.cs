using System.IO.Compression;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

internal sealed record EvidenceWriteResult(bool Ok, string? Path, long Bytes, string? Error);

/// <summary>
/// Writes a bundle to disk atomically: build the ZIP in a temporary file beside the destination,
/// validate it by reading it back through the hostile-input reader, and only then move it into
/// place. A partially written or structurally invalid bundle never appears at the destination.
/// </summary>
internal static class EvidenceBundleWriter
{
    public static byte[] ToBytes(
        EvidenceBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();
        using var buffer = new MemoryStream();
        WriteArchive(bundle, buffer, cancellationToken);
        var bytes = buffer.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        using var validationStream = new MemoryStream(bytes, writable: false);
        var validation = EvidenceBundleReader.Read(
            validationStream,
            cancellationToken: cancellationToken);
        var validationError = ValidateGeneratedReadback(bundle, validation);
        if (validationError is not null)
            throw new InvalidDataException($"The generated bundle failed validation: {validationError}");
        return bytes;
    }

    public static EvidenceWriteResult Write(
        EvidenceBundle bundle,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrEmpty(directory))
            return new EvidenceWriteResult(false, null, 0, "Output path has no parent directory.");

        if (!overwrite && File.Exists(destinationPath))
        {
            return new EvidenceWriteResult(false, null, 0,
                $"File already exists: {destinationPath} (use --overwrite to replace)");
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new EvidenceWriteResult(false, null, 0, $"Could not create the output directory: {ex.Message}");
        }

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteArchive(bundle, file, cancellationToken);
                file.Flush(flushToDisk: true);
            }

            // Validate before publishing: the same reader that guards imported bundles.
            var validation = EvidenceBundleReader.Read(temporaryPath, cancellationToken);
            var validationError = ValidateGeneratedReadback(bundle, validation);
            if (validationError is not null)
            {
                TryDelete(temporaryPath);
                return new EvidenceWriteResult(false, null, 0,
                    $"The generated bundle failed validation: {validationError}");
            }

            var bytes = new FileInfo(temporaryPath).Length;
            cancellationToken.ThrowIfCancellationRequested();

            if (overwrite)
                File.Move(temporaryPath, destinationPath, overwrite: true);
            else
                File.Move(temporaryPath, destinationPath); // fails if another writer won the race

            return new EvidenceWriteResult(true, Path.GetFullPath(destinationPath), bytes, null);
        }
        catch (IOException) when (!overwrite && File.Exists(destinationPath))
        {
            TryDelete(temporaryPath);
            return new EvidenceWriteResult(false, null, 0,
                $"File already exists: {destinationPath} (use --overwrite to replace)");
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDelete(temporaryPath);
            return new EvidenceWriteResult(false, null, 0, $"Could not write the bundle: {ex.Message}");
        }
    }

    private static void WriteArchive(
        EvidenceBundle bundle,
        Stream destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(
            archive,
            EvidenceFormat.ManifestEntry,
            bundle.ManifestBytes,
            cancellationToken);
        foreach (var entry in bundle.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteEntry(archive, entry.Name, entry.Content, cancellationToken);
        }
    }

    private static void WriteEntry(
        ZipArchive archive,
        string name,
        byte[] content,
        CancellationToken cancellationToken)
    {
        if (!EvidenceFormat.AllowedEntries.Contains(name, StringComparer.Ordinal))
            throw new InvalidOperationException($"Refusing to write unknown evidence entry '{name}'.");

        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        const int chunkSize = 81_920;
        for (var offset = 0; offset < content.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(chunkSize, content.Length - offset);
            stream.Write(content, offset, count);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* the temp file is uniquely named; a leftover is harmless */ }
    }

    private static string? ValidateGeneratedReadback(
        EvidenceBundle bundle,
        EvidenceReadResult validation)
    {
        if (!validation.Ok)
            return validation.Error;
        if (validation.Warnings.Count > 0)
            return string.Join("; ", validation.Warnings);

        foreach (var entry in bundle.Entries)
        {
            var present = entry.Name switch
            {
                EvidenceFormat.EnvironmentEntry => validation.Environment is not null,
                EvidenceFormat.TreeEntry => validation.Tree is not null,
                EvidenceFormat.LayoutEntry => validation.Layout is not null,
                EvidenceFormat.ProblemsEntry => validation.Problems is not null,
                EvidenceFormat.LogsEntry => validation.Logs is not null,
                EvidenceFormat.NetworkEntry => validation.Network is not null,
                EvidenceFormat.WorkflowEntry => validation.Workflow is not null,
                EvidenceFormat.ScreenshotEntry => validation.Screenshot is not null,
                _ => false,
            };
            if (!present)
                return $"Entry '{entry.Name}' did not survive typed readback.";
        }
        return null;
    }
}
