// recorder.mjs — Workflow Test Recorder for the MAUI DevFlow Inspector Canvas host.
//
// WHAT THIS IS
// -----------
// "Playwright codegen, for MAUI." While recording, every mutating action a human (or Copilot)
// performs THROUGH the canvas — tap / fill / scroll / navigate / back / set-theme / set-property —
// is captured as a normalized, durable STEP. Stop & Save writes a reproducible `.md` test into the
// MAUI project's `maui-tests/` folder, plus one screenshot per step. That `.md` can later be REPLAYED
// (see replay.mjs) to validate the app. Many `.md` files = an AI-runnable regression suite.
//
// WHY AT THE STORE LEVEL
// ----------------------
// Both the human path (extension.mjs /control) and the agent path (extension.mjs actions[]) call the
// SAME LiveStore methods. The store therefore owns a single `_recorder` hook that calls captureStep()
// after each successful mutation — so BOTH sources are recorded uniformly, with no duplication.
//
// DURABLE SELECTORS
// -----------------
// A live element id is transient. captureStep() resolves each target to a DURABLE selector via
// store._bestSelector(id): AutomationId > exact text > type+index (flagged `fragile`) > raw id.
// Recording thus doubles as a testability audit — controls missing an AutomationId are surfaced.
//
// CONTRACT
// --------
// - Never throws into a live action (captureStep is wrapped; failures are swallowed).
// - Deterministic: toMarkdown() always produces a valid machine-readable `.md` with NO LLM needed.
//   The fenced ```json maui-test block is the SOURCE OF TRUTH for replay; the prose is for humans and
//   can evolve independently without changing that block.
// - ASCII-only strings (matches the canvas's "no multibyte in shared payloads" rule).

import { Buffer } from "node:buffer";
import { existsSync, mkdirSync, mkdtempSync, copyFileSync, linkSync, readFileSync, realpathSync, writeFileSync, readdirSync, rmSync, statSync, unlinkSync } from "node:fs";
import { join, dirname, basename, extname, isAbsolute, relative, resolve, sep } from "node:path";
import { tmpdir, homedir } from "node:os";

export const RECORDER_SCHEMA_VERSION = 1;
export const RECORDING_MAX_BYTES = 1024 * 1024;

// Which store mutations count as recordable workflow steps (everything else — select, refresh,
// screenshot, listAgents, resize, logs — is inspection noise and is NOT recorded).
export const RECORDABLE = new Set(["tap", "fill", "scroll", "navigate", "back", "setTheme", "setProperty"]);

function slugify(name) {
  const s = String(name || "").toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "");
  return s || "scenario";
}
export { slugify };

function pad2(n) {
  return String(n).padStart(2, "0");
}

function safeArea(b) {
  return b && b.width > 0 && b.height > 0 ? b.width * b.height : 0;
}

function findProjectFiles(root, projectName, depth = 0, matches = []) {
  if (!root || !projectName || depth > 6 || matches.length > 1) return matches;
  let entries;
  try { entries = readdirSync(root, { withFileTypes: true }); } catch { return matches; }
  for (const entry of entries) {
    if (entry.isSymbolicLink()) continue;
    const full = join(root, entry.name);
    if (entry.isFile() && entry.name.toLowerCase() === projectName.toLowerCase()) {
      matches.push(full);
      if (matches.length > 1) return matches;
      continue;
    }
    if (!entry.isDirectory() || /^(?:\.git|bin|obj|node_modules|artifacts)$/i.test(entry.name)) continue;
    findProjectFiles(full, projectName, depth + 1, matches);
    if (matches.length > 1) return matches;
  }
  return matches;
}

export class Recorder {
  constructor() {
    this.recording = false;
    this.steps = [];
    this.name = "";
    this.preconditions = "";
    this.startedAt = null;
    this.app = null;        // { name, platform } captured at start
    this._stageDir = null;  // temp dir holding per-step PNGs until Save
    this._savedTo = null;   // last saved .md path
  }

