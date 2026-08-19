using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Testing;

internal static class MauiTestAppleHost
{
    public static void Run(MauiTestApp application)
    {
        var consumer = new MauiTestResultConsumer();
        consumer.TestCompleted += result =>
            Console.WriteLine($"[{result.Outcome.ToUpperInvariant()}] {result.Name}");

        _ = Task.Run(async () =>
        {
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var options = application.Services.GetRequiredService<MauiTestAppOptions>();
                var resultsPath = Path.Combine(documentsPath, options.ResultsDirectoryName);
                var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                var result = await application.RunAsync(args, resultsPath, consumer);
                Console.WriteLine(
                    $"Results: passed={result.Passed}, failed={result.Failed}, skipped={result.Skipped}");
                Console.WriteLine($"TRX report: {result.TrxReportPath}");
                Environment.Exit(result.ExitCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running tests: {ex}");
                Environment.Exit(1);
            }
        });
    }
}
