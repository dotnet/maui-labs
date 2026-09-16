using System.Text;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class XamlSourcePropertyEditorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "maui-devflow-source-edit-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PersistAsync_DirectLiteral_ReplacesOnlyValueAndPreservesUtf8Bom()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <?xml version="1.0" encoding="utf-8" ?>
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label AutomationId="HeaderLabel"
                       Text="Original"
                       FontSize="28" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "Saved & \"quoted\" <value>");

        Assert.Equal(XamlSourceEditStatus.Success, result.Status);
        var updated = await File.ReadAllTextAsync(sourcePath);
        Assert.Contains("Text=\"Saved &amp; &quot;quoted&quot; &lt;value>\"", updated);
        Assert.Contains("FontSize=\"28\"", updated);
        Assert.Equal(XamlSourcePropertyEditor.ComputeSourceHash(updated), result.SourceHash);

        var bytes = await File.ReadAllBytesAsync(sourcePath);
        Assert.True(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task PersistAsync_AtomicReplacement_PreservesFilePermissions()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.Hidden);
            try
            {
                Assert.Equal(XamlSourceEditStatus.Success, (await editor.PersistAsync(element, "Text", "Updated")).Status);
                Assert.True((File.GetAttributes(sourcePath) & FileAttributes.Hidden) != 0);
            }
            finally
            {
                File.SetAttributes(sourcePath, FileAttributes.Normal);
            }
        }
        else
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(sourcePath, mode);

            Assert.Equal(XamlSourceEditStatus.Success, (await editor.PersistAsync(element, "Text", "Updated")).Status);
            Assert.Equal(mode, File.GetUnixFileMode(sourcePath));
        }
    }

    [Fact]
    public async Task PersistAsync_SecondInspectorEdit_UsesTrackedFileVersion()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var first = await editor.PersistAsync(element, "Text", "First");
        var second = await editor.PersistAsync(element, "Text", "Second");

        Assert.Equal(XamlSourceEditStatus.Success, first.Status);
        Assert.Equal(XamlSourceEditStatus.Success, second.Status);
        Assert.Contains("Text=\"Second\"", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_SeparateInspectorInstances_ShareTrackedFileVersion()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var firstEditor = CreateEditor(sourcePath);
        var secondEditor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var first = await firstEditor.PersistAsync(element, "Text", "First");
        var second = await secondEditor.PersistAsync(element, "Text", "Second");

        Assert.Equal(XamlSourceEditStatus.Success, first.Status);
        Assert.Equal(XamlSourceEditStatus.Success, second.Status);
        Assert.Contains("Text=\"Second\"", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_ExternalEditAfterInspectorWrite_ReturnsStale()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");
        Assert.Equal(XamlSourceEditStatus.Success, (await editor.PersistAsync(element, "Text", "First")).Status);

        await File.AppendAllTextAsync(sourcePath, $"{Environment.NewLine}<!-- external edit -->");
        var externallyEdited = await File.ReadAllTextAsync(sourcePath);

        var result = await editor.PersistAsync(element, "Text", "Second");

        Assert.Equal(XamlSourceEditStatus.Stale, result.Status);
        Assert.Equal(externallyEdited, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_EncodingOnlyExternalEditAfterInspectorWrite_ReturnsStale()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");
        Assert.Equal(XamlSourceEditStatus.Success, (await editor.PersistAsync(element, "Text", "First")).Status);

        var sameText = await File.ReadAllTextAsync(sourcePath);
        await File.WriteAllTextAsync(sourcePath, sameText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var result = await editor.PersistAsync(element, "Text", "Second");

        Assert.Equal(XamlSourceEditStatus.Stale, result.Status);
        Assert.False((await File.ReadAllBytesAsync(sourcePath)).AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public async Task PersistAsync_MarkupExtension_ReturnsUnsupportedWithoutWriting()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="{Binding Title}" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "Replacement");

        Assert.Equal(XamlSourceEditStatus.Unsupported, result.Status);
        Assert.Contains("binding", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_ValueBeginningWithBrace_IsEscapedAsLiteral()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "{not a binding}");

        Assert.Equal(XamlSourceEditStatus.Success, result.Status);
        Assert.Contains("Text=\"{}{not a binding}\"", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_InvalidXmlCharacter_ReturnsInvalidRequestWithoutWriting()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "invalid\u000Bvalue");

        Assert.Equal(XamlSourceEditStatus.InvalidRequest, result.Status);
        Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
    }

    [Theory]
    [InlineData("xmlns")]
    [InlineData("x:Class")]
    [InlineData("AutomationId")]
    public async Task PersistAsync_NonCuratedAttribute_ReturnsInvalidRequestWithoutWriting(string propertyName)
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = CreateEditor(sourcePath);
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, propertyName, "Replacement");

        Assert.Equal(XamlSourceEditStatus.InvalidRequest, result.Status);
        Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_EarlierSameLineEdit_ShiftsLaterElementSafely()
    {
        const string xaml =
            "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"><Grid><Label Text=\"First\" /><Label Text=\"Second\" /></Grid></ContentPage>";
        var (sourcePath, _) = await CreateProjectAsync(xaml);
        var editor = CreateEditor(sourcePath);
        var first = CreateElement(sourcePath, xaml, "<Label", occurrence: 0);
        var second = CreateElement(sourcePath, xaml, "<Label", occurrence: 1);

        var firstResult = await editor.PersistAsync(first, "Text", "A much longer first value");
        var secondResult = await editor.PersistAsync(second, "Text", "Updated second");

        Assert.Equal(XamlSourceEditStatus.Success, firstResult.Status);
        Assert.Equal(XamlSourceEditStatus.Success, secondResult.Status);
        var updated = await File.ReadAllTextAsync(sourcePath);
        Assert.Contains("Text=\"A much longer first value\"", updated);
        Assert.Contains("Text=\"Updated second\"", updated);
    }

    [Fact]
    public async Task PersistAsync_SourceOutsideRegisteredProject_ReturnsForbidden()
    {
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "TestApp.csproj"), "<Project />");

        var outsideRoot = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(outsideRoot);
        var xaml = "<ContentPage><Label Text=\"Original\" /></ContentPage>";
        var sourcePath = Path.Combine(outsideRoot, "Outside.xaml");
        await File.WriteAllTextAsync(sourcePath, xaml);

        var projectPath = Path.Combine(projectRoot, "TestApp.csproj");
        var editor = new XamlSourcePropertyEditor(
            "TestApp.csproj",
            XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath));
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "Replacement");

        Assert.Equal(XamlSourceEditStatus.Forbidden, result.Status);
        Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task PersistAsync_RelativeProjectIdentity_MatchingSessionSucceeds()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var projectPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "TestApp.csproj");
        var editor = new XamlSourcePropertyEditor(
            "TestApp.csproj",
            XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath));
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "Updated");

        Assert.Equal(XamlSourceEditStatus.Success, result.Status);
        Assert.Contains("Text=\"Updated\"", await File.ReadAllTextAsync(sourcePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersistAsync_RelativeSourceAndProject_ResolvesFromWorkspace(bool hostLocalProcess)
    {
        var workspaceRoot = Path.Combine(_tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, ".git"), "gitdir: test");

        var projectRoot = Path.Combine(workspaceRoot, "samples", "TestApp");
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "TestApp.csproj");
        await File.WriteAllTextAsync(projectPath, "<Project />");

        var sourceDirectory = Path.Combine(projectRoot, "Views");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "MainPage.xaml");
        const string xaml = """
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """;
        await File.WriteAllTextAsync(sourcePath, xaml);

        var appDirectory = Path.Combine(workspaceRoot, "artifacts", "bin", "TestApp", "Debug");
        Directory.CreateDirectory(appDirectory);
        var appPath = Path.Combine(appDirectory, "TestApp.exe");
        await File.WriteAllBytesAsync(appPath, []);

        var editor = new XamlSourcePropertyEditor(
            "TestApp.csproj",
            XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath),
            appExecutablePath: hostLocalProcess ? appPath : null,
            workspace: XamlSourceWorkspace.Capture(workspaceRoot));
        var element = CreateElement(Path.Combine("Views", "MainPage.xaml"), xaml, "<Label");

        Assert.Equal(sourcePath, editor.ResolveSourcePath(element.SourceFile!, out var error));
        Assert.Null(error);
        var result = await editor.PersistAsync(element, "Text", "Updated");

        Assert.Equal(XamlSourceEditStatus.Success, result.Status);
        Assert.Contains("Text=\"Updated\"", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task ResolveSourcePath_WorkspaceRemovedAfterCapture_DoesNotSearchParent()
    {
        var (sourcePath, _) = await CreateProjectAsync("<ContentPage />");
        var projectRoot = Path.GetDirectoryName(sourcePath)!;
        var projectPath = Path.Combine(projectRoot, "TestApp.csproj");
        var workspacePath = Path.Combine(projectRoot, "workspace");
        Directory.CreateDirectory(workspacePath);
        var workspace = XamlSourceWorkspace.Capture(workspacePath);
        Directory.Delete(workspacePath);

        var editor = new XamlSourcePropertyEditor(
            "TestApp.csproj",
            XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath),
            workspace: workspace);

        Assert.Null(editor.ResolveSourcePath("MainPage.xaml", out var error));
        Assert.Contains("MAUI_DEVFLOW_PROJECT_ROOT", error);
    }

    [Fact]
    public async Task ResolveSourcePath_RelativeSource_ReturnsFullLocalPath()
    {
        var (sourcePath, _) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var projectPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "TestApp.csproj");
        var editor = new XamlSourcePropertyEditor(projectPath);

        var resolved = editor.ResolveSourcePath("MainPage.xaml", out var error);

        Assert.Null(error);
        Assert.Equal(sourcePath, resolved);
        Assert.True(Path.IsPathFullyQualified(resolved!));
    }

    [Fact]
    public async Task ResolveSourcePath_AbsoluteLinkedSource_ReturnsExistingPath()
    {
        var projectRoot = Path.Combine(_tempRoot, "project-with-link");
        Directory.CreateDirectory(projectRoot);
        var projectPath = Path.Combine(projectRoot, "TestApp.csproj");
        await File.WriteAllTextAsync(projectPath, "<Project />");

        var sharedRoot = Path.Combine(_tempRoot, "shared");
        Directory.CreateDirectory(sharedRoot);
        var sourcePath = Path.Combine(sharedRoot, "SharedPage.xaml");
        await File.WriteAllTextAsync(sourcePath, "<ContentPage />");
        var editor = new XamlSourcePropertyEditor(projectPath);

        var resolved = editor.ResolveSourcePath(sourcePath, out var error);

        Assert.Null(error);
        Assert.Equal(sourcePath, resolved);
    }

    [Theory]
    [InlineData(@"\\server\share\Page.xaml")]
    [InlineData(@"\\?\C:\source\Page.xaml")]
    [InlineData(@"\\.\C:\source\Page.xaml")]
    [InlineData(@"\??\C:\source\Page.xaml")]
    [InlineData(@"\??\UNC\server\share\Page.xaml")]
    [InlineData(@"\??\GLOBALROOT\Device\HarddiskVolumeShadowCopy1\Page.xaml")]
    public void UnsafeAbsoluteSourcePath_RejectsNetworkAndDeviceNamespaces(string path)
    {
        if (OperatingSystem.IsWindows())
            Assert.True(XamlSourcePropertyEditor.IsUnsafeAbsoluteSourcePath(path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindRelativeProjectRoot_AmbiguousWorkspaceReturnsNull(bool hostLocalProcess)
    {
        var workspaceRoot = Path.Combine(_tempRoot, "ambiguous-workspace");
        Directory.CreateDirectory(workspaceRoot);
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, ".git"), "gitdir: test");

        string? sessionId = null;
        string? firstProjectRoot = null;
        foreach (var name in new[] { "one", "two" })
        {
            var projectRoot = Path.Combine(workspaceRoot, name, "shared-application-folder");
            firstProjectRoot ??= projectRoot;
            Directory.CreateDirectory(projectRoot);
            var projectPath = Path.Combine(projectRoot, "TestApp.csproj");
            await File.WriteAllTextAsync(projectPath, "<Project />");
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "MainPage.xaml"), "<ContentPage />");
            sessionId ??= XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath);
            Assert.Equal(sessionId, XamlSourcePropertyEditor.ComputeDefaultSessionId(projectPath));
        }

        var appDirectory = Path.Combine(workspaceRoot, "artifacts", "bin", "TestApp", "Debug");
        Directory.CreateDirectory(appDirectory);

        var result = XamlSourcePropertyEditor.FindRelativeProjectRoot(
            "TestApp.csproj",
            sessionId,
            "MainPage.xaml",
            appExecutablePath: hostLocalProcess ? Path.Combine(appDirectory, "TestApp.exe") : null,
            currentDirectory: hostLocalProcess ? firstProjectRoot : workspaceRoot);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("WinUI", true)]
    [InlineData("windows", true)]
    [InlineData("macOS", true)]
    [InlineData("MacCatalyst", true)]
    [InlineData("GTK", true)]
    [InlineData("Android", false)]
    [InlineData("iOS", false)]
    [InlineData(null, false)]
    public void HostProcessLookup_OnlyUsesLocalPlatforms(string? platform, bool expected)
        => Assert.Equal(expected, InspectorServer.IsHostLocalPlatform(platform));

    [Theory]
    [InlineData(@"C:\apps\DevFlow.Sample.exe", "DevFlow.Sample.csproj", "MauiTodo", true)]
    [InlineData(@"C:\apps\MauiTodo.exe", "DevFlow.Sample.csproj", "MauiTodo", true)]
    [InlineData(@"C:\apps\Other.exe", "DevFlow.Sample.csproj", "MauiTodo", false)]
    [InlineData(null, "DevFlow.Sample.csproj", "MauiTodo", false)]
    public void HostProcessLookup_RequiresMatchingProjectOrAppExecutable(
        string? executablePath,
        string? project,
        string? appName,
        bool expected)
        => Assert.Equal(expected, InspectorServer.ExecutableMatchesAgent(executablePath, project, appName));

    [Fact]
    public async Task PersistAsync_RelativeProjectIdentity_MismatchedSessionIsForbidden()
    {
        var (sourcePath, xaml) = await CreateProjectAsync("""
            <ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui">
                <Label Text="Original" />
            </ContentPage>
            """);
        var editor = new XamlSourcePropertyEditor("TestApp.csproj", "dwotherproject");
        var element = CreateElement(sourcePath, xaml, "<Label");

        var result = await editor.PersistAsync(element, "Text", "Updated");

        Assert.Equal(XamlSourceEditStatus.Forbidden, result.Status);
        Assert.Equal(xaml, await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public void PersistPropertyRoute_IsMutationAndTokenGated()
    {
        Assert.True(InspectorServer.IsMutation("/api/persistProperty"));
        Assert.True(InspectorServer.IsTokenGatedPath("/api/persistProperty"));
    }

    private async Task<(string SourcePath, string Xaml)> CreateProjectAsync(string xaml)
    {
        var projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "TestApp.csproj"), "<Project />");

        var sourcePath = Path.Combine(projectRoot, "MainPage.xaml");
        await File.WriteAllTextAsync(sourcePath, xaml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return (sourcePath, xaml);
    }

    private static XamlSourcePropertyEditor CreateEditor(string sourcePath) =>
        new(Path.Combine(Path.GetDirectoryName(sourcePath)!, "TestApp.csproj"));

    private static ElementInfo CreateElement(string sourcePath, string xaml, string marker, int occurrence = 0)
    {
        var offset = -1;
        var searchStart = 0;
        for (var i = 0; i <= occurrence; i++)
        {
            offset = xaml.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (offset < 0)
                break;
            searchStart = offset + marker.Length;
        }
        Assert.True(offset >= 0);

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (xaml[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new ElementInfo
        {
            Id = "HeaderLabel",
            SourceFile = sourcePath,
            SourceLine = line,
            // XDocument's IXmlLineInfo reports the first element-name character after '<'.
            SourceColumn = offset - lineStart + 2,
            SourceHash = XamlSourcePropertyEditor.ComputeSourceHash(xaml),
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { }
    }
}
