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
// Decision rules (plan D3.1):
//   1. If cwd has no .csproj -> silent exit.
//   2. If `maui --version` fails -> emit install hint, exit 0.
//   3. If any .csproj already carries a Label="DevFlow" marker OR a
//      <!-- <DevFlow> --> fence AND a Microsoft.Maui.DevFlow.*
//      PackageReference -> silent exit (already wired).
//   4. If none of the cwd .csproj files look like MAUI (no <UseMaui>true
//      and no Microsoft.Maui.* / Platform.Maui.* PackageReference) ->
//      silent exit.
//   5. Otherwise emit the setup nudge.
//
// Debounce: .devflow/hook-state.json in CWD stores the last state we
// nudged for; a nudge is only emitted when the state changes inside a
// single session.

"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { execSync } = require("node:child_process");

const EVENT = process.argv[2] || "SessionStart";
const CWD = process.env.CLAUDE_PROJECT_DIR || process.cwd();

// We don't need stdin content, but drain it so the host doesn't block.
try { fs.readFileSync(0, "utf8"); } catch { /* no stdin */ }

function listCsprojs(dir) {
  try {
    return fs.readdirSync(dir)
      .filter(f => f.toLowerCase().endsWith(".csproj"))
      .map(f => path.join(dir, f));
  } catch { return []; }
}

function readText(p) {
  try { return fs.readFileSync(p, "utf8"); } catch { return ""; }
}

function isMauiCsproj(text) {
  if (/<UseMaui>\s*true\s*<\/UseMaui>/i.test(text)) return true;
  if (/<PackageReference[^>]*Include\s*=\s*"(Microsoft\.Maui\.|Platform\.Maui\.)[^"]*"/.test(text)) return true;
  if (/<ProjectReference[^>]*Include\s*=\s*"[^"]*Microsoft\.Maui\.[^"]*"/.test(text)) return true;
  return false;
}

function isDevFlowWired(text) {
  const hasPackage = /<PackageReference[^>]*Include\s*=\s*"Microsoft\.Maui\.DevFlow\.[^"]*"/.test(text);
  if (!hasPackage) return false;
  const hasLabel = /<(ItemGroup|PropertyGroup)[^>]*Label\s*=\s*"DevFlow"/.test(text);
  const hasFence = /<!--\s*<DevFlow>\s*-->/.test(text);
  return hasLabel || hasFence;
}

function detectFlavor(text) {
  const hasBlazor = /Microsoft\.AspNetCore\.Components\.WebView\.Maui/.test(text);
  const hasGtk = /Platform\.Maui\.Linux\.Gtk4/.test(text);
  if (hasGtk && hasBlazor) return "blazor-gtk";
  if (hasGtk) return "gtk";
  if (hasBlazor) return "blazor";
  return "standard";
}

function mauiCliAvailable() {
  if (process.env.MAUI_DEVFLOW_HOOK_ASSUME_CLI === "1") return true;
  if (process.env.MAUI_DEVFLOW_HOOK_ASSUME_CLI === "0") return false;
  try {
    execSync("maui --version", { stdio: "ignore", timeout: 5000 });
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

  // Pick the first MAUI-looking csproj (good enough for a nudge).
  let mauiCsproj = null;
  let text = "";
  for (const p of csprojs) {
    const t = readText(p);
    if (isMauiCsproj(t)) {
      mauiCsproj = p;
      text = t;
      break;
    }
  }
  if (!mauiCsproj) return; // not a MAUI project

  if (isDevFlowWired(text)) {
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

  const flavor = detectFlavor(text);
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
