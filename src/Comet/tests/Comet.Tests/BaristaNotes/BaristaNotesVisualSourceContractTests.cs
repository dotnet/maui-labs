#nullable enable
using System;
using System.IO;
using CometSamples.BaristaNotes.Components;
using CometSamples.BaristaNotes.Styles;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.BaristaNotes;

public sealed class BaristaNotesVisualSourceContractTests
{
    [Fact]
    public void SourceGeometryAndTransientPalette_MatchPinnedBaristaContracts()
    {
        Assert.Equal(16, BaristaSourceVisualContract.ContentInset);
        Assert.Equal(24, BaristaSourceVisualContract.SectionTopPadding);
        Assert.Equal(10, BaristaSourceVisualContract.SectionBottomPadding);
        Assert.Equal(22, BaristaSourceVisualContract.PickerNormalFontSize);
        Assert.Equal(28, BaristaSourceVisualContract.PickerSelectedFontSize);
        Assert.Equal(24, BaristaSourceVisualContract.PickerHorizontalPadding);
        Assert.Equal(18, BaristaSourceVisualContract.PickerVerticalPadding);
        Assert.Equal(420, BaristaSourceVisualContract.VoicePanelHeight);
        Assert.Equal(80, BaristaSourceVisualContract.VoiceMicSize);
        Assert.Equal(Color.FromArgb("#1E1E1E"), BaristaSourceVisualContract.VoicePanel);
        Assert.Equal(Colors.White, BaristaSourceVisualContract.VoicePrimaryText);
        Assert.Equal(Color.FromArgb("#CCCCCC"), BaristaSourceVisualContract.VoiceSecondaryText);
        Assert.Equal(Color.FromArgb("#FF9500"), BaristaSourceVisualContract.VoiceActive);
        Assert.Equal(8, BaristaSourceVisualContract.SourceButtonRadius);
        Assert.Equal(14, BaristaSourceVisualContract.SourceButtonHorizontalPadding);
        Assert.Equal(10, BaristaSourceVisualContract.SourceButtonVerticalPadding);
        Assert.Equal(44, BaristaSourceVisualContract.SourceButtonMinimumHeight);
        Assert.Equal(11, BaristaSourceVisualContract.BagStatusFontSize);
    }

    [Fact]
    public void SharedComponents_KeepSourceAlignmentDensityAndOneLineButtons()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var sourceComponents = Read(root!, "sample/Shared/BaristaNotes/Components/SourceComponents.cs");
        var shotLogging = Read(root!, "sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var settings = Read(root!, "sample/Shared/BaristaNotes/Pages/SettingsPage.cs");
        var profile = Read(root!, "sample/Shared/BaristaNotes/Components/ProfileComponents.cs");
        var bag = Read(root!, "sample/Shared/BaristaNotes/Pages/BagDetailPage.cs");

        Assert.Contains(".Alignment(Comet.Alignment.BottomLeading)", sourceComponents);
        Assert.Contains("new ListView<PickerChoice>", shotLogging);
        Assert.Contains(".InitialScrollTo(selectedIndex, ListScrollPosition.Center)", shotLogging);
        Assert.Contains("BaristaSourceVisualContract.PickerSelectedFontSize", shotLogging);
        Assert.Contains("BaristaSourceVisualContract.PickerHorizontalPadding", shotLogging);
        Assert.Contains(".FillHorizontal()", settings);
        Assert.Contains("BaristaSourceVisualContract.SectionTopPadding", settings);
        Assert.Contains(".LineBreakMode(LineBreakMode.NoWrap)", profile);
        Assert.Contains(".MinimumHeight(BaristaSourceVisualContract.SourceButtonMinimumHeight)", profile);
        Assert.Contains(".LineBreakMode(LineBreakMode.NoWrap)", bag);
        Assert.Contains(".MinimumWidth(154)", bag);
    }

    [Fact]
    public void VoiceAndActivityFilter_UseThemeIndependentDarkSurfaces()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var sourceComponents = Read(root!, "sample/Shared/BaristaNotes/Components/SourceComponents.cs");
        var filter = Read(root!, "sample/Shared/BaristaNotes/Components/ActivityFilterView.cs");
        var app = Read(root!, "sample/Shared/BaristaNotes/BaristaNotesApp.cs");

        Assert.Contains(".Background(BaristaSourceVisualContract.VoicePanel)", sourceComponents);
        Assert.Contains(".ClipShape(new Ellipse())", sourceComponents);
        Assert.Contains("? BaristaSourceVisualContract.VoiceActive", sourceComponents);
        Assert.Contains("BaristaSourceVisualContract.VoiceMicIdleOutline", sourceComponents);
        Assert.Contains("CoffeeTheme.Dark.Surface", filter);
        Assert.Contains("CoffeeTheme.Dark.TextPrimary", filter);
        Assert.Contains("useFixedDarkPalette: modalKind == BaristaModalKind.ActivityFilter", app);
    }

    [Fact]
    public void ProfileAndBagMultilineEditors_UseSourceTransparentChrome()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var profile = Read(root!, "sample/Shared/BaristaNotes/Pages/ProfileDetailPage.cs");
        var bag = Read(root!, "sample/Shared/BaristaNotes/Pages/BagDetailPage.cs");

        Assert.Contains(
            "SignalExtensions.TextEditor(_context)\n                .Placeholder",
            profile.Replace("\r\n", "\n"));
        Assert.Contains(
            ".IsEnabled(!IsOperationActive)\n                .SourceInputChrome()\n                .SourceText()",
            profile.Replace("\r\n", "\n"));
        Assert.Contains(
            "SignalExtensions.TextEditor(_notes)\n            .SourceInputChrome()\n            .SourceText()",
            bag.Replace("\r\n", "\n"));
        Assert.Equal(1, profile.Split(".SourceInputChrome()", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, bag.Split(".SourceInputChrome()", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void NewDrinkRating_RemainsOneLineAndFitsTheIOSCell()
    {
        var root = FindCometRoot();
        Assert.NotNull(root);

        var shotLogging = Read(root!, "sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");
        var sourceComponents = Read(root!, "sample/Shared/BaristaNotes/Components/SourceComponents.cs");

        Assert.Equal(38, AdaptiveTwoLineTile.ValueFontSize("★★★★☆", hasUnit: false));
        Assert.Contains("singleLineValue: true", shotLogging);
        Assert.Contains("const double RatingValueFontSize = 34;", shotLogging);
        Assert.Contains("valueFontSize: RatingValueFontSize", shotLogging);
        Assert.DoesNotContain("RatingValueCharacterSpacing", shotLogging);
        Assert.Contains(".CharacterSpacing(valueCharacterSpacing)", sourceComponents);
        Assert.Contains("valueText.LineBreakMode(LineBreakMode.NoWrap);", sourceComponents);
    }

    static string Read(string root, string relativePath) =>
        File.ReadAllText(IOPath.Combine(root, relativePath));

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var index = 0; index < 10 && directory is not null; index++)
        {
            if (File.Exists(IOPath.Combine(directory, "global.json"))
                && Directory.Exists(IOPath.Combine(directory, "sample")))
            {
                return directory;
            }

            directory = IOPath.GetDirectoryName(directory);
        }

        return null;
    }
}
