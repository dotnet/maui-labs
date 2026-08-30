import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

import {
  PROTOTYPE_STUDY_EVENT_KINDS,
  PROTOTYPE_STUDY_MAX_EVENTS,
  createPrototypeStudyJournal,
  renderPrototypeStudyEvidenceCard,
} from "../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-study.js";

const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const webRoot = resolve(sourceRoot, "Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web");
const read = (file) => readFileSync(resolve(webRoot, file), "utf8");

class MemorySessionStorage {
  #values = new Map();

  getItem(key) {
    return this.#values.get(key) ?? null;
  }

  setItem(key, value) {
    this.#values.set(key, String(value));
  }

  removeItem(key) {
    this.#values.delete(key);
  }
}

class FakeElement {
  constructor(ownerDocument, tagName) {
    this.ownerDocument = ownerDocument;
    this.tagName = tagName;
    this.children = [];
    this.attributes = new Map();
    this.listeners = new Map();
    this.className = "";
    this.textContent = "";
    this.hidden = false;
    this.type = "";
  }

  append(...children) {
    this.children.push(...children);
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  getAttribute(name) {
    return this.attributes.get(name) ?? null;
  }

  addEventListener(name, listener) {
    this.listeners.set(name, listener);
  }

  click() {
    this.listeners.get("click")?.();
  }
}

class FakeDocument {
  createElement(tagName) {
    return new FakeElement(this, tagName);
  }
}

function findElement(root, predicate) {
  if (predicate(root)) return root;
  for (const child of root.children || []) {
    const found = findElement(child, predicate);
    if (found) return found;
  }
  return null;
}

function clock(start = 0) {
  let now = start;
  return {
    now: () => now,
    advance(milliseconds) {
      now += milliseconds;
    },
  };
}

function journalOptions(time, storage = new MemorySessionStorage(), maxEventCount = PROTOTYPE_STUDY_MAX_EVENTS) {
  let nextId = 0;
  return {
    storage,
    maxEventCount,
    now: time.now,
    randomId: () => `opaque-${++nextId}`,
  };
}

test("study journal allows only value-free event kinds and rejects sensitive input keys", () => {
  const time = clock();
  const journal = createPrototypeStudyJournal(journalOptions(time));

  assert.equal(journal.record("workbench-opened", { provenance: "human" }), true);
  assert.equal(journal.record("not-an-event", {}), false);
  assert.equal(journal.record("goal-defined", { goal: "Buy a surprise present" }), false);
  assert.equal(journal.record("test-saved", {
    stepCount: 2,
    selector: "#private-payment",
  }), false);
  assert.equal(journal.record("assertion-added", {
    hard: true,
    selectorQuality: "durable",
    provenance: "human",
  }), true);

  const exported = journal.exportEvidence();
  assert.equal(exported.schema, "maui-devflow-prototype-study");
  assert.equal(exported.kind, "local-session-evidence");
  assert.equal(exported.version, 1);
  assert.equal(exported.localSessionOnly, true);
  assert.match(exported.session.id, /^local-/);
  assert.equal(exported.session.maxEventCount, PROTOTYPE_STUDY_MAX_EVENTS);
  assert.equal(exported.session.retention, "sessionStorage-current-tab");
  assert.deepEqual(exported.events.map((event) => event.kind), ["workbench-opened", "assertion-added"]);
  assert.deepEqual(PROTOTYPE_STUDY_EVENT_KINDS, [
    "workbench-opened", "goal-defined", "recording-started", "recording-stopped",
    "assertion-added", "test-saved", "run-started", "run-terminal", "results-opened",
    "improve-scanned", "agent-requested", "agent-approved", "agent-rejected",
    "agent-expired", "agent-stale", "agent-consumed",
    "repair-proposed", "repair-approved", "repair-rejected",
    "repair-applied", "repair-verified", "repair-rollback-required",
    "repair-rollback-failed", "repair-reverted",
  ]);
});

test("study journal bounds retained events and deduplicates terminal polling snapshots", () => {
  const time = clock();
  const journal = createPrototypeStudyJournal(journalOptions(time, new MemorySessionStorage(), 3));

  assert.equal(journal.record("run-started", { runId: "run-private-1", stepCount: 2 }), true);
  time.advance(10);
  assert.equal(journal.record("run-terminal", {
    runId: "run-private-1",
    state: "passed",
    durationMs: 10,
  }), true);
  assert.equal(journal.record("run-terminal", {
    runId: "run-private-1",
    state: "passed",
    durationMs: 10,
  }), false);
  assert.equal(journal.record("results-opened", { runId: "run-private-1", state: "passed" }), true);
  assert.equal(journal.record("results-opened", { runId: "run-private-1", state: "passed" }), false);

  time.advance(10);
  assert.equal(journal.record("workbench-opened", { provenance: "human" }), true);
  assert.equal(journal.exportEvidence().events.length, 3);
  assert.equal(journal.summary().storage.droppedEventCount, 1);
});

test("study summary calculates authoring, selector, replay, diagnosis, repair, and Improve metrics", () => {
  const time = clock();
  const journal = createPrototypeStudyJournal(journalOptions(time));

  journal.record("workbench-opened", { provenance: "human" });
  journal.record("agent-requested", { approvalRequestId: "approval-private", provenance: "agent" });
  time.advance(100);
  journal.record("agent-approved", { approvalRequestId: "approval-private", provenance: "human" });
  journal.record("goal-defined", { provenance: "human" });
  time.advance(100);
  journal.record("recording-started", { provenance: "human" });
  time.advance(1000);
  journal.record("recording-stopped", { stepCount: 3, provenance: "human" });
  time.advance(300);
  journal.record("assertion-added", { hard: true, selectorQuality: "durable", provenance: "human" });
  journal.record("test-saved", {
    stepCount: 3,
    hardAssertionCount: 2,
    durableSelectorCount: 3,
    fragileSelectorCount: 1,
    provenance: "agent",
  });
  time.advance(500);
  journal.record("run-started", { runId: "private-run-one", stepCount: 3, provenance: "human" });
  time.advance(3000);
  journal.record("run-terminal", { runId: "private-run-one", state: "passed", durationMs: 3000 });
  time.advance(500);
  journal.record("run-started", { runId: "private-run-two", stepCount: 3, provenance: "human" });
  time.advance(3000);
  journal.record("run-terminal", {
    runId: "private-run-two",
    state: "failed",
    durationMs: 3000,
    failureClass: "locator-not-found",
  });
  time.advance(100);
  journal.record("results-opened", { runId: "private-run-two", state: "failed" });
  time.advance(900);
  journal.record("improve-scanned", { findingCount: 4 });
  time.advance(100);
  journal.record("repair-proposed", { proposalId: "proposal-private" });
  journal.record("repair-approved", { proposalId: "proposal-private" });
  journal.record("repair-applied", { proposalId: "proposal-private" });
  journal.record("repair-verified", { proposalId: "proposal-private" });

  const summary = journal.summary();
  assert.equal(summary.localSessionOnly, true);
  assert.equal(summary.authoringMode, "mixed");
  assert.equal(summary.timeToGoalMs, 100);
  assert.equal(summary.recordingDurationMs, 1000);
  assert.equal(summary.reviewToSaveDurationMs, 300);
  assert.equal(summary.timeToFirstResultMs, 5000);
  assert.deepEqual(summary.runDurationsMs, [3000, 3000]);
  assert.equal(summary.stepCount, 3);
  assert.equal(summary.hardAssertionCount, 2);
  assert.equal(summary.durableSelectorCount, 3);
  assert.equal(summary.fragileSelectorCount, 1);
  assert.equal(summary.durableSelectorRatio, 0.75);
  assert.equal(summary.runs, 2);
  assert.equal(summary.passed, 1);
  assert.equal(summary.failed, 1);
  assert.equal(summary.replayStability.status, "unstable");
  assert.deepEqual(summary.failureClassificationCounts, { "locator-not-found": 1 });
  assert.equal(summary.timeToDiagnosisProxyMs, 1000);
  assert.deepEqual(summary.repair, {
    proposed: 1,
    approved: 1,
    rejected: 0,
    applied: 1,
    verified: 1,
    rollbackRequired: 0,
    rollbackFailed: 0,
    reverted: 0,
    unresolvedRollback: 0,
  });
  assert.deepEqual(summary.improve, { scans: 1, findings: 4 });
  assert.deepEqual(summary.agentApprovals, {
    requested: 1,
    approved: 1,
    rejected: 0,
    expired: 0,
    stale: 0,
    consumed: 0,
    pending: 0,
    decisionDurationsMs: [100],
    averageDecisionDurationMs: 100,
  });
  assert.deepEqual(summary.humanInvolvement.needsAttention, []);
  assert.deepEqual(journal.summary(), summary);
});

test("rollback study events keep unresolved and failed rollback out of completed repairs", () => {
  const time = clock();
  const journal = createPrototypeStudyJournal(journalOptions(time));

  journal.record("repair-proposed", { proposalId: "proposal-one" });
  journal.record("repair-approved", { proposalId: "proposal-one" });
  journal.record("repair-applied", { proposalId: "proposal-one" });
  journal.record("repair-rollback-required", { proposalId: "proposal-one" });
  journal.record("repair-rollback-failed", { proposalId: "proposal-one" });

  let summary = journal.summary();
  assert.equal(summary.repair.rollbackRequired, 1);
  assert.equal(summary.repair.rollbackFailed, 1);
  assert.equal(summary.repair.reverted, 0);
  assert.equal(summary.repair.unresolvedRollback, 1);
  assert.ok(summary.humanInvolvement.needsAttention.includes("repair-verification-or-rollback"));

  journal.record("repair-reverted", { proposalId: "proposal-one" });
  summary = journal.summary();
  assert.equal(summary.repair.reverted, 1);
  assert.equal(summary.repair.unresolvedRollback, 0);
  assert.ok(!summary.humanInvolvement.needsAttention.includes("repair-verification-or-rollback"));
});

test("study journal reports unavailable and corrupt storage without claiming persistence", () => {
  const time = clock();
  const corrupt = new MemorySessionStorage();
  corrupt.setItem("study", "{not-json");
  const recovered = createPrototypeStudyJournal({
    ...journalOptions(time, corrupt),
    storageKey: "study",
  });
  assert.equal(recovered.summary().storage.status, "recovered-corrupt");
  assert.match(recovered.summary().limitations.join("\n"), /corrupt local journal/i);

  const unavailable = {
    getItem() { throw new Error("blocked"); },
    setItem() { throw new Error("blocked"); },
    removeItem() { throw new Error("blocked"); },
  };
  const blocked = createPrototypeStudyJournal(journalOptions(time, unavailable));
  assert.equal(blocked.record("workbench-opened", { provenance: "human" }), false);
  assert.equal(blocked.clear(), false);
  assert.equal(blocked.summary().storage.available, false);
  assert.match(blocked.summary().limitations.join("\n"), /sessionStorage is unavailable/i);
});

test("study export never includes raw goal, flow, selector, or opaque input values", () => {
  const time = clock();
  const journal = createPrototypeStudyJournal(journalOptions(time));
  const privateGoal = "Ship the private payroll flow";
  const privateSelector = "automation-private-payroll";
  const privateRun = "run-private-payroll";
  const privateApproval = "approval-private-payroll";

  assert.equal(journal.record("goal-defined", { goal: privateGoal }), false);
  assert.equal(journal.record("test-saved", { selector: privateSelector }), false);
  assert.equal(journal.record("run-started", { runId: privateRun, stepCount: 1 }), true);
  assert.equal(journal.record("agent-requested", { approvalRequestId: privateApproval }), true);
  const serialized = JSON.stringify(journal.exportEvidence());

  for (const privateValue of [privateGoal, privateSelector, privateRun, privateApproval]) {
    assert.doesNotMatch(serialized, new RegExp(privateValue));
  }
  assert.match(serialized, /"localSessionOnly":true/);
});

test("Results prototype-study card is collapsed, accessible, and requires a clear confirmation", () => {
  const doc = new FakeDocument();
  let downloads = 0;
  let clears = 0;
  const card = renderPrototypeStudyEvidenceCard({
    summary: () => ({
      authoringMode: "human",
      timeToGoalMs: 1200,
      recordingDurationMs: 3400,
      reviewToSaveDurationMs: null,
      timeToFirstResultMs: 5600,
      stepCount: 2,
      hardAssertionCount: 1,
      durableSelectorCount: 2,
      fragileSelectorCount: 1,
      durableSelectorRatio: 2 / 3,
      passed: 1,
      failed: 0,
      replayStability: { status: "insufficient" },
      agentApprovals: { approved: 1, rejected: 0, pending: 0 },
      improve: { scans: 1, findings: 0 },
      limitations: ["Local session only."],
    }),
    downloadSessionEvidence() {
      downloads += 1;
      return true;
    },
    clearLocalSessionEvidence() {
      clears += 1;
      return true;
    },
  }, doc);

  assert.equal(card.tagName, "details");
  assert.equal(card.open, undefined);
  assert.equal(card.getAttribute("aria-label"), "Prototype evidence local only");
  const download = findElement(card, (element) => element.textContent === "Download session evidence");
  const clear = findElement(card, (element) => element.textContent === "Clear local session evidence");
  const status = findElement(card, (element) => element.getAttribute?.("aria-live") === "polite");
  assert.ok(download);
  assert.ok(clear);
  assert.ok(status);
  download.click();
  assert.equal(downloads, 1);
  assert.match(status.textContent, /file-only local session evidence JSON/i);
  clear.click();
  assert.equal(clears, 0);
  assert.equal(clear.textContent, "Confirm clear local session evidence");
  clear.click();
  assert.equal(clears, 1);
  assert.match(status.textContent, /was cleared/i);
});

test("study module has no transport client and Workbench hooks render local-only controls", () => {
  const study = read("inspector-study.js");
  const devflow = read("devflow.js");
  const plan = read("inspector-plan.js");
  const steps = read("inspector-steps.js");
  const trace = read("inspector-trace.js");
  const workbench = read("inspector-workbench.js");
  const css = read("inspector-workbench.css");

  assert.doesNotMatch(study, /\bfetch\s*\(/);
  assert.doesNotMatch(study, /\bXMLHttpRequest\b/);
  assert.doesNotMatch(study, /^import\s/m);
  assert.match(devflow, /recordStudyEvent\('recording-started'/);
  assert.match(devflow, /recordStudyEvent\('recording-stopped'/);
  assert.match(devflow, /studyController\?\.testSaved/);
  assert.match(devflow, /studyController\?\.runStarted/);
  assert.match(devflow, /studyController\?\.runTerminal/);
  assert.match(devflow, /studyController\?\.improveScanned/);
  assert.match(devflow, /studyController\?\.agentApprovalTransition/);
  assert.match(devflow, /studyController\?\.repairTransition/);
  assert.match(devflow, /studyController\.workbenchOpened/);
  assert.match(plan, /authoring\.noteGoalDefined\?\.\(value\)/);
  assert.match(steps, /authoring\.noteAssertionAdded\?\.\(assertion\)/);
  assert.match(workbench, /study\?\.resultsOpened/);
  assert.match(trace, /helpers\.studyEvidenceCard\?\.\(root\)/);
  for (const text of [
    "Prototype evidence (local only)",
    "Download session evidence",
    "Clear local session evidence",
    "Confirm clear local session evidence",
    "No telemetry or network egress",
  ]) {
    assert.match(study, new RegExp(text));
  }
  assert.match(study, /createElement\(doc, 'details'/);
  assert.match(study, /aria-live/);
  assert.match(css, /\.df-prototype-study-evidence/);
  assert.match(css, /\.df-study-metrics/);
  assert.match(css, /grid-template-columns: 1fr/);
});
