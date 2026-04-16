#!/usr/bin/env node
// plugins/dotnet-maui/hooks/check-devflow.js
//
// Shared hook script for the dotnet-maui plugin. Invoked from both
// SessionStart and PostToolUse to decide whether to nudge the user to
// wire DevFlow into the current MAUI project.
//
// Contract:
//   - Reads hook event JSON from stdin (may be ignored).
//   - First arg is the event name ("SessionStart" or "PostToolUse").
//   - Exits 0 always. Emits a JSON object on stdout when it wants to
//     inject context; otherwise stays silent.
//
// Detection strategy: ask MSBuild. `dotnet msbuild <csproj> -nologo
// -getProperty:UseMaui,EnableDevFlow -getItem:PackageReference` yields
// authoritative JSON that already accounts for Directory.Build.props,
// Directory.Packages.props, SDKs, and transitive .targets imports.
// We avoid brittle regex over a single csproj file.
//
// Debounce: .devflow/hook-state.json in CWD stores the last state we
// nudged for; a nudge is only emitted when the state changes inside a
// single session.
//
// Test override: set MAUI_DEVFLOW_HOOK_STUB=<json-file> to feed a canned
// MSBuild response (used by tests/dotnet-maui/check-devflow-hook.test.js
// so CI does not depend on a fully restored .NET SDK state).

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { execFileSync, spawnSync } = require("node:child_process");

const EVENT = process.argv[2] || "SessionStart";
const CWD = process.env.CLAUDE_PROJECT_DIR || process.cwd();

// Drain stdin and — for PostToolUse — peek at the edited file path so we
// can short-circuit edits unrelated to project wiring.
let stdinPayload = null;
try {
  const raw = fs.readFileSync(0, "utf8");
  if (raw && raw.trim().length > 0) stdinPayload = JSON.parse(raw);
} catch { /* no stdin or not JSON */ }

if (EVENT === "PostToolUse" && stdinPayload) {
  const input = stdinPayload.tool_input || stdinPayload.toolInput || {};
  const editedPath = input.file_path || input.filePath || input.path || "";
  if (editedPath) {
    const base = path.basename(editedPath).toLowerCase();
    const relevant =
      base === "mauiprogram.cs" ||
      base.endsWith(".csproj") ||
      base === "directory.packages.props" ||
      base === "directory.build.props" ||
      base === "directory.build.targets";
    if (!relevant) process.exit(0);
  }
}

function listCsprojs(dir) {
  try {
    return fs.readdirSync(dir)
      .filter(f => f.toLowerCase().endsWith(".csproj"))
      .map(f => path.join(dir, f));
  } catch { return []; }
}

// Invoke `dotnet msbuild <csproj> -getProperty:UseMaui,EnableDevFlow
// -getItem:PackageReference` and return the parsed JSON, or null on
// failure. Short timeout so a hook never blocks the host.
function evaluateCsproj(csproj) {
  const stub = process.env.MAUI_DEVFLOW_HOOK_STUB;
  if (stub) {
    try {
      return JSON.parse(fs.readFileSync(stub, "utf8"));
    } catch { return null; }
  }
  try {
    const result = spawnSync(
      "dotnet",
      ["msbuild", csproj, "-nologo",
       "-getProperty:UseMaui,EnableDevFlow",
       "-getItem:PackageReference"],
      { encoding: "utf8", timeout: 15000 }
    );
    if (result.status !== 0 || !result.stdout) return null;
    return JSON.parse(result.stdout);
  } catch {
    return null;
  }
}

function packageIdentities(eval_) {
  const items = (eval_ && eval_.Items && eval_.Items.PackageReference) || [];
  return items.map(i => i.Identity || "").filter(Boolean);
}

function isMauiProject(eval_) {
  if (!eval_) return false;
  const props = eval_.Properties || {};
  if (typeof props.UseMaui === "string" && props.UseMaui.toLowerCase() === "true") return true;
  for (const id of packageIdentities(eval_)) {
    if (/^Microsoft\.Maui\./i.test(id)) return true;
    if (/^Platform\.Maui\./i.test(id)) return true;
  }
  return false;
}

