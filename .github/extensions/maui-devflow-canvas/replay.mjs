// replay.mjs — replay a recorded MAUI workflow test (.md) and verify it.
//
// Parses the authoritative ```json maui-test``` block from a recorded `.md` (see recorder.mjs),
// drives each step through the SAME LiveStore methods a human/agent uses, then evaluates the
// step's verifiable assertions (propEquals, exists) with a short poll to tolerate async nav.
// Returns a per-step pass/fail report. Report-only asserts (routeIs, pageChanged) never fail.
//
// This is the "validate the app" half of the record -> .md -> replay loop.

import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { slugify } from "./recorder.mjs";

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const msg = (e) => String(e?.message || e);

// ── Public entry ────────────────────────────────────────────────────────────────
// opts: { file? absolute .md path, name? scenario name, json? already-parsed test object }
export async function replayTest(store, opts = {}) {
  const test = loadTest(store, opts);
  if (!test.ok) return test;

  const results = [];
  let passed = 0;
  let failed = 0;
  await store.refresh().catch(() => {}); // fresh tree so selectors resolve
  for (const step of test.steps) {
    const res = await runStep(store, step);
    results.push(res);
    if (res.ok) passed++;
    else failed++;
  }

  return {
    ok: failed === 0,
    name: test.name,
    file: test.file || null,
    total: test.steps.length,
    passed,
    failed,
    results,
  };
}

