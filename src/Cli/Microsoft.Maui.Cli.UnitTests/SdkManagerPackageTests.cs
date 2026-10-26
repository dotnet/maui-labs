// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Providers.Android;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class SdkManagerPackageTests : IDisposable
{
    readonly string _sdkPath = Path.Combine(Path.GetTempPath(), $"maui sdk {Guid.NewGuid():N}");

    [Fact]
    public async Task GetInstalledPackagesAsync_ClassicTable_PreservesPackages()
    {
        using var manager = CreateManager("""
			Loading package information...
			Installed packages:
			  Path                 | Version | Description               | Location
			  -------              | ------- | -------                   | -------
			  platform-tools       | 37.0.1  | Android SDK Platform-Tools | platform-tools
			  platforms;android-37.0 | 2      | Android SDK Platform 37.0 | platforms/android-37.0
			  cmdline-tools;latest | 23.0    | Android SDK Command-line Tools | cmdline-tools/latest
			Available Packages:
			  build-tools;37.0.0   | 37.0.0  | Android SDK Build-Tools 37
			Available Updates:
			  platform-tools       | 37.0.1  | 38.0.0
			""");

        var installed = await manager.GetInstalledPackagesAsync();
        var available = await manager.GetAvailablePackagesAsync();

        Assert.Equal(3, installed.Count);
        Assert.All(installed, package => Assert.True(package.IsInstalled));
        Assert.Contains(installed, package => package.Path == "platforms;android-37.0" && package.Version == "2");
        Assert.Equal("build-tools;37.0.0", Assert.Single(available).Path);
        Assert.False(available[0].IsInstalled);
    }

    [Fact]
    public async Task GetInstalledPackagesAsync_Tools23Table_NormalizesIdsAndPreservesInstalledRevisions()
    {
        using var manager = CreateManager("""
			WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated. Android CLI will be used instead.
			Installed packages:
			  platforms/android-37.0  2.0.0                  Android SDK Platform 37.0
			  platform-tools          37.0.1                 Android SDK Platform-Tools
			  cmdline-tools/latest    23.0.0                 Android SDK Command-line Tools (latest)
			  cmdline-tools/19.0       unknown   ->  19.0.0   Android SDK Command-line Tools
			Available packages:
			  build-tools/37.0.0      37.0.0                 Android SDK Build-Tools 37
			  build-tools/38.0.0-rc1   38.0.0 rc1             Android SDK Build-Tools 38-rc1
			""");

        var installed = await manager.GetInstalledPackagesAsync();
        var available = await manager.GetAvailablePackagesAsync();

        Assert.Equal(4, installed.Count);
        Assert.Contains(installed, package => package.Path == "platforms;android-37.0" && package.Version == "2.0.0");
        Assert.Contains(installed, package => package.Path == "cmdline-tools;19.0" &&
            package.Version == "unknown" && package.Description == "Android SDK Command-line Tools");
        Assert.All(installed, package => Assert.True(package.IsInstalled));
        Assert.Equal(2, available.Count);
        Assert.Contains(available, package => package.Path == "build-tools;37.0.0");
        Assert.Contains(available, package => package.Path == "build-tools;38.0.0-rc1" && package.Version == "38.0.0 rc1");
        Assert.All(available, package => Assert.False(package.IsInstalled));
    }

    [Fact]
    public async Task GetInstalledPackagesAsync_UnlistedCommandLineTools_ReadsInstalledMetadata()
    {
        using var manager = CreateManager("""
			Installed packages:
			  Path           | Version | Description
			  platform-tools | 37.0.1  | Android SDK Platform-Tools
			Available Packages:
			""");

        var installed = await manager.GetInstalledPackagesAsync();

        Assert.Contains(installed, package => package.Path == "cmdline-tools;latest" &&
            package.Version == "23.0" && package.IsInstalled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pkg.Path=cmdline-tools;latest\n")]
    [InlineData("Pkg.Revision=\r\nPkg.Path=cmdline-tools;latest\r\n")]
    [InlineData("Pkg.Revision= \t \r\n")]
    public async Task GetInstalledPackagesAsync_UnlistedToolsWithoutRevision_DoesNotInferSupportedVersion(string? properties)
    {
        using var manager = CreateManager("Installed packages:\nAvailable Packages:\n");
        var propertiesPath = Path.Combine(_sdkPath, "cmdline-tools", "latest", "source.properties");
        if (properties is null)
            File.Delete(propertiesPath);
        else
            File.WriteAllText(propertiesPath, properties);

        var installed = await manager.GetInstalledPackagesAsync();

        var tools = Assert.Single(installed);
        Assert.Equal("cmdline-tools;latest", tools.Path);
        Assert.Equal(string.Empty, tools.Version);
    }

    [Theory]
    [InlineData("Pkg.Revision=21.0\r\n", "21.0")]
    [InlineData("Pkg.Revision=\r\nPkg.Path=cmdline-tools;latest\r\n", "23.0.0")]
    [InlineData("Pkg.Revision= \t \r\n", "23.0.0")]
    [InlineData("Pkg.Path=cmdline-tools;latest\n", "23.0.0")]
    [InlineData("", "23.0.0")]
    [InlineData(null, "23.0.0")]
    public async Task GetInstalledPackagesAsync_ListedTools_UsesMetadataRevisionWithInventoryFallback(string? properties, string expectedVersion)
    {
        using var manager = CreateManager("Installed packages:\n  cmdline-tools/latest  23.0.0  Android SDK Command-line Tools\n");
        var propertiesPath = Path.Combine(_sdkPath, "cmdline-tools", "latest", "source.properties");
        if (properties is null)
            File.Delete(propertiesPath);
        else
            File.WriteAllText(propertiesPath, properties);

        var installed = await manager.GetInstalledPackagesAsync();

        Assert.Equal(expectedVersion, Assert.Single(installed).Version);
    }

    [Fact]
    public async Task GetAvailablePackagesAsync_EmptyRecognizedSections_ReturnsEmptyList()
    {
        using var manager = CreateManager("Installed packages:\nAvailable packages:\n");

        Assert.Empty(await manager.GetAvailablePackagesAsync());
    }

    [Theory]
    [InlineData("28.0.0-rc.1")]
    [InlineData("37.0.0-alpha.2")]
    [InlineData("37.0.0-beta.1")]
    [InlineData("37.0.0-rc1")]
    [InlineData("37.0.0 rc1")]
    public async Task GetAvailablePackagesAsync_PrereleaseRevision_PreservesVersion(string revision)
    {
        using var manager = CreateManager($"Installed packages:\nAvailable packages:\n  build-tools/37.0.0-rc1  {revision}  Android SDK Build-Tools\n");

        var available = await manager.GetAvailablePackagesAsync();

        Assert.Equal(revision, Assert.Single(available).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPackagesAsync_ProcessFailure_PropagatesExitCodeAndOutput(bool available)
    {
        using var manager = CreateManager("repository could not be loaded", "network access denied", exitCode: 7);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            available ? manager.GetAvailablePackagesAsync() : manager.GetInstalledPackagesAsync());

        Assert.Contains("7", error.Message);
        Assert.Contains("repository could not be loaded", error.Message);
        Assert.Contains("network access denied", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unexpected SDK output")]
    [InlineData("Installed packages:\n  malformed package row\n")]
    public async Task GetInstalledPackagesAsync_UnrecognizedOutput_ReportsParsingFailure(string output)
    {
        using var manager = CreateManager(output, "tool diagnostic");

        var error = await Assert.ThrowsAsync<FormatException>(() => manager.GetInstalledPackagesAsync());

        Assert.Contains("SDK", error.Message);
        Assert.Contains("tool diagnostic", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPackagesAsync_Canceled_PropagatesCancellation(bool available)
    {
        using var manager = CreateManager("Installed packages:\nAvailable Packages:\n");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            available ? manager.GetAvailablePackagesAsync(canceled.Token) : manager.GetInstalledPackagesAsync(canceled.Token));
    }

    [Fact]
    public async Task GetInstalledPackagesAsync_PassesSelectedSdkRoot()
    {
        using var manager = CreateManager("Installed packages:\nAvailable Packages:\n");

        await manager.GetInstalledPackagesAsync();

        var invocation = File.ReadAllLines(Path.Combine(_sdkPath, "cmdline-tools", "latest", "bin", "invocation.txt"));
        Assert.Contains("--list", invocation);
        Assert.Contains($"--sdk_root={_sdkPath}", invocation);
    }

    [Fact]
    public void CreatePackageListStartInfo_WindowsBatch_KeepsPathsOutOfCommandText()
    {
        const string sdkPath = @"C:\Android SDK %USERPROFILE% & tools!";
        var toolPath = sdkPath + @"\cmdline-tools\latest\bin\sdkmanager.bat";

        var startInfo = SdkManager.CreatePackageListStartInfo(toolPath, sdkPath, null, isWindows: true);

        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Empty(startInfo.ArgumentList);
        Assert.Equal("/d /s /v:off /c \"\"%MAUI_SDK_MANAGER_PATH%\" --list \"--sdk_root=%ANDROID_HOME%\"\"", startInfo.Arguments);
        Assert.Equal(toolPath, startInfo.Environment["MAUI_SDK_MANAGER_PATH"]);
        Assert.Equal(sdkPath, startInfo.Environment["ANDROID_HOME"]);
        Assert.DoesNotContain(sdkPath, startInfo.Arguments);
    }

    [Theory]
    [InlineData("23.0")]
    [InlineData("22.0")]
    public async Task EnsureLatestCommandLineToolsAsync_CurrentTools_ReturnsInstalledRevision(string catalogRevision)
    {
        using var manager = CreateManager($"""
            Installed packages:
              Path | Version | Description
              cmdline-tools;latest | 23.0 | Android SDK Command-line Tools
            Available Packages:
              cmdline-tools;latest | {catalogRevision} | Android SDK Command-line Tools
            """);

        var tool = await manager.EnsureLatestCommandLineToolsAsync(_sdkPath);

        Assert.Equal(manager.SdkManagerPath, tool.Path);
        Assert.Equal("23.0", tool.Revision);
        var invocation = File.ReadAllLines(Path.Combine(_sdkPath, "cmdline-tools", "latest", "bin", "invocation.txt"));
        Assert.Contains("--list", invocation);
        Assert.DoesNotContain("--install", invocation);
    }

    [Fact]
    public async Task EnsureLatestCommandLineToolsAsync_SynchronizesJdkPath()
    {
        using var manager = CreateManager("""
            Installed packages:
              Path | Version | Description
              cmdline-tools;latest | 23.0 | Android SDK Command-line Tools
            Available Packages:
            """, jdkPath: Path.Combine(_sdkPath, "jdk"));

        await manager.EnsureLatestCommandLineToolsAsync(_sdkPath);

        var javaHome = File.ReadAllText(Path.Combine(_sdkPath, "cmdline-tools", "latest", "bin", "java-home.txt")).Trim();
        Assert.Equal(Path.Combine(_sdkPath, "jdk"), javaHome);
    }

    SdkManager CreateManager(string output, string error = "", int exitCode = 0, string? jdkPath = null)
    {
        var toolRoot = Path.Combine(_sdkPath, "cmdline-tools", "latest");
        var binPath = Path.Combine(toolRoot, "bin");
        Directory.CreateDirectory(binPath);
        File.WriteAllText(Path.Combine(toolRoot, "source.properties"), "Pkg.Revision=23.0\n");
        File.WriteAllText(Path.Combine(binPath, "inventory.txt"), output);
        File.WriteAllText(Path.Combine(binPath, "inventory-error.txt"), error);
        var toolPath = Path.Combine(binPath, OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager");
        var script = OperatingSystem.IsWindows()
            ? $"@echo off\r\n(for %%a in (%*) do @echo %%~a)>\"%~dp0invocation.txt\"\r\necho %JAVA_HOME%>\"%~dp0java-home.txt\"\r\n" +
                $"type \"%~dp0inventory.txt\"\r\ntype \"%~dp0inventory-error.txt\" 1>&2\r\nexit /b {exitCode}\r\n"
            : "#!/bin/sh\nprintf '%s\\n' \"$@\" > \"$(dirname \"$0\")/invocation.txt\"\nprintf '%s\\n' \"$JAVA_HOME\" > \"$(dirname \"$0\")/java-home.txt\"\n" +
                $"cat \"$(dirname \"$0\")/inventory.txt\"\ncat \"$(dirname \"$0\")/inventory-error.txt\" >&2\nexit {exitCode}\n";
        File.WriteAllText(toolPath, script);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(toolPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new SdkManager(() => _sdkPath, () => jdkPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sdkPath))
            Directory.Delete(_sdkPath, recursive: true);
    }
}
