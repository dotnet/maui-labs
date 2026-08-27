using System.Runtime.InteropServices;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Detects the platform a DevFlow agent is running on and maps it to the name reported on
/// <c>GET /api/v1/agent/status</c> and in the broker registration.
/// </summary>
/// <remarks>
/// <para>
/// Platform backends that already know their identity should override
/// <c>DevFlowAgentService.PlatformName</c> instead of relying on detection. This type exists for
/// the framework-neutral paths — broker registration, the runtime profiler — that run before any
/// backend is available.
/// </para>
/// <para>
/// An out-of-tree agent whose platform DevFlow cannot detect can set the
/// <c>DEVFLOW_PLATFORM</c> environment variable to the name it wants reported. That value wins
/// over detection so a new platform never has to ship as <c>"Unknown"</c> — or, worse, as another
/// platform's name — while waiting for detection support to land here.
/// </para>
/// </remarks>
public static class DevFlowRuntimePlatform
{
    /// <summary>Environment variable that overrides the detected platform name.</summary>
    /// <remarks>
    /// The value is reported verbatim to clients, so it is validated before use: it must be
    /// 1–32 characters of letters, digits, <c>.</c>, <c>-</c>, <c>_</c> or spaces. Anything else
    /// is ignored in favour of detection rather than being placed on the wire.
    /// </remarks>
    public const string OverrideEnvironmentVariable = "DEVFLOW_PLATFORM";

    private const int MaxOverrideLength = 32;

    private static readonly bool s_isTizen = DetectTizen();

    /// <summary>
    /// Whether the process is running on Tizen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tizen is a Linux distribution, so <see cref="OperatingSystem.IsLinux"/> is also true there.
    /// Every platform switch must therefore test Tizen before Linux.
    /// </para>
    /// <para>
    /// This reports what the process is actually running on and is intentionally unaffected by the
    /// <c>DEVFLOW_PLATFORM</c> override, which only changes the <em>reported</em> name. The two can
    /// therefore disagree, and that is by design: use <see cref="DetectName"/> for anything that
    /// reaches a client.
    /// </para>
    /// </remarks>
    public static bool IsTizen => s_isTizen;

    /// <summary>
    /// The platform name reported to DevFlow clients — for example <c>"Android"</c>, <c>"iOS"</c>,
    /// <c>"MacCatalyst"</c>, <c>"macOS"</c>, <c>"Windows"</c>, <c>"Linux"</c> or <c>"Tizen"</c>.
    /// Returns <c>"Unknown"</c> when the platform cannot be determined.
    /// </summary>
    /// <param name="windowsName">
    /// Name to report on Windows. Backends differ: the UI-facing agent reports <c>"WinUI"</c>
    /// while host bootstrap and the profiler report <c>"Windows"</c>. Both normalize to the
    /// canonical <c>windows</c> identifier on the client.
    /// </param>
    /// <remarks>
    /// A valid <c>DEVFLOW_PLATFORM</c> override outranks detection even when detection succeeds.
    /// That is deliberate: the failure it exists to solve is detection returning a confidently
    /// <em>wrong</em> answer, not detection giving up. A Tizen host, for instance, satisfies
    /// <see cref="OperatingSystem.IsLinux"/>, so an override that only applied when the result was
    /// <c>"Unknown"</c> would never get a chance to correct it.
    /// </remarks>
    public static string DetectName(string windowsName = "Windows")
    {
        var overrideName = GetOverride();
        if (overrideName is not null)
            return overrideName;

        if (OperatingSystem.IsAndroid()) return "Android";
        if (OperatingSystem.IsMacCatalyst()) return "MacCatalyst";
        if (OperatingSystem.IsIOS()) return "iOS";
        if (OperatingSystem.IsTvOS()) return "tvOS";
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsWindows()) return windowsName;
        if (IsTizen) return "Tizen";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "Unknown";
    }

    private static string? GetOverride()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value!.Trim();
            return IsAcceptableOverride(trimmed) ? trimmed : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsAcceptableOverride(string value)
    {
        if (value.Length > MaxOverrideLength)
            return false;

        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_' && c != ' ')
                return false;
        }

        return true;
    }

    private static bool DetectTizen()
    {
        // .NET does not ship an OperatingSystem.IsTizen(), and the Tizen runtime reports itself as
        // Linux, so probe the signals Tizen actually exposes. Each probe is independently guarded:
        // a sandboxed or trimmed host may deny any one of them.
        try
        {
            // "TIZEN" is not in the analyzer's known-platform list (CA1418) and today's runtime
            // reports Tizen as Linux, so this is a forward-looking probe rather than the one that
            // fires. Keep it first so DevFlow picks the answer up for free if that ever changes.
#pragma warning disable CA1418
            if (OperatingSystem.IsOSPlatform("TIZEN"))
#pragma warning restore CA1418
                return true;
        }
        catch
        {
        }

        try
        {
            if (RuntimeInformation.RuntimeIdentifier.StartsWith("tizen", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
        }

        try
        {
            if (File.Exists("/etc/tizen-release"))
                return true;
        }
        catch
        {
        }

        try
        {
            // Present in every Tizen .NET application package.
            if (Type.GetType("Tizen.Applications.Application, Tizen.Applications.Common", throwOnError: false) is not null)
                return true;
        }
        catch
        {
        }

        return false;
    }
}
