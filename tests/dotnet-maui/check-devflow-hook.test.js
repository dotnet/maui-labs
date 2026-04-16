#!/usr/bin/env node
// tests/dotnet-maui/check-devflow-hook.test.js
//
// Self-contained smoke tests for the plugin SessionStart / PostToolUse
// hook script. Run directly:
//   node tests/dotnet-maui/check-devflow-hook.test.js
//
// Asserts silent exit vs. nudge JSON across:
//   - empty dir (not a project)
//   - non-MAUI .csproj
//   - standard MAUI app (fires)
//   - MAUI Blazor app (fires with "Blazor hybrid" label)
//   - GTK app (fires with "GTK" label; detects without <UseMaui>)
//   - already-wired (silent)
//   - debounced repeat (silent)
//   - PostToolUse with unrelated file path (silent)
//   - PostToolUse editing MauiProgram.cs (fires)

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

function runHook(cwd, event, stdinPayload) {
  const result = spawnSync("node", [SCRIPT, event], {
    cwd,
    env: { ...process.env, MAUI_DEVFLOW_HOOK_ASSUME_CLI: "1", CLAUDE_PROJECT_DIR: cwd },
    input: stdinPayload ? JSON.stringify(stdinPayload) : "",
    encoding: "utf8",
  });
  return { stdout: result.stdout, stderr: result.stderr, status: result.status };
}

function mkTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "devflow-hook-"));
}

function writeCsproj(dir, name, content) {
  fs.writeFileSync(path.join(dir, name), content);
}

// --- tests ---

function testEmptyDir() {
  const dir = mkTempDir();
  try {
    const r = runHook(dir, "SessionStart");
    assertEq(r.stdout, "", "empty dir is silent");
    assertEq(r.status, 0, "empty dir exits 0");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testNonMauiCsproj() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>`);
    const r = runHook(dir, "SessionStart");
    assertEq(r.stdout, "", "non-MAUI csproj is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testStandardMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>`);
    const r = runHook(dir, "SessionStart");
    assertContains(r.stdout, "(standard)", "standard MAUI reports standard flavor");
    assertContains(r.stdout, "set up DevFlow", "standard MAUI suggests setup");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testBlazorMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup><UseMaui>true</UseMaui></PropertyGroup>
        <ItemGroup><PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" /></ItemGroup>
      </Project>`);
    const r = runHook(dir, "SessionStart");
    assertContains(r.stdout, "Blazor hybrid", "Blazor hybrid flavor reported");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testGtkMaui() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk.Razor">
        <ItemGroup><PackageReference Include="Platform.Maui.Linux.Gtk4" /></ItemGroup>
      </Project>`);
    const r = runHook(dir, "SessionStart");
    assertContains(r.stdout, "(GTK)", "GTK flavor reported");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testAlreadyWired() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup><UseMaui>true</UseMaui></PropertyGroup>
        <ItemGroup Label="DevFlow" Condition="'$(EnableDevFlow)' == 'true'">
          <PackageReference Include="Microsoft.Maui.DevFlow.Agent" Version="0.1.0-preview.4" />
        </ItemGroup>
      </Project>`);
    const r = runHook(dir, "SessionStart");
    assertEq(r.stdout, "", "already-wired project is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testDebounce() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>`);
    const r1 = runHook(dir, "SessionStart");
    assertContains(r1.stdout, "set up DevFlow", "first SessionStart nudges");
    const r2 = runHook(dir, "SessionStart");
    assertEq(r2.stdout, "", "second SessionStart debounced");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testPostToolUseUnrelated() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>`);
    const r = runHook(dir, "PostToolUse", { tool_input: { file_path: path.join(dir, "README.md") } });
    assertEq(r.stdout, "", "PostToolUse on README is silent");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

function testPostToolUseRelevant() {
  const dir = mkTempDir();
  try {
    writeCsproj(dir, "App.csproj",
      `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseMaui>true</UseMaui></PropertyGroup></Project>`);
    const r = runHook(dir, "PostToolUse", { tool_input: { file_path: path.join(dir, "MauiProgram.cs") } });
    assertContains(r.stdout, "set up DevFlow", "PostToolUse on MauiProgram.cs nudges");
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}

// run
testEmptyDir();
testNonMauiCsproj();
testStandardMaui();
testBlazorMaui();
testGtkMaui();
testAlreadyWired();
testDebounce();
testPostToolUseUnrelated();
testPostToolUseRelevant();

if (failed > 0) {
  for (const f of fails) console.error(f);
  console.error(`\n${passed} passed, ${failed} failed`);
  process.exit(1);
}
console.log(`${passed} passed, 0 failed`);
