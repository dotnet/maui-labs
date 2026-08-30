using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The device host is an untrusted, optional peer, and a screen recording is the one place where it
/// hands DevFlow a filesystem path that DevFlow then reads, copies, hashes, publishes as evidence,
/// and serves back over HTTP.
/// <para>
/// If that path were taken on trust, a compromised or merely buggy host could answer <c>stop</c>
/// with <c>~/.mauidevflow/broker.json</c> or a private key and have DevFlow exfiltrate it as a
/// "device recording". These tests pin the containment that makes that impossible.
/// </para>
/// </summary>
public class DeviceRecordingPathContainmentTests
{
    /// <summary>
    /// The canonical spelling of a path on this machine. Not the same as
    /// <see cref="Path.GetFullPath(string)"/>: the temp directory is reached through a link on
    /// macOS (<c>/var</c> → <c>/private/var</c>) and can be on Windows, so an expectation written
    /// as a lexical full path would encode a namespace the guard deliberately does not return.
    /// </summary>
    private static string? Canonical(string path)
        => DeviceRecordingPathGuard.ResolveFinalPath(path);

    [Fact]
    public void AcceptsAFileDevFlowItselfNamedInsideItsOwnRecordingRoot()
    {
        var path = Path.Combine(MobileCanvasDeviceSurface.RecordingRoot, "run.mp4");

        var resolved = MobileCanvasDeviceSurface.ResolveContainedRecordingPath(path);

        Assert.Equal(Canonical(path), resolved);
    }

