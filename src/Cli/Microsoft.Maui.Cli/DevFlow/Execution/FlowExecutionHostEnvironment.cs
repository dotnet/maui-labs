using System.Runtime.InteropServices;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal interface IFlowExecutionHostEnvironment
{
    bool IsWindows { get; }
    bool IsMacOS { get; }
    Architecture ProcessArchitecture { get; }
    string MachineName { get; }
    string OsVersion { get; }
}

internal sealed class SystemFlowExecutionHostEnvironment : IFlowExecutionHostEnvironment
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;
    public string MachineName => Environment.MachineName;
    public string OsVersion => Environment.OSVersion.VersionString;
}
