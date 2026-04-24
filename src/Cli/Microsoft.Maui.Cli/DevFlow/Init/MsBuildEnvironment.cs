using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Cli.DevFlow.Init;

/// <summary>
/// One-time MSBuild SDK registration via MSBuildLocator. Must be called before any
/// <c>Microsoft.Build.*</c> type is JIT-resolved by the CLR. The two-method pattern
/// (<see cref="EnsureRegistered"/> → <see cref="RegisterCore"/>) ensures the locator
/// installs an assembly resolver before any Microsoft.Build types are encountered.
/// </summary>
internal static class MsBuildEnvironment
{
    // 0 = not attempted, 1 = succeeded, -1 = failed
    static int s_state;

    /// <summary>
    /// Ensures <c>MSBuildLocator.RegisterDefaults()</c> has run exactly once.
    /// Safe to call repeatedly — idempotent after the first success.
    /// Throws if registration fails (e.g. no .NET SDK installed).
    /// </summary>
    public static void EnsureRegistered()
    {
        var current = Volatile.Read(ref s_state);
        if (current == 1)
            return;
        if (current == -1)
            throw new InvalidOperationException("MSBuild SDK registration previously failed. Ensure a .NET SDK is installed.");

        // First call — try to register.
        try
        {
            RegisterCore();
            Volatile.Write(ref s_state, 1);
        }
        catch
        {
            Volatile.Write(ref s_state, -1);
            throw;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if MSBuildLocator can register (or already has).
    /// Does not throw.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            var current = Volatile.Read(ref s_state);
            if (current == 1) return true;
            if (current == -1) return false;

            try
            {
                EnsureRegistered();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "DevFlow init is a dev-time command; not used at app runtime.")]
    static void RegisterCore()
    {
        if (!global::Microsoft.Build.Locator.MSBuildLocator.IsRegistered)
        {
            global::Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }
    }
}
