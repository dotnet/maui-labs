import assert from "node:assert/strict";
import test from "node:test";
import {
  buildCaptureBody,
  buildPreviewBody,
  createEvidenceController,
  evidenceFileName,
  formatEvidenceEnvironment,
  formatEvidencePlan,
} from "../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-evidence.js";

const plan = {
  formatVersion: 1,
  redactionVersion: 2,
  app: { name: "Sample App" },
  platform: { name: "Windows" },
  included: [
    { name: "manifest.json", description: "Bundle description" }, { name: "tree.json", count: 42 },
    { name: "problems.json", count: 3 }, { name: "logs.json", count: 120 }, { name: "network.json", count: 8 },
  ],
  excluded: [{ name: "screenshot.png", reason: "Screenshots are opt-in." }],
  neverIncluded: ["Element Text/Value content"],
  screenshot: { requested: false, included: false, omittedReason: "Screenshots are opt-in." },
  counts: { treeElements: 42, problems: 3, logs: 120, networkRequests: 8 },
  limits: { logs: 200, network: 100, treeElements: 5000 },
  warnings: ["Network capture unavailable."],
  suggestedFileName: "SampleApp-20260909-112233.mauitrace",
};

function previewResult(body = {}) {
  return {
    ok: true,
    body: {
      ok: true,
      plan: {
        ...plan,
        included: [
          ...plan.included,
          ...(body.includeScreenshot ? [{ name: "screenshot.png" }] : []),
          ...(body.includeWorkflow ? [{ name: "workflow.md" }] : []),
        ],
        screenshot: { requested: body.includeScreenshot === true, included: body.includeScreenshot === true },
      },
    },
  };
}

test("plan renders counts, inclusions, exclusions, limits, and redaction", () => {
  const view = formatEvidencePlan(plan);
  assert.equal(view.title, "Share evidence from Sample App \u00b7 Windows");
  assert.match(view.summary, /42 elements.*3 problems.*8 request summaries/);
  assert.match(view.limits, /200 logs/);
  assert.match(view.redaction, /redaction ruleset v2/);
  assert.deepEqual(view.includes.map(entry => entry.name), plan.included.map(entry => entry.name));
  assert.deepEqual(view.excludes.map(entry => entry.name), ["screenshot.png"]);
  assert.deepEqual(view.warnings, plan.warnings);
  assert.equal(view.screenshotRequested, false);
  assert.match(view.screenshotNote, /opt-in/);
});

test("unavailable sections never appear as zero-problem or zero-request successes", () => {
  const view = formatEvidencePlan({
    ...plan,
    included: [{ name: "tree.json" }, { name: "logs.json" }],
    counts: { treeElements: 1, logs: 1, problems: 0, networkRequests: 0 },
  });
  assert.equal(view.summary, "1 element \u00b7 1 log entry");
});

test("opted-in screenshots warn about pixels and malformed plans remain displayable", () => {
  assert.match(formatEvidencePlan({ screenshot: { included: true } }).screenshotNote, /on-screen data/);
  const empty = formatEvidencePlan(null);
  assert.equal(empty.title, "Share evidence bundle");
  assert.deepEqual(empty.includes, []);
  assert.deepEqual(empty.excludes, []);
  assert.deepEqual(empty.never, []);
});

test("environment distinguishes app viewport from display and preserves unknowns", () => {
  const view = formatEvidenceEnvironment({
    app: { version: "1.2", build: "42" },
    viewport: { width: 400, height: 700, density: 1.5 },
    display: { width: 1920, height: 1080, orientation: "Portrait" },
    theme: { effective: "dark" },
    unavailable: [{ name: "fontScale", reason: "Not exposed by this agent." }],
  });
  assert.equal(view.rows.find(row => row.label === "App viewport").value, "400 \u00d7 700 DIP");
  assert.equal(view.rows.find(row => row.label === "Display").value, "1920 \u00d7 1080 px");
  assert.equal(view.rows.find(row => row.label === "App theme").value, "dark");
  assert.match(view.gaps[0], /fontScale/);
  assert.equal(formatEvidenceEnvironment({ display: { width: 100, height: 200 } }).rows.some(row => row.label === "App viewport"), false);
  assert.match(formatEvidenceEnvironment(null).gaps[0], /unavailable/);
});

test("download names are portable and cannot escape a directory", () => {
  assert.equal(evidenceFileName(plan), plan.suggestedFileName);
  assert.equal(evidenceFileName({ suggestedFileName: "../../etc/evil.mauitrace" }), "evil.mauitrace");
  assert.equal(evidenceFileName({ suggestedFileName: "C:\\Windows\\evil.mauitrace" }), "evil.mauitrace");
  for (const name of ["CON.mauitrace", "report.html", `${"a".repeat(200)}.mauitrace`])
    assert.match(evidenceFileName({ suggestedFileName: name }), /^devflow-.*\.mauitrace$/);
});

test("only explicit boolean consent attaches screenshot or workflow", () => {
  assert.deepEqual(buildCaptureBody(), { includeScreenshot: false });
  assert.deepEqual(buildPreviewBody(), { includeScreenshot: false, includeWorkflow: false });
  assert.deepEqual(buildCaptureBody({
    choice: { includeScreenshot: "true", includeWorkflow: 1 }, workflow: "private",
  }), { includeScreenshot: false });
  assert.deepEqual(buildCaptureBody({
    choice: { includeScreenshot: true, includeWorkflow: true }, elementId: "e1", workflow: "  ",
  }), { includeScreenshot: true, elementId: "e1" });
});

