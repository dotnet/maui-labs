// selftest.mjs — end-to-end smoke test for the MAUI DevFlow Inspector Canvas adapter.
//
// Drives the SAME LiveStore/DevflowDevice the canvas uses, but from plain Node — so you can
// validate the whole bridge against a running app WITHOUT the Copilot app:
//
//   node selftest.mjs
//
// Prereqs: a DevFlow-enabled MAUI app is running (see README) and `maui` is on PATH (or set
// MAUI_CLI). Optionally pin an agent with MAUI_DEVFLOW_AGENT_PORT / _PLATFORM / _DEVICE.
//
// Exit code 0 = all critical steps passed; 1 = a critical step failed.

import { LiveStore } from "./store.mjs";
import { replayTest } from "./replay.mjs";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const opts = {};
if (process.env.MAUI_DEVFLOW_PLATFORM) opts.platform = process.env.MAUI_DEVFLOW_PLATFORM;
if (process.env.MAUI_DEVFLOW_DEVICE) opts.device = process.env.MAUI_DEVFLOW_DEVICE;
if (process.env.MAUI_DEVFLOW_AGENT_PORT) opts.agentPort = Number(process.env.MAUI_DEVFLOW_AGENT_PORT);

let failures = 0;
const line = (s) => process.stdout.write(s + "\n");
function check(name, cond, extra = "") {
  const ok = !!cond;
  if (!ok) failures++;
  line(`${ok ? "  \u2713 PASS" : "  \u2717 FAIL"}  ${name}${extra ? "  \u2014 " + extra : ""}`);
  return ok;
}

// Flatten helper.
function flatten(roots, out = []) {
  for (const el of roots || []) { out.push(el); if (el.children) flatten(el.children, out); }
  return out;
}

line("\n== MAUI DevFlow Inspector — Canvas adapter selftest ==\n");
line(`  targeting: ${JSON.stringify(opts.agentPort ? { agentPort: opts.agentPort } : "auto-resolve")}`);

const store = new LiveStore(opts);
let forcedLeaseHeld = false;
let leaseCleanupPromise = null;
async function releaseForcedLease() {
  if (!forcedLeaseHeld) return;
  forcedLeaseHeld = false;
  await store.device.releaseMutationLease().catch(() => {});
}
function emergencyReleaseAndExit(error) {
  if (error) console.error(error);
  leaseCleanupPromise ??= releaseForcedLease();
  leaseCleanupPromise.finally(() => process.exit(1));
}
process.once("uncaughtException", emergencyReleaseAndExit);
process.once("unhandledRejection", emergencyReleaseAndExit);

// 1) Resolve + connect + pull tree + screenshot.
line("\n[1] refresh() — resolve agent, pull tree + screenshot");
const snap = await store.refresh();
const port = store.device.whichPort();
const agent = store.device.resolvedAgent();
check("connected to a running agent", snap.connected, `agentPort=${port}`);
if (agent) line(`      agent: ${agent.appName} (${agent.platform}, ${agent.tfm}) @ port ${agent.port}`);
check("app info present", !!snap.info?.appName, `app=${snap.info?.appName} platform=${snap.info?.platform}`);
check("window size known", (snap.info?.window?.width || 0) > 0,
  `${Math.round(snap.info?.window?.width)}\u00d7${Math.round(snap.info?.window?.height)} @${snap.info?.density}x`);
const forceLease = process.env.MAUI_DEVFLOW_FORCE_LEASE === "1";
if (forceLease) {
  const lease = await store.device.claimMutationLease(true);
  forcedLeaseHeld = lease?.youHold === true;
  check("selftest took control of the mutation lease", lease?.youHold === true,
    lease?.label || lease?.error || lease?.holderKind || "no lease status");
}

const all = flatten(snap.roots);
check("visual tree non-empty", all.length > 1, `${all.length} elements`);
check("screenshot captured", !!store.currentShotPath(), store.currentShotPath() || "(none)");

