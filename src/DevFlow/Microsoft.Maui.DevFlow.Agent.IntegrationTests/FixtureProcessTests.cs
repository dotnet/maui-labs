using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public class FixtureProcessTests
{
    [Fact]
    public async Task RunProcess_CompletedProcess_ReturnsBothStreamsAndExitCode()
    {
        var command = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c \"echo standard output & echo standard error 1>&2 & exit /b 7\"")
            : ("/bin/sh", "-c \"printf 'standard output\\n'; printf 'standard error\\n' >&2; exit 7\"");

        var result = await ProcessFixture.RunAsync(command.Item1, command.Item2, 10);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("standard output", result.Stdout);
        Assert.Contains("standard error", result.Stderr);
    }

    [Fact]
    public async Task RunProcess_Timeout_PreservesStartupDiagnostics()
    {
        var command = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c \"echo startup output & echo startup error 1>&2 & ping -n 30 127.0.0.1 >NUL\"")
            : ("/bin/sh", "-c \"printf 'startup output\\n'; printf 'startup error\\n' >&2; sleep 30\"");

        var failure = await Assert.ThrowsAsync<TimeoutException>(
            () => ProcessFixture.RunAsync(command.Item1, command.Item2, 1));

        Assert.Contains("startup output", failure.Message);
        Assert.Contains("startup error", failure.Message);
        Assert.Contains("Exited before timeout: False", failure.Message);
    }

    sealed class ProcessFixture : AppFixtureBase
    {
        public override string Platform => "none";

        internal static Task<(string Stdout, string Stderr, int ExitCode)> RunAsync(
            string fileName, string arguments, int timeoutSeconds) =>
            RunProcessAsync(fileName, arguments, timeoutSeconds: timeoutSeconds);

        protected override Task InitializePlatformAsync() => throw new NotSupportedException();
        protected override Task DisposePlatformAsync() => Task.CompletedTask;
    }
}
