using System.Text.Json;

namespace Microsoft.Maui.Cli.DevFlow.Init;

/// <summary>
/// Reads evaluated MSBuild properties and items by shelling out to
/// <c>dotnet msbuild -getProperty</c> / <c>-getItem</c>. This avoids
/// any dependency on Microsoft.Build NuGet packages, MSBuild Locator,
/// or DOTNET_ROOT environment variables.
/// </summary>
internal static class DotnetCliProjectReader
{
    /// <summary>
    /// Get one or more evaluated MSBuild properties from a project.
    /// Returns a dictionary of property name → value. Missing or empty
    /// properties are omitted from the result.
    /// </summary>
    public static Dictionary<string, string> GetProperties(string projectPath, params string[] propertyNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (propertyNames.Length == 0)
            return result;

        var joined = string.Join(',', propertyNames);
        var json = RunDotnetMsBuild(projectPath, $"-getProperty:{joined}");
        if (json == null)
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Properties", out var props))
            {
                foreach (var name in propertyNames)
                {
                    if (props.TryGetProperty(name, out var val))
                    {
                        var s = val.GetString();
                        if (!string.IsNullOrEmpty(s))
                            result[name] = s;
                    }
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Get a single evaluated property value, or empty string if not found.
    /// </summary>
    public static string GetProperty(string projectPath, string propertyName)
    {
        var dict = GetProperties(projectPath, propertyName);
        return dict.TryGetValue(propertyName, out var value) ? value : string.Empty;
    }

    /// <summary>
    /// Get a boolean property value.
    /// </summary>
    public static bool GetBooleanProperty(string projectPath, string propertyName)
        => string.Equals(GetProperty(projectPath, propertyName), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get all PackageReference identities from a project (fully evaluated, including
    /// items from Directory.Build.props, Directory.Packages.props, etc.).
    /// </summary>
    public static HashSet<string> GetPackageReferenceIds(string projectPath)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var json = RunDotnetMsBuild(projectPath, "-getItem:PackageReference");
        if (json == null)
            return result;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Items", out var items) &&
                items.TryGetProperty("PackageReference", out var refs))
            {
                foreach (var item in refs.EnumerateArray())
                {
                    if (item.TryGetProperty("Identity", out var id))
                    {
                        var s = id.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            result.Add(s);
                    }
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Get target frameworks from the evaluated project.
    /// </summary>
    public static IReadOnlyList<string> GetTargetFrameworks(string projectPath)
    {
        var props = GetProperties(projectPath, "TargetFramework", "TargetFrameworks");
        var list = new List<string>();

        if (props.TryGetValue("TargetFramework", out var tf))
            AddEntries(list, tf);
        if (props.TryGetValue("TargetFrameworks", out var tfs))
            AddEntries(list, tfs);

        return list
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static void AddEntries(List<string> list, string raw)
        {
            foreach (var entry in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(entry);
        }
    }

    static string? RunDotnetMsBuild(string projectPath, string msbuildArg)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("msbuild");
            psi.ArgumentList.Add(projectPath);
            psi.ArgumentList.Add(msbuildArg);

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);

            if (process.ExitCode != 0)
                return null;

            return string.IsNullOrWhiteSpace(stdout) ? null : stdout;
        }
        catch
        {
            return null;
        }
    }
}