function isDevFlowWired(eval_) {
  if (!eval_) return false;
  for (const id of packageIdentities(eval_)) {
    if (/^Microsoft\.Maui\.DevFlow\./i.test(id)) return true;
  }
  return false;
}

function detectFlavor(eval_) {
  const ids = packageIdentities(eval_);
  const hasBlazor = ids.some(id => /^Microsoft\.AspNetCore\.Components\.WebView\.Maui$/i.test(id));
  const hasGtk = ids.some(id =>
    /^Platform\.Maui\.Linux\.Gtk/i.test(id) ||
    /^Microsoft\.Maui\.DevFlow\.Agent\.Gtk$/i.test(id) ||
    /^Microsoft\.Maui\.DevFlow\.Blazor\.Gtk$/i.test(id));
  if (hasGtk && hasBlazor) return "blazor-gtk";
  if (hasGtk) return "gtk";
  if (hasBlazor) return "blazor";
  return "standard";
}

function mauiCliAvailable() {
  if (process.env.MAUI_DEVFLOW_HOOK_ASSUME_CLI === "1") return true;
  if (process.env.MAUI_DEVFLOW_HOOK_ASSUME_CLI === "0") return false;
  try {
    execFileSync("maui", ["--version"], { stdio: "ignore", timeout: 5000 });
    return true;
  } catch {
    return false;
  }
}

function emit(state, message) {
  // Debounce: only nudge once per state-change per session directory.
  const stateDir = path.join(CWD, ".devflow");
  const stateFile = path.join(stateDir, "hook-state.json");
  try {
    const prev = JSON.parse(fs.readFileSync(stateFile, "utf8"));
    if (prev && prev.lastState === state && prev.lastEvent === EVENT) return;
  } catch { /* no prior state */ }
  try {
    fs.mkdirSync(stateDir, { recursive: true });
    fs.writeFileSync(stateFile, JSON.stringify({ lastState: state, lastEvent: EVENT, at: new Date().toISOString() }, null, 2));
  } catch { /* best-effort */ }

  // Emit structured output. Both Claude Code and Copilot CLI understand
  // `hookSpecificOutput.additionalContext` for SessionStart; the bare
  // `context` field is a broadly-accepted fallback.
  const payload = {
    context: message,
    hookSpecificOutput: {
      hookEventName: EVENT,
      additionalContext: message
    }
  };
  process.stdout.write(JSON.stringify(payload));
}

function main() {
  const csprojs = listCsprojs(CWD);
  if (csprojs.length === 0) return; // not a project dir

  // Evaluate each csproj with MSBuild and pick the first one that looks
  // like MAUI. If MSBuild eval fails (missing SDK, restore required,
  // timeout), we stay silent — a nudge hook should never yell about a
  // project it can't understand.
  let mauiEval = null;
  for (const csproj of csprojs) {
    const ev = evaluateCsproj(csproj);
    if (isMauiProject(ev)) { mauiEval = ev; break; }
  }
  if (!mauiEval) return; // not MAUI, or MSBuild couldn't evaluate

  if (isDevFlowWired(mauiEval)) {
    // Already wired — no nudge. Don't record state either, so that
    // un-wiring cleanly re-arms the nudge on the next session.
    return;
  }

  if (!mauiCliAvailable()) {
    emit("maui-missing",
      "🔧 MAUI project detected but the `maui` CLI isn't on PATH. " +
      "Install it with `dotnet tool install -g Microsoft.Maui.Cli --prerelease`, " +
      "then ask me to set up DevFlow.");
    return;
  }

  const flavor = detectFlavor(mauiEval);
  const flavorLabel = flavor === "blazor-gtk" ? "Blazor GTK"
                    : flavor === "gtk"        ? "GTK"
                    : flavor === "blazor"     ? "Blazor hybrid"
                    :                           "standard";
  emit(`unwired-${flavor}`,
    `🔧 MAUI project detected (${flavorLabel}) but DevFlow is not wired. ` +
    `Say "set up DevFlow" and I'll run the maui-devflow-setup skill, ` +
    `or run \`maui devflow diagnose\` for details.`);
}

try { main(); } catch (err) {
  // Never block the host on hook failure.
  try { process.stderr.write(`[maui-devflow hook] ${err && err.message ? err.message : err}\n`); } catch { /* ignore */ }
}
