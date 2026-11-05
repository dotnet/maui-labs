// BaristaNotes Acceptance Contracts — Probe Wiring
// Verifies that both probe hosts (Android/iOS) include the shared BaristaNotes
// source and support the CometSample property for side-by-side installs.

using System;
using Xunit;

#nullable enable

namespace Comet.Tests.BaristaNotes;

// ==========================================================================
// A3: Probe Host Wiring Contracts
// ==========================================================================

public class ProbeWiringContractTests
{
    /// <summary>Both probes must include the Shared/BaristaNotes source via Compile Include.</summary>
    [Theory]
    [InlineData("sample/CometComposeProbe/CometComposeProbe.csproj")]
    [InlineData("sample/CometSwiftUIProbe/CometSwiftUIProbe.csproj")]
    public void Probe_IncludesSharedBaristaNotes(string relativeCsproj)
    {
        var root = FindCometRoot();
        if (root is null) return; // CI without source tree
        var csproj = System.IO.Path.Combine(root, relativeCsproj);
        if (!System.IO.File.Exists(csproj)) { Assert.Fail($"Probe csproj not found: {csproj}"); return; }
        var content = System.IO.File.ReadAllText(csproj);
        Assert.Contains("Shared\\BaristaNotes\\", content.Replace("/", "\\"));
    }

    /// <summary>Both probes must define the CometSample property conditional.</summary>
    [Theory]
    [InlineData("sample/CometComposeProbe/CometComposeProbe.csproj")]
    [InlineData("sample/CometSwiftUIProbe/CometSwiftUIProbe.csproj")]
    public void Probe_SupportsCometSampleProperty(string relativeCsproj)
    {
        var root = FindCometRoot();
        if (root is null) return;
        var csproj = System.IO.Path.Combine(root, relativeCsproj);
        if (!System.IO.File.Exists(csproj)) { Assert.Fail($"Probe csproj not found: {csproj}"); return; }
        var content = System.IO.File.ReadAllText(csproj);
        Assert.Contains("CometSample", content);
        Assert.Contains("com.comet.sample.$(CometSample)", content);
    }

    /// <summary>Android probe must have baristanotes icon assets.</summary>
    [Fact]
    public void AndroidProbe_HasBaristaNotesIconAssets()
    {
        var root = FindCometRoot();
        if (root is null) return;
        var iconDir = System.IO.Path.Combine(root, "sample/CometComposeProbe/Icons/baristanotes");
        if (!System.IO.Directory.Exists(iconDir)) { Assert.Fail($"Missing: {iconDir}"); return; }
        var appIcon = System.IO.Path.Combine(iconDir, "appicon.png");
        var probeIcon = System.IO.Path.Combine(root, "sample/CometComposeProbe/Icons/probe/appicon.png");
        Assert.True(System.IO.File.Exists(appIcon), "appicon.png missing");
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(iconDir, "strings.xml")), "strings.xml missing");
        Assert.False(
            System.IO.File.ReadAllBytes(appIcon).AsSpan().SequenceEqual(
                System.IO.File.ReadAllBytes(probeIcon)),
            "BaristaNotes must not package the generic Comet launcher icon.");
    }

    /// <summary>iOS probe must have baristanotes xcassets.</summary>
    [Fact]
    public void iOSProbe_HasBaristaNotesXcassets()
    {
        var root = FindCometRoot();
        if (root is null) return;
        var xcassets = System.IO.Path.Combine(root, "sample/CometSwiftUIProbe/Icons/baristanotes.xcassets");
        Assert.True(System.IO.Directory.Exists(xcassets), $"Missing: {xcassets}");
        var appIcon = System.IO.Path.Combine(xcassets, "AppIcon.appiconset/appicon.png");
        var probeIcon = System.IO.Path.Combine(
            root,
            "sample/CometSwiftUIProbe/Icons/probe.xcassets/AppIcon.appiconset/appicon.png");
        Assert.True(System.IO.File.Exists(appIcon), "BaristaNotes iOS appicon.png missing");
        Assert.False(
            System.IO.File.ReadAllBytes(appIcon).AsSpan().SequenceEqual(
                System.IO.File.ReadAllBytes(probeIcon)),
            "BaristaNotes must not package the generic Comet launcher icon.");
    }

    static string? FindCometRoot()
    {
        // Walk up from test assembly to find src/Comet
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "global.json")) &&
                System.IO.Directory.Exists(System.IO.Path.Combine(dir, "sample")))
                return dir;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return null;
    }
}
