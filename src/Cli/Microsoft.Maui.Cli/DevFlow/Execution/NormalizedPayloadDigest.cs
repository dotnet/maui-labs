using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// Computes a signing-insensitive payload identity for a packaged app artifact.
/// </summary>
/// <remarks>
/// <para>
/// The signed package digest identifies an <em>occurrence</em> of a build, not the build itself.
/// Two deployments of the same sources produce different signed packages for reasons that have
/// nothing to do with what the app will do at runtime: the package is re-signed with fresh
/// signature blocks, the ZIP entries carry per-build timestamps, and DevFlow injects its own agent
/// session id into the package so the CLI can bind to exactly the agent it launched. Comparing
/// signed digests across two occurrences therefore always fails, which is why <c>flow reproduce</c>
/// has never been able to match one occurrence to another.
/// </para>
/// <para>
/// The normalized payload digest covers every entry of the package except two precisely bounded
/// exclusions, and nothing else is relaxed:
/// </para>
/// <list type="bullet">
/// <item>
/// JAR/APK v1 signature material (<c>META-INF/MANIFEST.MF</c> and the <c>.SF</c>/<c>.RSA</c>/
/// <c>.DSA</c>/<c>.EC</c> blocks beside it). These files are derived from the payload rather than
/// part of it. The v2/v3 APK Signing Block sits outside the ZIP central directory and is excluded
/// by construction because only entries are hashed.
/// </item>
/// <item>
/// DevFlow's own agent session id, wherever it appears as raw UTF-8 or UTF-16 bytes inside a hashed
/// entry, replaced in place by a same-length placeholder. This value is derived by this process from
/// the build inputs immediately before the build and injected by DevFlow's own MSBuild targets; it is
/// instrumentation DevFlow added, not app payload. Neutralizing it is length preserving and value
/// specific, so any other byte of the manifest, of any dex, resource, asset, or native library still
/// changes the digest. ZIP entry metadata other than the name and the uncompressed length —
/// compression method, timestamps, extra fields — is outside the digest by construction.
/// </item>
/// </list>
/// <para>
/// The digest is a diagnostic fact, not a cross-occurrence identity. It answers "did anything
/// outside signature material and DevFlow's own injected session id change?", and it is published
/// on the run report so a reproduction can report that question's answer. That question is only
/// well posed between builds that share a session id. Neutralization substitutes the identity's
/// literal bytes, but the identity is also an input to deterministic compilation, so an assembly
/// built under a different one differs in its MVID, its deterministic PE timestamp and its debug
/// directory content id — bytes no length-preserving substitution can reach. Because
/// <c>FlowExecutionCoordinator.CreateBuildScopedAgentSessionId</c> keys the identity on the absolute
/// project path, this digest is comparable within one project path on one machine and not across
/// machines. It is deliberately not wired to rescue a signed-digest mismatch, and the measured
/// reason is that the underlying package is not reproducible even within that scope.
/// </para>
/// <para>
/// An earlier note here claimed that two ordinary incremental <c>dotnet build</c> invocations were
/// byte-identical across all 879 non-signature entries, and concluded that the instability lived in
/// the isolated per-invocation build <c>flow run</c> performs. That comparison was not like for
/// like. An ordinary <c>dotnet build</c> of a Debug Android app produces a fast-deployment package
/// that does not embed the app assemblies at all, so it is stable precisely because it does not
/// contain the bytes that change. <c>flow run</c> builds through
/// <c>Microsoft.Maui.Build.AppProjectReference</c>, which sets <c>EmbedAssembliesIntoApk=true</c>,
/// producing the roughly four-times-larger embedded-assembly package that a device install actually
/// needs. Three consecutive no-op builds of that embedded package, at the project's own output path
/// with no DevFlow host project, no redirected <c>OutputPath</c> and a fixed session id, produced
/// three distinct packages: two differed only in ZIP entry timestamps (the DOS mod-time and the
/// Unix <c>UT</c> extra field of roughly 1,872 <c>lib/&lt;abi&gt;/lib_*.dll.so</c> entries, 3,744
/// two-byte ranges, with every entry's uncompressed content identical and in the same order), and
/// the third additionally carried 354 extra Kotlin metadata entries
/// (<c>commonMain/default/linkdata/**.knm</c>) contributed when the referenced Android class
/// libraries re-extracted their AAR imports. Package non-reproducibility is therefore a property of
/// Android packaging for this package kind, not of DevFlow's build isolation, and no build-root
/// caching, locking or output-path stabilization changes it.
/// </para>
/// <para>
/// The consequence for this type is that normalization cannot close the gap either: the timestamp
/// drift is already outside the digest by construction, but the Kotlin metadata entries are real
/// payload, and excluding them would mean excluding real payload. This normalization therefore
/// stops where the evidence stops. Measured after the session identity became build-scoped, two
/// consecutive <c>flow run</c> invocations of one flow on one Android emulator against one clean
/// commit still published two different normalized payload digests, so the entry-set contributor
/// alone is sufficient to keep agreement unobserved.
/// </para>
/// <para>
/// A caller that cannot compute a digest gets <see langword="null"/>, and every consuming gate
/// keeps refusing, which is the correct fail-closed default.
/// </para>
/// </remarks>
internal static class NormalizedPayloadDigest
{
    /// <summary>Identifies the normalization rules, so a digest is never compared across revisions.</summary>
    internal const string AlgorithmId = "maui-devflow/normalized-payload/1";