// 2) absBounds computed (the overlay depends on this).
line("\n[2] absBounds accumulation (overlay coordinates)");
const withAbs = all.filter((e) => e.absBounds && (e.absBounds.width > 0));
check("elements carry absBounds", withAbs.length > 0, `${withAbs.length} sized elements`);
const sample = withAbs.find((e) => e.type && /Button|Label|Entry|Editor/.test(e.type)) || withAbs[0];
if (sample) {
  const a = sample.absBounds;
  line(`      sample ${sample.type} #${sample.id} absBounds=(${Math.round(a.x)},${Math.round(a.y)}) ${Math.round(a.width)}\u00d7${Math.round(a.height)}`);
}

// 3) Hit-test a fixed on-screen point and verify self-consistency: the returned element's
//    authoritative windowBounds should contain the hit point (independent of accumulation).
line("\n[3] hit-test (authoritative windowBounds, self-consistent)");
{
  const px = Math.round((snap.info?.window?.width || 411) * 0.5);
  const py = Math.round((snap.info?.window?.height || 914) * 0.5);
  const hit = await store.hitTestSelect(px, py);
  check("hit-test returned an element", !!hit, hit ? `${hit.type} #${hit.id}` : "(none)");
  const wb = hit?.windowBounds;
  check("selected element carries authoritative windowBounds", !!wb,
    wb ? `(${Math.round(wb.x)},${Math.round(wb.y)}) ${Math.round(wb.width)}\u00d7${Math.round(wb.height)}` : "(none)");
  if (wb) {
    const contains = px >= wb.x && px <= wb.x + wb.width && py >= wb.y && py <= wb.y + wb.height;
    check("windowBounds contains the hit point", contains, `point (${px},${py})`);
    // Deepest-first ordering: a content point should resolve to a SPECIFIC control, not the
    // full-window root shell. Guards against picking els[last] (the root) by mistake.
    const winArea = (snap.info?.window?.width || 411) * (snap.info?.window?.height || 914);
    const elArea = wb.width * wb.height;
    check("hit-test picked a specific (non-root) element", hit.type !== "AppShell" && elArea < winArea * 0.95,
      `${hit.type} covers ${((elArea / winArea) * 100).toFixed(0)}% of window`);
  }
}

// 4) Read a property.
line("\n[4] get_property (read)");
// Pick a target whose Text ROUND-TRIPS through the agent. Some controls (e.g. certain Android
// Buttons) surface text in the visual tree but not via the property bag — getProperty returns
// undefined and set-property is a no-op. Those make misleading edit targets, so prefer an
// Entry/Editor/Label whose read-back matches the tree text; fall back to the old heuristic.
async function pickTarget(cands) {
  const stableFirst = (items) => [
    ...items.filter((e) => e.automationId),
    ...items.filter((e) => !e.automationId),
  ];
  const ordered = [
    ...stableFirst(cands.filter((e) => e.type === "Entry" || e.type === "Editor")),
    ...stableFirst(cands.filter((e) => e.type === "Label" && e.text)),
    ...stableFirst(cands.filter((e) => e.type === "Button" && e.text)),
    ...stableFirst(cands.filter((e) => e.text)),
  ];
  const seen = new Set();
  for (const e of ordered) {
    if (seen.has(e.id)) continue;
    seen.add(e.id);
    try {
      const r = await store.getProperty(e.id, "Text");
      if (r && r.ok && r.value != null && String(r.value) === String(e.text ?? "")) return e;
    } catch { /* try the next candidate */ }
  }
  return cands.find((e) => e.text) || null; // graceful fallback (may not round-trip)
}
const target = await pickTarget(all);
if (target) {
  line(`      target: ${target.type} #${target.id} text="${target.text}"`);
  const read = await store.getProperty(target.id, "Text");
  check("read Text property", read.ok, read.ok ? `Text="${read.value}"` : read.error);
} else {
  check("found an element with text to read", false, "no text-bearing element in tree");
}

// 5) Live edit + verify (the headline capability). Restores the original value afterward.
line("\n[5] apply_and_verify (live edit round-trip)");
if (target) {
  const original = target.text ?? "";
  const probe = original + " (edited)";
  const res = await store.applyAndVerify(target.id, "Text", probe);
  check("set-property succeeded", res.ok, res.error || "");
  check("value verified via read-back", res.verified === true, `expected="${res.expected}" actual="${res.actual}"`);
  // Restore.
  const restore = await store.applyAndVerify(target.id, "Text", original);
  check("restored original text", restore.verified === true, `back to "${original}"`);
} else {
  check("had a target for live edit", false);
}