// ── Load + parse ──────────────────────────────────────────────────────────────
function loadTest(store, { file, name, json, root }) {
  if (json && Array.isArray(json.steps)) {
    return { ok: true, name: json.name || name || "scenario", steps: json.steps };
  }
  let path = file || null;
  if (!path && name) {
    if (!root) return { ok: false, error: "The workflow test directory could not be resolved." };
    path = join(root, `${slugify(name)}.md`);
  }
  if (!path) return { ok: false, error: "Provide a test file path or a scenario name to replay." };
  if (!existsSync(path)) return { ok: false, error: `Test not found: ${path}` };

  let md;
  try {
    md = readFileSync(path, "utf8");
  } catch (e) {
    return { ok: false, error: `Could not read test: ${msg(e)}` };
  }
  const m = md.match(/```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/);
  if (!m) return { ok: false, error: "No ```json maui-test``` block found in the test file." };
  let parsed;
  try {
    parsed = JSON.parse(m[1]);
  } catch (e) {
    return { ok: false, error: `Invalid JSON in the maui-test block: ${msg(e)}` };
  }
  if (!Array.isArray(parsed.steps)) return { ok: false, error: "The maui-test block has no steps[]." };
  return { ok: true, name: parsed.name || name || "scenario", steps: parsed.steps, file: path };
}

// ── Per-step drive + verify ─────────────────────────────────────────────────────
async function runStep(store, step) {
  const r = { seq: step.seq, action: step.action, label: step.label || step.action, ok: true, asserts: [] };
  try {
    await drive(store, step);
  } catch (e) {
    r.ok = false;
    r.error = `drive failed: ${msg(e)}`;
    return r;
  }
  for (const as of step.asserts || []) {
    if (!as.verify) {
      r.asserts.push({ kind: as.kind, ok: true, skipped: true });
      continue;
    }
    const ok = await pollAssert(store, as);
    const entry = { kind: as.kind, ok };
    if (as.name) entry.name = as.name;
    if (as.expected != null) entry.expected = as.expected;
    r.asserts.push(entry);
    if (!ok) r.ok = false;
  }
  return r;
}

async function drive(store, step) {
  const a = step.args || {};
  switch (step.action) {
    case "tap":
      return store.tap(needSel(resolveSel(store, a.selector), "tap"));
    case "fill":
      return store.fill(needSel(resolveSel(store, a.selector), "fill"), a.text ?? "");
    case "setProperty": {
      // device.setProperty() takes a CONCRETE id (unlike tap/fill, which resolve selectors
      // internally), so resolve the durable selector to a live id first.
      const id = (await resolveToId(store, a.selector)) ?? (await resolveToId(store, targetToSelector(step.target)));
      return store.setProperty(needSel(id, "setProperty"), a.name || "Text", a.value ?? "");
    }
    case "scroll": {
      const opts = { ...a };
      // args.element is a stale live id; re-resolve from the step's durable selector.
      const id = await resolveToId(store, targetToSelector(step.target));
      if (id) opts.element = id;
      else delete opts.element;
      return store.scroll(opts);
    }
    case "navigate":
      return store.navigate(a.route);
    case "back":
      return store.back();
    case "setTheme":
      return store.setTheme(a.theme || "light");
    default:
      throw new Error(`unknown action: ${step.action}`);
  }
}

function needSel(sel, action) {
  if (!sel) throw new Error(`${action} target could not be resolved (selector missing or element not found)`);
  return sel;
}

// ── Assertions ────────────────────────────────────────────────────────────────
async function pollAssert(store, as, tries = 4, gapMs = 300) {
  for (let i = 0; i < tries; i++) {
    if (await evalAssert(store, as)) return true;
    await sleep(gapMs);
    await store.refresh({ info: false }).catch(() => {});
  }
  return false;
}

async function evalAssert(store, as) {
  try {
    if (as.kind === "propEquals") {
      const id = await resolveToId(store, as.selector);
      if (!id) return false;
      const r = await store.getProperty(id, as.name || "Text");
      if (!r || r.ok === false) return false;
      const val = r.value != null ? r.value : r.data;
      return String(val).trim() === String(as.expected).trim();
    }
    if (as.kind === "exists") {
      const id = await resolveToId(store, as.selector);
      return id != null;
    }
  } catch {
    return false;
  }
  return true; // unknown/non-verifiable => don't fail the step
}

// ── Selector resolution ─────────────────────────────────────────────────────────
// A recorded selector is one of: {automationId} | {text} | {typeIndex:{type,index}} | {id}.
// typeIndex is resolved to a concrete live id; the rest the device resolves itself.
function resolveSel(store, selector) {
  if (!selector) return null;
  if (selector.typeIndex) {
    const id = store._resolveTypeIndex(selector.typeIndex.type, selector.typeIndex.index);
    return id ? { id } : null;
  }
  if (selector.automationId) return { automationId: selector.automationId };
  if (selector.text) return { text: selector.text };
  if (selector.id != null) return { id: String(selector.id) };
  return null;
}

// Build a replay selector from a recorded step.target (durable-selector object).
function targetToSelector(t) {
  if (!t) return null;
  if (t.automationId) return { automationId: t.automationId };
  if (t.text) return { text: t.text };
  if (t.selectorKind === "typeIndex" && t.type && Number.isInteger(t.index)) {
    return { typeIndex: { type: t.type, index: t.index } };
  }
  if (t.id != null) return { id: String(t.id) };
  return null;
}

// Resolve any recorded selector to a concrete live id (or null if not present).
async function resolveToId(store, selector) {
  const sel = resolveSel(store, selector);
  if (!sel) return null;
  if (sel.id != null) return store.getElement(String(sel.id)) ? String(sel.id) : null;
  // Local index first (authoritative + fast; we refresh the tree each step).
  const local = findInIndex(store, sel);
  if (local) return local;
  // Then the device's own resolver (same path taps use), then a query.
  const dev = store.device;
  if (dev && typeof dev._resolveElementId === "function") {
    try {
      const id = await dev._resolveElementId(sel);
      if (id != null && store.getElement(String(id))) return String(id);
    } catch {
      /* fall through to query */
    }
  }
  const q = await store.query(sel).catch(() => null);
  const arr = normQuery(q);
  return arr.length ? String(arr[0].id) : null;
}

function findInIndex(store, sel) {
  const wantAid = sel.automationId != null ? String(sel.automationId) : null;
  const wantText = sel.text != null ? String(sel.text).trim() : null;
  for (const { el } of store._index.values()) {
    if (wantAid && el.automationId === wantAid) return String(el.id);
    if (wantText && el.text != null && String(el.text).trim() === wantText) return String(el.id);
  }
  return null;
}

function normQuery(q) {
  if (!q) return [];
  if (Array.isArray(q)) return q;
  if (Array.isArray(q.elements)) return q.elements;
  if (Array.isArray(q.results)) return q.results;
  if (q.element) return [q.element];
  return [];
}
