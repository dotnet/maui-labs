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

test("shared build and device workflows default to the GA workload set", () => {
  for (const file of ["_build.yml", "_device-tests.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /workload-version:\n[\s\S]*?default: '11\.0\.100\.1'/);
    assert.match(text, /workload-source:\n[\s\S]*?default: 'https:\/\/api\.nuget\.org\/v3\/index\.json'/);
    assert.match(text, /workload install .* --version \$\{\{ inputs\.workload-version \}\} --source \$\{\{ inputs\.workload-source \}\}/);
    assert.doesNotMatch(text, /workload-rollback-file|--from-rollback-file/);
  }
});

test("Apple build and device callers give the SDK a physical Xcode path", () => {
  for (const file of ["_build.yml", "_device-tests.yml", "ci-macos-appkit.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /developer_dir="\$\(cd \/Applications\/Xcode_[^\n]+ && pwd -P\)"/);
    assert.match(text, /sudo xcode-select -s "\$developer_dir"/);
    assert.match(text, /echo "DEVELOPER_DIR=\$developer_dir" >> "\$GITHUB_ENV"/);
    assert.match(text, /echo "MD_APPLE_SDK_ROOT=\$\(cd "\$developer_dir\/\.\.\/\.\." && pwd -P\)" >> "\$GITHUB_ENV"/);
  }
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

test("MAUI iOS keeps the released net10 override required by the hosted actool runtime", () => {
  const text = read(".github/workflows/devflow-integration.yml");
  const iosRollback = JSON.parse(read("eng/workloads/dotnet11-ga-ios-net10.json"));
  assert.deepEqual(iosRollback, {
    "microsoft.net.sdk.android": "37.2.31/11.0.100",
    "microsoft.net.sdk.ios": "26.4.10259/10.0.100",
    "microsoft.net.sdk.maccatalyst": "27.2.11039/11.0.100",
    "microsoft.net.sdk.macos": "27.2.11039/11.0.100",
    "microsoft.net.sdk.maui": "11.0.0/11.0.100",
    "microsoft.net.sdk.tvos": "27.2.11039/11.0.100",
  });
  assert.match(text, /DOTNET_WORKLOAD_VERSION: 11\.0\.100\.1/);
  assert.match(text, /DOTNET_WORKLOAD_SOURCE: https:\/\/api\.nuget\.org\/v3\/index\.json/);
  assert.match(text, /DEVFLOW_TEST_IOS_VERSION: \$\{\{ inputs\.ios-runtime \|\| '26\.2' \}\}/);
  assert.match(text, /if \[\[ "\$\{\{ matrix\.framework \}\}" == maui \]\]; then\n\s+CANDIDATES=\(\/Applications\/Xcode_26\.4\.app \/Applications\/Xcode_26\.4\.1\.app\)\n\s+else\n\s+CANDIDATES=\(\/Applications\/Xcode_26\.0\.app \/Applications\/Xcode_26\.app\)/);
  assert.match(text, /if \[\[ "\$\{\{ matrix\.framework \}\}" == maui \]\]; then[\s\S]*?workload install .* mobile-librarybuilder-net10 --from-rollback-file eng\/workloads\/dotnet11-ga-ios-net10\.json --source \$\{\{ env\.DOTNET_WORKLOAD_SOURCE \}\}\n\s+else\n\s+dotnet workload install .* --version \$\{\{ env\.DOTNET_WORKLOAD_VERSION \}\} --source \$\{\{ env\.DOTNET_WORKLOAD_SOURCE \}\}/);
  assert.match(text, /name: Verify SDK-matching iOS simulator runtime\n\s+if: matrix\.framework == 'maui'/);
  assert.match(text, /runtime\["version"\] == "26\.4\.1" and runtime\["isAvailable"\]/);
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

test("GA workload callers do not request the retired bundled Tizen workload", () => {
  for (const file of [
    ".github/workflows/_build.yml",
    ".github/workflows/ci-macos-appkit.yml",
    ".github/workflows/ci-wpf.yml",
    "eng/pipelines/devflow-official.yml",
  ]) {
    assert.doesNotMatch(read(file), /maui-tizen/);
  }
});

test("community packages use GA servicing releases", () => {
  const versions = read("eng/Versions.props");
  assert.match(versions, /<CommunityPlatformMauiVersion>11\.0\.10<\/CommunityPlatformMauiVersion>/);
  assert.match(versions, /<CommunityPlatformAspNetCoreComponentsWebViewVersion>11\.0\.0<\/CommunityPlatformAspNetCoreComponentsWebViewVersion>/);
});

test("GA Apple targets are additive and leave .NET 10 defaults intact", () => {
  for (const file of [
    "src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj",
    "samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj",
    "tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj",
  ]) {
    const text = read(file);
    assert.match(text, /<TargetFrameworks Condition="!\$\(\[MSBuild\]::IsOSPlatform\('linux'\)\)">\$\(TargetFrameworks\);net10\.0-ios;net10\.0-maccatalyst/);
    assert.match(text, /<TargetFrameworks Condition="'\$\(UseNet11AppleTargets\)' == 'true'[^"]*">\$\(TargetFrameworks\);net11\.0-ios27\.2;net11\.0-maccatalyst27\.2/);
    assert.doesNotMatch(text, /UseXcode27Preview|net11\.0-(?:ios|maccatalyst|macos)27\.0|net10\.0-(?:ios|maccatalyst|macos)27\./);
  }
});