  // ── Lifecycle ───────────────────────────────────────────────────────────────
  start(store, { name, preconditions } = {}) {
    this._cleanupStage();
    this.recording = true;
    this.steps = [];
    this.name = String(name || "").trim() || `scenario-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, "-")}`;
    this.preconditions = String(preconditions || "").trim() || "App is launched and on its start page.";
    this.startedAt = new Date().toISOString();
    const info = store?.state?.info || {};
    this.app = { name: info.appName || "app", platform: info.platform || "device" };
    this._lastPage = null;
    try {
      this._stageDir = mkdtempSync(join(tmpdir(), "maui-rec-"));
    } catch {
      this._stageDir = null;
    }
    return this.status();
  }

  stop() {
    this.recording = false;
    return this.status();
  }

  clear() {
    this.recording = false;
    this.steps = [];
    this.name = "";
    this.preconditions = "";
    this.startedAt = null;
    this._lastPage = null;
    this._cleanupStage();
    return this.status();
  }

  deleteLast() {
    this.steps.pop();
    return this.status();
  }

  // Compact, JSON-serializable view for the snapshot / UI Steps panel.
  status() {
    return {
      recording: this.recording,
      name: this.name || null,
      preconditions: this.preconditions || null,
      count: this.steps.length,
      savedTo: this._savedTo,
      steps: this.steps.map((s) => ({
        seq: s.seq,
        action: s.action,
        label: s.label,
        fragile: !!s.fragile,
        asserts: (s.asserts || []).length,
      })),
    };
  }

  // ── Capture (called by store._recordAction after a successful mutation) ───────
  // meta: { action, target?:{id}|{automationId}|{text}, value?, name?, args?, beforeHash? }
  async captureStep(store, meta = {}) {
    if (!this.recording) return;
    const action = meta.action;
    if (!RECORDABLE.has(action)) return;

    const seq = this.steps.length + 1;

    // Resolve a durable selector for the target, if this action has one.
    let target = null;
    if (meta.target && meta.target.id != null && typeof store._bestSelector === "function") {
      // For actions that CHANGE the element's Text (fill / setProperty Text), don't key the
      // selector on that text — it would be circular and unresolvable on a clean replay.
      const avoidText = action === "fill" || (action === "setProperty" && (!meta.name || meta.name === "Text"));
      target = store._bestSelector(meta.target.id, { avoidText });
    } else if (meta.target && (meta.target.automationId || meta.target.text || meta.target.id != null)) {
      const t = meta.target;
      target = t.automationId
        ? { selectorKind: "automationId", selector: t.automationId, automationId: t.automationId }
        : t.text
        ? { selectorKind: "text", selector: t.text, text: t.text }
        : { selectorKind: "id", selector: String(t.id), id: String(t.id) };
    }

    // Page + navigation detection. A page-label change across steps means the app
    // navigated (robust: a fill/text edit changes the tree hash but NOT the page label).
    const page = typeof store._pageSignature === "function" ? store._pageSignature() : { label: null, hash: 0 };
    const navigated = this._lastPage != null && this._lastPage !== page.label;
    this._lastPage = page.label;

    // Post-action screenshot (reuses the store's current shot file), staged until Save.
    let screenshot = null;
    const src = typeof store.currentShotPath === "function" ? store.currentShotPath() : null;
    if (src && this._stageDir && existsSync(src)) {
      const shotName = `step-${pad2(seq)}.png`;
      try {
        copyFileSync(src, join(this._stageDir, shotName));
        screenshot = shotName;
      } catch {
        screenshot = null;
      }
    }

    // Machine-replay args + human label + auto-assertions.
    const args = this._buildArgs(action, target, meta);
    const label = this._label(action, target, meta.value, page);
    const asserts = this._autoAsserts(action, target, meta, navigated);

    this.steps.push({
      seq,
      action,
      target,
      value: meta.value ?? null,
      propName: meta.name || null,
      args,
      page: page.label || null,
      navigated,
      fragile: !!(target && target.fragile),
      screenshot,
      label,
      asserts,
      ts: Date.now(),
    });
  }

