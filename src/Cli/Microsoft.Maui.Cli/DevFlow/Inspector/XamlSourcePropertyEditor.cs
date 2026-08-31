using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

internal enum XamlSourceEditStatus
{
    Success,
    InvalidRequest,
    SourceUnavailable,
    Forbidden,
    Stale,
    Unsupported,
    Failed,
}

internal sealed record XamlSourceEditResult(
    XamlSourceEditStatus Status,
    string? Error = null,
    string? File = null,
    int? Line = null,
    int? Column = null,
    string? SourceHash = null)
{
    public bool Success => Status == XamlSourceEditStatus.Success;
}

/// <summary>
/// Persists Inspector property changes to existing direct-literal XAML attributes. Source writes
/// stay on the developer machine; the in-app agent only supplies the mapped source location.
/// </summary>
internal sealed class XamlSourcePropertyEditor
{
    private const long MaxSourceFileBytes = 5 * 1024 * 1024;
    private const int MaxPropertyValueLength = 64 * 1024;
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly HashSet<string> SupportedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text",
        "TextColor",
        "FontSize",
        "FontAttributes",
        "HorizontalTextAlignment",
        "LineBreakMode",
        "IsVisible",
        "IsEnabled",
        "Opacity",
        "BackgroundColor",
        "Placeholder",
        "IsChecked",
        "Color",
        "IsToggled",
        "OnColor",
        "BorderColor",
        "CornerRadius",
        "HasShadow",
        "Spacing",
    };

    // Shared across every inspector instance in the broker so the same XAML file cannot be edited
    // concurrently through different agents, TFMs, browser tabs, or embedded hosts.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(PathComparer);
    private static readonly ConcurrentDictionary<string, SourceEditState> EditStates = new(PathComparer);

    private readonly string? _project;
    private readonly string? _sessionId;

    public XamlSourcePropertyEditor(string? project, string? sessionId = null)
    {
        _project = string.IsNullOrWhiteSpace(project) ? null : project;
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId;
    }

    public Task<XamlSourceEditResult> ValidateAsync(
        ElementInfo element,
        string propertyName,
        string value,
        CancellationToken cancellationToken = default)
        => EditAsync(element, propertyName, value, writeChanges: false, cancellationToken);

    public Task<XamlSourceEditResult> PersistAsync(
        ElementInfo element,
        string propertyName,
        string value,
        CancellationToken cancellationToken = default)
        => EditAsync(element, propertyName, value, writeChanges: true, cancellationToken);

    private async Task<XamlSourceEditResult> EditAsync(
        ElementInfo element,
        string propertyName,
        string value,
        bool writeChanges,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedPropertyName(propertyName) || value.Length > MaxPropertyValueLength)
        {
            return new(
                XamlSourceEditStatus.InvalidRequest,
                "This property is not supported for XAML source persistence, or its value exceeds 64 KB.");
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);
        }
        catch (XmlException)
        {
            return new(
                XamlSourceEditStatus.InvalidRequest,
                "The property value contains characters that are not valid in XML.");
        }

        if (string.IsNullOrWhiteSpace(element.SourceFile) ||
            element.SourceLine is not > 0 ||
            element.SourceColumn is not > 0 ||
            !IsValidSourceHash(element.SourceHash))
        {
            return new(
                XamlSourceEditStatus.SourceUnavailable,
                "This element does not have writable Debug XAML source metadata.");
        }

        var pathValidation = ValidateSourcePath(element.SourceFile!);
        if (!pathValidation.Success)
            return new(XamlSourceEditStatus.Forbidden, pathValidation.Error);

        var sourcePath = pathValidation.Path!;
        var gate = FileLocks.GetOrAdd(sourcePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            SourceFileSnapshot snapshot;
            try
            {
                snapshot = await SourceFileSnapshot.ReadAsync(sourcePath, cancellationToken);
            }
            catch (SourceFileTooLargeException)
            {
                return new(XamlSourceEditStatus.Forbidden, "XAML source files larger than 5 MB are not writable.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                return new(XamlSourceEditStatus.Failed, "Could not read the XAML source file.");
            }

            var buildHash = element.SourceHash!;
            var currentShortHash = ComputeSourceHash(snapshot.Text);
            var currentContentHash = snapshot.RawHash;

            var matchesBuild = string.Equals(currentShortHash, buildHash, StringComparison.OrdinalIgnoreCase);
            var matchesTrackedEdit =
                EditStates.TryGetValue(sourcePath, out var state) &&
                string.Equals(state.BuildHash, buildHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(state.CurrentContentHash, currentContentHash, StringComparison.Ordinal);

            if (!matchesBuild && !matchesTrackedEdit)
            {
                return new(
                    XamlSourceEditStatus.Stale,
                    "The XAML file changed since the app was built or the last Inspector edit. Rebuild the app before applying this property.");
            }

            var buildText = matchesTrackedEdit ? state!.BuildText : snapshot.Text;
            var edit = TryReplaceLiteralAttribute(
                buildText,
                snapshot.Text,
                element.SourceLine.Value,
                element.SourceColumn.Value,
                propertyName,
                value);

            if (!edit.Success)
                return new(XamlSourceEditStatus.Unsupported, edit.Error);

            var updatedText = edit.UpdatedText!;
            var updatedContentHash = currentContentHash;
            if (!string.Equals(updatedText, snapshot.Text, StringComparison.Ordinal))
            {
                if (!IsWellFormedXml(updatedText))
                {
                    return new(
                        XamlSourceEditStatus.InvalidRequest,
                        "The property value would make the XAML document invalid.");
                }

                if (writeChanges)
                {
                    try
                    {
                        var writeResult = await snapshot.WriteIfUnchangedAsync(
                            sourcePath,
                            updatedText,
                            currentContentHash,
                            () => ValidateSourcePath(sourcePath),
                            cancellationToken);
                        if (writeResult.Status != XamlSourceEditStatus.Success)
                            return new(writeResult.Status, writeResult.Error);
                        updatedContentHash = writeResult.ContentHash!;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EncoderFallbackException or DecoderFallbackException or PlatformNotSupportedException)
                    {
                        return new(XamlSourceEditStatus.Failed, "Could not write the XAML source file.");
                    }
                }
            }

            var updatedShortHash = ComputeSourceHash(updatedText);
            if (!writeChanges)
            {
                return new(
                    XamlSourceEditStatus.Success,
                    File: sourcePath,
                    Line: element.SourceLine,
                    Column: element.SourceColumn,
                    SourceHash: updatedShortHash);
            }

            if (string.Equals(updatedShortHash, buildHash, StringComparison.OrdinalIgnoreCase))
                EditStates.TryRemove(sourcePath, out _);
            else
                EditStates[sourcePath] = new(buildHash, buildText, updatedContentHash);

            return new(
                XamlSourceEditStatus.Success,
                File: sourcePath,
                Line: element.SourceLine,
                Column: element.SourceColumn,
                SourceHash: updatedShortHash);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static string ComputeSourceHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();

    internal static bool IsSupportedPropertyName(string propertyName)
        => IsValidPropertyName(propertyName) && SupportedPropertyNames.Contains(propertyName);

    private static string ComputeContentHash(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private SourcePathValidation ValidateSourcePath(string sourceFile)
    {
        if (_project is null)
            return new(false, Error: "Source writing requires a broker-registered project identity.");

        string sourcePath;
        try
        {
            if (!Path.IsPathFullyQualified(sourceFile))
                return new(false, Error: "Only absolute XAML source paths are writable.");

            sourcePath = Path.GetFullPath(sourceFile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, Error: "The mapped XAML source path is invalid.");
        }

        try
        {
            if (!File.Exists(sourcePath) ||
                !string.Equals(Path.GetExtension(sourcePath), ".xaml", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, Error: "The mapped XAML source file does not exist.");
            }

            var attributes = File.GetAttributes(sourcePath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return new(false, Error: "Symbolic-link and reparse-point XAML files are not writable.");
            if ((attributes & FileAttributes.ReadOnly) != 0)
                return new(false, Error: "Read-only XAML source files are not writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, Error: "The mapped XAML source file is not accessible.");
        }

        var projectRoot = FindProjectRoot(sourcePath, _project, _sessionId);
        if (projectRoot is null || !IsUnderRoot(sourcePath, projectRoot))
        {
            return new(
                false,
                Error: "Only XAML files under the registered app project are writable.");
        }

        if (PathContainsReparsePoint(projectRoot, sourcePath))
        {
            return new(
                false,
                Error: "XAML files reached through symbolic links, junctions, or reparse points are not writable.");
        }

        return new(true, sourcePath);
    }

    /// <summary>
    /// Path containment and identity checks are shared with the Inspector's read-only source
    /// lookups. They live in <see cref="InspectorSourcePathSafety"/> so the write path and the read
    /// path can never drift into two different definitions of "inside the project".
    /// </summary>
    internal static string? FindProjectRoot(string sourcePath, string project, string? sessionId)
        => InspectorSourcePathSafety.FindProjectRoot(sourcePath, project, sessionId);

    internal static string ComputeDefaultSessionId(string projectPath)
        => InspectorSourcePathSafety.ComputeDefaultSessionId(projectPath);

    internal static bool IsUnderRoot(string path, string root)
        => InspectorSourcePathSafety.IsUnderRoot(path, root);

    internal static bool PathContainsReparsePoint(string root, string path)
        => InspectorSourcePathSafety.PathContainsReparsePoint(root, path);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsValidPropertyName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || propertyName.Length > 256)
            return false;

        foreach (var ch in propertyName)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not '_' and not '.' and not ':')
                return false;
        }

        return true;
    }

    private static bool IsValidSourceHash(string? sourceHash)
        => sourceHash is { Length: 16 } && sourceHash.All(Uri.IsHexDigit);

    private static LiteralAttributeEdit TryReplaceLiteralAttribute(
        string buildSource,
        string currentSource,
        int line,
        int column,
        string propertyName,
        string value)
    {
        if (!TryResolveCurrentElementOffset(buildSource, currentSource, line, column, out var elementStart))
        {
            return new(
                false,
                Error: "The mapped source location is stale. Rebuild the app before applying this property.");
        }

        if (elementStart >= currentSource.Length || currentSource[elementStart] != '<')
        {
            return new(
                false,
                Error: "The mapped source location is stale. Rebuild the app before applying this property.");
        }

        var cursor = elementStart + 1;
        if (cursor >= currentSource.Length || currentSource[cursor] is '/' or '!' or '?')
            return new(false, Error: "The mapped source location does not identify a XAML element.");

        while (cursor < currentSource.Length &&
               !char.IsWhiteSpace(currentSource[cursor]) &&
               currentSource[cursor] is not '>' and not '/')
        {
            cursor++;
        }

        while (cursor < currentSource.Length)
        {
            SkipWhitespace(currentSource, ref cursor);
            if (cursor >= currentSource.Length || currentSource[cursor] == '>' ||
                (currentSource[cursor] == '/' && cursor + 1 < currentSource.Length && currentSource[cursor + 1] == '>'))
            {
                return new(
                    false,
                    Error: $"Property '{propertyName}' is not declared as a direct XAML attribute.");
            }

            var nameStart = cursor;
            while (cursor < currentSource.Length &&
                   !char.IsWhiteSpace(currentSource[cursor]) &&
                   currentSource[cursor] is not '=' and not '>' and not '/')
            {
                cursor++;
            }

            if (cursor == nameStart)
                return new(false, Error: "The XAML element start tag could not be parsed safely.");

            var attributeName = currentSource[nameStart..cursor];
            SkipWhitespace(currentSource, ref cursor);
            if (cursor >= currentSource.Length || currentSource[cursor] != '=')
                return new(false, Error: "The XAML element start tag could not be parsed safely.");

            cursor++;
            SkipWhitespace(currentSource, ref cursor);
            if (cursor >= currentSource.Length || currentSource[cursor] is not ('"' or '\''))
                return new(false, Error: "Only quoted XAML attributes can be updated.");

            var quote = currentSource[cursor++];
            var valueStart = cursor;
            while (cursor < currentSource.Length && currentSource[cursor] != quote)
                cursor++;

            if (cursor >= currentSource.Length)
                return new(false, Error: "The XAML attribute value is not terminated.");

            var valueEnd = cursor;
            cursor++;

            if (!string.Equals(attributeName, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            var currentValue = WebUtility.HtmlDecode(currentSource[valueStart..valueEnd]);
            var trimmed = currentValue.TrimStart();
            if (trimmed.StartsWith('{') && !trimmed.StartsWith("{}", StringComparison.Ordinal))
            {
                return new(
                    false,
                    Error: $"Property '{propertyName}' uses a binding, resource, or markup extension and cannot be replaced safely.");
            }

            var escapedValue = EscapeAttributeValue(value, quote);
            var updated = string.Concat(
                currentSource.AsSpan(0, valueStart),
                escapedValue.AsSpan(),
                currentSource.AsSpan(valueEnd));
            return new(true, updated);
        }

        return new(false, Error: "The XAML element start tag could not be parsed safely.");
    }

    private static bool TryResolveCurrentElementOffset(
        string buildSource,
        string currentSource,
        int line,
        int column,
        out int elementStart)
    {
        elementStart = 0;
        try
        {
            var buildDocument = XDocument.Parse(buildSource, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var currentDocument = XDocument.Parse(currentSource, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            if (buildDocument.Root is null || currentDocument.Root is null)
                return false;

            var buildElement = buildDocument.Root
                .DescendantsAndSelf()
                .FirstOrDefault(element =>
                {
                    if (element is not IXmlLineInfo info || !info.HasLineInfo() || info.LineNumber != line)
                        return false;
                    // Generated maps point at the first name character; accept '<' for hand-built maps.
                    return info.LinePosition == column || info.LinePosition == column + 1;
                });
            if (buildElement is null)
                return false;

            var path = GetElementPath(buildElement);
            var currentElement = currentDocument.Root;
            foreach (var index in path)
            {
                currentElement = currentElement.Elements().ElementAtOrDefault(index);
                if (currentElement is null)
                    return false;
            }

            if (currentElement.Name != buildElement.Name ||
                currentElement is not IXmlLineInfo currentInfo ||
                !currentInfo.HasLineInfo() ||
                !TryGetOffset(currentSource, currentInfo.LineNumber, currentInfo.LinePosition, out elementStart))
            {
                return false;
            }

            if (elementStart < currentSource.Length && currentSource[elementStart] != '<' &&
                elementStart > 0 && currentSource[elementStart - 1] == '<')
            {
                elementStart--;
            }

            return elementStart < currentSource.Length && currentSource[elementStart] == '<';
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<int> GetElementPath(XElement element)
    {
        var path = new List<int>();
        while (element.Parent is XElement parent)
        {
            var index = 0;
            foreach (var sibling in parent.Elements())
            {
                if (ReferenceEquals(sibling, element))
                    break;
                index++;
            }
            path.Add(index);
            element = parent;
        }
        path.Reverse();
        return path;
    }

    private static bool IsWellFormedXml(string text)
    {
        try
        {
            XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string EscapeAttributeValue(string value, char quote)
    {
        var firstNonWhitespace = 0;
        while (firstNonWhitespace < value.Length && char.IsWhiteSpace(value[firstNonWhitespace]))
            firstNonWhitespace++;
        if (firstNonWhitespace < value.Length && value[firstNonWhitespace] == '{')
            value = value.Insert(firstNonWhitespace, "{}");

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '"':
                    builder.Append(quote == '"' ? "&quot;" : "\"");
                    break;
                case '\'':
                    builder.Append(quote == '\'' ? "&apos;" : "'");
                    break;
                case '\r': builder.Append("&#13;"); break;
                case '\n': builder.Append("&#10;"); break;
                case '\t': builder.Append("&#9;"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    private static bool TryGetOffset(string text, int line, int column, out int offset)
    {
        var lineStart = 0;
        var currentLine = 1;
        while (currentLine < line)
        {
            var newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                offset = 0;
                return false;
            }
            lineStart = newline + 1;
            currentLine++;
        }

        var newlineIndex = text.IndexOf('\n', lineStart);
        var lineEnd = newlineIndex < 0 ? text.Length : newlineIndex;
        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
            lineEnd--;

        offset = lineStart + column - 1;
        if (offset < lineStart || offset >= lineEnd)
            return false;

        return true;
    }

    private static void SkipWhitespace(string source, ref int cursor)
    {
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
    }

    private sealed record SourceEditState(string BuildHash, string BuildText, string CurrentContentHash);

    private readonly record struct SourcePathValidation(bool Success, string? Path = null, string? Error = null);

    private readonly record struct LiteralAttributeEdit(bool Success, string? UpdatedText = null, string? Error = null);

    private sealed class SourceFileSnapshot
    {
        private SourceFileSnapshot(string text, Encoding encoding, byte[] preamble, string rawHash)
        {
            Text = text;
            Encoding = encoding;
            Preamble = preamble;
            RawHash = rawHash;
        }

        public string Text { get; }
        public string RawHash { get; }
        private Encoding Encoding { get; }
        private byte[] Preamble { get; }

        public static async Task<SourceFileSnapshot> ReadAsync(string path, CancellationToken cancellationToken)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxSourceFileBytes)
                throw new SourceFileTooLargeException();

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var (encoding, preambleLength) = DetectEncoding(bytes);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new(text, encoding, bytes[..preambleLength], ComputeContentHash(bytes));
        }

        public async Task<ConditionalWriteResult> WriteIfUnchangedAsync(
            string path,
            string text,
            string expectedCurrentContentHash,
            Func<SourcePathValidation> validatePath,
            CancellationToken cancellationToken)
        {
            var body = Encoding.GetBytes(text);
            var bytes = new byte[Preamble.Length + body.Length];
            Preamble.CopyTo(bytes, 0);
            body.CopyTo(bytes, Preamble.Length);
            var updatedContentHash = ComputeContentHash(bytes);

            var directory = Path.GetDirectoryName(path)!;
            var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            var backupPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak");
            var rejectedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.rejected");
            var cleanupBackup = true;
            try
            {
                if (!OperatingSystem.IsWindows() &&
                    !await CloneUnixFileMetadataAsync(path, tempPath, cancellationToken))
                {
                    return new(
                        XamlSourceEditStatus.Forbidden,
                        "The XAML file metadata could not be preserved safely on this filesystem.");
                }

                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

                var pathValidation = validatePath();
                if (!pathValidation.Success)
                    return new(XamlSourceEditStatus.Forbidden, pathValidation.Error);

                var current = await ReadAsync(path, cancellationToken);
                if (!string.Equals(
                    current.RawHash,
                    expectedCurrentContentHash,
                    StringComparison.Ordinal))
                {
                    return new(
                        XamlSourceEditStatus.Stale,
                        "The XAML file changed while Inspector was applying the property. No source changes were written.");
                }

                CopyFileMetadata(path, tempPath);
                // ReplaceFile can fail after moving the original to the backup. From this point on,
                // never delete that backup unless success or an explicit restoration is verified.
                cleanupBackup = false;
                File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: false);

                // The backup is the exact file version replaced by the atomic operation. If it is
                // not the version we validated, an external save won the race. Restore that save
                // unless the destination changed again after our replacement.
                var replacedContentHash = await ComputeFileContentHashAsync(backupPath, cancellationToken);
                if (!string.Equals(replacedContentHash, expectedCurrentContentHash, StringComparison.Ordinal))
                {
                    if (await TryRestoreBackupAsync(
                        path,
                        backupPath,
                        rejectedPath,
                        updatedContentHash,
                        cancellationToken))
                    {
                        cleanupBackup = true;
                        return new(
                            XamlSourceEditStatus.Stale,
                            "The XAML file changed while Inspector was applying the property. The external version was restored.");
                    }

                    return new(
                        XamlSourceEditStatus.Failed,
                        $"The XAML file changed concurrently. Inspector left a recovery copy at '{backupPath}'.");
                }

                cleanupBackup = true;
                TryDelete(backupPath);
                return new(XamlSourceEditStatus.Success, ContentHash: updatedContentHash);
            }
            catch (Exception ex) when (
                !cleanupBackup &&
                ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                if (await TryRestoreBackupAsync(
                    path,
                    backupPath,
                    rejectedPath,
                    updatedContentHash,
                    cancellationToken))
                {
                    cleanupBackup = true;
                    return new(
                        XamlSourceEditStatus.Failed,
                        "Inspector could not verify the source write, so the original file was restored.");
                }

                return new(
                    XamlSourceEditStatus.Failed,
                    $"Inspector could not verify the source write. A recovery copy remains at '{backupPath}'.");
            }
            finally
            {
                TryDelete(tempPath);
                TryDelete(rejectedPath);
                if (cleanupBackup)
                    TryDelete(backupPath);
            }
        }

        private static void CopyFileMetadata(string sourcePath, string destinationPath)
        {
            if (!OperatingSystem.IsWindows())
                return;

            // ReplaceFile preserves the destination's Windows ACL and core metadata. Mirror
            // settable flags on the temporary file as well for filesystems that do not.
            const FileAttributes copyable =
                FileAttributes.Hidden |
                FileAttributes.System |
                FileAttributes.Archive |
                FileAttributes.NotContentIndexed;
            var attributes = File.GetAttributes(sourcePath) & copyable;
            File.SetAttributes(destinationPath, attributes == 0 ? FileAttributes.Normal : attributes);
        }

        private static async Task<bool> CloneUnixFileMetadataAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/cp",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (OperatingSystem.IsLinux())
            {
                startInfo.ArgumentList.Add("--preserve=all");
                startInfo.ArgumentList.Add("--");
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo.ArgumentList.Add("-p");
            }
            else
            {
                return false;
            }
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(destinationPath);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }

        private static async Task<bool> TryRestoreBackupAsync(
            string destinationPath,
            string backupPath,
            string rejectedPath,
            string inspectorContentHash,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(backupPath))
                return false;

            try
            {
                if (!File.Exists(destinationPath))
                {
                    File.Move(backupPath, destinationPath);
                    return true;
                }

                var destinationContentHash = await ComputeFileContentHashAsync(destinationPath, cancellationToken);
                if (!string.Equals(destinationContentHash, inspectorContentHash, StringComparison.Ordinal))
                    return false;

                File.Replace(backupPath, destinationPath, rejectedPath, ignoreMetadataErrors: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static async Task<string> ComputeFileContentHashAsync(
            string path,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
                return (new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
                return (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
                return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 3);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
                return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
                return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2);

            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), 0);
        }
    }

    private readonly record struct ConditionalWriteResult(
        XamlSourceEditStatus Status,
        string? Error = null,
        string? ContentHash = null);

    private sealed class SourceFileTooLargeException : Exception
    {
    }
}
