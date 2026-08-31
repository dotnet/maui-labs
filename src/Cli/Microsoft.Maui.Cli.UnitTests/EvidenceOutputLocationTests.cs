using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Where an evidence bundle is *written*, which project's suppression policy applies, and which
/// root its source paths normalize against are three separate concerns, and they have to stay
/// separate.
///
/// Pinning policy lookup and source-path normalization to the connected app's project is a
/// correctness fix: a broker or MCP server started by an editor sits in a different repository
/// from the running app, so probing a working directory applies the wrong project's reviewed
/// suppressions and fails to relativize the app's own paths. Moving where the bundle is *written*
/// is a different, user-visible change — a capture that used to land in the caller's directory
/// would silently start landing somewhere else — and this layer does not make it.
/// </summary>
[Collection("CLI")]
public class EvidenceOutputLocationTests : IDisposable
{
    private static readonly DateTime Utc = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    // Deliberately outside the repository: the default-location probe walks up from this process's
    // working directory, which is inside the repo, so a temp root is the only way to model "the app
    // was built somewhere the broker's discovery will never reach".
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "evidence-output-location", Guid.NewGuid().ToString("N"));

    public EvidenceOutputLocationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DefaultOutputLocationIgnoresTheLayoutPolicyRoot()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var policyRoot = Path.Combine(_root, "some-other-project");
        Directory.CreateDirectory(policyRoot);

        var plan = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            LayoutPolicyStartPath = policyRoot,
            UtcNow = Utc,
        });

        // The destination is exactly what it was before the layout layer existed: the discovery
        // that starts from this process's own directory, never the connected app's project root.
        var expectedDirectory = Path.GetDirectoryName(
            EvidencePaths.ResolveDefaultOutputPath(EvidencePaths.FindProjectRoot(null), "App", Utc));
        Assert.Equal(expectedDirectory, Path.GetDirectoryName(plan.OutputPath));
        Assert.DoesNotContain(policyRoot, plan.OutputPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnExplicitOutputPathStillWins()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var requested = Path.Combine(_root, "chosen.mauitrace");

        var plan = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            OutputPath = requested,
            LayoutPolicyStartPath = Path.Combine(_root, "some-other-project"),
            UtcNow = Utc,
        });

        Assert.Equal(requested, plan.OutputPath);
    }

    [Fact]
    public async Task AnExplicitProjectHintStillDecidesTheDefaultLocation()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var hinted = Path.Combine(_root, "hinted-project");
        Directory.CreateDirectory(hinted);

        var plan = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
        {
            Source = "cli",
            ProjectHint = hinted,
            UtcNow = Utc,
        });

        Assert.StartsWith(
            Path.Combine(hinted, EvidenceFormat.DefaultFolderName),
            plan.OutputPath!,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The real deployment this fix exists for: an editor starts the broker/MCP server inside
    /// repository B while the app it inspects was built from repository A.
    ///
    /// Both halves have to hold at once. The bundle must still land where it landed before the
    /// layout layer existed — repository B's discovery, because relocating a tool's default output
    /// is a visible change a caller cannot see coming — while the app's absolute source paths must
    /// normalize against repository A. Normalizing against B would find no match and drop every
    /// path to a bare file name, losing the folder structure that makes a finding locatable.
    /// </summary>
    [Fact]
    public async Task WithABrokerInOneRepositoryAndTheAppInAnother_OutputStaysPutAndSourcePathsFollowTheApp()
    {
        var appRepository = Path.Combine(_root, "repo-a");
        Directory.CreateDirectory(Path.Combine(appRepository, "Views"));
        var appSourceFile = Path.Combine(appRepository, "Views", "MainPage.xaml");

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "automationId": "MainPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(appSourceFile)}},
                "sourceLine": 12,
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var request = new EvidenceRequest
        {
            Source = "mcp",
            // What an MCP evidence tool sets from the connected agent's registration. Neither
            // field steers the destination.
            LayoutPolicyStartPath = appRepository,
            SourcePathRoot = appRepository,
            UtcNow = Utc,
        };

        var plan = await EvidenceCapture.PreviewAsync(client, request);
        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, request);
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        // Destination: repository B's pre-layout discovery, unchanged.
        var expectedDirectory = Path.GetDirectoryName(
            EvidencePaths.ResolveDefaultOutputPath(EvidencePaths.FindProjectRoot(null), "App", Utc));
        Assert.Equal(expectedDirectory, Path.GetDirectoryName(plan.OutputPath));
        Assert.DoesNotContain(appRepository, plan.OutputPath!, StringComparison.OrdinalIgnoreCase);

        // Source paths: project-relative to repository A, and never absolute.
        Assert.Contains("Views/MainPage.xaml", tree, StringComparison.Ordinal);
        Assert.DoesNotContain(appRepository, tree, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Without the dedicated root the same capture falls through to the bare-file-name policy,
    /// which is what makes the field load-bearing rather than decorative.
    /// </summary>
    [Fact]
    public async Task WithoutASourcePathRoot_TheAppsPathsDropToFileNamesRatherThanLeaking()
    {
        var appRepository = Path.Combine(_root, "repo-a-unpinned");
        Directory.CreateDirectory(Path.Combine(appRepository, "Views"));
        var appSourceFile = Path.Combine(appRepository, "Views", "MainPage.xaml");

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(appSourceFile)}},
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            UtcNow = Utc,
        });
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        Assert.Contains("MainPage.xaml", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Views/MainPage.xaml", tree, StringComparison.Ordinal);
        Assert.DoesNotContain(appRepository, tree, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The single-repository case, which is the common one: the editor's working directory is the
    /// repository and the agent registered one project inside it. Narrowing to that project would
    /// drop every file in a sibling project or shared library to a bare file name — the same loss
    /// the pinned root exists to prevent — so the wider enclosing root is kept.
    /// </summary>
    [Fact]
    public async Task WhenTheAppProjectSitsInsideTheDiscoveredRoot_TheWiderRootIsKept()
    {
        var repository = Path.Combine(_root, "one-repo");
        var appProject = Path.Combine(repository, "src", "App");
        var sharedFile = Path.Combine(repository, "src", "Shared", "Controls", "Card.xaml");
        Directory.CreateDirectory(appProject);
        Directory.CreateDirectory(Path.GetDirectoryName(sharedFile)!);

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(sharedFile)}},
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            // The destination root is the repository; the agent registered the app project inside
            // it. Preferring the app project here would lose "src/Shared/Controls/".
            ProjectRoot = repository,
            SourcePathRoot = appProject,
            UtcNow = Utc,
        });
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        Assert.Contains("src/Shared/Controls/Card.xaml", tree, StringComparison.Ordinal);
        Assert.DoesNotContain(repository, tree, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The new root is a redaction input, not a destination. A caller that supplies only it must
    /// see no movement in where the bundle is written.
    /// </summary>
    [Fact]
    public async Task ASourcePathRootNeverSteersTheDestination()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var elsewhere = Path.Combine(_root, "source-root-only");
        Directory.CreateDirectory(elsewhere);

        var pinned = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            SourcePathRoot = elsewhere,
            UtcNow = Utc,
        });
        var unpinned = await EvidenceCapture.PreviewAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            UtcNow = Utc,
        });

        Assert.Equal(unpinned.OutputPath, pinned.OutputPath);
        Assert.DoesNotContain(elsewhere, pinned.OutputPath!, StringComparison.OrdinalIgnoreCase);
    }

    // ── bare volume roots ────────────────────────────────────────────────────────────────────
    //
    // A root is what decides whether an absolute source path becomes project-relative or is
    // reduced to a file name. A volume root encloses the whole machine, so accepting one inverts
    // the redaction: every path in the bundle keeps its full directory chain and the capture
    // describes the user's disk rather than the app's project.

    /// <summary>
    /// The caller-supplied root is the one an MCP tool fills in from an agent's registration, so a
    /// mis-registered or hostile agent must not be able to widen normalization to the volume.
    /// </summary>
    [Fact]
    public async Task ABareVolumeRootIsRefusedAsASourcePathRoot()
    {
        var appRepository = Path.Combine(_root, "repo-bare-source-root");
        Directory.CreateDirectory(Path.Combine(appRepository, "Views"));
        var appSourceFile = Path.Combine(appRepository, "Views", "MainPage.xaml");

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(appSourceFile)}},
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            SourcePathRoot = Path.GetPathRoot(appSourceFile),
            UtcNow = Utc,
        });
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        Assert.Contains("MainPage.xaml", tree, StringComparison.Ordinal);
        AssertNoMachineLayout(tree, appSourceFile);
    }

    /// <summary>
    /// The destination root reaches the same normalization by falling back, and it is discovered by
    /// probing upward from a working directory — so it can land on a volume root without anyone
    /// asking for it. It must be discarded before the enclosure comparison, not after: a volume
    /// root encloses every project, so an unchecked comparison hands it the win outright and the
    /// app's own legitimate root is discarded instead.
    /// </summary>
    [Fact]
    public async Task ABareVolumeDestinationRootNeverWinsOverTheAppsOwnRoot()
    {
        var appRepository = Path.Combine(_root, "repo-bare-destination-root");
        Directory.CreateDirectory(Path.Combine(appRepository, "Views"));
        var appSourceFile = Path.Combine(appRepository, "Views", "MainPage.xaml");

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(appSourceFile)}},
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            ProjectRoot = Path.GetPathRoot(appSourceFile),
            SourcePathRoot = appRepository,
            UtcNow = Utc,
        });
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        // The app's root survives, so the file stays locatable rather than collapsing to a name.
        Assert.Contains("Views/MainPage.xaml", tree, StringComparison.Ordinal);
        AssertNoMachineLayout(tree, appSourceFile, allow: "Views/MainPage.xaml");
    }

    /// <summary>
    /// With both roots bare there is nothing left to normalize against, and the capture must fall
    /// to the file-name-only policy rather than publishing what it could not relativize.
    /// </summary>
    [Fact]
    public async Task WithEveryRootBare_PathsFallToFileNamesRatherThanTheMachineLayout()
    {
        var appRepository = Path.Combine(_root, "repo-every-root-bare");
        Directory.CreateDirectory(Path.Combine(appRepository, "Views"));
        var appSourceFile = Path.Combine(appRepository, "Views", "MainPage.xaml");

        await using var server = new MockAgentServer(visualTree: $$"""
            [
              {
                "id": "el-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "sourceFile": {{System.Text.Json.JsonSerializer.Serialize(appSourceFile)}},
                "children": []
              }
            ]
            """);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var root = Path.GetPathRoot(appSourceFile);
        var (bundle, _) = await EvidenceCapture.CaptureToBytesAsync(client, new EvidenceRequest
        {
            Source = "mcp",
            ProjectRoot = root,
            SourcePathRoot = root,
            UtcNow = Utc,
        });
        var tree = Encoding.UTF8.GetString(
            bundle.Entries.Single(entry => entry.Name == EvidenceFormat.TreeEntry).Content);

        Assert.Contains("MainPage.xaml", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Views/MainPage.xaml", tree, StringComparison.Ordinal);
        AssertNoMachineLayout(tree, appSourceFile);
    }

    /// <summary>
    /// The disclosure a volume root produces is not the absolute path — it is the absolute path
    /// with its root removed, which still names every directory between the volume and the file.
    /// </summary>
    private static void AssertNoMachineLayout(
        string tree,
        string absoluteSourceFile,
        string? allow = null)
    {
        Assert.DoesNotContain(absoluteSourceFile, tree, StringComparison.OrdinalIgnoreCase);

        var root = Path.GetPathRoot(absoluteSourceFile)!;
        var withoutRoot = absoluteSourceFile[root.Length..].Replace('\\', '/').TrimStart('/');
        Assert.DoesNotContain(withoutRoot, tree, StringComparison.OrdinalIgnoreCase);

        // Each intermediate directory name, so a partial rewrite cannot pass either. Short
        // segments are skipped: a temp path on macOS contains one-character directories that
        // collide with ordinary JSON keys and would assert nothing.
        foreach (var segment in withoutRoot.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Length < 8 || segment.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                continue;
            if (allow is not null && allow.Contains(segment, StringComparison.Ordinal))
                continue;
            Assert.DoesNotContain(segment, tree, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The MCP evidence tools resolve the connected agent's project root for one reason only, and
    /// asserting that from source keeps a future edit from quietly re-coupling the two.
    /// </summary>
    [Fact]
    public void TheMcpEvidenceToolsPinPolicyOnlyNotTheDestination()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "Tools", "EvidenceTools.cs"));

        Assert.Contains(
            "var appProjectRoot = await session.TryGetAgentProjectRootAsync(agentPort)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("LayoutPolicyStartPath = appProjectRoot", source, StringComparison.Ordinal);
        Assert.Contains("SourcePathRoot = appProjectRoot", source, StringComparison.Ordinal);
        // ProjectHint and ProjectRoot are the destination-steering fields. Assigning the agent's
        // root to either is exactly the relocation this layer refuses to make. The lookbehind
        // keeps the local `appProjectRoot` from reading as an assignment to the request field.
        Assert.DoesNotContain("ProjectHint", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?<![A-Za-z])ProjectRoot\s*="), source);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
