using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Guards the AppKit dialog registration role/discriminator contract used by
/// <c>AlertManagerSubscription</c> so it cannot silently drift from the established
/// cross-platform convention: the dialog surface and prompt input both register under the
/// "Dialog" role (not a separate "DialogInput" role), buttons register under "DialogAction",
/// and every realized dialog element uses the constant "RealizedView" discriminator rather
/// than a per-element index. The contract has no AppKit dependency, so it is compiled
/// directly into this test project (see the Compile Include in the .csproj).
/// </summary>
public class DialogNativeElementContractTests
{
    [Fact]
    public void DialogRole_MatchesEstablishedConvention()
        => Assert.Equal("Dialog", DialogNativeElementContract.DialogRole);

    [Fact]
    public void DialogActionRole_MatchesEstablishedConvention()
        => Assert.Equal("DialogAction", DialogNativeElementContract.DialogActionRole);

    [Fact]
    public void RealizedViewDiscriminator_MatchesEstablishedConvention()
        => Assert.Equal("RealizedView", DialogNativeElementContract.RealizedViewDiscriminator);

    [Fact]
    public void DialogAndDialogActionRoles_AreDistinct()
        => Assert.NotEqual(
            DialogNativeElementContract.DialogRole,
            DialogNativeElementContract.DialogActionRole);
}
