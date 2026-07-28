using System.Runtime.CompilerServices;
using VerifyTests;

namespace Microsoft.Maui.AI.Attributes.Generators.Tests;

internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
        VerifierSettings.UseUtf8NoBom();
        DerivePathInfo((sourceFile, projectDir, type, method) =>
            new PathInfo(directory: Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots")));
    }
}