// 6) Snapshot is JSON-serializable and leaks no local paths.
line("\n[6] snapshot hygiene");
const finalSnap = store.snapshot();
let serialized = "";
try { serialized = JSON.stringify(finalSnap); } catch { /* */ }
check("snapshot serializes to JSON", serialized.length > 0, `${(serialized.length / 1024).toFixed(0)} KB`);
check("snapshot omits local file paths", !serialized.includes(".png") && !/[A-Za-z]:\\\\/.test(serialized));
check("timeline recorded actions", (finalSnap.timeline || []).length > 0, `${(finalSnap.timeline || []).length} events`);

// 7) Live push — subscribe to the agent event stream and confirm an in-app change is PUSHED
//    (treeChange), not merely discovered by polling. This is the channel that makes canvas
//    updates instant. NON-FATAL: agents predating /ws/v1/ui/events fall back to polling.
//    Runs BEFORE the multi-byte probe below, which can wedge an unfixed agent's accept loop.
line("\n[7] live push (WS /ws/v1/ui/events, non-fatal)");
if (target) {
  const events = [];
  let connected = false;
  const stream = store.device.openEventStream(
    (ev) => events.push(ev),
    (st) => { if (st.connected) connected = true; }
  );
  for (let i = 0; i < 20 && !connected; i++) await new Promise((r) => setTimeout(r, 100));
  if (connected) {
    const original = target.text ?? "";
    await store.device.setProperty(target.id, "Text", original + " \u00b7"); // provoke a treeChange
    for (let i = 0; i < 20 && !events.some((e) => e.type === "treeChange"); i++) {
      await new Promise((r) => setTimeout(r, 100));
    }
    await store.device.setProperty(target.id, "Text", original).catch(() => {});
    const got = events.filter((e) => e.type === "treeChange").length;
    if (got > 0) {
      check("agent pushed a treeChange over WS", true,
        `${events.length} event(s); types: ${[...new Set(events.map((e) => e.type))].join(", ")}`);
    } else {
      line(`  \u26a0 connected but no treeChange within 2s (events: ${events.map((e) => e.type).join(", ") || "none"})`);
    }
    // Also confirm a themeChange is PUSHED — our _onAgentEvent routes it to a live-sync refresh,
    // so an in-app / external theme flip reflects instantly instead of waiting for the safety poll.
    const themeBefore = await store.device.themeGet();
    const effBefore = themeBefore?.data?.effectiveTheme || themeBefore?.data?.theme;
    const userBefore = themeBefore?.data?.userAppTheme || "system";
    if (effBefore) {
      const flipTo = String(effBefore).toLowerCase() === "dark" ? "light" : "dark";
      const themeSeen = () => events.filter((e) => e.type === "themeChange").length;
      const before = themeSeen();
      await store.device.themeSet(flipTo);
      for (let i = 0; i < 20 && themeSeen() === before; i++) await new Promise((r) => setTimeout(r, 100));
      if (themeSeen() > before) check("agent pushed a themeChange over WS", true, `flipped ${effBefore}\u2192${flipTo}`);
      else line(`  \u26a0 no themeChange frame within 2s \u2014 theme flips fall back to the safety poll`);
      await store.device.themeSet(userBefore).catch(() => {}); // restore original theme setting
    }
  } else {
    line("  \u26a0 KNOWN ISSUE  agent did not accept a WS event-stream connection \u2014 live-sync falls back to polling.");
  }
  try { stream.close(); } catch { /* */ }
}

// 8) Platform picker — list running agents, and switch between them if more than one is up.
line("\n[8] platform picker (list agents + switch)");
const agents = await store.listAgents();
check("listAgents returns at least one agent", agents.length >= 1, `${agents.length} agent(s)`);
check("exactly one agent marked active", agents.filter((a) => a.active).length === 1,
  agents.map((a) => `${a.platform}:${a.port}${a.active ? "*" : ""}`).join(" "));
