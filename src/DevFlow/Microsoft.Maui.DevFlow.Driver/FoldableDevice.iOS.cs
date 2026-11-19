using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed partial class FoldableDevice
{
    private string? _iosHelperPath;

    private async Task<int[]> GetIosHingesAsync(CancellationToken token)
    {
        var inventory = await _run("xcrun", ["simctl", "list", "devices", "booted", "-j"], token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(inventory);
        if (!document.RootElement.GetProperty("devices").EnumerateObject()
            .SelectMany(runtime => runtime.Value.EnumerateArray())
            .Any(device => device.GetProperty("udid").GetString()?.Equals(DeviceId, StringComparison.OrdinalIgnoreCase) == true
                && device.GetProperty("state").GetString() == "Booted"))
            throw new InvalidOperationException($"iOS Simulator {DeviceId} is not booted.");
        if (!_allowExperimentalIos)
            return [];
        return (await ReadIosAnglesAsync(token).ConfigureAwait(false)).Select(hinge => hinge.Index).ToArray();
    }

    private async Task<FoldHinge[]> ReadIosAnglesAsync(CancellationToken token)
    {
        var helper = _iosHelperPath ??= await _prepareIosHelper(token).ConfigureAwait(false);
        var output = await _run("xcrun", ["simctl", "spawn", DeviceId, helper, "get"], token).ConfigureAwait(false);
        return ParseIosAngles(output);
    }

    internal static FoldHinge[] ParseIosAngles(string output)
    {
        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.GetProperty("supported").GetBoolean())
            return [];
        var angle = document.RootElement.GetProperty("angleDegrees").GetDouble();
        if (!double.IsFinite(angle))
            throw new InvalidOperationException("Simulator returned a non-finite hinge angle.");
        return [new FoldHinge { Index = 0, AngleDegrees = angle }];
    }

    private async Task SetIosAngleAsync(double angle, double duration, CancellationToken token)
    {
        var helper = _iosHelperPath ??= await _prepareIosHelper(token).ConfigureAwait(false);
        await _run("xcrun", ["simctl", "spawn", DeviceId, helper, "set",
            angle.ToString("R", CultureInfo.InvariantCulture), duration.ToString("R", CultureInfo.InvariantCulture)],
            token).ConfigureAwait(false);
    }

    private async Task<FoldDisplay[]> ReadIosDisplaysAsync(CancellationToken token)
    {
        var output = await _run("xcrun", ["devicectl", "device", "info", "displays",
            "--device", DeviceId, "--json-output", "-"], token).ConfigureAwait(false);
        return ParseIosDisplays(output);
    }

    internal static FoldDisplay[] ParseIosDisplays(string output)
    {
        using var document = JsonDocument.Parse(output);
        var displays = new List<FoldDisplay>();
        foreach (var display in document.RootElement.GetProperty("result").GetProperty("displays").EnumerateArray())
        {
            if (!display.GetProperty("type").TryGetProperty("integrated", out _))
                continue;
            var native = display.GetProperty("nativeSize");
            var scale = display.GetProperty("pointScale").GetDouble();
            if (scale <= 0 || !double.IsFinite(scale))
                throw new InvalidOperationException("Simulator display has an invalid scale.");
            displays.Add(new FoldDisplay
            {
                Id = display.GetProperty("displayId").ToString(),
                UniqueId = display.GetProperty("uniqueId").GetString() ?? throw new InvalidOperationException("Display UUID is missing."),
                Name = display.GetProperty("name").GetString() ?? "",
                Active = display.GetProperty("active").GetBoolean(),
                Primary = display.GetProperty("primary").GetBoolean(),
                Scale = scale, NativeWidth = native[0].GetDouble(), NativeHeight = native[1].GetDouble(),
                Rotation = display.GetProperty("currentOrientation").GetString(),
            });
        }
        if (displays.Count == 0)
            throw new InvalidOperationException("Simulator returned no integrated display metadata.");
        return displays.ToArray();
    }

    private async Task<string> BuildIosHelperAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.OSArchitecture != Architecture.Arm64)
            throw new NotSupportedException("The experimental iOS hinge helper currently requires an Apple Silicon Mac.");
        var sdk = await _run("xcrun", ["--sdk", "iphonesimulator", "--show-sdk-path"], token).ConfigureAwait(false);
        var sdkVersion = await _run("xcrun", ["--sdk", "iphonesimulator", "--show-sdk-version"], token).ConfigureAwait(false);
        if (!Version.TryParse(sdkVersion.Trim(), out var version) || version < new Version(27, 1))
            throw new NotSupportedException("The experimental hinge helper requires the iOS 27.1 or newer Simulator SDK.");
        var xcode = await _run("xcodebuild", ["-version"], token).ConfigureAwait(false);
        var coreDevice = await _run("xcrun", ["devicectl", "--version"], token).ConfigureAwait(false);
        using var resource = typeof(FoldableDevice).Assembly.GetManifestResourceStream(
            "Microsoft.Maui.DevFlow.Driver.FoldableSimulator.m")
            ?? throw new InvalidOperationException("The packaged simulator hinge helper source is missing.");
        using var reader = new StreamReader(resource);
        var source = await reader.ReadToEndAsync(token).ConfigureAwait(false);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source + sdk + sdkVersion + xcode + coreDevice + RuntimeInformation.OSArchitecture)));
        Directory.CreateDirectory(CacheDirectory);
        var destination = Path.Combine(CacheDirectory, $"ios-hinge-{key}");
        if (File.Exists(destination))
            return destination;
        var staging = Path.Combine(CacheDirectory, $"build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var sourcePath = Path.Combine(staging, "hinge.m");
        var binaryPath = Path.Combine(staging, "hinge");
        try
        {
            await File.WriteAllTextAsync(sourcePath, source, token).ConfigureAwait(false);
            await _run("xcrun", ["--sdk", "iphonesimulator", "clang", "-arch", "arm64",
                "-mios-simulator-version-min=27.1", "-fobjc-arc", "-O2", sourcePath,
                "-framework", "Foundation", "-framework", "CoreMotion", "-framework", "IOKit",
                "-o", binaryPath], token).ConfigureAwait(false);
            await _run("codesign", ["--force", "--sign", "-", binaryPath], token).ConfigureAwait(false);
            File.Move(binaryPath, destination, overwrite: true);
            return destination;
        }
        finally
        {
            File.Delete(sourcePath);
            File.Delete(binaryPath);
            Directory.Delete(staging);
        }
    }
}
