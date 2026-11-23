const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");

const root = path.resolve(__dirname, "../../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const sdkConfig = JSON.parse(read("global.json"));
const sdk = sdkConfig.sdk;

test("root and Testing select the same GA SDK feature band", () => {
  const testingSdk = JSON.parse(read("src/Testing/global.json")).sdk;
  assert.equal(sdk.version, "11.0.100");
  assert.equal(testingSdk.version, sdk.version);
  assert.equal(sdk.rollForward, "latestPatch");
  assert.equal(testingSdk.rollForward, "latestPatch");
  assert.equal("allowPrerelease" in sdk, false);
  assert.equal("allowPrerelease" in testingSdk, false);
  assert.equal(sdkConfig.tools.dotnet, sdk.version);
  const testing = read(".github/workflows/ci-testing.yml");
  assert.match(testing, /global-json-file: global\.json/);
  assert.doesNotMatch(testing, /dotnet-version:|dotnet-quality:/);
  assert.match(read("eng/pipelines/devflow-official.yml"), new RegExp(`version: ${sdk.version.replaceAll(".", "\\.")}\\s`));
});

test("shared build and device workflows default to the mirrored GA manifests", () => {
  for (const file of ["_build.yml", "_device-tests.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /workload-rollback-file:\n[\s\S]*?default: 'eng\/workloads\/dotnet11-ga\.json'/);
    assert.match(text, /workload install .* --from-rollback-file \$\{\{ inputs\.workload-rollback-file \}\}/);
    assert.doesNotMatch(text, /workload-source|api\.nuget\.org/);
  }
});

test("WPF build and template smoke isolate the SDK from machine-wide workloads", () => {
  const build = read(".github/workflows/_build.yml");
  assert.match(build, /isolated-dotnet:\n\s+required: false\n\s+type: boolean\n\s+default: false/);
  assert.match(build, /DOTNET_INSTALL_DIR: \$\{\{ inputs\.isolated-dotnet && format\('\{0\}\/maui-ci-dotnet', runner\.temp\) \|\| '' \}\}/);
  const wpf = read(".github/workflows/ci-wpf.yml");
  assert.match(wpf, /project-name: wpf\n\s+additional-dotnet-version: 10\.0\.403\n\s+isolated-dotnet: true/);
  assert.match(wpf, /template-smoke:[\s\S]*?uses: actions\/setup-dotnet@v5\n\s+env:\n\s+DOTNET_INSTALL_DIR: \$\{\{ runner\.temp \}\}\/maui-ci-dotnet\n\s+with:\n\s+global-json-file: global\.json\n\s+dotnet-version: 10\.0\.403/);
});

test("Windows workload jobs use an isolated SDK and install the net10 runtime when needed", () => {
  const jobSection = (file, job) => {
    const text = read(`.github/workflows/${file}`);
    const match = text.match(new RegExp(`^  ${job}:\\n[\\s\\S]*?(?=^  [\\w-]+:\\s*$|(?![\\s\\S]))`, "m"));
    assert.ok(match, `${file} must contain ${job}`);
    return match[0];
  };
  for (const [file, job] of [
    ["ci-devflow.yml", "build"],
    ["ci-essentialsai.yml", "build-windows"],
  ]) {
    const section = jobSection(file, job);
    assert.match(section, /additional-dotnet-version: 10\.0\.403\n\s+isolated-dotnet: true/);
  }
  for (const job of ["windows", "wpf"]) {
    const section = jobSection("devflow-integration.yml", job);
    assert.match(section, /DOTNET_INSTALL_DIR: \$\{\{ runner\.temp \}\}\/maui-ci-dotnet/);
    assert.match(section, /dotnet-version: 10\.0\.403/);
  }
  const device = read(".github/workflows/_device-tests.yml");
  assert.match(device, /DOTNET_INSTALL_DIR: \$\{\{ runner\.os == 'Windows' && format\('\{0\}\/maui-ci-dotnet', runner\.temp\) \|\| '' \}\}/);
  assert.match(device, /dotnet-version: \$\{\{ runner\.os == 'Windows' && '10\.0\.403' \|\| '' \}\}/);
  assert.match(read(".github/workflows/ci-testing.yml"), /DOTNET_INSTALL_DIR: \$\{\{ runner\.os == 'Windows' && format\('\{0\}\/maui-ci-dotnet', runner\.temp\) \|\| '' \}\}/);
});

test("Apple build and device callers export only a physical DEVELOPER_DIR", () => {
  for (const file of ["_build.yml", "_device-tests.yml", "ci-macos-appkit.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /developer_dir="\$\(cd \/Applications\/Xcode_[^\n]+ && pwd -P\)"/);
    assert.match(text, /sudo xcode-select -s "\$developer_dir"/);
    assert.match(text, /echo "DEVELOPER_DIR=\$developer_dir" >> "\$GITHUB_ENV"/);
    assert.doesNotMatch(text, /MD_APPLE_SDK_ROOT/);
  }
  for (const file of ["devflow-integration.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /export DEVELOPER_DIR="\$DEVELOPER_ROOT"/);
    assert.match(text, /echo "DEVELOPER_DIR=\$DEVELOPER_ROOT" >> "\$GITHUB_ENV"/);
    assert.doesNotMatch(text, /MD_APPLE_SDK_ROOT/);
  }
  assert.doesNotMatch(read("eng/pipelines/devflow-official.yml"), /MD_APPLE_SDK_ROOT/);
});

test("GA net11 Apple lanes select Xcode 27.2", () => {
  for (const file of ["ci-ai.yml", "ci-essentialsai.yml"]) {
    assert.match(read(`.github/workflows/${file}`), /xcode-version: '27\.2'/);
  }
  assert.match(read(".github/workflows/ci-testing.yml"), /Xcode_27\.2\.app/);
  const official = read("eng/pipelines/devflow-official.yml");
  assert.match(official, /Xcode_27\.2\.app/);
  assert.match(official, /\^Xcode 27\\\.2/);
});

test("iOS integration uses GA net11 while retaining the net10 compatibility manifest", () => {
  const text = read(".github/workflows/devflow-integration.yml");
  const rollback = JSON.parse(read("eng/workloads/dotnet11-ga.json"));
  const iosRollback = JSON.parse(read("eng/workloads/dotnet11-ga-ios-net10.json"));
  assert.deepEqual(rollback, {
    "microsoft.net.sdk.android": "37.2.31/11.0.100",
    "microsoft.net.sdk.ios": "27.2.11039/11.0.100",
    "microsoft.net.sdk.maccatalyst": "27.2.11039/11.0.100",
    "microsoft.net.sdk.macos": "27.2.11039/11.0.100",
    "microsoft.net.sdk.maui": "11.0.0/11.0.100",
    "microsoft.net.sdk.tvos": "27.2.11039/11.0.100",
  });
  assert.equal(iosRollback["microsoft.net.sdk.ios"], "26.4.10259/10.0.100");
  delete iosRollback["microsoft.net.sdk.ios"];
  delete rollback["microsoft.net.sdk.ios"];
  assert.deepEqual(iosRollback, rollback);
  assert.match(text, /DOTNET_WORKLOAD_ROLLBACK_FILE: eng\/workloads\/dotnet11-ga\.json/);
  assert.match(text, /ios:\n[\s\S]*?runs-on: xcode-27/);
  assert.match(text, /DEVFLOW_TEST_IOS_VERSION: \$\{\{ inputs\.ios-runtime \|\| '27\.2' \}\}/);
  assert.match(text, /for candidate in \/Applications\/Xcode_27\.2\.app \/Applications\/Xcode_27\.2\.0\.app/);
  assert.match(text, /dotnet workload install maui ios macos maccatalyst android wasm-tools --from-rollback-file \$\{\{ env\.DOTNET_WORKLOAD_ROLLBACK_FILE \}\}/);
  assert.doesNotMatch(text, /dotnet11-ga-ios-net10\.json/);
  assert.match(text, /name: Verify SDK-matching iOS simulator runtime\n\s+if: matrix\.framework == 'maui'/);
  assert.match(text, /runtime\["version"\]\.startswith\("27\.2"\) and runtime\["isAvailable"\]/);
  assert.doesNotMatch(text, /xcodebuild -downloadPlatform/);
});

test("root-SDK callers do not install a .NET 10 workload set", () => {
  for (const file of [
    ".github/workflows/ci-ai.yml",
    ".github/workflows/ci-essentialsai.yml",
    ".github/workflows/ci-macos-appkit.yml",
    ".github/workflows/ci-wpf.yml",
    ".github/workflows/devflow-integration.yml",
    "eng/pipelines/devflow-official.yml",
  ]) {
    const text = read(file);
    assert.doesNotMatch(text, /workload install .* --version 10\./);
    assert.doesNotMatch(text, /dotnet11-rc2/);
  }
});

test("AppKit net10 uses the servicing workload for the same Xcode as net11", () => {
  const rollback = JSON.parse(read("eng/workloads/dotnet11-ga.json"));
  const appkitRollback = JSON.parse(read("eng/workloads/dotnet11-ga-macos-net10.json"));
  assert.equal(appkitRollback["microsoft.net.sdk.macos"], "27.2.10000/10.0.100");
  delete appkitRollback["microsoft.net.sdk.macos"];
  delete rollback["microsoft.net.sdk.macos"];
  assert.deepEqual(appkitRollback, rollback);
  const appkit = read(".github/workflows/ci-macos-appkit.yml");
  assert.match(appkit, /AppKitTargetFramework net\$\{\{ matrix\.dotnet \}\}\.0-macos/);
  assert.match(appkit, /maui-version: \['', '10\.0\.110'\]/);
  assert.doesNotMatch(appkit, /26\.0\.1|macos-26/);
  for (const text of [appkit, read(".github/workflows/devflow-integration.yml")]) {
    assert.match(text, /eng\/workloads\/dotnet11-ga-macos-net10\.json/);
  }
});

test("GA workload callers retain the MAUI Tizen workload declared by the GA manifest", () => {
  for (const file of [
    ".github/workflows/_build.yml",
    ".github/workflows/ci-macos-appkit.yml",
    ".github/workflows/ci-wpf.yml",
    "eng/pipelines/devflow-official.yml",
  ]) {
    assert.match(read(file), /maui-tizen/);
  }
});

test("foundation workload installs do not bypass the repository feeds", () => {
  for (const file of [
    ".github/CONTRIBUTING.md",
    ".github/workflows/_build.yml",
    ".github/workflows/_device-tests.yml",
    ".github/workflows/ci-macos-appkit.yml",
    ".github/workflows/ci-testing.yml",
    ".github/workflows/ci-wpf.yml",
    ".github/workflows/devflow-integration.yml",
    "AGENTS.md",
    "eng/pipelines/devflow-official.yml",
    "src/AI/README.md",
  ]) {
    assert.doesNotMatch(read(file), /api\.nuget\.org|workload-source/);
  }
});

test("community packages use GA servicing releases", () => {
  const versions = read("eng/Versions.props");
  assert.match(versions, /<CommunityPlatformMauiVersion>11\.0\.10<\/CommunityPlatformMauiVersion>/);
  assert.match(versions, /<CommunityPlatformAspNetCoreComponentsWebViewVersion>11\.0\.0<\/CommunityPlatformAspNetCoreComponentsWebViewVersion>/);
});

test("AI Apple libraries retain net10 compatibility while app heads use net11", () => {
  for (const [file, defaults] of [
    ["src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj",
      "net10.0-ios;net11.0-ios;net10.0-maccatalyst;net11.0-maccatalyst;net10.0-macos;net11.0-macos"],
    ["samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj",
      "net11.0-ios;net11.0-maccatalyst"],
    ["tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj",
      "net11.0-ios;net11.0-maccatalyst"],
  ]) {
    const text = read(file);
    assert.ok(text.includes(`<TargetFrameworks Condition="'$(UseNet11AppleTargets)' != 'true' and !$([MSBuild]::IsOSPlatform('linux'))">$(TargetFrameworks);${defaults}</TargetFrameworks>`));
    assert.match(text, /<TargetFrameworks Condition="'\$\(UseNet11AppleTargets\)' == 'true'[^"]*">\$\(TargetFrameworks\);net11\.0-ios27\.2;net11\.0-maccatalyst27\.2/);
    assert.doesNotMatch(text, /net11\.0-(?:ios|maccatalyst|macos)27\.0|net10\.0-(?:ios|maccatalyst|macos)27\./);
  }
});

test("MAUI Testing Apple templates use the GA scene lifecycle", () => {
  for (const platform of ["iOS", "MacCatalyst"]) {
    const root = `src/Testing/templates/mauitest/Platforms/${platform}`;
    const plist = read(`${root}/Info.plist`);
    const sceneDelegate = read(`${root}/SceneDelegate.cs`);
    assert.match(plist, /<key>UIApplicationSceneManifest<\/key>/);
    assert.match(plist, /<key>UIApplicationSupportsMultipleScenes<\/key>\s*<false\/>/);
    assert.match(plist, /<key>UISceneConfigurationName<\/key>\s*<string>__MAUI_DEFAULT_SCENE_CONFIGURATION__<\/string>/);
    assert.match(plist, /<key>UISceneDelegateClassName<\/key>\s*<string>SceneDelegate<\/string>/);
    assert.match(sceneDelegate, /\[Register\("SceneDelegate"\)\]/);
    assert.match(sceneDelegate, /class SceneDelegate : MauiUISceneDelegate/);
  }
});