test("attachments are re-previewed and confirmed before the token-stamped download", async t => {
  const events = [];
  const previews = [];
  t.mock.method(globalThis, "fetch", async (url, options) => {
    events.push("capture");
    assert.equal(url, "/inspector/app/api/evidence/capture");
    assert.equal(options.headers["X-DevFlow-Inspector-Token"], "test-read-token");
    assert.deepEqual(JSON.parse(options.body), {
      includeScreenshot: true, elementId: "e1", workflow: "# Repro",
    });
    return new Response("bundle", { headers: { "Content-Type": "application/zip" } });
  });
  const controller = createEvidenceController({
    basePath: "/inspector/app",
    inspectorToken: "test-read-token",
    api: { postDetailed: async (path, body) => {
      assert.equal(path, "/api/evidence/preview");
      events.push("preview");
      previews.push(body);
      return previewResult(body);
    } },
    setStatus() {},
    getSelectedId: () => "e1",
    getWorkflow: () => "# Repro",
    showEvidenceDialog: async (view, options) => {
      events.push("choose");
      assert.equal(options.hasWorkflow, true);
      assert.equal(view.screenshotRequested, false);
      return { includeScreenshot: true, includeWorkflow: true };
    },
    showEvidenceFinalDialog: async view => {
      events.push("confirm");
      assert.equal(view.screenshotRequested, true);
      assert.ok(view.includes.some(entry => entry.name === "workflow.md"));
      assert.ok(view.includes.some(entry => entry.name === "screenshot.png"));
      return true;
    },
    downloadBlob: (blob, name) => {
      events.push("download");
      assert.equal(blob.size, 6);
      assert.equal(name, plan.suggestedFileName);
    },
  });
  await controller.open();
  assert.deepEqual(events, ["preview", "choose", "preview", "confirm", "capture", "download"]);
  assert.deepEqual(previews, [
    { includeScreenshot: false, includeWorkflow: false, elementId: "e1" },
    { includeScreenshot: true, includeWorkflow: true, elementId: "e1", workflow: "# Repro" },
  ]);
});

for (const phase of ["initial", "final"]) {
  test(`cancelling the ${phase} dialog never captures`, async t => {
    const fetch = t.mock.method(globalThis, "fetch", async () => { throw new Error("Unexpected capture"); });
    const statuses = [];
    const controller = createEvidenceController({
      api: { postDetailed: async (path, body) => previewResult(body) },
      setStatus: text => statuses.push(text),
      showEvidenceDialog: async () => phase === "initial" ? null : { includeScreenshot: true },
      showEvidenceFinalDialog: async () => false,
    });
    await controller.open();
    assert.equal(fetch.mock.callCount(), 0);
    assert.equal(statuses.at(-1), "Evidence capture cancelled.");
  });
}

test("HTTP failure cannot be turned into a successful preview by its body", async t => {
  const errors = t.mock.method(console, "error", () => {});
  let presented = false;
  const statuses = [];
  const controller = createEvidenceController({
    api: { postDetailed: async () => ({ ok: false, status: 403, body: { ok: true, plan, error: "forbidden" } }) },
    setStatus: text => statuses.push(text),
    showEvidenceDialog: async () => { presented = true; },
  });
  await controller.open();
  assert.equal(presented, false);
  assert.equal(statuses.at(-1), "forbidden");
  assert.equal(errors.mock.callCount(), 1);
});

test("capture errors are visible and the controller can be retried", async t => {
  t.mock.method(console, "error", () => {});
  const statuses = [];
  let downloads = 0;
  let fail = true;
  t.mock.method(globalThis, "fetch", async () => fail
    ? Response.json({ error: "Agent disconnected" }, { status: 503 })
    : new Response("bundle", { headers: { "Content-Type": "application/zip" } }));
  const controller = createEvidenceController({
    basePath: "/inspector/app",
    api: { postDetailed: async () => previewResult() },
    setStatus: text => statuses.push(text),
    showEvidenceDialog: async () => ({ includeScreenshot: false, includeWorkflow: false }),
    downloadBlob: () => { downloads++; },
  });
  await controller.open();
  assert.equal(statuses.at(-1), "Agent disconnected");
  assert.equal(downloads, 0);
  fail = false;
  await controller.open();
  assert.equal(downloads, 1);
  assert.match(statuses.at(-1), /downloaded/);
});

test("an HTML error page is never saved as evidence", async t => {
  t.mock.method(console, "error", () => {});
  t.mock.method(globalThis, "fetch", async () => new Response("<html>Error</html>", { headers: { "Content-Type": "text/html" } }));
  const statuses = [];
  let downloads = 0;
  const controller = createEvidenceController({
    api: { postDetailed: async () => previewResult() },
    setStatus: text => statuses.push(text),
    showEvidenceDialog: async () => ({}),
    downloadBlob: () => { downloads++; },
  });
  await controller.open();
  assert.equal(downloads, 0);
  assert.match(statuses.at(-1), /No file was downloaded/);
});

test("repeated clicks share one in-flight preview", async () => {
  let finish;
  let previews = 0;
  const pending = new Promise(resolve => { finish = resolve; });
  const controller = createEvidenceController({
    api: { postDetailed: async () => { previews++; return pending; } },
    setStatus() {},
    showEvidenceDialog: async () => null,
  });
  const first = controller.open();
  await controller.open();
  assert.equal(previews, 1);
  finish(previewResult());
  await first;
});
