const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");

const root = path.resolve(__dirname, "../../..");
const read = file => fs.readFileSync(path.join(root, file), "utf8");
const sdk = JSON.parse(read("global.json")).sdk;

test("root and Testing select the same exact SDK for the RC2 manifests", () => {
  const testingSdk = JSON.parse(read("src/Testing/global.json")).sdk;
  assert.equal(sdk.version, "11.0.100-rc.2.26504.105");
  assert.equal(testingSdk.version, sdk.version);
  assert.equal(sdk.rollForward, "disable");
  assert.equal(testingSdk.rollForward, "disable");
  const testing = read(".github/workflows/ci-testing.yml");
  assert.match(testing, /global-json-file: global\.json/);
  assert.doesNotMatch(testing, /dotnet-version:|dotnet-quality:/);
  assert.match(read("eng/pipelines/devflow-official.yml"), new RegExp(`version: ${sdk.version.replaceAll(".", "\\.")}\\s`));
});

test("shared build and device workflows default to RC2 rollback installs", () => {
  for (const file of ["_build.yml", "_device-tests.yml"]) {
    const text = read(`.github/workflows/${file}`);
    assert.match(text, /workload-rollback-file:\n[\s\S]*?default: 'eng\/workloads\/dotnet11-rc2\.json'/);
    assert.match(text, /workload install .* --from-rollback-file /);
    assert.doesNotMatch(text, /workload-version|workload install .* --version /);
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

test("iOS integration installs the actool runtime without changing the test-device runtime", () => {
  const text = read(".github/workflows/devflow-integration.yml");
  assert.match(text, /DEVFLOW_TEST_IOS_VERSION: \$\{\{ inputs\.ios-runtime \|\| '26\.2' \}\}/);
  assert.match(text, /xcodebuild -downloadPlatform iOS -buildVersion 26\.0\.1/);
  assert.match(text, /runtime\["version"\] == "26\.0\.1" and runtime\["isAvailable"\]/);
});

test("root-SDK callers do not reinstall a .NET 10 workload set", () => {
  for (const file of [
    ".github/workflows/ci-ai.yml",
    ".github/workflows/ci-essentialsai.yml",
    ".github/workflows/ci-macos-appkit.yml",
    ".github/workflows/ci-wpf.yml",
    ".github/workflows/devflow-integration.yml",
    "eng/pipelines/devflow-official.yml",
  ]) {
    assert.doesNotMatch(read(file), /workload-version:|DOTNET_WORKLOAD_VERSION|workload install .* --version 10\./);
  }
});

test("the rollback MAUI version matches the shared contract package version", () => {
  const rollback = JSON.parse(read("eng/workloads/dotnet11-rc2.json"));
  const maui = read("eng/Versions.props").match(/<CommunityPlatformMauiVersion>([^<]+)<\/CommunityPlatformMauiVersion>/);
  assert.ok(maui);
  assert.equal(rollback["microsoft.net.sdk.maui"], maui[1]);
});

test("Apple preview targets are additive and leave .NET 10 defaults intact", () => {
  for (const file of [
    "src/AI/Microsoft.Maui.Essentials.AI/Microsoft.Maui.Essentials.AI.csproj",
    "samples/AIExtensions.Sample.ChatPlayground/AIExtensions.Sample.ChatPlayground.csproj",
    "tests/AI/Microsoft.Maui.Essentials.AI.DeviceTests/Microsoft.Maui.Essentials.AI.DeviceTests.csproj",
  ]) {
    const text = read(file);
    assert.match(text, /<TargetFrameworks Condition="!\$\(\[MSBuild\]::IsOSPlatform\('linux'\)\)">\$\(TargetFrameworks\);net10\.0-ios;net10\.0-maccatalyst/);
    assert.match(text, /<TargetFrameworks Condition="'\$\(UseXcode27Preview\)' == 'true'[^"]*">\$\(TargetFrameworks\);net11\.0-ios27\.0;net11\.0-maccatalyst27\.0/);
    assert.doesNotMatch(text, /net10\.0-(?:ios|maccatalyst|macos)27\.0/);
  }
});
