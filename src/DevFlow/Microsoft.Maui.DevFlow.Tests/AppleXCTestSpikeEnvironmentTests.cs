using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Deliberately environment-gated runtime proof. Windows only compiles this test; it never claims
/// that a simulator or Xcode runtime was exercised.
/// </summary>
public sealed class AppleXCTestSpikeEnvironmentTests
{
    [Fact]
    public async Task AppleSpike_WhenExplicitlyEnabled_ProducesProvedMachineReadableReport()
    {
        if (!OperatingSystem.IsMacOS() ||
            !string.Equals(Environment.GetEnvironmentVariable("DEVFLOW_RUN_APPLE_XCTEST_SPIKE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var targetApp = RequiredEnvironment("DEVFLOW_APPLE_SPIKE_TARGET_APP");
        var targetBundleId = RequiredEnvironment("DEVFLOW_APPLE_SPIKE_TARGET_BUNDLE_ID");
        var simulatorId = RequiredEnvironment("DEVFLOW_APPLE_SPIKE_SIMULATOR_ID");
        var safeActionId = RequiredEnvironment("DEVFLOW_APPLE_SPIKE_SAFE_ACTION_ID");
        var root = FindRepositoryRoot();
        var results = Path.Combine(root, "artifacts", "TestResults", "devflow-flow", "ios");
        var runId = $"apple-xctest-env-{Guid.NewGuid():N}";
        var artifactRoot = Path.Combine(root, "artifacts", "devflow", runId, "ios");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/bin/bash")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("eng/devflow/Run-DevFlowFlowQa.sh");
        process.StartInfo.ArgumentList.Add("--platform");
        process.StartInfo.ArgumentList.Add("ios");
        process.StartInfo.ArgumentList.Add("--apple-spike");
        process.StartInfo.ArgumentList.Add("--target-app");
        process.StartInfo.ArgumentList.Add(targetApp);
        process.StartInfo.ArgumentList.Add("--target-bundle-id");
        process.StartInfo.ArgumentList.Add(targetBundleId);
        process.StartInfo.ArgumentList.Add("--simulator-id");
        process.StartInfo.ArgumentList.Add(simulatorId);
        process.StartInfo.ArgumentList.Add("--safe-action-id");
        process.StartInfo.ArgumentList.Add(safeActionId);
        process.StartInfo.ArgumentList.Add("--results-root");
        process.StartInfo.ArgumentList.Add(results);
        process.StartInfo.Environment["DEVFLOW_FLOW_QA_RUN_ID"] = runId;

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which a 30-minute Apple lane will do long before it exits.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = string.Empty;
        var stderr = string.Empty;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            // The spike drives a simulator, an app, and an XCTest host. Leaving it running would
            // hold the device and the agent port for whatever runs next on this machine, and the
            // two read tasks would never be observed. Both are settled before the failure is
            // reported.
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It exited between the timeout and the kill.
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
                stdout = await stdoutTask;
                stderr = await stderrTask;
            }
            catch (Exception)
            {
                // The pipes died with the process; the timeout is still the failure to report.
            }

            Assert.Fail($"Apple spike timed out. stdout={stdout}; stderr={stderr}");
            return;
        }
        Assert.True(process.ExitCode == 0, $"Apple spike was not proved. stdout={stdout}; stderr={stderr}");

        var reportPath = Path.Combine(artifactRoot, "apple-xctest-spike.json");
        Assert.True(File.Exists(reportPath));
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.Equal("proved", report.RootElement.GetProperty("status").GetString());
        Assert.True(report.RootElement.GetProperty("foregroundOwnership").GetProperty("asserted").GetBoolean());
        Assert.True(report.RootElement.GetProperty("transport").GetProperty("authenticated").GetBoolean());
        Assert.True(report.RootElement.GetProperty("parity").GetProperty("passed").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, report.RootElement.GetProperty("commandReceipt").ValueKind);
        Assert.Equal("apple-agent-cancelled", report.RootElement.GetProperty("cancellation").GetProperty("code").GetString());
        var operationProbe = report.RootElement.GetProperty("operationProbe");
        Assert.True(operationProbe.GetProperty("treeCount").GetInt32() > 0);
        Assert.Equal(1, operationProbe.GetProperty("queryCount").GetInt32());
        Assert.True(operationProbe.GetProperty("actionSucceeded").GetBoolean());
        Assert.NotEmpty(report.RootElement.GetProperty("artifacts").EnumerateArray());
        var qaPath = Path.Combine(artifactRoot, "apple-flow-qa.json");
        Assert.True(File.Exists(qaPath));
        using var qa = JsonDocument.Parse(File.ReadAllText(qaPath));
        Assert.Equal("devflow-apple-flow-qa", qa.RootElement.GetProperty("kind").GetString());
    }

    static string RequiredEnvironment(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required when DEVFLOW_RUN_APPLE_XCTEST_SPIKE=1.");

    static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MauiLabs.slnx")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