  _buildArgs(action, target, meta) {
    const sel = this._selForReplay(target);
    switch (action) {
      case "tap":
        return { selector: sel };
      case "fill":
        return { selector: sel, text: meta.value ?? "" };
      case "setProperty":
        return { selector: sel, name: meta.name || "Text", value: meta.value ?? "" };
      case "scroll":
        return { ...(meta.args || {}) };
      case "navigate":
        return { route: meta.value ?? meta.args?.route ?? "" };
      case "back":
        return {};
      case "setTheme":
        return { theme: meta.value ?? meta.args?.theme ?? "light" };
      default:
        return {};
    }
  }

  // The minimal selector object the store methods accept on replay.
  _selForReplay(target) {
    if (!target) return null;
    if (target.automationId) return { automationId: target.automationId };
    if (target.text) return { text: target.text };
    if (target.type && Number.isInteger(target.index)) return { typeIndex: { type: target.type, index: target.index } };
    if (target.id != null) return { id: String(target.id) };
    return null;
  }

  _label(action, target, value, page) {
    const who = target
      ? (target.automationId ? `#${target.automationId}` : target.text ? `"${target.text}"` : target.type || target.selector || "element")
      : "";
    switch (action) {
      case "tap":
        return `Tap ${who}`.trim();
      case "fill":
        return `Fill ${who} = "${value ?? ""}"`.trim();
      case "setProperty":
        return `Set ${who} ${page ? "" : ""}property = "${value ?? ""}"`.replace(/\s+/g, " ").trim();
      case "scroll":
        return `Scroll ${who || "view"}`.trim();
      case "navigate":
        return `Navigate to ${value ?? ""}`.trim();
      case "back":
        return "Go back";
      case "setTheme":
        return `Set theme to ${value ?? ""}`.trim();
      default:
        return action;
    }
  }

  // Conservative auto-assertions. `verify:true` ones are hard-checked on replay
  // (propEquals, exists); `verify:false` ones are informational (route/page hints).
  _autoAsserts(action, target, meta, navigated) {
    const a = [];
    const sel = this._selForReplay(target);
    if (action === "fill" && sel) {
      a.push({ kind: "propEquals", selector: sel, name: "Text", expected: String(meta.value ?? ""), verify: true });
    } else if (action === "setProperty" && sel && meta.name) {
      a.push({ kind: "propEquals", selector: sel, name: meta.name, expected: String(meta.value ?? ""), verify: true });
    }
    if (action === "tap" && sel && !navigated) {
      a.push({ kind: "exists", selector: sel, verify: true });
    }
    if (action === "navigate" && meta.value) {
      a.push({ kind: "routeIs", expected: String(meta.value), verify: false });
    }
    if (navigated) {
      a.push({ kind: "pageChanged", verify: false, note: "Visual tree changed after this action." });
    }
    return a;
  }

  // ── Serialization ─────────────────────────────────────────────────────────────
  toJSON() {
    return {
      schema: RECORDER_SCHEMA_VERSION,
      name: this.name,
      app: this.app?.name || null,
      platform: this.app?.platform || null,
      recordedAt: this.startedAt,
      preconditions: this.preconditions,
      steps: this.steps.map((s) => ({
        seq: s.seq,
        action: s.action,
        target: s.target,
        value: s.value,
        args: s.args,
        page: s.page,
        navigated: s.navigated,
        fragile: s.fragile,
        screenshot: s.screenshot,
        asserts: s.asserts,
      })),
    };
  }

