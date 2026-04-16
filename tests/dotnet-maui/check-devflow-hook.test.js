#!/usr/bin/env node
// tests/dotnet-maui/check-devflow-hook.test.js
//
// Self-contained smoke tests for the plugin SessionStart / PostToolUse
// hook script. Run directly:
//   node tests/dotnet-maui/check-devflow-hook.test.js
//
// The hook defers detection to `dotnet msbuild -getProperty -getItem`.
// Rather than require a fully restored .NET SDK inside tests, we feed
// the hook canned MSBuild JSON via the MAUI_DEVFLOW_HOOK_STUB env var.
// That isolates the tests from environment drift while still exercising
// the hook's parsing and decision logic end-to-end.

"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const SCRIPT = path.resolve(__dirname,
  "..", "..", "plugins", "dotnet-maui", "hooks", "check-devflow.js");

let passed = 0;
let failed = 0;
const fails = [];

function assertEq(actual, expected, label) {
  if (actual === expected) { passed++; return; }
  failed++;
  fails.push(`FAIL: ${label}\n  expected: ${JSON.stringify(expected)}\n  actual:   ${JSON.stringify(actual)}`);
}

function assertContains(actual, needle, label) {
  if (typeof actual === "string" && actual.includes(needle)) { passed++; return; }
  failed++;
  fails.push(`FAIL: ${label}\n  expected to contain: ${JSON.stringify(needle)}\n  actual: ${JSON.stringify(actual)}`);
}

// Build a JSON object in the same shape `dotnet msbuild -getProperty -getItem`
// emits: { "Properties": { ... }, "Items": { "PackageReference": [...] } }.
function stubJson(props, packageIds) {
  return {
    Properties: {
      UseMaui: props.UseMaui ?? "",
      EnableDevFlow: props.EnableDevFlow ?? ""
    },
    Items: {
      PackageReference: (packageIds || []).map(id => ({ Identity: id }))
    }
  };
}

function writeStub(dir, data) {
  const p = path.join(dir, "stub.json");
  fs.writeFileSync(p, JSON.stringify(data));
  return p;
}

function runHook(cwd, event, { stubPath, stdinPayload, cliPresent = true } = {}) {
  const env = { ...process.env, CLAUDE_PROJECT_DIR: cwd };
  if (cliPresent) env.MAUI_DEVFLOW_HOOK_ASSUME_CLI = "1";
  else            env.MAUI_DEVFLOW_HOOK_ASSUME_CLI = "0";
  if (stubPath) env.MAUI_DEVFLOW_HOOK_STUB = stubPath;
  const result = spawnSync("node", [SCRIPT, event], {
    cwd,
    env,
    input: stdinPayload ? JSON.stringify(stdinPayload) : "",
    encoding: "utf8",
  });
  return { stdout: result.stdout, stderr: result.stderr, status: result.status };
}

function mkTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "devflow-hook-"));
}

function writeCsproj(dir, name) {
  // Contents don't matter — the hook hands the path to MSBuild (or the
  // stub override). We only need a file with a .csproj extension.
  fs.writeFileSync(path.join(dir, name),
    `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>`);
}

// --- tests ---

function testEmptyDir() {
  const dir = mkTempDir();
  try {
    // No csproj at all, no stub either — hook should exit silently.
    const r = runHook(dir, "SessionStart");
    assertEq(r.stdout, "", "empty dir is silent");
    assertEq(r.status, 0, "empty dir exits 0");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testNonMauiCsproj() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir, stubJson({}, []));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertEq(r.stdout, "", "non-MAUI csproj is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testStandardMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" }, ["Microsoft.Maui.Controls"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertContains(r.stdout, "(standard)", "standard MAUI reports standard flavor");
    assertContains(r.stdout, "set up DevFlow", "standard MAUI suggests setup");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testBlazorMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" },
        ["Microsoft.Maui.Controls", "Microsoft.AspNetCore.Components.WebView.Maui"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertContains(r.stdout, "Blazor hybrid", "Blazor hybrid flavor reported");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testGtkMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    // GTK apps don't set <UseMaui>true</UseMaui>; they pull in the MAUI
    // runtime via Platform.Maui.Linux.Gtk4 (+ related) packages.
    const stub = writeStub(dir,
      stubJson({}, ["Platform.Maui.Linux.Gtk4"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertContains(r.stdout, "(GTK)", "GTK flavor reported");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testAlreadyWired() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" },
        ["Microsoft.Maui.Controls", "Microsoft.Maui.DevFlow.Agent"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertEq(r.stdout, "", "already-wired project is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testAlreadyWiredViaCpm() {
  // The new design trusts MSBuild: a DevFlow package reference authored
  // in Directory.Packages.props (and surfaced to MSBuild item resolution)
  // should be treated as wired, even without a Label="DevFlow" marker.
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" },
        ["Microsoft.Maui.Controls", "Microsoft.Maui.DevFlow.Agent"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub });
    assertEq(r.stdout, "",
      "DevFlow package discovered via MSBuild evaluation counts as wired regardless of how it was authored");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testDebounce() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" }, ["Microsoft.Maui.Controls"]));
    const r1 = runHook(dir, "SessionStart", { stubPath: stub });
    assertContains(r1.stdout, "set up DevFlow", "first SessionStart nudges");
    const r2 = runHook(dir, "SessionStart", { stubPath: stub });
    assertEq(r2.stdout, "", "second SessionStart debounced");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testPostToolUseUnrelated() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" }, ["Microsoft.Maui.Controls"]));
    const r = runHook(dir, "PostToolUse",
      { stubPath: stub, stdinPayload: { tool_input: { file_path: path.join(dir, "README.md") } } });
    assertEq(r.stdout, "", "PostToolUse on README is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testPostToolUseRelevant() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" }, ["Microsoft.Maui.Controls"]));
    const r = runHook(dir, "PostToolUse",
      { stubPath: stub, stdinPayload: { tool_input: { file_path: path.join(dir, "MauiProgram.cs") } } });
    assertContains(r.stdout, "set up DevFlow", "PostToolUse on MauiProgram.cs nudges");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testMauiCliMissing() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj");
    const stub = writeStub(dir,
      stubJson({ UseMaui: "true" }, ["Microsoft.Maui.Controls"]));
    const r = runHook(dir, "SessionStart", { stubPath: stub, cliPresent: false });
    assertContains(r.stdout, "`maui` CLI isn't on PATH", "missing CLI prompt shown");
    assertContains(r.stdout, "dotnet tool install", "install hint included");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

// run
testEmptyDir();
testNonMauiCsproj();
testStandardMaui();
testBlazorMaui();
testGtkMaui();
testAlreadyWired();
testAlreadyWiredViaCpm();
testDebounce();
testPostToolUseUnrelated();
testPostToolUseRelevant();
testMauiCliMissing();

if (failed > 0) {
  for (const f of fails) console.error(f);
  console.error(`\n${passed} passed, ${failed} failed`);
  process.exit(1);
}
console.log(`${passed} passed, 0 failed`);
