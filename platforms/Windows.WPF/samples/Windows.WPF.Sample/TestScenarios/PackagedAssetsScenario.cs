using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;
using Microsoft.Maui.Storage;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios;

public static class PackagedAssetsScenario
{
    public static int Run()
    {
        using var app = MauiApp.CreateBuilder().UseWPFEssentials().Build();
        var fileSystem = app.Services.GetRequiredService<IFileSystem>();
        var failures = 0;
        var cases = AssetCases.All.ToArray();
        if (cases.Length == 0)
            throw new InvalidOperationException("No packaged asset cases were registered.");

        foreach (var testCase in cases)
        {
            var name = (string)testCase[0];
            var expected = (string)testCase[1];
            var exists = fileSystem.AppPackageFileExistsAsync(name).GetAwaiter().GetResult();
            try
            {
                using var stream = fileSystem.OpenAppPackageFileAsync(name).GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                var actual = reader.ReadToEnd().Trim();
                if (!exists || actual != expected)
                {
                    Console.Error.WriteLine($"FAIL {name}: exists={exists}, content='{actual}'");
                    failures++;
                }
                else
                {
                    Console.WriteLine($"PASS {name}: exists=true, content='{actual}'");
                }
            }
            catch (FileNotFoundException exception)
            {
                Console.Error.WriteLine($"FAIL {name}: exists={exists}, {exception.Message}");
                failures++;
            }
        }

        const string missing = "Data/missing.txt";
        if (fileSystem.AppPackageFileExistsAsync(missing).GetAwaiter().GetResult())
            throw new InvalidOperationException("A missing asset was reported as present.");
        try
        {
            using var stream = fileSystem.OpenAppPackageFileAsync(missing).GetAwaiter().GetResult();
            throw new InvalidOperationException("A missing asset was opened.");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine("PASS missing asset: exists=false, open throws FileNotFoundException");
        }

        Console.WriteLine($"Asset root: {AppContext.BaseDirectory}; cases: {cases.Length}; failures: {failures}");
        return failures == 0 ? 0 : 1;
    }
}
