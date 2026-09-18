using System;
using System.IO;
using Xunit;

namespace Comet.Tests.BaristaNotes;

public class R1VisualContractTests
{
    [Fact]
    public void NewDrinkPeopleTile_UsesSourceAvatarComposition()
    {
        var source = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains("contentGrid.Add(PeopleTile()", source);
        Assert.Contains("View PeopleTile()", source);
        Assert.Contains("PersonChip(maker", source);
        Assert.Contains("PersonChip(recipient", source);
        Assert.Contains("CoffeeIcons.Person", source);
        Assert.DoesNotContain("Tile(\"MADE BY / FOR\", PeopleValue", source);
        Assert.Contains(".Alignment(Comet.Alignment.BottomLeading)", source);
    }

    [Fact]
    public void NewDrinkSystemBack_OnlyInterceptsAnActivePicker()
    {
        var source = Read("sample/Shared/BaristaNotes/Pages/ShotLoggingPage.cs");

        Assert.Contains(
            "() => _picker.Value != DrinkPickerKind.None",
            source);
        Assert.Contains(
            "_picker.PropertyChanged += (_, _) => _backCommand.RaiseCanExecuteChanged();",
            source);
        Assert.DoesNotContain("void AttemptBack()", source);

        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        Assert.Contains("public void NotifyShotEditorDisposed(ShotLoggingPage page)", app);
        Assert.Contains("_navigation.NotifyShotEditorDisposed(this);", source);
        Assert.Contains("if (ReferenceEquals(view, _rootShotEditor))", app);
        Assert.DoesNotContain("view.Dispose();", app);
    }

    [Fact]
    public void AndroidBaristaHost_UsesReferenceSystemBarContract()
    {
        var host = Read("sample/CometComposeProbe/MainActivity.cs");
        var app = Read("sample/Shared/BaristaNotes/BaristaNotesApp.cs");
        var safeArea = Read("sample/Shared/BaristaNotes/Styles/BaristaSafeAreaLayout.cs");
        var components = Read("sample/Shared/BaristaNotes/Components/SourceComponents.cs");

        Assert.Contains("bool baristaScreen = Screen == \"baristanotes\"", host);
        Assert.Contains("CoffeeTheme.SurfaceColor", host);
        Assert.Contains("Window.DecorView.SetBackgroundColor(statusTint)", host);
        Assert.Contains("CoffeeTheme.Mode.PropertyChanged += OnBaristaThemeChanged", host);
        Assert.Contains("ApplyBaristaSystemBars()", host);
        Assert.Contains("WindowCompat.GetInsetsController", host);
        Assert.Contains("controller.AppearanceLightStatusBars = lightStatusBars", host);
        Assert.Contains("controller.AppearanceLightNavigationBars = lightNavigationBars", host);
        Assert.Contains("Build.VERSION.SdkInt < BuildVersionCodes.O", host);
        Assert.DoesNotContain("Window.InsetsController", host);
        Assert.DoesNotContain("#512BD4", host);
        Assert.DoesNotContain("BaristaSourceVisualContract.AndroidStatusBar", app);
        Assert.Contains("BaristaEdgeFrame.BuildTopInset(", app);
        var insetStart = components.IndexOf("public static Grid BuildTopInset(", StringComparison.Ordinal);
        Assert.True(insetStart >= 0);
        Assert.Contains(".Background(CoffeeTheme.SurfaceColor)", components[insetStart..]);
        Assert.Contains("AndroidStatusBarHeight()", app);
        Assert.Contains("BuildVersionCodes.VanillaIceCream", safeArea);
        Assert.Contains("GetIdentifier(\"status_bar_height\"", safeArea);
    }

