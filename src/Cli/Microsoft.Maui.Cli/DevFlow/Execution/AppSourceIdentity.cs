using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record AppSourceIdentity
{
    public string? SourceRevision { get; init; }
    public string? AppSourceFingerprint { get; init; }
}

internal interface IAppSourceIdentityProvider
{
    Task<AppSourceIdentity> ResolveAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Establishes a cross-host source identity only for a clean Git project tree. A dirty or
/// unverifiable project deliberately yields no source fingerprint so matching remains fail-closed.
/// </summary>
internal sealed class GitAppSourceIdentityProvider(IExecutionProcessRunner processRunner)
    : IAppSourceIdentityProvider
{
    private readonly IExecutionProcessRunner _processRunner =
        processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<AppSourceIdentity> ResolveAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return new AppSourceIdentity();

        string fullProjectPath;
        try
        {
            fullProjectPath = Path.GetFullPath(projectPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new AppSourceIdentity();
        }

        if (!File.Exists(fullProjectPath))
            return new AppSourceIdentity();

        var repositoryRoot = FindRepositoryRoot(Path.GetDirectoryName(fullProjectPath)!);
        if (repositoryRoot is null)
            return new AppSourceIdentity();

        try
        {
            var revisionResult = await _processRunner.RunAsync(
                "git",
                ["-C", repositoryRoot, "rev-parse", "--verify", "HEAD"],
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var revision = NormalizeRevision(revisionResult.Success
                ? revisionResult.StandardOutput
                : null);
            if (revision is null)
                return new AppSourceIdentity();

            var relativeProject = Path.GetRelativePath(repositoryRoot, fullProjectPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (relativeProject.StartsWith("../", StringComparison.Ordinal) ||
                string.Equals(relativeProject, "..", StringComparison.Ordinal))
            {
                return new AppSourceIdentity { SourceRevision = revision };
            }

            var projectDirectory = Path.GetDirectoryName(relativeProject)?
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var statusPath = string.IsNullOrWhiteSpace(projectDirectory)
                ? "."
                : projectDirectory;
            var statusResult = await _processRunner.RunAsync(
                "git",
                [
                    "-C",
                    repositoryRoot,
                    "status",
                    "--porcelain=v1",
                    "--untracked-files=all",
                    "--",
                    statusPath,
                ],
                timeout: TimeSpan.FromSeconds(30),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!statusResult.Success || !string.IsNullOrWhiteSpace(statusResult.StandardOutput))
                return new AppSourceIdentity { SourceRevision = revision };

            var material = "maui-devflow-app-source-v1\u001f" +
                revision +
                "\u001f" +
                relativeProject;
            return new AppSourceIdentity
            {
                SourceRevision = revision,
                AppSourceFingerprint = "sha256:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant(),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AppSourceIdentity();
        }
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }
        }
        return null;
    }

    private static string? NormalizeRevision(string? value)
    {
        var revision = value?.Trim();
        return revision is { Length: 40 or 64 } && revision.All(Uri.IsHexDigit)
            ? revision.ToLowerInvariant()
            : null;
    }
}

internal sealed class NullAppSourceIdentityProvider : IAppSourceIdentityProvider
{
    public Task<AppSourceIdentity> ResolveAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new AppSourceIdentity());
}
