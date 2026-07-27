namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Registration role and discriminator contract for realized AppKit dialog native elements
/// (the dialog surface, its buttons, and any prompt input), matching the established
/// cross-platform Dialog/DialogAction registration convention. Kept in a plain C# file with no
/// AppKit dependency so the contract itself can be covered by a unit test without requiring an
/// AppKit runtime.
/// </summary>
internal static class DialogNativeElementContract
{
    /// <summary>Role for the realized dialog surface and (for prompts) its input field.</summary>
    public const string DialogRole = "Dialog";

    /// <summary>Role for each realized dialog button.</summary>
    public const string DialogActionRole = "DialogAction";

    /// <summary>
    /// Discriminator used for every realized dialog native element. Registration identity is
    /// already unique per native object (see <c>NativeElementRegistrationRegistry</c>), so this
    /// is a stable, constant tag rather than a per-element index.
    /// </summary>
    public const string RealizedViewDiscriminator = "RealizedView";
}