  // Deterministic dual-layer Markdown. The prose is human-facing; the fenced
  // ```json maui-test block is the authoritative replay source.
  toMarkdown() {
    const j = this.toJSON();
    const L = [];
    L.push(`# Scenario: ${j.name}`);
    L.push("");
    L.push("<!-- Recorded by MAUI DevFlow Inspector. The fenced ```json maui-test block below is the source of");
    L.push("     truth for replay; edit the prose freely but keep that block valid. -->");
    L.push("");
    L.push(`- **App:** ${j.app || "(unknown)"}`);
    L.push(`- **Platform:** ${j.platform || "(unknown)"}`);
    L.push(`- **Recorded:** ${j.recordedAt || "(unknown)"}`);
    L.push(`- **Preconditions:** ${j.preconditions}`);
    L.push(`- **Steps:** ${j.steps.length}`);
    const fragile = j.steps.filter((s) => s.fragile).length;
    if (fragile) {
      L.push(`- **Warning:** ${fragile} step(s) use a fragile selector (no AutomationId). Add AutomationIds for durable tests.`);
    }
    L.push("");
    L.push("## Steps");
    L.push("");
    if (!j.steps.length) {
      L.push("_(no steps recorded)_");
    }
    for (const s of j.steps) {
      const flags = [];
      if (s.fragile) flags.push("fragile-selector");
      if (s.navigated) flags.push("page-changed");
      const suffix = flags.length ? `  _(${flags.join(", ")})_` : "";
      L.push(`${s.seq}. ${s.label}${suffix}`);
      for (const as of s.asserts || []) {
        if (as.kind === "propEquals") L.push(`   - Expect ${as.name} == "${as.expected}"`);
        else if (as.kind === "exists") L.push(`   - Expect target still present`);
        else if (as.kind === "routeIs") L.push(`   - Expect route ${as.expected}`);
        else if (as.kind === "pageChanged") L.push(`   - Note: screen changed`);
      }
    }
    L.push("");
    L.push("## Replay (machine-readable — source of truth)");
    L.push("");
    L.push("```json maui-test");
    L.push(JSON.stringify(j, null, 2));
    L.push("```");
    L.push("");
    if (j.steps.some((s) => s.screenshot)) {
      L.push("## Screenshots");
      L.push("");
      for (const s of j.steps) {
        if (s.screenshot) L.push(`- Step ${s.seq}: ![step ${s.seq}](${slugify(j.name)}/${s.screenshot})`);
      }
      L.push("");
    }
    return L.join("\n");
  }

  // ── Persistence ───────────────────────────────────────────────────────────────
  // Where tests land: the MAUI project's own `maui-tests/` dir. The agent reports `project`
  // as the .csproj FILE path (sometimes a dir), so normalize to the containing directory;
  // fall back to a user-scoped folder when the project can't be resolved.
  outputRoot(store) {
    const proj = store?.device?.resolvedAgent?.()?.project;
    if (proj && existsSync(proj)) {
      try {
        const dir = statSync(proj).isDirectory() ? proj : dirname(proj);
        if (dir) return join(dir, "maui-tests");
      } catch { /* fall through to user-scoped default */ }
    }
    const projectRoot = store?.device?.opts?.projectRoot;
    const projectName = proj ? basename(proj) : null;
    if (projectRoot && projectName && existsSync(projectRoot)) {
      const matches = findProjectFiles(projectRoot, projectName);
      if (matches.length === 1) return join(dirname(matches[0]), "maui-tests");
    }
    return join(homedir(), ".copilot", "maui-live-canvas", "tests");
  }

  resolveTestName(store, { name, file } = {}) {
    const root = resolve(this.outputRoot(store));
    let candidateName = null;

    if (typeof file === "string" && file.trim()) {
      const candidate = resolve(isAbsolute(file) ? file : join(root, file));
      const rel = relative(root, candidate);
      if (rel === ".." || rel.startsWith(`..${sep}`) || isAbsolute(rel) || dirname(candidate) !== root) {
        return { ok: false, error: "Test files must be top-level Markdown files inside the resolved maui-tests directory." };
      }
      if (existsSync(root) && existsSync(candidate)) {
        try {
          const realRoot = realpathSync.native(root);
          const realCandidate = realpathSync.native(candidate);
          if (dirname(realCandidate) !== realRoot) {
            return { ok: false, error: "Test files must be top-level Markdown files inside the resolved maui-tests directory." };
          }
        } catch (e) {
          return { ok: false, error: `Could not resolve test path: ${String(e?.message || e)}` };
        }
      }
      candidateName = basename(candidate);
    } else if (typeof name === "string" && name.trim()) {
      candidateName = name.trim();
      if (!extname(candidateName)) candidateName += ".md";
    }

    if (!candidateName ||
        candidateName.length > 255 ||
        basename(candidateName) !== candidateName ||
        extname(candidateName).toLowerCase() !== ".md") {
      return { ok: false, error: "Provide a top-level Markdown test name from the resolved maui-tests directory." };
    }

    return { ok: true, name: candidateName };
  }