    [Fact]
    public void RefusesAPathOutsideTheRecordingRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), "somewhere-else.mp4");

        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(outside));
    }

    /// <summary>
    /// Containment is checked after normalization, so a traversal that lexically starts inside the
    /// root but resolves outside it is refused.
    /// </summary>
    [Fact]
    public void RefusesATraversalOutOfTheRecordingRoot()
    {
        var traversal = Path.Combine(
            MobileCanvasDeviceSurface.RecordingRoot,
            "..",
            "..",
            "broker.json.mp4");

        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(traversal));
    }

    /// <summary>
    /// The root itself is not a file, and a sibling directory that merely shares the root's name
    /// prefix is not inside it.
    /// </summary>
    [Fact]
    public void RefusesTheRootItselfAndAPrefixSibling()
    {
        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(
            MobileCanvasDeviceSurface.RecordingRoot));
        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(
            MobileCanvasDeviceSurface.RecordingRoot + "-elsewhere" + Path.DirectorySeparatorChar + "run.mp4"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RefusesAnAbsentPath(string? reported)
        => Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(reported));

    /// <summary>
    /// Even inside the root, only the media type DevFlow asked for is accepted. A host that writes
    /// something else there is reporting an artifact DevFlow never requested.
    /// </summary>
    [Fact]
    public void RefusesAContainedFileWithTheWrongExtension()
    {
        var path = Path.Combine(MobileCanvasDeviceSurface.RecordingRoot, "run.json");

        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(path));
    }

    /// <summary>
    /// The root lives under DevFlow's own temp namespace rather than anywhere the host chooses, so
    /// the containment check has something fixed to compare against.
    /// </summary>
    [Fact]
    public void RecordingRootIsOwnedByDevFlow()
    {
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(MobileCanvasDeviceSurface.RecordingRoot),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maui-devflow", MobileCanvasDeviceSurface.RecordingRoot, StringComparison.Ordinal);
    }

    /// <summary>
    /// A trusted root of <c>C:\</c> or <c>/</c> is not a trust boundary — it contains every
    /// <c>.mp4</c> on the machine — so it is refused outright rather than honoured. The way this
    /// happens in practice is banal: a configured artifact directory arrives empty or as a bare
    /// drive letter, and the guard would then cheerfully vouch for any file the untrusted host named.
    /// </summary>
    [Fact]
    public void RefusesATrustedRootThatIsAFilesystemRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        // The premise: this file genuinely is inside the drive root, so only the root check can be
        // what refuses it.
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(real));
        Assert.False(string.IsNullOrEmpty(driveRoot));

        Assert.Null(DeviceRecordingPathGuard.Resolve(real, driveRoot!));
        Assert.Null(new TrustedRootRecordingPathAuthority(driveRoot!).ResolveContainedRecordingPath(real));
        Assert.Null(DeviceRecordingCapture.ForOwnedCopy(real, driveRoot!));
    }

    /// <summary>
    /// The same refusal for every spelling of a root: a trailing separator, or its absence on
    /// Windows, must not be the difference between refusing and admitting the whole volume.
    /// </summary>
    [Fact]
    public void RefusesEverySpellingOfAFilesystemRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(real))!;

        foreach (var spelling in Spellings(driveRoot))
        {
            Assert.True(
                DeviceRecordingPathGuard.IsFilesystemRoot(spelling),
                $"'{spelling}' names a filesystem root.");
            Assert.Null(DeviceRecordingPathGuard.Resolve(real, spelling));
        }

        static IEnumerable<string> Spellings(string root)
        {
            yield return root;
            var trimmed = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length > 0 && !string.Equals(trimmed, root, StringComparison.Ordinal))
                yield return trimmed;
        }
    }

    /// <summary>
    /// The check is about roots, not about being short: an ordinary directory one level below the
    /// volume root is still a usable boundary, so the refusal above is not over-broad.
    /// </summary>
    [Fact]
    public void AcceptsARootThatIsMerelyCloseToTheFilesystemRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(real))!;

        // A single segment below the volume root is not a root, and neither is the scratch root the
        // rest of these tests rely on.
        Assert.False(DeviceRecordingPathGuard.IsFilesystemRoot(Path.Combine(driveRoot, "somewhere")));
        Assert.False(DeviceRecordingPathGuard.IsFilesystemRoot(scratch.Root));
        Assert.Equal(Canonical(real), DeviceRecordingPathGuard.Resolve(real, scratch.Root));
    }

    /// <summary>
    /// A root that only becomes a filesystem root after normalization is refused for the same
    /// reason. This is the shape the mistake actually takes: a configured directory joined with a
    /// traversal, or with an empty segment, quietly collapsing onto the volume.
    /// </summary>
    [Fact]
    public void RefusesATrustedRootThatOnlyNormalizesToAFilesystemRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(real))!;

        // Climb back out of the scratch root to the volume with traversal segments only.
        var depth = Path.GetFullPath(scratch.Root)[driveRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Length;
        var collapsing = Path.Combine(
            [scratch.Root, .. Enumerable.Repeat("..", depth)]);
        Assert.Equal(
            Path.GetFullPath(driveRoot),
            Path.GetFullPath(collapsing).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);

        Assert.Null(DeviceRecordingPathGuard.Resolve(real, collapsing));
    }

    /// <summary>
    /// A symlink planted inside the trusted root passes every lexical check while the bytes read
    /// come from wherever it points. Containment is therefore resolved to the final target.
    /// </summary>
    [Fact]
    public void RefusesASymbolicLinkInsideTheRootThatEscapesIt()
    {
        using var scratch = new ScratchRoot();
        var secret = scratch.WriteOutside("stolen.mp4", "secret");
        var link = Path.Combine(scratch.Root, "run.mp4");
        if (!FileSystemLinks.CreatedFileLink(link, secret))
            return;

        // The premise of the whole case: the link is really there, and really points out of the
        // root. Without this the assertion below could pass because nothing was created at all.
        Assert.True(File.Exists(link));
        Assert.Equal(Canonical(secret), Canonical(link));

        Assert.Null(DeviceRecordingPathGuard.Resolve(link, scratch.Root));
    }

    /// <summary>
    /// A directory link in the middle of the path is the same escape one level up: the leaf never
    /// looks like a link at all, so the whole path has to be resolved segment by segment.
    /// </summary>
    [Fact]
    public void RefusesADirectoryLinkInsideTheRootThatEscapesIt()
    {
        using var scratch = new ScratchRoot();
        var target = scratch.CreateOutsideDirectory("elsewhere");
        File.WriteAllText(Path.Combine(target, "run.mp4"), "secret");
        var link = Path.Combine(scratch.Root, "nested");
        // Windows can always make a junction unprivileged and Unix can always make a symlink, so a
        // silent skip would hide the very reparse point this guard exists for.
        FileSystemLinks.CreateDirectoryLink(link, target);

        Assert.Null(DeviceRecordingPathGuard.Resolve(
            Path.Combine(link, "run.mp4"),
            scratch.Root));
    }

    /// <summary>
    /// A link that stays inside the root is still a recording: the rule is containment of the
    /// final target, not a ban on links.
    /// </summary>
    [Fact]
    public void AcceptsALinkWhoseFinalTargetStaysInsideTheRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "real.mp4");
        File.WriteAllText(real, "frames");
        var link = Path.Combine(scratch.Root, "alias.mp4");
        if (!FileSystemLinks.CreatedFileLink(link, real))
            return;

        Assert.Equal(
            Canonical(real),
            DeviceRecordingPathGuard.Resolve(link, scratch.Root));
    }

    /// <summary>
    /// The same acceptance through a directory-level reparse point, which every supported platform
    /// can create without elevation. It keeps the "links inside the root are fine" half of the rule
    /// exercised on Windows sessions where a file symlink is not creatable at all.
    /// </summary>
    [Fact]
    public void AcceptsARecordingBehindADirectoryLinkThatStaysInsideTheRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "inner");
        Directory.CreateDirectory(real);
        var recording = Path.Combine(real, "run.mp4");
        File.WriteAllText(recording, "frames");
        var link = Path.Combine(scratch.Root, "alias");
        FileSystemLinks.CreateDirectoryLink(link, real);

        Assert.Equal(
            Canonical(recording),
            DeviceRecordingPathGuard.Resolve(Path.Combine(link, "run.mp4"), scratch.Root));
    }

    /// <summary>
    /// A trusted root that is itself reached through a link still contains its own files. Resolving
    /// only the candidate would make every recording under such a root look like an escape.
    /// </summary>
    [Fact]
    public void AcceptsARecordingWhenTheTrustedRootIsItselfReachedThroughALink()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var rootLink = Path.Combine(scratch.Parent, "root-alias");
        FileSystemLinks.CreateDirectoryLink(rootLink, scratch.Root);

        Assert.Equal(
            Canonical(real),
            DeviceRecordingPathGuard.Resolve(Path.Combine(rootLink, "run.mp4"), rootLink));
    }

    /// <summary>
    /// Resolution is idempotent: the path this guard returns is accepted again, unchanged, against
    /// the same root. Callers re-prove containment on the handle they are about to read, so a guard
    /// that refused its own output would drop every recording on a machine whose root is reached
    /// through a link.
    /// </summary>
    [Fact]
    public void ResolvingItsOwnResultAgainReturnsTheSamePath()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var rootLink = Path.Combine(scratch.Parent, "root-alias");
        // Not conditional on the platform: a symlink is unprivileged on Unix and the Windows
        // fallback is a junction, which any user can create. A silent skip here would report a
        // pass while never exercising the linked root this test exists for.
        FileSystemLinks.CreateDirectoryLink(rootLink, scratch.Root);

        var first = DeviceRecordingPathGuard.Resolve(Path.Combine(rootLink, "run.mp4"), rootLink);
        var second = DeviceRecordingPathGuard.Resolve(first, rootLink);

        Assert.Equal(Canonical(real), first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// The plain case has to stay idempotent too, so the property is not an artefact of a linked
    /// root: a root with no reparse point anywhere in it round-trips as well.
    /// </summary>
    [Fact]
    public void ResolvingItsOwnResultAgainReturnsTheSamePathForAnUnlinkedRoot()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");

        var first = DeviceRecordingPathGuard.Resolve(real, scratch.Root);
        var second = DeviceRecordingPathGuard.Resolve(first, scratch.Root);

        Assert.Equal(Canonical(real), first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// The macOS <c>/var</c> case, reproduced with a scratch directory: the trusted root is the
    /// link (<c>/var/…</c>) while the host reports the file in the resolved namespace
    /// (<c>/private/var/…</c>). Both spellings name the same bytes, so both must be accepted —
    /// and the escape they could be confused with must still be refused.
    /// </summary>
    [Fact]
    public void AcceptsEitherSpellingOfALinkedRootWithoutWideningIt()
    {
        using var scratch = new ScratchRoot();
        // "private" stands in for the real directory a platform link points at; "var" is the
        // link callers name it by.
        var privateRoot = Path.Combine(scratch.Parent, "private", "recordings");
        Directory.CreateDirectory(privateRoot);
        var real = Path.Combine(privateRoot, "run.mp4");
        File.WriteAllText(real, "frames");
        var varRoot = Path.Combine(scratch.Parent, "var-recordings");
        FileSystemLinks.CreateDirectoryLink(varRoot, privateRoot);

        // Reported through the link, and reported already resolved: one answer for both.
        Assert.Equal(
            Canonical(real),
            DeviceRecordingPathGuard.Resolve(Path.Combine(varRoot, "run.mp4"), varRoot));
        Assert.Equal(
            Canonical(real),
            DeviceRecordingPathGuard.Resolve(real, varRoot));

        // Resolving the root is not a licence to accept its neighbours: a sibling of the resolved
        // root, and a sibling of the link, are both still outside.
        var outsidePrivate = Path.Combine(scratch.Parent, "private", "elsewhere.mp4");
        File.WriteAllText(outsidePrivate, "secret");
        Assert.Null(DeviceRecordingPathGuard.Resolve(outsidePrivate, varRoot));
        Assert.Null(DeviceRecordingPathGuard.Resolve(
            Path.Combine(scratch.Parent, "var-recordings-elsewhere", "run.mp4"),
            varRoot));
    }

    /// <summary>
    /// On Windows a colon after the drive specifier names an NTFS alternate data stream, so the
    /// bytes read are not the file the metadata describes. It is refused on every platform, since
    /// no recording DevFlow names ever contains one.
    /// </summary>
    [Fact]
    public void RefusesAWindowsAlternateDataStream()
    {
        var path = Path.Combine(MobileCanvasDeviceSurface.RecordingRoot, "run.mp4:hidden.mp4");

        if (!OperatingSystem.IsWindows())
        {
            // A colon is an ordinary filename character elsewhere, so only the Windows rule is
            // asserted here; the guard simply never produces such a name itself.
            Assert.False(DeviceRecordingPathGuard.HasAlternateDataStream(Path.GetFullPath(path)));
            return;
        }

        Assert.True(DeviceRecordingPathGuard.HasAlternateDataStream(Path.GetFullPath(path)));
        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(path));
        Assert.Null(MobileCanvasDeviceSurface.ResolveContainedRecordingPath(
            Path.Combine(MobileCanvasDeviceSurface.RecordingRoot, "run.mp4::$DATA")));
    }

    /// <summary>
    /// A capture is a decision, not a claim that a decision was made. Handing the constructor a
    /// path its own authority refuses is a defect in the caller — the alternative is a capture that
    /// travels several hops advertising an authority that never vouched for it, and is refused only
    /// at the point something tries to read the bytes.
    /// </summary>
    [Fact]
    public void ACaptureCannotBeBuiltFromAPathItsAuthorityRefuses()
    {
        using var scratch = new ScratchRoot();
        var outside = scratch.WriteOutside("run.mp4", "frames");
        var authority = new TrustedRootRecordingPathAuthority(scratch.Root);

        Assert.Throws<ArgumentException>(() => new DeviceRecordingCapture(outside, authority));

        // The same refusal as an ordinary answer, for the hops where "there is no recording" is
        // expected rather than exceptional.
        Assert.Null(DeviceRecordingCapture.TryCreate(outside, authority));
        Assert.Null(DeviceRecordingCapture.TryCreate(null, authority));
    }

    /// <summary>
    /// What the capture carries is the authority's own canonical spelling, not the caller's. A
    /// capture that stored the lexical path would make the next hop's re-validation compare two
    /// different namespaces and refuse a recording that is genuinely contained.
    /// </summary>
    [Fact]
    public void ACaptureStoresTheCanonicalSpellingItsAuthorityVouchedFor()
    {
        using var scratch = new ScratchRoot();
        var real = Path.Combine(scratch.Root, "run.mp4");
        File.WriteAllText(real, "frames");
        var lexical = Path.Combine(scratch.Root, "nested", "..", "run.mp4");
        var authority = new TrustedRootRecordingPathAuthority(scratch.Root);

        var capture = new DeviceRecordingCapture(lexical, authority);

        Assert.Equal(Canonical(real), capture.Path);
        Assert.Equal(capture.Path, capture.ResolveForRead());
    }

    /// <summary>
    /// Abandoned recordings are swept by age only, and only ordinary files. Anything written inside
    /// the retention window may still be recording, including from another DevFlow process.
    /// </summary>
    [Fact]
    public void SweepsOnlyRecordingsOlderThanTheRetentionWindow()
    {
        using var scratch = new ScratchRoot();
        var now = DateTimeOffset.UtcNow;
        var stale = Path.Combine(scratch.Root, "stale.mp4");
        var fresh = Path.Combine(scratch.Root, "fresh.mp4");
        var other = Path.Combine(scratch.Root, "notes.json");
        File.WriteAllText(stale, "frames");
        File.WriteAllText(fresh, "frames");
        File.WriteAllText(other, "{}");
        File.SetLastWriteTimeUtc(stale, now.UtcDateTime - TimeSpan.FromHours(48));
        File.SetLastWriteTimeUtc(other, now.UtcDateTime - TimeSpan.FromHours(48));

        MobileCanvasDeviceSurface.SweepAbandonedRecordings(
            scratch.Root,
            TimeSpan.FromHours(24),
            now);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
    }

    /// <summary>
    /// The sweep never follows a link out of the root: a stale link planted there would otherwise
    /// turn cleanup into arbitrary deletion.
    /// </summary>
    [Fact]
    public void SweepNeverDeletesThroughALinkOutOfTheRoot()
    {
        using var scratch = new ScratchRoot();
        var outside = scratch.WriteOutside("keep.mp4", "frames");
        var link = Path.Combine(scratch.Root, "stale.mp4");
        if (!FileSystemLinks.CreatedFileLink(link, outside))
            return;
        File.SetLastWriteTimeUtc(link, DateTime.UtcNow - TimeSpan.FromHours(48));

        MobileCanvasDeviceSurface.SweepAbandonedRecordings(
            scratch.Root,
            TimeSpan.FromHours(24),
            DateTimeOffset.UtcNow);

        Assert.True(File.Exists(outside));
    }

    /// <summary>
    /// A disposable pair of directories: a trusted root and a sibling the tests escape to. Link
    /// creation itself lives in <see cref="FileSystemLinks"/>, which owns the one policy about when
    /// a failure may be skipped.
    /// </summary>
    private sealed class ScratchRoot : IDisposable
    {
        public ScratchRoot()
        {
            Parent = Path.Combine(Path.GetTempPath(), "devflow-link-tests-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(Parent, "device-recordings");
            Outside = Path.Combine(Parent, "outside");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Outside);
        }

        public string Parent { get; }
        public string Root { get; }
        public string Outside { get; }

        public string WriteOutside(string name, string content)
        {
            var path = Path.Combine(Outside, name);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateOutsideDirectory(string name)
        {
            var path = Path.Combine(Outside, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Parent, recursive: true); }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