    private const byte PlaceholderByte = (byte)'#';
    private const int MaximumEntries = 65_536;
    private const long MaximumEntryBytes = 128L * 1024 * 1024;
    private const long MaximumTotalBytes = 4L * 1024 * 1024 * 1024;

    private static readonly string[] SignatureExtensions = [".SF", ".RSA", ".DSA", ".EC"];

    /// <summary>
    /// Returns the normalized payload digest for a ZIP-shaped package, or <see langword="null"/>
    /// when the artifact is not a readable ZIP-shaped package.
    /// </summary>
    /// <param name="path">The resolved artifact path.</param>
    /// <param name="agentSessionId">The DevFlow agent session id injected into this build.</param>
    /// <param name="cancellationToken">Cancels a long-running package read.</param>
    public static async Task<string?> TryComputeAsync(
        string path,
        string? agentSessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            await using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                });
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return await ComputeAsync(archive, agentSessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or OutOfMemoryException)
        {
            return null;
        }
    }

    internal static async Task<string?> ComputeAsync(
        ZipArchive archive,
        string? agentSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var entries = archive.Entries
            .Where(static entry => !IsSignatureMaterial(entry.FullName))
            .OrderBy(static entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length == 0 || entries.Length > MaximumEntries)
            return null;

        var patterns = CreateSessionIdPatterns(agentSessionId);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes(AlgorithmId + "\n"));

        var total = 0L;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Entries are neutralized in one pass, so an entry that will not fit in memory is
            // refused outright rather than hashed under weaker rules.
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
                return null;
            total += entry.Length;
            if (total > MaximumTotalBytes)
                return null;

            var content = new byte[(int)entry.Length];
            await using (var stream = entry.Open())
            {
                var offset = 0;
                while (offset < content.Length)
                {
                    var read = await stream.ReadAsync(
                        content.AsMemory(offset),
                        cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                        return null;
                    offset += read;
                }
                if (await stream.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false) != 0)
                    return null;
            }

            NeutralizeSessionId(content, patterns);
            digest.AppendData(Encoding.UTF8.GetBytes(entry.FullName));
            digest.AppendData("\u001f"u8);
            digest.AppendData(Encoding.UTF8.GetBytes(
                entry.Length.ToString(CultureInfo.InvariantCulture)));
            digest.AppendData("\u001f"u8);
            digest.AppendData(SHA256.HashData(content));
            digest.AppendData("\n"u8);
        }

        return "sha256:" + Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>Returns whether a ZIP entry is JAR/APK v1 signature material rather than payload.</summary>
    internal static bool IsSignatureMaterial(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            return false;
        if (!entryName.StartsWith("META-INF/", StringComparison.Ordinal))
            return false;
        var name = entryName["META-INF/".Length..];
        if (name.Contains('/', StringComparison.Ordinal))
            return false;
        if (string.Equals(name, "MANIFEST.MF", StringComparison.OrdinalIgnoreCase))
            return true;
        return SignatureExtensions.Any(extension =>
            name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the byte encodings of the agent session id that could appear inside a package entry.
    /// Android stores manifest strings as UTF-16, managed metadata stores them as UTF-8.
    /// </summary>
    internal static byte[][] CreateSessionIdPatterns(string? agentSessionId)
    {
        var value = agentSessionId?.Trim();
        // Too short a value would neutralize unrelated bytes; DevFlow session ids are far longer.
        if (string.IsNullOrEmpty(value) || value.Length < 8)
            return [];
        return
        [
            Encoding.UTF8.GetBytes(value),
            Encoding.Unicode.GetBytes(value),
        ];
    }

    internal static void NeutralizeSessionId(Span<byte> content, byte[][] patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.Length == 0 || pattern.Length > content.Length)
                continue;
            var offset = 0;
            while (offset <= content.Length - pattern.Length)
            {
                var index = content[offset..].IndexOf(pattern);
                if (index < 0)
                    break;
                content.Slice(offset + index, pattern.Length).Fill(PlaceholderByte);
                offset += index + pattern.Length;
            }
        }
    }
}
