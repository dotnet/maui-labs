using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

internal static class DevFlowTestingAssemblyResolver
{
    private const string TestingAssemblyName = "Microsoft.Maui.DevFlow.Testing";

    [ModuleInitializer]
    internal static void Initialize()
        => AssemblyLoadContext.Default.Resolving += Resolve;

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName requested)
    {
        if (!string.Equals(requested.Name, TestingAssemblyName, StringComparison.Ordinal))
            return null;

        var path = Path.Combine(AppContext.BaseDirectory, TestingAssemblyName + ".dll");
        if (!File.Exists(path))
            return null;

        try
        {
            var candidate = System.Reflection.AssemblyName.GetAssemblyName(path);
            if (!string.Equals(candidate.Name, TestingAssemblyName, StringComparison.Ordinal) ||
                requested.Version != candidate.Version)
            {
                return null;
            }
            return context.LoadFromAssemblyPath(path);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or FileLoadException or IOException)
        {
            return null;
        }
    }
}
