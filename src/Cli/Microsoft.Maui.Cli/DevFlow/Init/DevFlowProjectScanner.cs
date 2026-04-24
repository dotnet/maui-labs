using System.Xml.Linq;
using Microsoft.Maui.Cli.Commands;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal static class DevFlowProjectScanner
{
    static readonly HashSet<string> s_excludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "packages"
    };

    public static IReadOnlyList<DevFlowProjectCandidate> Discover(string workspaceRoot)
    {
        var results = new List<DevFlowProjectCandidate>();
        var pending = new Queue<string>();
        pending.Enqueue(workspaceRoot);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            try
            {
                foreach (var projectPath in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
                {
                    var candidate = CreateCandidate(workspaceRoot, projectPath);
                    if (candidate != null)
                        results.Add(candidate);
                }
            }
            catch
            {
                // Best effort scan.
            }

            try
            {
                foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(subdirectory);
                    if (!string.IsNullOrEmpty(name) && !s_excludedDirectories.Contains(name))
                        pending.Enqueue(subdirectory);
                }
            }
            catch
            {
                // Best effort scan.
            }
        }

        return results
            .OrderBy(candidate => candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static DevFlowProjectCandidate? DescribeProject(string workspaceRoot, string projectPath)
        => CreateCandidate(workspaceRoot, projectPath);

    static DevFlowProjectCandidate? CreateCandidate(string workspaceRoot, string projectPath)
    {
        // Try dotnet msbuild evaluation first — it fully resolves properties and package
        // references from Directory.Build.props, Directory.Packages.props, imported
        // .props/.targets, and composed values. Fall back to raw XML if dotnet CLI
        // is unavailable or evaluation fails.
        ProjectView? view = null;
        var props = DotnetCliProjectReader.GetProperties(projectPath, "UseMaui", "TargetFramework", "TargetFrameworks");
        if (props.Count > 0)
        {
            var cliTfms = DotnetCliProjectReader.GetTargetFrameworks(projectPath);
            var pkgIds = DotnetCliProjectReader.GetPackageReferenceIds(projectPath);
            view = ProjectView.FromDotnetCli(props, cliTfms, pkgIds);
        }
        else
        {
            view = ProjectView.FromXml(projectPath);
            if (view == null)
                return null;
        }

        var hasUseMaui = view.GetBooleanProperty("UseMaui");
        var tfms = view.TargetFrameworks;
        var isGtk =
            view.HasPackageReference("Maui.Gtk") ||
            view.HasPackageReference("Platform.Maui.Linux.Gtk4") ||
            view.HasPackageReference("GirCore.Gtk-4.0") ||
            view.HasPackageReference("Platform.Maui.Linux.Gtk4.BlazorWebView");

        var hasKnownMauiTfm = tfms.Any(tfm =>
            tfm.Contains("-android", StringComparison.OrdinalIgnoreCase) ||
            tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase) ||
            tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase) ||
            tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase));

        var isMaui = hasUseMaui || hasKnownMauiTfm || isGtk;
        if (!isMaui)
            return null;

        var mauiProgramPath = FindMauiProgramPath(projectPath);
        var mauiProgramText = mauiProgramPath != null && File.Exists(mauiProgramPath)
            ? File.ReadAllText(mauiProgramPath)
            : null;

        var needsBlazor =
            view.HasPackageReference("Microsoft.AspNetCore.Components.WebView.Maui") ||
            (mauiProgramText?.Contains("AddMauiBlazorWebView", StringComparison.Ordinal) ?? false);

        var hasAgentPackage =
            view.HasPackageReference("Microsoft.Maui.DevFlow.Agent") ||
            view.HasPackageReference("Microsoft.Maui.DevFlow.Agent.Gtk");
        var hasAgentRegistration =
            (mauiProgramText?.Contains("AddMauiDevFlowAgent", StringComparison.Ordinal) ?? false);
        var hasBlazorPackage =
            view.HasPackageReference("Microsoft.Maui.DevFlow.Blazor") ||
            view.HasPackageReference("Microsoft.Maui.DevFlow.Blazor.Gtk");
        var hasBlazorRegistration =
            (mauiProgramText?.Contains("AddMauiBlazorDevFlowTools", StringComparison.Ordinal) ?? false);

        var fullyIntegrated = hasAgentPackage && hasAgentRegistration && (!needsBlazor || (hasBlazorPackage && hasBlazorRegistration));
        var flavor = isGtk
            ? (needsBlazor ? "gtk-blazor" : "gtk")
            : needsBlazor
                ? "standard-maui-blazor"
                : "standard-maui";

        return new DevFlowProjectCandidate
        {
            ProjectPath = Path.GetFullPath(projectPath),
            RelativePath = Path.GetRelativePath(workspaceRoot, projectPath),
            Flavor = flavor,
            IsSupported = true,
            NeedsBlazor = needsBlazor,
            IsAlreadyIntegrated = fullyIntegrated,
            MauiProgramPath = mauiProgramPath
        };
    }

    static string? FindMauiProgramPath(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (projectDirectory == null)
            return null;

        var mauiProgramPath = Path.Combine(projectDirectory, "MauiProgram.cs");
        return File.Exists(mauiProgramPath) ? mauiProgramPath : null;
    }

    /// <summary>
    /// Unified read-only view over either an MSBuild-evaluated project or raw XML fallback.
    /// MSBuild evaluation correctly resolves properties and package references coming from
    /// Directory.Build.props, Directory.Packages.props, and other imported .props/.targets.
    /// </summary>
    sealed class ProjectView
    {
        readonly Func<string, bool> _getBooleanProperty;
        readonly Func<string, bool> _hasPackageReference;

        public IReadOnlyList<string> TargetFrameworks { get; }

        ProjectView(
            IReadOnlyList<string> targetFrameworks,
            Func<string, bool> getBooleanProperty,
            Func<string, bool> hasPackageReference)
        {
            TargetFrameworks = targetFrameworks;
            _getBooleanProperty = getBooleanProperty;
            _hasPackageReference = hasPackageReference;
        }

        public bool GetBooleanProperty(string name) => _getBooleanProperty(name);
        public bool HasPackageReference(string packageId) => _hasPackageReference(packageId);

        public static ProjectView FromDotnetCli(
            Dictionary<string, string> properties,
            IReadOnlyList<string> targetFrameworks,
            HashSet<string> packageReferenceIds)
        {
            return new ProjectView(
                targetFrameworks,
                name => properties.TryGetValue(name, out var v) &&
                        string.Equals(v, "true", StringComparison.OrdinalIgnoreCase),
                packageId => packageReferenceIds.Contains(packageId));
        }

        public static ProjectView? FromXml(string projectPath)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(projectPath);
            }
            catch
            {
                return null;
            }

            var tfms = TryGetTargetFrameworksFromProcess(projectPath);
            if (tfms.Count == 0)
                tfms = ReadTfmsFromXml(document);

            var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in document.Descendants()
                .Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)))
            {
                var include = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    packageIds.Add(include);
            }

            return new ProjectView(
                tfms,
                name => HasProperty(document, name, "true"),
                packageId => packageIds.Contains(packageId));
        }

        static IReadOnlyList<string> TryGetTargetFrameworksFromProcess(string projectPath)
        {
            try
            {
                return MauiProjectResolver.GetTargetFrameworks(projectPath);
            }
            catch
            {
                return [];
            }
        }

        static IReadOnlyList<string> ReadTfmsFromXml(XDocument document)
        {
            var tfms = new List<string>();
            foreach (var element in document.Descendants())
            {
                if (element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                    element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var entry in element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        tfms.Add(entry);
                }
            }
            return tfms;
        }

        static bool HasProperty(XDocument document, string propertyName, string expectedValue)
        {
            return document.Descendants()
                .Any(element =>
                    element.Name.LocalName.Equals(propertyName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(element.Value.Trim(), expectedValue, StringComparison.OrdinalIgnoreCase));
        }
    }
}