const startPort = store.snapshot().activePort;
const other = agents.find((a) => a.port !== startPort);
if (other) {
  const sw = await store.selectAgent({ port: other.port, platform: other.platform });
  check("selectAgent switched active port", sw.activePort === other.port, `now ${sw.info?.platform} @ ${sw.activePort}`);
  check("switched agent pulled a visual tree", (sw.roots || []).length > 0 && sw.connected, `${flatten(sw.roots).length} elements`);
  const backSnap = await store.selectAgent({ port: startPort });
  check("switched back to the original agent", backSnap.activePort === startPort, `back to ${backSnap.info?.platform} @ ${backSnap.activePort}`);
} else {
  line("  \u2139 only one agent running — switch test skipped (run an Android + Windows build together to exercise it)");
}

// 9) Selection → Copilot context — the exact payload our "Attach to Copilot" attachment carries.
//    The host composer pill can't be observed headless, so we validate the DATA CONTRACT
//    (store.selectionContext() IS what becomes the bounded text payload) and that the
//    extension's push wiring is present in source (feature-detected SDK call).
line("\n[9] selection \u2192 Copilot context (composer attachment payload)");
if (target) {
  store.select(target.id);
  const sel = store.selectionContext();
  check("selectionContext resolves the selected id", String(sel.selectedId) === String(target.id),
    `selectedId=${sel.selectedId}`);
  check("selectionContext has a one-line summary", typeof sel.summary === "string" && sel.summary.length > 0,
    sel.summary || "(none)");
  check("selectionContext.element carries id + type", !!(sel.element && sel.element.id && sel.element.type),
    sel.element ? `${sel.element.type} #${sel.element.id}` : "(none)");
  check("selectionContext exposes app { name, platform }", !!(sel.app && sel.app.platform),
    sel.app ? `${sel.app.name} / ${sel.app.platform}` : "(none)");
  check("selectionContext lists suggested actions", Array.isArray(sel.suggestedActions) && sel.suggestedActions.length > 0,
    `${(sel.suggestedActions || []).length} action(s)`);
  const title = `MAUI selection \u00b7 ${sel.summary}`;
  const payload = { ...sel, capturedAt: new Date().toISOString() };
  let attachmentText = "";
  try {
    attachmentText = JSON.stringify({ kind: "mauiDevFlowContext", title, payload }, null, 2);
  } catch { /* */ }
  const attachmentData = Buffer.from(attachmentText, "utf8").toString("base64");
  check("context attachment is JSON-serializable + carries the id",
    attachmentText.length > 0 && attachmentText.includes(String(target.id)),
    `${(attachmentText.length / 1024).toFixed(1)} KB`);
  check("context attachment base64 round-trips as UTF-8 text",
    Buffer.from(attachmentData, "base64").toString("utf8") === attachmentText);

  // Nothing-selected path → a friendly, non-throwing hint the button/agent can surface.
  store.select("__no_such_element__");
  const none = store.selectionContext();
  check("empty selection yields { selectedId:null, hint }", none.selectedId === null && !!none.hint);
  store.select(target.id); // restore selection for later steps
} else {
  check("had a selectable target for the context payload", false);
}
// Source wiring — the host push path exists and feature-detects the SDK API (asserted even offline).
{
  let src = "";
  try { src = readFileSync(new URL("./extension.mjs", import.meta.url), "utf8"); } catch { /* */ }
  check("extension registers an attach_selection action", /name:\s*"attach_selection"/.test(src));
  check("/control handles the attachSelection case", /case\s+"attachSelection"/.test(src));
  check("/control handles the generic attachCopilot case", /case\s+"attachCopilot"/.test(src));
  check("/control handles the attachData case", /case\s+"attachData"/.test(src));
  check("Data context is bounded before push", /safe context size/.test(src));
  let shell = "";
  try { shell = readFileSync(new URL("./shell.mjs", import.meta.url), "utf8"); } catch { /* */ }
  check("Data attachment returns a host acknowledgement", /devflow:hostResult/.test(shell));
  check("Canvas advertises copilotContext capability", /copilotContext/.test(shell));
  check("Canvas relays devflow:attachCopilot with acknowledgement", /devflow:attachCopilot/.test(shell)
    && /action:\s*'attachCopilot'/.test(shell));
  check("push uses a standard blob attachment", /type:\s*"blob"/.test(src));
  check("push labels the blob as text/plain", /mimeType:\s*"text\/plain"/.test(src));
  check("push base64-encodes the bounded UTF-8 context", /toString\("base64"\)/.test(src));
  check("push targets session.rpc.extensions.sendAttachmentsToMessage", /sendAttachmentsToMessage/.test(src));
  check("push feature-detects old runtimes (unsupported_runtime)", /unsupported_runtime/.test(src));
  check("push bounds selection and Data context before encoding", /CONTEXT_ATTACHMENT_MAX_BYTES/.test(src)
    && /Buffer\.byteLength\(content,\s*"utf8"\)/.test(src));
  // The attachment remains text-only: no automatic image payload, and every push/capability
  // handler is time-boxed. Placeholders remain ASCII for host compatibility.
  check("attach does not auto-push an image blob", !/mimeType:\s*["']image\//.test(src));
  check("attach push is time-boxed (withTimeout)", /withTimeout\s*\(/.test(src) && /"Attach-to-Copilot"/.test(src));
  check("every capability handler is time-boxed (wrapActions)", /actions:\s*wrapActions\(\[/.test(src) && /function wrapActions/.test(src));
  let storeSrc = "";
  try { storeSrc = readFileSync(new URL("./store.mjs", import.meta.url), "utf8"); } catch { /* */ }
  check("store keeps elementShot() for on-demand pull", /elementShot\s*\(/.test(storeSrc));
  check("suggestedActions use ASCII placeholders (no multibyte)", !/"\u2026"/.test(storeSrc) && /<new value>/.test(storeSrc));
}

// 10) Theme-settle race guard and snapshot revision ordering.
//     Source checks are offline-safe; the live sub-checks exercise the epoch/settle/rev path
//     against the running agent (flip theme, confirm the settled frame + monotonic rev).
line("\n[10] theme-settle · rev guard");
{
  let storeSrc = "";
  try { storeSrc = readFileSync(new URL("./store.mjs", import.meta.url), "utf8"); } catch { /* */ }
  // store.mjs — theme epoch + settle loop + shot fingerprint + rev in the snapshot.
  check("store has a theme epoch counter", /_themeEpoch/.test(storeSrc));
  check("store runs a theme-settle loop", /_settleThemeShot\s*\(/.test(storeSrc));
  check("store fingerprints screenshots (settle stability)", /hashBytes\s*\(/.test(storeSrc) && /_lastShotHash/.test(storeSrc));
  check("snapshot() carries a monotonic rev", /rev:\s*this\._rev/.test(storeSrc));
  // Live: rev strictly increases across an emit.
  const rev0 = store.snapshot().rev;
  await store.refresh({ info: false });
  const rev1 = store.snapshot().rev;
  check("snapshot rev increments across a refresh", typeof rev1 === "number" && rev1 > rev0, `${rev0} \u2192 ${rev1}`);

  // Live: theme-settle — flip, then confirm the store emits a settled snapshot whose theme
  // matches the request (proves the epoch-guarded recapture publishes the correct frame).
  const th = await store.device.themeGet();
  const eff = th?.data?.effectiveTheme || th?.data?.theme;
  const userBefore = th?.data?.userAppTheme || "system";
  if (eff) {
    const want = String(eff).toLowerCase() === "dark" ? "light" : "dark";
    const seenThemes = [];
    let sawWant = false, maxRev = store.snapshot().rev;
    const unsub = store.subscribe((s) => {
      seenThemes.push(s.info?.theme);
      if (String(s.info?.theme).toLowerCase() === want) sawWant = true;
      if (typeof s.rev === "number") maxRev = Math.max(maxRev, s.rev);
    });
    const shotBefore = store.snapshot().shotSeq;
    await store.setTheme(want);
    // Give the settle loop time to converge (delays cap ~2.5s).
    for (let i = 0; i < 30 && !(sawWant && store.snapshot().shotSeq !== shotBefore); i++) {
      await new Promise((r) => setTimeout(r, 100));
    }
    try { unsub(); } catch { /* */ }
    const finalTheme = String(store.snapshot().info?.theme).toLowerCase();
    check("setTheme drives the store to the requested theme", finalTheme === want, `\u2192 ${finalTheme}`);
    check("theme-settle emitted a matching frame", sawWant, `themes seen: ${[...new Set(seenThemes)].join(", ")}`);
    check("theme change advanced the screenshot + rev", store.snapshot().shotSeq !== shotBefore && maxRev > rev1,
      `shotSeq ${shotBefore}\u2192${store.snapshot().shotSeq}, rev\u2264${maxRev}`);
    // Restore the app's original theme setting (mirrors step [7]).
    await store.setTheme(userBefore).catch(() => {});
  } else {
    line("  \u2139 theme unavailable on this agent \u2014 live theme-settle check skipped");
  }
}

// 11) Workflow recorder — record → .md → replay-to-verify.
//     Source checks are offline-safe; the live sub-checks drive ONE durable action through the
//     same store path the canvas uses, save a real .md, then replay it against the running app and
//     assert every step + auto-assertion passes. Runs BEFORE the multi-byte probe (which can wedge
//     an unfixed agent). Generated test artifacts are cleaned up afterward.
line("\n[11] workflow recorder — record → .md → replay");
{
  let recSrc = "", repSrc = "", storeSrc = "", extSrc = "";
  try { recSrc = readFileSync(new URL("./recorder.mjs", import.meta.url), "utf8"); } catch { /* */ }
  try { repSrc = readFileSync(new URL("./replay.mjs", import.meta.url), "utf8"); } catch { /* */ }
  try { storeSrc = readFileSync(new URL("./store.mjs", import.meta.url), "utf8"); } catch { /* */ }
  try { extSrc = readFileSync(new URL("./extension.mjs", import.meta.url), "utf8"); } catch { /* */ }

  check("recorder.mjs exports Recorder + RECORDABLE set", /export\b[\s\S]*Recorder/.test(recSrc) && /RECORDABLE/.test(recSrc));
  check("store hooks recording into every mutation (_recordAction)", /_recordAction\s*\(/.test(storeSrc));
  check("store resolves durable selectors (_bestSelector)", /_bestSelector\s*\(/.test(storeSrc));
  check("store detects navigation by page label (_pageSignature)", /_pageSignature\s*\(/.test(storeSrc));
  check("snapshot() surfaces recording + recorder state", /recording:/.test(storeSrc) && /recorder:/.test(storeSrc));
  check("replay.mjs exports replayTest", /export\s+async\s+function\s+replayTest/.test(repSrc));
  check("extension exposes record.* control verbs", /record\.start/.test(extSrc) && /record\.save/.test(extSrc) && /"replay"/.test(extSrc));
  check("extension registers recorder agent actions", /start_recording/.test(extSrc) && /replay_test/.test(extSrc) && /list_tests/.test(extSrc));
  // Live: record one durable edit, save a real .md, replay it, assert all steps pass.
  if (target && snap.connected) {
    const originalText = target.text ?? "";
    const probeVal = "RecTest" + String(Date.now()).slice(-5);
    let recordingActive = false;
    let tempRoot = null;
    try {
      const started = await store.device.recordingStart({
        name: "selftest-recorder",
        preconditions: "Selftest recorded scenario.",
      });
      store._recordingStatus = started;
      recordingActive = started?.recording === true;
      check("broker recorder is active after start", started?.ok === true && recordingActive,
        started?.error || started?.recordingId || "no recording status");

      // Drive a durable text edit through the SAME store method the canvas uses.
      await store.applyAndVerify(target.id, "Text", probeVal);
      const status = await store.device.recordingStatus();
      store._recordingStatus = status;
      check("captured >= 1 step from a live edit", Number(status?.steps) >= 1, `${status?.steps || 0} step(s)`);

      const stopped = await store.device.recordingStop(status?.recordingId || started?.recordingId);
      recordingActive = false;
      store._recordingStatus = stopped;
      const mdText = typeof stopped?.markdown === "string" ? stopped.markdown : "";
      check("stop returned a replayable Markdown test", stopped?.ok === true && /```json maui-test/.test(mdText),
        stopped?.error || `${stopped?.steps || 0} step(s)`);
      const block = mdText.match(/```json maui-test\s*\r?\n([\s\S]*?)\r?\n```/);
      let flow = null;
      try { flow = block ? JSON.parse(block[1]) : null; } catch { /* asserted below */ }
      const step0 = flow?.steps?.[0] || {};
      const selector = step0.target || step0.args?.selector || {};
      check("step carries a durable selector",
        !!(selector.automationId || selector.text || selector.typeIndex || selector.id),
        JSON.stringify(selector));
      // Regression guard: a text edit must NOT capture the element by the very text we just wrote
      // (that selector is circular — it can't be resolved from a clean state on replay).
      check("selector avoids the circular just-written text",
        String(selector.text || "") !== probeVal,
        JSON.stringify(selector));
      check("step auto-generated a verifiable assertion",
        (step0.asserts || []).some((a) => a.verify), `${(step0.asserts || []).length} assert(s)`);

      // Persist the broker-returned Markdown to a temporary file, matching the extension's
      // persistRecording path without leaving a test artifact in the user's project.
      tempRoot = mkdtempSync(join(tmpdir(), "maui-broker-rec-"));
      const testFile = join(tempRoot, "selftest-recorder.md");
      writeFileSync(testFile, mdText, "utf8");
      check("broker Markdown was persisted for replay", readFileSync(testFile, "utf8") === mdText, testFile);

      // Replay the saved test against the live app.
      if (mdText) {
        const report = await replayTest(store, { file: testFile });
        check("replay executed the recorded steps", !!(report && Array.isArray(report.results) && report.results.length >= 1),
          report && report.error ? report.error : (report ? `${report.results?.length || 0} step result(s)` : "(no report)"));
        check("replay: all steps + asserts passed", !!(report && report.ok === true),
          report ? `${report.passed}/${report.total} passed` : "(no report)");
      }
    } finally {
      // Restore the app + remove the generated test artifacts (keep the tree clean).
      if (recordingActive) await store.device.recordingCancel(store._recordingStatus?.recordingId).catch(() => {});
      await store.applyAndVerify(target.id, "Text", originalText).catch(() => {});
      store._recordingStatus = null;
      if (tempRoot) try { rmSync(tempRoot, { recursive: true, force: true }); } catch { /* */ }
    }
  } else {
    line("  \u2139 no editable target / not connected \u2014 live record\u2192replay skipped");
  }
}

// 12) Hybrid host shells — VS Code + Canvas theme/profile handshake and the disconnected fallback.
//     Offline-safe source checks only (no live agent needed): confirms the runtime fallback is the
//     lightweight hybrid `renderDisconnected` shell, that both host shells
//     send a `profile` object alongside `devflow:host`, and that the VS Code THEME_MAP covers the
//     semantic/high-contrast tokens the shared inspector's THEME_VARS whitelist accepts.
line("\n[12] hybrid host shells — theme/profile handshake · disconnected fallback");
{
  let extSrc = "", shellSrc = "", vscodeSrc = "";
  try { extSrc = readFileSync(new URL("./extension.mjs", import.meta.url), "utf8"); } catch { /* */ }
  try { shellSrc = readFileSync(new URL("./shell.mjs", import.meta.url), "utf8"); } catch { /* */ }
  try {
    vscodeSrc = readFileSync(new URL("../../../src/DevFlow/js/vscode-inspector/src/extension.ts", import.meta.url), "utf8");
  } catch { /* the vscode-inspector package may not be checked out in every clone */ }

  // extension.mjs — renderDisconnected (shell.mjs) is the runtime fallback.
  check("extension.mjs imports renderDisconnected from shell.mjs", /renderDisconnected/.test(extSrc) && /from\s+["']\.\/shell\.mjs["']/.test(extSrc));
  check("the '/' handler falls back to renderDisconnected", /renderDisconnected\s*\(/.test(extSrc));

  // shell.mjs — renderDisconnected exists, self-heals via /inspector-ready, shares the hybrid tokens.
  check("shell.mjs exports renderDisconnected", /export\s+function\s+renderDisconnected/.test(shellSrc));
  check("renderDisconnected polls /inspector-ready and reloads", /inspector-ready/.test(shellSrc) && /location\.reload/.test(shellSrc));
  check("renderDisconnected uses the shared --df-* token language", /--df-bg/.test(shellSrc) && /--df-accent/.test(shellSrc));
  // shell.mjs — devflow:host carries a profile with surface/contrast/reducedMotion/font, and a real
  // (non-light/dark-only) palette sourced from Primer/Copilot vars with a literal fallback.
  check("canvas devflow:host includes a profile object", /type:\s*'devflow:host'[\s\S]{0,400}profile:\s*buildProfile\s*\(\)/.test(shellSrc));
  check("canvas profile reports surface: 'side-panel'", /surface:\s*'side-panel'/.test(shellSrc));
  check("canvas theme sends a palette (not just light/dark mode)", /buildPalette\s*\(/.test(shellSrc) && /PRIMER_MAP/.test(shellSrc));
  check("canvas palette has a literal Primer fallback", /PRIMER_FALLBACK/.test(shellSrc));

  // VS Code host shell — profile + extended THEME_MAP (best-effort; skipped if not checked out).
  if (vscodeSrc) {
    check("vscode devflow:host includes a profile object", /hostKind:\s*'vscode'[\s\S]{0,200}profile:\s*buildProfile\s*\(\)/.test(vscodeSrc));
    check("vscode profile reports surface: 'editor'", /surface:\s*'editor'/.test(vscodeSrc));
    check("vscode profile detects high-contrast themes", /contrast\s*=\s*'high'/.test(vscodeSrc) || /profile\.contrast/.test(vscodeSrc));
    for (const tok of ["--df-type", "--df-name", "--df-source", "--df-success", "--df-outline-hover", "--df-outline-select"]) {
      check(`vscode THEME_MAP covers ${tok}`, vscodeSrc.includes(`'${tok}'`));
    }
    check("vscode package.json documents mauiDevflow.openLocation", (() => {
      try {
        const pkg = JSON.parse(readFileSync(new URL("../../../src/DevFlow/js/vscode-inspector/package.json", import.meta.url), "utf8"));
        const prop = pkg?.contributes?.configuration?.properties?.["mauiDevflow.openLocation"];
        return !!prop && prop.default === "auto" && Array.isArray(prop.enum) && ["auto", "beside", "active"].every((v) => prop.enum.includes(v));
      } catch { return false; }
    })());
  } else {
    line("  \u2139 vscode-inspector source not found relative to this checkout \u2014 vscode-side checks skipped");
  }
}

// 13) Multi-byte UTF-8 edit — DevFlow agent compatibility probe (non-fatal, runs last).
//     The probe confirms that byte-based Content-Length handling accepts non-ASCII edits without
//     hanging. It remains last because an incompatible running agent can block its request loop.
line("\n[13] multi-byte UTF-8 edit (agent capability, non-fatal)");
if (process.env.MAUI_SELFTEST_SKIP_MULTIBYTE) {
  line("  \u2139 skipped (MAUI_SELFTEST_SKIP_MULTIBYTE set) \u2014 avoids wedging an unfixed agent before a live demo");
} else if (target) {
  const original = target.text ?? "";
  const probe = original + " \u2713";
  const res = await store.applyAndVerify(target.id, "Text", probe);
  if (res.verified) {
    check("multi-byte edit round-trips (agent has the fix)", true, `set "${probe}"`);
    await store.applyAndVerify(target.id, "Text", original);
  } else {
    line(`  \u26a0 KNOWN ISSUE  multi-byte body not supported by this agent \u2014 ${res.error || res.stage || "timeout"}`);
    line("               Fixed in maui-labs AgentHttpServer; rebuild/redeploy the app's DevFlow agent to enable.");
    await store.applyAndVerify(target.id, "Text", original).catch(() => {});
  }
}

await releaseForcedLease();
line(`\n== ${failures === 0 ? "ALL CHECKS PASSED \u2713" : failures + " CHECK(S) FAILED \u2717"} ==\n`);
process.exit(failures === 0 ? 0 : 1);
