using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Starts the broker daemon so that it outlives the CLI invocation without holding on to anything
/// the invocation was handed.
/// <para>
/// On Windows <see cref="Process.Start(ProcessStartInfo)"/> always calls <c>CreateProcess</c> with
/// <c>bInheritHandles=TRUE</c>, so every inheritable handle the CLI owns is duplicated into the
/// daemon — including the pipe a shell installed as the CLI's own stdout for
/// <c>maui devflow flow run ... | Tee-Object</c>. Redirecting the daemon's stdio does not undo
/// that: the daemon holds the caller's pipe open for its whole lifetime, so the pipeline never
/// completes even though the CLI process itself has already exited. The daemon is therefore created
/// with handle inheritance switched off and detached from the caller's console.
/// </para>
/// <para>
/// Other platforms keep <see cref="Process"/>: the child there receives only the descriptors that
/// were explicitly redirected, so the caller's stdout is never leaked, and the daemon's early
/// stderr stays available for start-up diagnostics.
/// </para>
/// </summary>
internal sealed class DetachedDaemonProcess : IDisposable
{
    private readonly Process? _managed;
    private readonly SafeProcessHandle? _native;
    private readonly StringBuilder _stderr = new();

    private DetachedDaemonProcess(Process managed)
    {
        _managed = managed;
        ProcessId = managed.Id;
    }

    private DetachedDaemonProcess(SafeProcessHandle native, int processId)
    {
        _native = native;
        ProcessId = processId;
    }

    public int ProcessId { get; }

    public bool HasExited => _managed is not null
        ? _managed.HasExited
        : NativeMethods.WaitForSingleObject(_native!, 0) == NativeMethods.WaitObject0;

    /// <summary>
    /// The exit code once <see cref="HasExited"/> is true, or null when it cannot be read. Reading
    /// it while the daemon is still running is not meaningful, so callers gate on
    /// <see cref="HasExited"/> first.
    /// </summary>
    public int? ExitCode
    {
        get
        {
            try
            {
                if (_managed is not null)
                    return _managed.ExitCode;
                return NativeMethods.GetExitCodeProcess(_native!, out var code) ? unchecked((int)code) : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Start-up stderr observed on platforms where the daemon's stderr is redirected into this
    /// process. Empty on Windows, where the daemon is detached and
    /// <c>maui devflow broker start --foreground</c> is the diagnostic path.
    /// </summary>
    public string CapturedStandardError
    {
        get
        {
            lock (_stderr)
                return _stderr.ToString().Trim();
        }
    }

    /// <summary>
    /// Launches <paramref name="fileName"/> with <paramref name="arguments"/> as a daemon that does
    /// not inherit this process's handles. Returns null when the daemon could not be launched.
    /// </summary>
    public static DetachedDaemonProcess? Start(string fileName, string arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        return OperatingSystem.IsWindows()
            ? StartDetachedWindows(fileName, arguments)
            : StartRedirected(fileName, arguments);
    }

    private static DetachedDaemonProcess? StartRedirected(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        var process = Process.Start(startInfo);
        if (process is null)
            return null;

        var daemon = new DetachedDaemonProcess(process);
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            lock (daemon._stderr)
            {
                if (daemon._stderr.Length > 0)
                    daemon._stderr.AppendLine();
                daemon._stderr.Append(e.Data);
            }
        };
        process.BeginErrorReadLine();

        // Close stdout and stdin — the daemon is detached and stderr is captured above.
        process.StandardOutput.Close();
        process.StandardInput.Close();
        return daemon;
    }

    private static DetachedDaemonProcess? StartDetachedWindows(string fileName, string arguments)
    {
        var commandLine = new StringBuilder();
        commandLine.Append(QuoteArgument(fileName));
        if (!string.IsNullOrEmpty(arguments))
            commandLine.Append(' ').Append(arguments);

        var startupInfo = new NativeMethods.StartupInfo { cb = Marshal.SizeOf<NativeMethods.StartupInfo>() };
        if (!NativeMethods.CreateProcessW(
                lpApplicationName: fileName,
                lpCommandLine: commandLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes: IntPtr.Zero,
                bInheritHandles: false,
                dwCreationFlags: NativeMethods.DetachedProcess | NativeMethods.CreateNewProcessGroup,
                lpEnvironment: IntPtr.Zero,
                lpCurrentDirectory: null,
                lpStartupInfo: ref startupInfo,
                lpProcessInformation: out var processInformation))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (processInformation.hThread != IntPtr.Zero)
            NativeMethods.CloseHandle(processInformation.hThread);

        return new DetachedDaemonProcess(
            new SafeProcessHandle(processInformation.hProcess, ownsHandle: true),
            processInformation.dwProcessId);
    }

    /// <summary>
    /// Quotes a single command-line argument using the rules the Windows CRT parses back, so a
    /// daemon path containing spaces stays one argument.
    /// </summary>
    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;

        var quoted = new StringBuilder(value.Length + 2);
        quoted.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var backslashes = 0;
            while (index < value.Length && value[index] == '\\')
            {
                index++;
                backslashes++;
            }

            if (index == value.Length)
            {
                quoted.Append('\\', backslashes * 2);
                break;
            }

            if (value[index] == '"')
            {
                quoted.Append('\\', (backslashes * 2) + 1);
                quoted.Append('"');
            }
            else
            {
                quoted.Append('\\', backslashes);
                quoted.Append(value[index]);
            }
        }

        quoted.Append('"');
        return quoted.ToString();
    }

    public void Dispose()
    {
        if (_managed is not null)
        {
            try { _managed.CancelErrorRead(); } catch { /* the daemon may already be gone */ }
            _managed.Dispose();
        }

        _native?.Dispose();
    }

    private static class NativeMethods
    {
        internal const uint DetachedProcess = 0x00000008;
        internal const uint CreateNewProcessGroup = 0x00000200;
        internal const uint WaitObject0 = 0x00000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            public int cb;
            public IntPtr lpReserved;
            public IntPtr lpDesktop;
            public IntPtr lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcessW(
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfo lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeProcessHandle handle, out uint exitCode);
    }
}
