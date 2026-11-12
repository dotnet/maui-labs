using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class HostProcess
{
    internal static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(executable);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        var result = await CaptureAsync(startInfo, token, killEntireProcessTree: true).ConfigureAwait(false);
        if (result.ExitCode != 0 || (result.Output + "\n" + result.Error).Split('\n')
            .Any(line => line.TrimStart().StartsWith("KO:", StringComparison.Ordinal)))
            throw new InvalidOperationException($"{executable} {string.Join(" ", arguments)} failed ({result.ExitCode}): {result.Error.Trim()} {result.Output.Trim()}");
        return result.Output;
    }

    internal static async Task<(int ExitCode, string Output, string Error)> CaptureAsync(
        ProcessStartInfo startInfo,
        CancellationToken token,
        bool killEntireProcessTree,
        Action<int>? onStarted = null)
    {
        token.ThrowIfCancellationRequested();
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        using var process = new Process
        {
            StartInfo = startInfo,
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            onStarted?.Invoke(process.Id);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: killEntireProcessTree); }
            catch (InvalidOperationException ex) { Trace.WriteLine($"Host process exited during cancellation: {ex.Message}"); }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException ex) { Trace.WriteLine($"Host process did not exit after cancellation: {ex.Message}"); }
            try { await Task.WhenAll(output, error).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
