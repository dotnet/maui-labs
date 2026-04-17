using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4;

/// <summary>
/// Conditionally disables the WebKit process sandbox based on environment
/// detection or explicit user opt-in, rather than unconditionally disabling it.
/// </summary>
internal static class WebKitSandboxHelper
{
	private static bool _configured;

	[DllImport("libc", SetLastError = true)]
	private static extern int setenv(string name, string value, int overwrite);

	/// <summary>
	/// Configures the WebKit sandbox policy. Disables the sandbox only when:
	/// 1. The user explicitly opts in via MAUI_WEBKIT_DISABLE_SANDBOX=1, OR
	/// 2. The kernel does not support unprivileged user namespaces (required by bubblewrap).
	/// </summary>
	public static void ConfigureSandbox()
	{
		if (_configured)
			return;
		_configured = true;

		if (ShouldDisableSandbox())
		{
			setenv("WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS", "1", 0);
		}
	}

	private static bool ShouldDisableSandbox()
	{
		// Explicit opt-in via environment variable
		if (Environment.GetEnvironmentVariable("MAUI_WEBKIT_DISABLE_SANDBOX") == "1")
			return true;

		// Auto-detect: check if unprivileged user namespaces are available.
		// WebKit's bubblewrap sandbox requires CLONE_NEWUSER; if the kernel
		// restricts it (common in VMs, containers, hardened systems), the
		// sandbox must be disabled for WebKit to function.
		return !AreUserNamespacesAvailable();
	}

	private static bool AreUserNamespacesAvailable()
	{
		try
		{
			// This sysctl controls unprivileged user namespace creation.
			// If the file exists and contains "1", user namespaces are enabled.
			// If the file doesn't exist, the kernel likely enables them by default (5.x+).
			const string sysctlPath = "/proc/sys/kernel/unprivileged_userns_clone";
			if (File.Exists(sysctlPath) && File.ReadAllText(sysctlPath).Trim() != "1")
				return false;

			// Ubuntu 24.04+ restricts unprivileged user namespaces via AppArmor even
			// when the kernel sysctl allows them. If this is set to "1", bubblewrap
			// will fail with "setting up uid map: Permission denied".
			const string appArmorPath = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";
			if (File.Exists(appArmorPath) && File.ReadAllText(appArmorPath).Trim() == "1")
				return false;

			return true;
		}
		catch
		{
			// If we can't determine the state, assume sandboxing won't work
			return false;
		}
	}
}
