using System.IO.Compression;
using System.Text;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class NormalizedPayloadDigestTests
{
    private const string SessionA = "flow0123456789abcdef0123456789abcd";
    private const string SessionB = "flowfedcba9876543210fedcba98765432";

    [Fact]
    public async Task Compute_TwoArchivesDifferingOnlyBySessionId_ProducesSameDigest()
    {
        var first = await ComputeAsync(SessionA, entries: BuildEntries(SessionA));
        var second = await ComputeAsync(SessionB, entries: BuildEntries(SessionB));

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Compute_TwoArchivesDifferingOnlyBySignatureMaterial_ProducesSameDigest()
    {
        var payload = BuildEntries(SessionA);
        var withFirstSignature = new Dictionary<string, byte[]>(payload)
        {
            ["META-INF/MANIFEST.MF"] = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\nfirst\n"),
            ["META-INF/ANDROIDD.RSA"] = [1, 2, 3, 4],
            ["META-INF/ANDROIDD.SF"] = Encoding.UTF8.GetBytes("first-signature-file"),
        };
        var withSecondSignature = new Dictionary<string, byte[]>(payload)
        {
            ["META-INF/MANIFEST.MF"] = Encoding.UTF8.GetBytes("Manifest-Version: 1.0\nsecond\n"),
            ["META-INF/ANDROIDD.RSA"] = [9, 9, 9, 9, 9, 9],
            ["META-INF/ANDROIDD.SF"] = Encoding.UTF8.GetBytes("second-signature-file"),
        };

        var first = await ComputeAsync(SessionA, withFirstSignature);
        var second = await ComputeAsync(SessionA, withSecondSignature);

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Compute_PayloadByteChange_ProducesDifferentDigest()
    {
        var baseline = BuildEntries(SessionA);
        var mutated = new Dictionary<string, byte[]>(baseline)
        {
            ["classes.dex"] = Encoding.UTF8.GetBytes("dex-payload-changed"),
        };

        var first = await ComputeAsync(SessionA, baseline);
        var second = await ComputeAsync(SessionA, mutated);

        Assert.NotNull(first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Compute_NestedMetaInfEntry_IsStillHashed()
    {
        var baseline = BuildEntries(SessionA);
        var withNested = new Dictionary<string, byte[]>(baseline)
        {
            ["META-INF/services/provider.RSA"] = Encoding.UTF8.GetBytes("nested-payload"),
        };

        var first = await ComputeAsync(SessionA, baseline);
        var second = await ComputeAsync(SessionA, withNested);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Compute_EntryRenamed_ProducesDifferentDigest()
    {
        var baseline = BuildEntries(SessionA);
        var renamed = new Dictionary<string, byte[]>(baseline);
        renamed["assets/renamed.txt"] = renamed["assets/data.txt"];
        renamed.Remove("assets/data.txt");

        Assert.NotEqual(await ComputeAsync(SessionA, baseline), await ComputeAsync(SessionA, renamed));
    }

    [Fact]
    public async Task TryCompute_NonZipArtifact_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".apk");
        await File.WriteAllTextAsync(path, "this is not a zip archive");
        try
        {
            Assert.Null(await NormalizedPayloadDigest.TryComputeAsync(path, SessionA));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TryCompute_MissingArtifact_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".apk");
        Assert.Null(await NormalizedPayloadDigest.TryComputeAsync(path, SessionA));
    }

    [Theory]
    [InlineData("META-INF/MANIFEST.MF", true)]
    [InlineData("META-INF/ANDROIDD.SF", true)]
    [InlineData("META-INF/ANDROIDD.RSA", true)]
    [InlineData("META-INF/SIGNER.DSA", true)]
    [InlineData("META-INF/SIGNER.EC", true)]
    [InlineData("META-INF/com/android/build/gradle/app-metadata.properties", false)]
    [InlineData("META-INF/services/provider.RSA", false)]
    [InlineData("AndroidManifest.xml", false)]
    [InlineData("classes.dex", false)]
    public void IsSignatureMaterial_ExcludesOnlyTopLevelV1Material(string entryName, bool expected)
        => Assert.Equal(expected, NormalizedPayloadDigest.IsSignatureMaterial(entryName));

    [Fact]
    public void CreateSessionIdPatterns_ShortValue_IsIgnored()
        => Assert.Empty(NormalizedPayloadDigest.CreateSessionIdPatterns("abc"));

    [Fact]
    public void CreateSessionIdPatterns_ProducesUtf8AndUtf16Encodings()
    {
        var patterns = NormalizedPayloadDigest.CreateSessionIdPatterns(SessionA);

        Assert.Equal(2, patterns.Length);
        Assert.Equal(Encoding.UTF8.GetBytes(SessionA), patterns[0]);
        Assert.Equal(Encoding.Unicode.GetBytes(SessionA), patterns[1]);
    }

    [Fact]
    public void NeutralizeSessionId_ReplacesEveryOccurrenceInPlace()
    {
        var content = Encoding.UTF8.GetBytes($"a{SessionA}b{SessionA}c");
        var original = content.Length;

        NormalizedPayloadDigest.NeutralizeSessionId(content, NormalizedPayloadDigest.CreateSessionIdPatterns(SessionA));

        Assert.Equal(original, content.Length);
        var text = Encoding.UTF8.GetString(content);
        Assert.DoesNotContain(SessionA, text, StringComparison.Ordinal);
        Assert.StartsWith("a#", text, StringComparison.Ordinal);
        Assert.EndsWith("#c", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NeutralizeSessionId_LeavesUnrelatedBytesUntouched()
    {
        var content = Encoding.UTF8.GetBytes("unrelated-payload-bytes");
        var expected = content.ToArray();

        NormalizedPayloadDigest.NeutralizeSessionId(content, NormalizedPayloadDigest.CreateSessionIdPatterns(SessionA));

        Assert.Equal(expected, content);
    }

    [Fact]
    public async Task Compute_EmptyArchive_ReturnsNull()
    {
        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        Assert.Null(await NormalizedPayloadDigest.ComputeAsync(archive, SessionA));
    }

    private static Dictionary<string, byte[]> BuildEntries(string sessionId) => new(StringComparer.Ordinal)
    {
        // Android stores manifest strings as UTF-16, which is where the injected session id lands.
        ["AndroidManifest.xml"] = Encoding.Unicode.GetBytes($"manifest:{sessionId}:end"),
        ["classes.dex"] = Encoding.UTF8.GetBytes("dex-payload"),
        ["assets/data.txt"] = Encoding.UTF8.GetBytes($"asset carrying {sessionId} too"),
        ["resources.arsc"] = [7, 7, 7, 7],
    };

    private static async Task<string?> ComputeAsync(string sessionId, Dictionary<string, byte[]> entries)
    {
        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var entry = writer.CreateEntry(name, CompressionLevel.NoCompression);
                await using var stream = entry.Open();
                await stream.WriteAsync(content);
            }
        }

        buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        return await NormalizedPayloadDigest.ComputeAsync(archive, sessionId);
    }
}
