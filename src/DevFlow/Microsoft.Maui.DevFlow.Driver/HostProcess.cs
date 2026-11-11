using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class HostProcess
{
    internal static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await output.ConfigureAwait(false);
            var stderr = await error.ConfigureAwait(false);
            if (process.ExitCode != 0 || (stdout + "\n" + stderr).Split('\n')
                .Any(line => line.TrimStart().StartsWith("KO:", StringComparison.Ordinal)))
                throw new InvalidOperationException($"{executable} {string.Join(" ", arguments)} failed ({process.ExitCode}): {stderr.Trim()} {stdout.Trim()}");
            return stdout;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException ex) { Trace.WriteLine($"Host process exited during cancellation: {ex.Message}"); }
            try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException ex) { Trace.WriteLine($"Host process did not exit after cancellation: {ex.Message}"); }
            try { await Task.WhenAll(output, error).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
