// selftest-recorder.mjs — OFFLINE end-to-end proof of the record -> .md -> replay loop.
// Uses a tiny in-memory mock "app" (no DevFlow, no running app) that implements exactly the
// method contract the REAL Recorder + replayTest call. Proves: durable-selector capture, the
// avoidText rule (text-editing an element must NOT select it by that text), auto assertions,
// the .md dual-layer round-trip, and that replay actually re-drives fill / setProperty /
// typeIndex targets — the last from a CLEAN state. Temp artifacts are cleaned up.

import { Recorder } from "./recorder.mjs";
import { replayTest } from "./replay.mjs";
import { readFileSync, writeFileSync, mkdtempSync, mkdirSync, rmSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

let failures = 0;
const check = (name, cond, extra = "") => {
  const ok = !!cond;
  if (!ok) failures++;
  console.log(`  ${ok ? "\u2713 PASS" : "\u2717 FAIL"}  ${name}${extra ? "  \u2014 " + extra : ""}`);
};

class MockStore {
  constructor() {
    this.els = new Map([
      ["e1", { id: "e1", type: "Entry", automationId: "nameEntry", text: "", absBounds: { x: 0, y: 0, width: 100, height: 40 } }],
      ["e2", { id: "e2", type: "Label", automationId: "titleLabel", text: "Hello", absBounds: { x: 0, y: 50, width: 100, height: 40 } }],
      ["e3", { id: "e3", type: "Button", text: "Click", absBounds: { x: 0, y: 100, width: 100, height: 40 } }], // no AutomationId on purpose
    ]);
    this._index = new Map([...this.els].map(([id, el]) => [id, { el }]));
    this.state = { info: { appName: "MockApp", platform: "test", theme: "light", window: { width: 400, height: 800 } }, order: [...this.els.keys()] };
    this._recorder = null;
    this._outRoot = mkdtempSync(join(tmpdir(), "maui-recoffline-"));
    this.device = {
      _resolveElementId: async (sel) => this._resolve(sel),
      resolvedAgent: () => ({ project: this._outRoot }),
    };
  }
  getElement(id) { const e = this._index.get(String(id)); return e ? e.el : undefined; }
  _typeIndexOf(el) { return [...this.els.values()].filter((e) => e.type === el.type).findIndex((e) => e.id === el.id); }
  // Mirror of the REAL store._bestSelector, incl. the avoidText rule under test:
  // for text-editing actions, don't key the selector on the (about-to-change) text.
  _bestSelector(id, { avoidText = false } = {}) {
    const el = this.getElement(id);
    if (!el) return { selectorKind: "id", selector: String(id), id: String(id), fragile: true };
    const base = { type: el.type, id: String(el.id) };
    if (el.automationId) return { ...base, selectorKind: "automationId", selector: el.automationId, automationId: el.automationId, fragile: false };
    const text = el.text != null ? String(el.text).trim() : "";
    if (text && !avoidText) return { ...base, selectorKind: "text", selector: text, text, fragile: false };
    const index = this._typeIndexOf(el);
    if (index >= 0) return { ...base, selectorKind: "typeIndex", selector: `${el.type}[${index}]`, index, fragile: true };
    return { ...base, selectorKind: "id", selector: String(id), fragile: true };
  }
  _pageSignature() { return { label: "MainPage", hash: 1 }; }
  currentShotPath() { return null; }
  _resolveTypeIndex(type, index) { return [...this.els.values()].filter((e) => e.type === type)[index]?.id ?? null; }
  _resolve(sel) {
    if (!sel) return null;
    if (sel.id != null) return this.getElement(sel.id) ? String(sel.id) : null;
    if (sel.automationId) { for (const e of this.els.values()) if (e.automationId === sel.automationId) return e.id; }
    if (sel.text) { for (const e of this.els.values()) if (String(e.text).trim() === String(sel.text).trim()) return e.id; }
    return null;
  }
  async refresh() { return; }
  async fill(sel, text) { const id = this._resolve(sel); if (!id) return { ok: false, error: "no el" }; this.getElement(id).text = String(text); return { ok: true }; }
  async setProperty(id, name, value) { const el = this.getElement(id); if (!el) return { ok: false, error: "no el " + id }; el[name === "Text" ? "text" : name] = String(value); return { ok: true }; }
  async getProperty(id, name) { const el = this.getElement(id); if (!el) return { ok: false }; return { ok: true, value: name === "Text" ? el.text : el[name] }; }
  async query(sel) { const id = this._resolve(sel); return { ok: true, elements: id ? [this.getElement(id)] : [] }; }
  async tap() { return { ok: true }; }
  async scroll() { return { ok: true }; }
  async navigate() { return { ok: true }; }
  async back() { return { ok: true }; }
  async setTheme() { return { ok: true }; }
}

console.log("\n== recorder/replay OFFLINE end-to-end ==\n");
const store = new MockStore();
const rec = new Recorder();
store._recorder = rec;

// 1) Record three steps: fill Entry, setProperty(Text) on the AutomationId Label, and
//    setProperty(Text) on the AutomationId-less Button (must select by type+index, not its text).
rec.start(store, { name: "offline-scenario", preconditions: "Mock app on MainPage." });
store.getElement("e1").text = "Alice";
await rec.captureStep(store, { action: "fill", target: { id: "e1" }, value: "Alice", beforeHash: 0, ok: true });
store.getElement("e2").text = "World";
await rec.captureStep(store, { action: "setProperty", target: { id: "e2" }, name: "Text", value: "World", beforeHash: 0, ok: true });
store.getElement("e3").text = "Clicked";
await rec.captureStep(store, { action: "setProperty", target: { id: "e3" }, name: "Text", value: "Clicked", beforeHash: 0, ok: true });

check("captured 3 steps", rec.steps.length === 3, `${rec.steps.length}`);
check("step 1 uses a durable automationId selector", rec.steps[0].target?.automationId === "nameEntry", rec.steps[0].target?.selector);
check("step 1 is not fragile (has AutomationId)", rec.steps[0].fragile === false);
check("step 1 auto-asserts propEquals Text==Alice",
  rec.steps[0].asserts?.some((a) => a.kind === "propEquals" && a.expected === "Alice" && a.verify));
check("step 2 auto-asserts propEquals Text==World",
  rec.steps[1].asserts?.some((a) => a.kind === "propEquals" && a.expected === "World" && a.verify));
// The key regression: editing the Button's Text must NOT capture that new text as the selector.
check("step 3 avoids a circular text selector (uses type+index)",
  rec.steps[2].target?.selectorKind === "typeIndex", rec.steps[2].target?.selector);
check("step 3 selector is not the value we just wrote",
  rec.steps[2].target?.selector !== "Clicked" && rec.steps[2].target?.text == null);

// 2) Save -> .md, and confirm the dual-layer contract round-trips.
const saved = rec.save(store);
check("save() wrote a .md", saved.ok && existsSync(saved.file), saved.file || saved.error);
const md = saved.ok ? readFileSync(saved.file, "utf8") : "";
check(".md has the json maui-test block", /```json maui-test/.test(md));
check(".md prose names the scenario", /# Scenario: offline-scenario/.test(md));

// 3) Reset the mock app to a CLEAN state, then replay the saved .md and prove it RE-DRIVES all
//    three actions — including the Button, which must now resolve by type+index because its text
//    differs from the recorded value (this would fail if we'd selected it by its post-edit text).
store.getElement("e1").text = "";
store.getElement("e2").text = "Hello";
store.getElement("e3").text = "Click";
rec.stop();
const report = await replayTest(store, { file: saved.file, root: saved.root });
check("replay ok (all steps passed)", report.ok === true, `${report.passed}/${report.total}`);
check("replay re-drove the fill (e1.text=Alice)", store.getElement("e1").text === "Alice", store.getElement("e1").text);
check("replay re-drove the AutomationId setProperty (e2.text=World)", store.getElement("e2").text === "World", store.getElement("e2").text);
check("replay re-drove the type+index setProperty (e3.text=Clicked)", store.getElement("e3").text === "Clicked", store.getElement("e3").text);
check("every replayed assertion passed",
  Array.isArray(report.results) && report.results.every((r) => r.ok && (r.asserts || []).every((a) => a.ok)));

// 4) A negative check: replay against a regressed app must FAIL (proves asserts really assert).
store.getElement("e2").text = "REGRESSED";
// Make setProperty a no-op so replay can't "fix" the regression, isolating the assertion.
const origSet = store.setProperty.bind(store);
store.setProperty = async (id, name, value) => (id === "e2" ? { ok: true } : origSet(id, name, value));
const bad = await replayTest(store, { file: saved.file, root: saved.root });
check("replay FAILS when the app regressed", bad.ok === false, `${bad.passed}/${bad.total}`);
store.setProperty = origSet;

// 5) outputRoot normalization: a .csproj FILE path must resolve to <projectDir>/maui-tests
//    (the live agent reports project as the .csproj file), and a project DIRECTORY works too.
const projDir = mkdtempSync(join(tmpdir(), "maui-recproj-"));
const csproj = join(projDir, "App.csproj");
writeFileSync(csproj, "<Project/>", "utf8");
check("outputRoot normalizes a .csproj file to <dir>/maui-tests",
  rec.outputRoot({ device: { resolvedAgent: () => ({ project: csproj }) } }) === join(projDir, "maui-tests"));
check("outputRoot accepts a project directory too",
  rec.outputRoot({ device: { resolvedAgent: () => ({ project: projDir }) } }) === join(projDir, "maui-tests"));
try { rmSync(projDir, { recursive: true, force: true }); } catch { /* */ }

const workspaceRoot = mkdtempSync(join(tmpdir(), "maui-recworkspace-"));
const nestedProject = join(workspaceRoot, "samples", "App");
mkdirSync(nestedProject, { recursive: true });
writeFileSync(join(nestedProject, "Nested.csproj"), "<Project/>", "utf8");
check("outputRoot resolves a relative agent project under the Canvas workspace",
  rec.outputRoot({
    device: {
      opts: { projectRoot: workspaceRoot },
      resolvedAgent: () => ({ project: "Nested.csproj" }),
    },
  }) === join(nestedProject, "maui-tests"));
try { rmSync(workspaceRoot, { recursive: true, force: true }); } catch { /* */ }

try { rmSync(store._outRoot, { recursive: true, force: true }); } catch { /* */ }
console.log(`\n== ${failures === 0 ? "ALL OFFLINE CHECKS PASSED \u2713" : failures + " CHECK(S) FAILED \u2717"} ==\n`);
process.exit(failures === 0 ? 0 : 1);