    [Fact]
    public void SettingsCellsAndEquipmentRows_FollowSourceAlignmentAndDividers()
    {
        var settings = Read("sample/Shared/BaristaNotes/Pages/SettingsPage.cs");
        var safeArea = Read("sample/Shared/BaristaNotes/Styles/BaristaSafeAreaLayout.cs");
        var range = Read("sample/Shared/BaristaNotes/Pages/ValueRangeEditorPage.cs");
        var equipment = Read("sample/Shared/BaristaNotes/Pages/EquipmentManagementPage.cs");

        Assert.True(
            settings.Split(
                ".Alignment(Comet.Alignment.BottomLeading)",
                StringSplitOptions.None).Length - 1 >= 3);
        Assert.DoesNotContain(
            "content.Add(new Grid().Frame(height: CoffeeSpacing.M)",
            range);
        Assert.Contains(
            "new Grid(rows: new object[] { \"Auto\", CoffeeSpacing.Divider })",
            equipment);
        Assert.Contains(
            ".MinimumHeight(BaristaSourceVisualContract.EquipmentHeaderHeight)",
            equipment);
        Assert.DoesNotContain(
            ".Margin(bottom: CoffeeSpacing.Divider)",
            equipment);
        Assert.Contains(
            ".MinimumHeight(BaristaSafeAreaLayout.SettingsHeaderMinimumHeight(safeArea))",
            settings);
        Assert.Contains(
            "return BaristaSourceVisualContract.SettingsHeaderContentHeight;",
            safeArea);
        Assert.Contains(
            "public const float SettingsHeaderContentHeight = 77f;",
            Read("sample/Shared/BaristaNotes/Styles/BaristaSourceVisualContract.cs"));
    }

    [Fact]
    public void SwiftUITextInput_UsesKeyboardAccessoryWithoutResizingThePage()
    {
        var shim = Read("src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift");
        var navigation = Read("src/Comet/Platform/SwiftUI/SwiftUINavigationNode.cs");
        var binding = Read("src/Comet.SwiftUI.Binding/ApiDefinition.cs");

        Assert.Contains("ToolbarItemGroup(placement: .keyboard)", shim);
        Assert.Contains("if isFocused.wrappedValue", shim);
        Assert.Contains("clipShape(Circle())", shim);
        Assert.Contains("ignoresSafeArea(.keyboard, edges: .bottom)", shim);
        Assert.Contains("ignoresSafeArea([.container, .keyboard])", shim);
        Assert.Contains("if manualSafeAreaLayout && !node.backVisible", shim);
        Assert.Contains("if let current = node.children.last", shim);
        Assert.Contains("setCompletedHandler", shim);
        Assert.Contains("SetCompletedHandler", binding);
        Assert.DoesNotContain("CometSwiftUIKeyboard", navigation);
        Assert.Contains("dialogtitle", shim);
        Assert.Contains("let title = node.dialogTitle.isEmpty", shim);
    }

    [Fact]
    public void NativeTextFields_HonorTheRequestedFontContract()
    {
        var backend = Read("src/Comet/Backend/TextField.Backend.cs");
        var compose = Read("src/Comet/Platform/Compose/ComposeInputNodes.cs");

        Assert.Contains("PropertyIds.Text_FontSize", backend);
        Assert.Contains("PropertyIds.Text_FontFamily", backend);
        Assert.Contains("PropertyIds.Text_FontWeight", backend);
        Assert.Contains("InputTextStyle(textColor)", compose);
        Assert.Contains("FontSize = new AndroidX.Compose.Sp(EffectiveFontSize())", compose);
        Assert.Contains("ComposeFontRegistry.Resolve(_fontFamily, _fontWeight, _fontItalic)", compose);
        Assert.Contains("TextMeasure.SingleLine(", compose);
        Assert.Contains("outlined.Placeholder = StyledPlaceholder(placeholder)", compose);
        Assert.Contains("field.Placeholder = StyledPlaceholder(placeholder)", compose);
        Assert.Contains("AndroidX.Compose.FontWeight.ExtraBold", compose);
        Assert.Contains("AndroidX.Compose.FontWeight.Thin", compose);
        Assert.DoesNotContain("BorderlessFontSp", compose);
        Assert.Contains(
            ".textFieldStyle(.roundedBorder)",
            Read("src/Comet.SwiftUI.Shim/Sources/CometSwiftUIShim/CometSwiftUIShim.swift"));
    }

    static string Read(string relativePath)
    {
        var root = FindCometRoot();
        Assert.NotNull(root);
        var path = System.IO.Path.Combine(root!, relativePath);
        Assert.True(File.Exists(path), $"Missing source file: {path}");
        return File.ReadAllText(path);
    }

    static string? FindCometRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var depth = 0; depth < 12 && directory is not null; depth++)
        {
            if (File.Exists(System.IO.Path.Combine(directory, "global.json"))
                && Directory.Exists(System.IO.Path.Combine(directory, "sample")))
                return directory;
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        return null;
    }
}
