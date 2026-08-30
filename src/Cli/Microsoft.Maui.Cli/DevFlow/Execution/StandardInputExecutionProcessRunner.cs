using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal interface IExecutionStandardInputProcessRunner
{
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

internal sealed class ExecutionStandardInputProcessRunner : IExecutionStandardInputProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = false;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
            {
                return Failed(stopwatch, "The process could not be started.");
            }
            started = true;

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);
            try
            {
                await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                }
                return Failed(stopwatch, "The process timed out.");
            }

            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await standardOutput.ConfigureAwait(false),
                StandardError = await standardError.ConfigureAwait(false),
                Duration = stopwatch.Elapsed,
            };
        }
        catch (OperationCanceledException)
        {
            if (started)
                await StopProcessAsync(process).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            if (started)
                await StopProcessAsync(process).ConfigureAwait(false);
            return Failed(stopwatch, ex.Message);
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
                return;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Trace.WriteLine($"Failed to stop standard-input process: {ex.GetType().Name}.");
        }
    }

    private static ProcessResult Failed(Stopwatch stopwatch, string error)
    {
        stopwatch.Stop();
        return new ProcessResult
        {
            ExitCode = -1,
            StandardError = error,
            Duration = stopwatch.Elapsed,
        };
    }
}
