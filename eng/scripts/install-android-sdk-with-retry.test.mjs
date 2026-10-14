import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const script = fileURLToPath(new URL("./install-android-sdk-with-retry.sh", import.meta.url));
const temporaryRoot = fileURLToPath(new URL("../../artifacts/android-sdk-retry-tests/", import.meta.url));

function install({ failures = 0, error = "", exitCode = 1, packages } = {}) {
  mkdirSync(temporaryRoot, { recursive: true });
  const directory = mkdtempSync(join(temporaryRoot, "case-"));
  try {
    const sdkRoot = join(directory, "Android SDK");
    const bin = join(sdkRoot, "cmdline-tools", "latest", "bin");
    const tools = join(directory, "tools");
    const logs = join(directory, "logs");
    mkdirSync(bin, { recursive: true });
    mkdirSync(tools);
    mkdirSync(logs);
    const state = join(directory, "attempts");
    const args = join(directory, "arguments");
    const sleeps = join(directory, "sleeps");
    writeFileSync(join(bin, "sdkmanager"), [
      "#!/usr/bin/env bash",
      "set -eu",
      "count=0",
      'if [ -f "$MOCK_STATE" ]; then count=$(cat "$MOCK_STATE"); fi',
      "count=$((count + 1))",
      'printf "%s\\n" "$count" > "$MOCK_STATE"',
      'printf "%s\\n" "$@" > "$MOCK_ARGUMENTS"',
      'if [ "$count" -le "$MOCK_FAILURES" ]; then',
      '  printf "%s\\n" "$MOCK_ERROR" >&2',
      '  exit "$MOCK_EXIT_CODE"',
      "fi",
      'echo "SDK packages installed."',
    ].join("\n"), { mode: 0o755 });
    writeFileSync(join(tools, "sleep"),
      '#!/usr/bin/env bash\nprintf "%s\\n" "$@" >> "$MOCK_SLEEPS"\n',
      { mode: 0o755 });

    const result = spawnSync("bash", [
      script,
      ...(packages ?? ["emulator", "system-images;android-35;google_apis;x86_64", "--channel=0"]),
    ], {
      encoding: "utf8",
      timeout: 5000,
      env: {
        ...process.env,
        ANDROID_HOME: sdkRoot,
        RUNNER_TEMP: logs,
        PATH: `${tools}:${process.env.PATH}`,
        MOCK_STATE: state,
        MOCK_ARGUMENTS: args,
        MOCK_SLEEPS: sleeps,
        MOCK_FAILURES: String(failures),
        MOCK_ERROR: error,
        MOCK_EXIT_CODE: String(exitCode),
      },
    });
    assert.ifError(result.error);
    assert.equal(result.signal, null);
    assert.deepEqual(readdirSync(logs), [], "Temporary sdkmanager output must be cleaned up.");
    const files = readdirSync(directory);
    return {
      ...result,
      attempts: files.includes("attempts") ? Number(readFileSync(state, "utf8")) : 0,
      args: files.includes("arguments") ? readFileSync(args, "utf8").trim().split("\n") : [],
      sleeps: files.includes("sleeps") ? readFileSync(sleeps, "utf8").trim().split("\n") : [],
    };
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
}

test("successful installs preserve package arguments and do not retry on yes SIGPIPE", () => {
  const result = install();
  assert.equal(result.status, 0);
  assert.equal(result.attempts, 1);
  assert.deepEqual(result.sleeps, []);
  assert.deepEqual(result.args, [
    "--install", "emulator", "system-images;android-35;google_apis;x86_64", "--channel=0",
  ]);
  assert.match(result.stdout, /SDK packages installed/);
});

for (const error of [
  "Warning: An error occurred while preparing SDK package Google APIs: Error on ZipFile unknown archive.",
  "java.util.zip.ZipException: zip END header not found",
  "java.net.SocketTimeoutException: Read timed out",
  "java.net.UnknownHostException: dl.google.com",
  "java.net.UnknownHostException: dl.google.com\nWarning: Failed to find package \"emulator\"",
  "java.io.IOException: Connection reset",
  "Warning: An error occurred during download: unexpected end of stream.",
  "Server returned HTTP response code: 503 for URL: https://dl.google.com/android/repository/image.zip",
  "HTTP 429 Too Many Requests",
]) {
  test(`transient SDK error recovers after two retries: ${error}`, () => {
    const result = install({ failures: 2, error });
    assert.equal(result.status, 0);
    assert.equal(result.attempts, 3);
    assert.deepEqual(result.sleeps, ["5", "10"]);
    assert.match(result.stderr, /Retrying Android SDK installation/);
    assert.ok(result.stdout.includes(error), "The original SDK error must remain in the log.");
  });
}

test("exhausted transient failures preserve the SDK exit code and stop after three attempts", () => {
  const result = install({ failures: 99, error: "Error on ZipFile unknown archive", exitCode: 23 });
  assert.equal(result.status, 23);
  assert.equal(result.attempts, 3);
  assert.deepEqual(result.sleeps, ["5", "10"]);
  assert.match(result.stderr, /failed after 3 attempts/);
});

for (const error of [
  'Warning: Failed to find package "system-images;android-invalid;google_apis;x86_64"',
  "License not accepted.",
  "Permission denied",
  "No space left on device",
  "Download failed: Server returned HTTP response code: 404 for URL: https://dl.google.com/missing.zip",
  "Unexpected SDK installer failure",
]) {
  test(`permanent or unrecognized SDK errors fail immediately: ${error}`, () => {
    const result = install({ failures: 99, error, exitCode: 7 });
    assert.equal(result.status, 7);
    assert.equal(result.attempts, 1);
    assert.deepEqual(result.sleeps, []);
    assert.match(result.stderr, /not a retryable ZIP\/download error/);
  });
}

test("missing packages fail before invoking sdkmanager", () => {
  const result = install({ packages: [] });
  assert.equal(result.status, 64);
  assert.equal(result.attempts, 0);
  assert.deepEqual(result.sleeps, []);
  assert.match(result.stderr, /Usage:/);
});
