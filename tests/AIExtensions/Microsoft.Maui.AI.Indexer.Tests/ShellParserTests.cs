namespace Microsoft.Maui.AI.Indexer.Tests;

/// <summary>Tests for Shell navigation parsing.</summary>
public class ShellParserTests
{
    [Fact]
    public void Parse_TabBarWithShellContents()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <TabBar>
        <ShellContent Route=""home"" Title=""Home"" />
        <ShellContent Route=""settings"" Title=""Settings"" />
    </TabBar>
</Shell>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("AppShell.xaml", xaml));
        var src = sources["AppShell_UiIndex.g.cs"];
        Assert.Contains("ShellContent: \"Home\" [route: home]", src);
        Assert.Contains("ShellContent: \"Settings\" [route: settings]", src);
    }

    [Fact]
    public void Parse_TabsWithNestedContent()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <TabBar>
        <Tab Title=""Browse"" Route=""browse"">
            <ShellContent Route=""catalog"" Title=""Catalog"" />
            <ShellContent Route=""search"" Title=""Search"" />
        </Tab>
    </TabBar>
</Shell>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("AppShell.xaml", xaml));
        var src = sources["AppShell_UiIndex.g.cs"];
        Assert.Contains("Tab: \"Browse\" [route: browse]", src);
        Assert.Contains("ShellContent: \"Catalog\" [route: catalog]", src);
        Assert.Contains("ShellContent: \"Search\" [route: search]", src);
    }

    [Fact]
    public void Parse_FlyoutItems()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <FlyoutItem Title=""Dashboard"">
        <ShellContent Route=""dashboard"" Title=""Dashboard"" />
    </FlyoutItem>
    <FlyoutItem Title=""Profile"">
        <ShellContent Route=""profile"" Title=""Profile"" />
    </FlyoutItem>
</Shell>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("AppShell.xaml", xaml));
        var src = sources["AppShell_UiIndex.g.cs"];
        Assert.Contains("route: dashboard", src);
        Assert.Contains("route: profile", src);
    }

    [Fact]
    public void Parse_ShellContentWithoutTitle_UsesRoute()
    {
        var xaml = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<Shell xmlns=""http://schemas.microsoft.com/dotnet/2021/maui""
       xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml""
       x:Class=""MyApp.AppShell"">
    <TabBar>
        <ShellContent Route=""chat"" />
    </TabBar>
</Shell>";
        var sources = GeneratorTestHarness.GetGeneratedSources(("AppShell.xaml", xaml));
        var src = sources["AppShell_UiIndex.g.cs"];
        Assert.Contains("ShellContent: \"chat\"", src);
    }
}
