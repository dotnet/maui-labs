using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal sealed record FlowExecutionProcessStartRequest
{
    public required string ExecutablePath { get; init; }
    public required string WorkingDirectory { get; init; }
    public required string DisplayName { get; init; }
}

internal sealed record FlowExecutionOwnedProcess
{
    public required int ProcessId { get; init; }
    public required string ExecutablePath { get; init; }
    public required IFlowExecutionProcessHandle Handle { get; init; }
}

internal sealed record FlowExecutionProcessStopResult
{
    public required bool Succeeded { get; init; }
    public bool AlreadyExited { get; init; }
    public string? DetailCode { get; init; }
}

internal interface IFlowExecutionProcessController
{
    FlowExecutionOwnedProcess Start(FlowExecutionProcessStartRequest request);

    Task<FlowExecutionProcessStopResult> StopAsync(
        FlowExecutionOwnedProcess process,
        CancellationToken cancellationToken = default);

    void Release(FlowExecutionOwnedProcess process);
}

internal interface IFlowExecutionProcessHandle : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }

    /// <summary>
    /// The exit code once the process has exited, or <see langword="null"/> when it is still
    /// running or the host cannot read it.
    /// </summary>
    int? ExitCode => null;

    void Kill();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

internal sealed class SystemFlowExecutionProcessHandle(Process process) : IFlowExecutionProcessHandle
{
    private readonly Process _process = process ?? throw new ArgumentNullException(nameof(process));

    public int ProcessId => _process.Id;
    public bool HasExited => _process.HasExited;

