import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const webRoot = resolve(sourceRoot, "Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web");
const read = (file) => readFileSync(resolve(webRoot, file), "utf8");

async function loadImprove() {
  return import(`data:text/javascript;base64,${Buffer.from(read("inspector-improve.js")).toString("base64")}`);
}

test("Improve grouping and filters are deterministic", async () => {
  const improve = await loadImprove();
  const findings = [
    { diagnosticId: "DFSH009", findingId: "b", severity: "warning", category: "assertion", stepId: "2", platforms: ["android"] },
    { diagnosticId: "DFSH001", findingId: "a", severity: "error", category: "selector", stepId: "1", platforms: ["android", "windows"] },
    { diagnosticId: "DFSH011", findingId: "c", severity: "info", category: "coverage", platforms: ["windows"] },
  ];

  const groups = improve.groupFindings(findings);
  assert.deepEqual(groups.map((group) => `${group.severity}:${group.category}`), [
    "error:selector", "warning:assertion", "info:coverage",
  ]);
  assert.equal(improve.filterFindings(findings, { platform: "windows" }).length, 2);
  assert.equal(improve.filterFindings(findings, { step: "2" })[0].diagnosticId, "DFSH009");
});

test("Improve stale state requires explicit read-only rescan", async () => {
  const improve = await loadImprove();

  assert.equal(improve.isImproveStale({ stale: true }), true);
  assert.equal(improve.isImproveStale({ analysis: {}, inputKey: "old", currentKey: "new" }), true);
  assert.equal(improve.isImproveStale({ analysis: {}, inputKey: "same", currentKey: "same" }), false);
});

test("ambiguity context is redacted, bounded, and never guesses duplicate or truncated IDs", async () => {
  const improve = await loadImprove();
  const sensitive = "CorrectHorseBatteryStaple";
  const context = improve.normalizeAmbiguityContext({
    stepId: "step-3",
    stepSequence: 3,
    selectorKind: "text",
    totalCount: 23,
    truncated: true,
    matches: Array.from({ length: 23 }, (_, index) => ({
      id: `ephemeral-${index}`,
      type: "Button",
      role: "button",
      automationId: index === 0 ? "first" : "duplicate",
      isVisible: true,
      isEnabled: true,
      bounds: { x: index, y: 0, width: 10, height: 20 },
      windowBounds: { x: 0, y: 0, width: 100, height: 100 },
      hasSource: true,
      sourceLine: index + 1,
      text: sensitive,
      value: sensitive,
      frameworkProperties: { Secret: sensitive },
      sourceFile: `C:\\private\\${sensitive}.xaml`,
    })),
  });

  assert.equal(context.matches.length, 20);
  assert.equal(context.totalCount, 23);
  assert.equal(context.truncated, true);
  assert.equal(improve.isUniqueReturnedAutomationId(context.matches[0], context.matches, context.truncated), false);
  assert.equal(improve.hasUniqueReturnedAutomationId(context), false);
  assert.doesNotMatch(JSON.stringify(context), new RegExp(sensitive));
  assert.doesNotMatch(JSON.stringify(context), /sourceFile|frameworkProperties|"text"|"value"/);

  const complete = improve.normalizeAmbiguityContext({
    totalCount: 3,
    matches: [
      { id: "one", automationId: "unique" },
      { id: "two", automationId: "duplicate" },
      { id: "three", automationId: "duplicate" },
    ],
  });
  assert.equal(improve.isUniqueReturnedAutomationId(complete.matches[0], complete.matches, complete.truncated), true);
  assert.equal(improve.isUniqueReturnedAutomationId(complete.matches[1], complete.matches, complete.truncated), false);
  assert.equal(improve.hasUniqueReturnedAutomationId(complete), true);
});

test("Improve renderer exposes evidence, links, filters, and no apply action", () => {
  const source = read("inspector-improve.js");
  const css = read("inspector-workbench.css");

  for (const text of [
    "No test to scan", "Scan test", "Scan again", "Include the current live visual tree",
    "Filter findings", "Coverage details", "Improve this test with your agent", "Open Steps", "Open Trace", "Source anchor", "Evidence:",
  ]) {
    assert.match(source, new RegExp(text));
  }
  assert.match(source, /read-only and does not create a repair/i);
  assert.match(source, /The scan is read-only and does not create a repair/i);
  assert.match(source, /aria-live', 'polite/);
  assert.ok(
    source.indexOf("root.append(scanOptions);") < source.indexOf("if (!analysis) return root;"),
    "Scan options must render before the first analysis exists",
  );
  assert.match(read("devflow.js"), /openSourceDiff/);
  assert.match(css, /\.df-improve-filters/);
  assert.match(css, /repeat\(auto-fit, minmax\(150px, 1fr\)\)/);
  assert.match(read("inspector-workbench.js"), /role.*dialog|role.*region/);
});

test("Improve ambiguity card is accessible and supports only reviewed human choices", () => {
  const source = read("inspector-improve.js");
  const css = read("inspector-workbench.css");

  for (const text of [
    "Ambiguous selector", "Highlight in app", "Use this AutomationId",
    "Improve app testability", "DevFlow will not choose automatically",
    "Only the first 20 matches are shown", "canonical verification",
  ]) {
    assert.match(source, new RegExp(text));
  }
  assert.match(source, /aria-labelledby/);
  assert.match(source, /aria-live', 'polite/);
  assert.match(source, /Use this AutomationId for the failed step after global verification/);
  assert.match(source, /sourceLine/);
  assert.doesNotMatch(source.slice(source.indexOf("function safeAmbiguityMatch"), source.indexOf("function severityOrder")), /\.text|\.value|sourceFile|frameworkProperties/);
  assert.match(css, /\.df-ambiguity-card/);
  assert.match(css, /\.df-ambiguity-matches/);
});
