import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const source = readFileSync(
  new URL("../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.js", import.meta.url),
  "utf8",
);
const css = readFileSync(
  new URL("../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/devflow.css", import.meta.url),
  "utf8",
);
const html = readFileSync(
  new URL("../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector.html", import.meta.url),
  "utf8",
);

test("Interact binds the rendered target while both modes share hover feedback", () => {
  assert.match(source, /const tapEl = hoverHitTest\(e\.clientX, e\.clientY\)/);
  assert.match(source, /const targetId = isTapTargetOverlay\(targetEl\)/);
  assert.match(source, /getElementCaptureMetadata\(targetEl\)/);
  assert.match(source, /if \(targetId\) payload\.elementId = targetId/);
  assert.match(source, /inspectorApi\.postDetailed\('\/api\/tap', payload\)/);
  assert.doesNotMatch(source, /\bapi\.postDetailed\('\/api\/tap'/);
  assert.match(source, /if \(tap\.ok && tap\.target\) await recordStep\('tap', tap\.target\)/);
  assert.match(source, /result\.ok !== true/);
  assert.match(source, /Tap did not run/);
  assert.match(source, /const node = document\.elementFromPoint\(clientX, clientY\)/);
  assert.doesNotMatch(source, /document\.elementsFromPoint\(clientX, clientY\)/);
  assert.match(source, /Interactive element/);
  assert.match(source, /Scrollable element/);
  assert.match(source, /Visual element/);
  assert.match(css, /\.devflow-element\.df-action-target/);
  assert.match(css, /#df-badge\[data-kind="interactive"\]/);
  assert.match(source, /viewportWidth - b\.offsetWidth/);
  assert.match(source, /vpWrap\.classList\.toggle\('df-fit-active', fitMode\)/);
  assert.match(source, /Copilot can request mutation control with maui_take_control/);
  assert.match(css, /#df-viewport-wrap\.df-fit-active\s*\{\s*overflow:\s*hidden/);
  assert.match(css, /\.df-hit-candidates\s*\{[\s\S]*position:\s*absolute/);
  assert.match(css, /#df-preview-surface\s*\{[\s\S]*position:\s*relative/);
  assert.match(
    html,
    /id="df-preview-surface"[\s\S]*id="df-hit-candidates"[\s\S]*id="df-viewport-wrap"/,
  );
});

test("active destinations use an accent border without competing with the active mode", () => {
  const toolbarState = css.match(/\.df-tool-btn\.df-active\s*\{([\s\S]*?)\}/);
  assert.ok(toolbarState);
  assert.match(toolbarState[1], /border-color:\s*var\(--df-accent\)/);
  assert.match(toolbarState[1], /background:\s*var\(--df-surface-2\)/);
  assert.match(toolbarState[1], /box-shadow:\s*none/);

  const dockState = css.match(/\.df-dock-tab\.df-active\s*\{([\s\S]*?)\}/);
  assert.ok(dockState);
  assert.match(dockState[1], /inset 0 -2px 0 var\(--df-accent\)/);

  const modeState = css.match(/\.df-mode-btn\.df-active\s*\{([\s\S]*?)\}/);
  assert.ok(modeState);
  assert.match(modeState[1], /background:\s*var\(--df-accent\)/);
  assert.match(modeState[1], /color:\s*var\(--df-accent-fg\)/);
});
