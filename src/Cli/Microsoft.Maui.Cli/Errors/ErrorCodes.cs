// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Errors;

/// <summary>
/// Error code taxonomy for MAUI Dev Tools.
/// Format: E{category}{number}
/// Categories:
///   1xxx - Tool errors (internal bugs)
///   2xxx - Platform/SDK errors
///   3xxx - User action required
///   4xxx - Network errors
///   5xxx - Permission errors
/// </summary>
public static class ErrorCodes
{
	// Tool errors (E1xxx)
	public const string InternalError = "E1001";
	public const string InvalidArgument = "E1004";
	public const string DeviceNotFound = "E1006";
	public const string PlatformNotSupported = "E1007";

	// Platform/SDK errors - JDK (E20xx)
	public const string JdkNotFound = "E2001";
	public const string JdkVersionUnsupported = "E2002";
	public const string JdkInstallFailed = "E2003";

	// Platform/SDK errors - Android SDK (E21xx)
	public const string AndroidSdkNotFound = "E2101";
	public const string AndroidSdkManagerNotFound = "E2102";
	public const string AndroidLicensesNotAccepted = "E2103";
	public const string AndroidPackageInstallFailed = "E2105";
	public const string AndroidEmulatorNotFound = "E2106";
	public const string AndroidAvdCreateFailed = "E2108";
	public const string AndroidAdbNotFound = "E2110";
	public const string AndroidDeviceNotFound = "E2111";
	public const string AndroidAvdDeleteFailed = "E2112";

	// Platform/SDK errors - Apple (E22xx)
	public const string AppleXcodeNotFound = "E2201";
	public const string AppleCltNotFound = "E2202";
	public const string AppleSimctlFailed = "E2203";
	public const string AppleSimulatorNotFound = "E2204";
	public const string AppleXcodeLicenseNotAccepted = "E2205";
	public const string AppleSetupFailed = "E2206";
	public const string AppleSimulatorCreateFailed = "E2207";
	public const string AppleSimulatorEraseFailed = "E2208";
	public const string AppleSimulatorInstallFailed = "E2209";
	public const string AppleSimulatorUninstallFailed = "E2210";
	public const string AppleSimulatorLaunchFailed = "E2211";
	public const string AppleSimulatorTerminateFailed = "E2212";
	public const string AppleSimulatorGetContainerFailed = "E2213";
	public const string AppleSimulatorUnavailable = "E2214";

	// Platform/SDK errors - Windows (E23xx)
	public const string WindowsSdkNotFound = "E2301";

	// Platform/SDK errors - .NET (E24xx)
	public const string DotNetNotFound = "E2401";
	public const string MauiWorkloadMissing = "E2402";
	public const string DiagnosticsToolNotFound = "E2403";
}