  load(store, input = {}) {
    const selected = this.resolveTestName(store, input);
    if (!selected.ok) return selected;

    const root = resolve(this.outputRoot(store));
    const file = join(root, selected.name);
    if (!existsSync(file))
      return { ok: false, error: `Test not found: ${file}` };

    const confined = this.resolveTestName(store, { file });
    if (!confined.ok) return confined;
    try {
      const markdown = readFileSync(file, "utf8");
      if (Buffer.byteLength(markdown, "utf8") > RECORDING_MAX_BYTES)
        return { ok: false, error: "workflow test exceeds the 1 MiB limit" };
      return { ok: true, name: selected.name, file, markdown };
    } catch (e) {
      return { ok: false, error: `Could not read test: ${String(e?.message || e)}` };
    }
  }

  save(store) {
    if (!this.steps.length) return { ok: false, error: "Nothing recorded yet — start recording and perform some actions first." };
    const root = this.outputRoot(store);
    const slug = slugify(this.name);
    const shotDir = join(root, slug);
    try {
      mkdirSync(shotDir, { recursive: true });
      // Move staged screenshots next to the test.
      if (this._stageDir && existsSync(this._stageDir)) {
        for (const s of this.steps) {
          if (!s.screenshot) continue;
          const from = join(this._stageDir, s.screenshot);
          if (existsSync(from)) {
            try { copyFileSync(from, join(shotDir, s.screenshot)); } catch { /* best-effort */ }
          }
        }
      }
      const markdown = this.toMarkdown();
      if (Buffer.byteLength(markdown, "utf8") > RECORDING_MAX_BYTES) {
        return { ok: false, error: "recording exceeds the 1 MiB limit" };
      }
      const file = join(root, `${slug}.md`);
      writeNewFileAtomic(file, markdown);
      this._savedTo = file;
      return { ok: true, file, dir: shotDir, steps: this.steps.length, root };
    } catch (e) {
      return { ok: false, error: `Save failed: ${String(e?.message || e)}` };
    }
  }

  persist(store, { markdown, name } = {}) {
    const md = typeof markdown === "string" ? markdown : "";
    if (!md) return { ok: false, error: "no markdown" };
    if (Buffer.byteLength(md, "utf8") > RECORDING_MAX_BYTES) {
      return { ok: false, error: "recording exceeds the 1 MiB limit" };
    }

    const root = this.outputRoot(store);
    const file = join(root, `${slugify(name || "recording")}.md`);
    try {
      mkdirSync(root, { recursive: true });
      writeNewFileAtomic(file, md);
      return { ok: true, file, root };
    } catch (e) {
      return { ok: false, error: `Save failed: ${String(e?.message || e)}` };
    }

  }

  // List saved tests in the output root (for the list_tests agent action).
  list(store) {
    const root = this.outputRoot(store);
    if (!existsSync(root)) return { ok: true, root, tests: [] };
    try {
      const tests = readdirSync(root)
        .filter((f) => f.toLowerCase().endsWith(".md"))
        .map((f) => ({ name: f.replace(/\.md$/i, ""), file: join(root, f) }));
      return { ok: true, root, tests };
    } catch (e) {
      return { ok: false, root, error: String(e?.message || e) };
    }
  }

  _cleanupStage() {
    if (this._stageDir && existsSync(this._stageDir)) {
      try { rmSync(this._stageDir, { recursive: true, force: true }); } catch { /* ignore */ }
    }
    this._stageDir = null;
  }
}

function writeNewFileAtomic(file, content) {
  const temporary = `${file}.${process.pid}.${Date.now()}.tmp`;
  try {
    writeFileSync(temporary, content, { encoding: "utf8", flag: "wx" });
    linkSync(temporary, file);
  } finally {
    try { unlinkSync(temporary); } catch { /* best effort */ }
  }
}