    public int? ExitCode
    {
        get
        {
            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public void Kill() => _process.Kill();
    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _process.WaitForExitAsync(cancellationToken);
    public void Dispose() => _process.Dispose();
}

internal sealed class SystemFlowExecutionProcessController : IFlowExecutionProcessController
{
    public FlowExecutionOwnedProcess Start(FlowExecutionProcessStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var executablePath = Path.GetFullPath(request.ExecutablePath);
        if (!File.Exists(executablePath))
        {
            throw FlowExecutionException.Infrastructure(
                "desktop-executable-missing",
                $"The exact {request.DisplayName} executable is no longer available.");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetFullPath(request.WorkingDirectory),
            },
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw FlowExecutionException.Infrastructure(
                    "desktop-process-start-failed",
                    $"The exact {request.DisplayName} process could not be started.");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw FlowExecutionException.Infrastructure(
                "desktop-process-start-failed",
                $"The exact {request.DisplayName} process could not be started.",
                ex);
        }

        var handle = new SystemFlowExecutionProcessHandle(process);
        return new FlowExecutionOwnedProcess
        {
            ProcessId = handle.ProcessId,
            ExecutablePath = executablePath,
            Handle = handle,
        };
    }

    public async Task<FlowExecutionProcessStopResult> StopAsync(
        FlowExecutionOwnedProcess owned,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owned);
        var process = owned.Handle;
        if (process.ProcessId != owned.ProcessId)
        {
            return new FlowExecutionProcessStopResult
            {
                Succeeded = false,
                DetailCode = "desktop-process-ownership-invalid",
            };
        }

        try
        {
            if (process.HasExited)
            {
                return new FlowExecutionProcessStopResult
                {
                    Succeeded = true,
                    AlreadyExited = true,
                    DetailCode = "desktop-process-already-exited",
                };
            }

            process.Kill();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new FlowExecutionProcessStopResult
            {
                Succeeded = true,
                DetailCode = "desktop-process-stopped",
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FlowExecutionProcessStopResult
            {
                Succeeded = false,
                DetailCode = "desktop-process-stop-timeout",
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new FlowExecutionProcessStopResult
            {
                Succeeded = false,
                DetailCode = "desktop-process-stop-failed",
            };
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Release(FlowExecutionOwnedProcess owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        if (owned.Handle.ProcessId == owned.ProcessId)
            owned.Handle.Dispose();
    }
}

internal sealed record DesktopFlowExecutionPreflightState
{
    public required string ExecutablePath { get; init; }
    public string? OwnedBuildRoot { get; init; }
    public required string RuntimeKind { get; init; }
    public required string DeviceProfile { get; init; }
    public object? PlatformState { get; init; }
}

internal sealed record DesktopFlowExecutionSessionState
{
    public required FlowExecutionOwnedProcess Process { get; init; }
}

internal abstract class DesktopFlowExecutionAdapterBase : IFlowExecutionPlatformAdapter
{
    private readonly IFlowExecutionProcessController _processController;

    protected DesktopFlowExecutionAdapterBase(IFlowExecutionProcessController processController)
        => _processController = processController ?? throw new ArgumentNullException(nameof(processController));

    public abstract FlowExecutionPlatformDescriptor Descriptor { get; }

    public abstract void ValidateHost();

    public abstract string? GetDefaultRuntimeIdentifier();

    public abstract Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default);

    protected virtual void ValidateImmediatelyBeforeLaunch(FlowExecutionPlatformPreflight preflight)
    {
    }

    public Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
        FlowExecutionPlatformRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Preflight.State is not DesktopFlowExecutionPreflightState state)
        {
            throw FlowExecutionException.Infrastructure(
                "desktop-preflight-state-missing",
                $"The {Descriptor.DisplayName} launch preflight state is unavailable.");
        }

        ValidateImmediatelyBeforeLaunch(request.Preflight);
        if (!string.IsNullOrWhiteSpace(state.OwnedBuildRoot))
        {
            ExecutionPathSafety.ValidateConfinedArtifactPath(
                state.OwnedBuildRoot,
                state.ExecutablePath);
        }
        var process = _processController.Start(new FlowExecutionProcessStartRequest
        {
            ExecutablePath = state.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(state.ExecutablePath) ?? Environment.CurrentDirectory,
            DisplayName = Descriptor.DisplayName,
        });
        return Task.FromResult(new FlowExecutionPlatformSession
        {
            Device = request.Preflight.Device,
            DeviceSerial = request.Preflight.DeviceSerial,
            PackageId = request.Preflight.PackageId,
            Platform = Descriptor.Platform,
            RuntimeKind = state.RuntimeKind,
            DeviceProfile = state.DeviceProfile,
            Experimental = Descriptor.Experimental,
            ProcessId = process.ProcessId,
            InstalledByInvocation = false,
            LaunchedByInvocation = true,
            State = new DesktopFlowExecutionSessionState { Process = process },
        });
    }

    public Task EstablishAgentForwardingAsync(
        FlowExecutionPlatformSession session,
        int agentPort,
        int brokerPort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reports whether the launched desktop process is still alive. The host owns the process
    /// handle, so liveness and the exit code are directly observable — but a bare exit code is not
    /// a crash reason, so no reason is claimed and the classifier will not call this a crash on
    /// its own.
    /// </summary>
    public Task<MauiFlowAppProcessEvidence?> ProbeAppProcessAsync(
        FlowExecutionAppProbeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Session.State is not DesktopFlowExecutionSessionState state)
        {
            return Task.FromResult<MauiFlowAppProcessEvidence?>(new MauiFlowAppProcessEvidence
            {
                Probed = false,
                Source = ProbeSource,
                ProbeError = "The desktop process session was unavailable, so app liveness was not observed.",
            });
        }

        try
        {
            var exited = state.Process.Handle.HasExited;
            return Task.FromResult<MauiFlowAppProcessEvidence?>(new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = ProbeSource,
                ProcessExited = exited,
                ExitCode = exited ? state.Process.Handle.ExitCode : null,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<MauiFlowAppProcessEvidence?>(new MauiFlowAppProcessEvidence
            {
                Probed = false,
                Source = ProbeSource,
                ProbeError = "The desktop process handle could not be read.",
            });
        }
    }

    private const string ProbeSource = "process-handle";

    public async Task<FlowExecutionCleanupResult> CleanupAsync(
        FlowExecutionPlatformSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State is not DesktopFlowExecutionSessionState state)
        {
            return new FlowExecutionCleanupResult
            {
                Succeeded = false,
                DetailCode = "desktop-process-session-missing",
            };
        }

        if (string.Equals(cleanupPolicy, FlowExecutionCleanupPolicies.None, StringComparison.Ordinal))
        {
            _processController.Release(state.Process);
            return new FlowExecutionCleanupResult
            {
                Succeeded = true,
                DetailCode = "desktop-cleanup-none",
            };
        }

        var stopped = await _processController.StopAsync(state.Process, cancellationToken).ConfigureAwait(false);
        return new FlowExecutionCleanupResult
        {
            Succeeded = stopped.Succeeded,
            PackageStopped = stopped.Succeeded,
            UninstallSkippedNotOwned = string.Equals(
                cleanupPolicy,
                FlowExecutionCleanupPolicies.Uninstall,
                StringComparison.Ordinal),
            DetailCode = stopped.DetailCode,
        };
    }
}
