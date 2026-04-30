// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Errors;

public static class ErrorCodeCatalogue
{
static readonly ErrorCodeDescriptor[] s_all =
[
new ErrorCodeDescriptor { Code = ErrorCodes.InternalError, Name = nameof(ErrorCodes.InternalError), Category = "tool", Description = "An unexpected internal error occurred in the tool", DefaultRemediationType = "file-bug" },
new ErrorCodeDescriptor { Code = ErrorCodes.InvalidArgument, Name = nameof(ErrorCodes.InvalidArgument), Category = "tool", Description = "An invalid argument was supplied to the command" },
new ErrorCodeDescriptor { Code = ErrorCodes.DeviceNotFound, Name = nameof(ErrorCodes.DeviceNotFound), Category = "tool", Description = "The requested device could not be found" },
new ErrorCodeDescriptor { Code = ErrorCodes.PlatformNotSupported, Name = nameof(ErrorCodes.PlatformNotSupported), Category = "tool", Description = "The operation is not supported on this platform" },
new ErrorCodeDescriptor { Code = ErrorCodes.JdkNotFound, Name = nameof(ErrorCodes.JdkNotFound), Category = "platform", Subcategory = "jdk", Description = "No Java Development Kit installation was found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.JdkVersionUnsupported, Name = nameof(ErrorCodes.JdkVersionUnsupported), Category = "platform", Subcategory = "jdk", Description = "The installed JDK version is not supported", DefaultRemediationType = "upgrade" },
new ErrorCodeDescriptor { Code = ErrorCodes.JdkInstallFailed, Name = nameof(ErrorCodes.JdkInstallFailed), Category = "platform", Subcategory = "jdk", Description = "JDK installation failed", DefaultRemediationType = "retry" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidSdkNotFound, Name = nameof(ErrorCodes.AndroidSdkNotFound), Category = "platform", Subcategory = "android", Description = "Android SDK installation not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidSdkManagerNotFound, Name = nameof(ErrorCodes.AndroidSdkManagerNotFound), Category = "platform", Subcategory = "android", Description = "Android SDK manager executable not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidLicensesNotAccepted, Name = nameof(ErrorCodes.AndroidLicensesNotAccepted), Category = "platform", Subcategory = "android", Description = "Android SDK licenses have not been accepted", DefaultRemediationType = "user-action" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidPackageInstallFailed, Name = nameof(ErrorCodes.AndroidPackageInstallFailed), Category = "platform", Subcategory = "android", Description = "Android SDK package installation failed", DefaultRemediationType = "retry" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidEmulatorNotFound, Name = nameof(ErrorCodes.AndroidEmulatorNotFound), Category = "platform", Subcategory = "android", Description = "Android emulator executable not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidAvdCreateFailed, Name = nameof(ErrorCodes.AndroidAvdCreateFailed), Category = "platform", Subcategory = "android", Description = "Android virtual device (AVD) creation failed", DefaultRemediationType = "retry" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidAdbNotFound, Name = nameof(ErrorCodes.AndroidAdbNotFound), Category = "platform", Subcategory = "android", Description = "Android Debug Bridge (adb) executable not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidDeviceNotFound, Name = nameof(ErrorCodes.AndroidDeviceNotFound), Category = "platform", Subcategory = "android", Description = "No Android device or emulator is connected", DefaultRemediationType = "user-action" },
new ErrorCodeDescriptor { Code = ErrorCodes.AndroidAvdDeleteFailed, Name = nameof(ErrorCodes.AndroidAvdDeleteFailed), Category = "platform", Subcategory = "android", Description = "Android virtual device (AVD) deletion failed", DefaultRemediationType = "retry" },
new ErrorCodeDescriptor { Code = ErrorCodes.AppleXcodeNotFound, Name = nameof(ErrorCodes.AppleXcodeNotFound), Category = "platform", Subcategory = "apple", Description = "Xcode installation not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AppleCltNotFound, Name = nameof(ErrorCodes.AppleCltNotFound), Category = "platform", Subcategory = "apple", Description = "Xcode Command Line Tools not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.AppleSimctlFailed, Name = nameof(ErrorCodes.AppleSimctlFailed), Category = "platform", Subcategory = "apple", Description = "Apple simctl command failed", DefaultRemediationType = "retry" },
new ErrorCodeDescriptor { Code = ErrorCodes.AppleSimulatorNotFound, Name = nameof(ErrorCodes.AppleSimulatorNotFound), Category = "platform", Subcategory = "apple", Description = "No matching Apple simulator found", DefaultRemediationType = "user-action" },
new ErrorCodeDescriptor { Code = ErrorCodes.WindowsSdkNotFound, Name = nameof(ErrorCodes.WindowsSdkNotFound), Category = "platform", Subcategory = "windows", Description = "Windows SDK installation not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.DotNetNotFound, Name = nameof(ErrorCodes.DotNetNotFound), Category = "platform", Subcategory = "dotnet", Description = ".NET SDK installation not found", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.MauiWorkloadMissing, Name = nameof(ErrorCodes.MauiWorkloadMissing), Category = "platform", Subcategory = "dotnet", Description = "MAUI workload is not installed", DefaultRemediationType = "install" },
new ErrorCodeDescriptor { Code = ErrorCodes.DiagnosticsToolNotFound, Name = nameof(ErrorCodes.DiagnosticsToolNotFound), Category = "platform", Subcategory = "dotnet", Description = ".NET diagnostics tool not found", DefaultRemediationType = "install" },
];

public static IReadOnlyList<ErrorCodeDescriptor> All => s_all;

public static IReadOnlyList<ErrorCodeDescriptor> ByCategory(string category) =>
s_all.Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

public static IReadOnlyList<ErrorCodeDescriptor> ByPrefix(string prefix) =>
s_all.Where(d => d.Code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
}