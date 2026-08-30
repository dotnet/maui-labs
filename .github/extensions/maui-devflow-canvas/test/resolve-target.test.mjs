// tap/fill must refuse to guess when a non-id selector matches several elements.
//
// MAUI CollectionView rows reuse the same AutomationId and text for every row — proven
// live against the sample app, where three rows all expose automationId "DeleteButton"
// with text "✕". Resolving that to elements[0] silently fires a destructive action on an
// arbitrary row, so an ambiguous target must be an error that names the candidates.

import test from "node:test";
import assert from "node:assert/strict";
import { DevflowDevice } from "../devflow.mjs";

function deviceMatching(elements) {
  const device = new DevflowDevice({});
  const calls = [];
  device.query = async () => ({ ok: true, elements });
  device._client = {
    tap: async ({ elementId }) => {
      calls.push(["tap", elementId]);
      return { ok: true };
    },
    fill: async (id, text) => {
      calls.push(["fill", id, text]);
      return { ok: true };
    },
  };
  device.calls = calls;
  return device;
}

const threeRows = [
  { id: "DeleteButton", automationId: "DeleteButton", text: "✕" },
  { id: "DeleteButton_b888d494", automationId: "DeleteButton", text: "✕" },
  { id: "DeleteButton_da3ba21a", automationId: "DeleteButton", text: "✕" },
];

const hiddenShellNodes = [
  { id: "shell-item", type: "ShellItem", fullType: "Microsoft.Maui.Controls.ShellItem", automationId: "Blazor", text: "Blazor", isVisible: false, isEnabled: false },
  { id: "shell-section", type: "ShellSection", fullType: "Microsoft.Maui.Controls.ShellSection", automationId: "Blazor", text: "Blazor", isVisible: false, isEnabled: false },
  { id: "shell-content", type: "ShellContent", fullType: "Microsoft.Maui.Controls.ShellContent", automationId: "Blazor", text: "Blazor", isVisible: false, isEnabled: false },
];

const visibleBlazorTargets = [
  ...hiddenShellNodes,
  { id: "FlyoutItem_IMPL_blazor", type: "FlyoutItem", fullType: "Microsoft.Maui.DevFlow.Agent.Core.FlyoutItem", automationId: "FlyoutItem_IMPL_blazor", text: "Blazor", isVisible: true, isEnabled: true },
  { id: "FlyoutItem_IMPL_multi_blazor", type: "FlyoutItem", fullType: "Microsoft.Maui.DevFlow.Agent.Core.FlyoutItem", automationId: "FlyoutItem_IMPL_multi_blazor", text: "Multi-Blazor", isVisible: true, isEnabled: true },
];


test("tap by an ambiguous automationId is refused and names the candidates", async () => {
  const device = deviceMatching(threeRows);

  const r = await device.tap({ automationId: "DeleteButton" });

  assert.equal(r.ok, false);
  assert.match(r.error, /ambiguous/i);
  assert.match(r.error, /3 visible elements match/);
  assert.match(r.error, /DeleteButton_b888d494/);
  assert.deepEqual(device.calls, [], "no element may be tapped when the target is ambiguous");
});

test("tap by an ambiguous text is refused", async () => {
  const device = deviceMatching(threeRows);

  const r = await device.tap({ text: "✕" });

  assert.equal(r.ok, false);
  assert.match(r.error, /ambiguous/i);
  assert.deepEqual(device.calls, []);
});

test("tap by text prefers the exact visible actionable/synthetic target over hidden shell nodes", async () => {
  const device = deviceMatching(visibleBlazorTargets);

  const r = await device.tap({ text: "Blazor" });

  assert.equal(r.ok, true);
  assert.deepEqual(device.calls, [["tap", "FlyoutItem_IMPL_blazor"]]);
});

test("tap by text reports only visible candidates when hidden shell nodes also match", async () => {
  const device = deviceMatching([
    ...hiddenShellNodes,
    { id: "FlyoutItem_IMPL_multi_blazor", type: "FlyoutItem", fullType: "Microsoft.Maui.DevFlow.Agent.Core.FlyoutItem", automationId: "FlyoutItem_IMPL_multi_blazor", text: "Multi-Blazor", isVisible: true, isEnabled: true },
    { id: "FlyoutItem_IMPL_multi_blazor_2", type: "FlyoutItem", fullType: "Microsoft.Maui.DevFlow.Agent.Core.FlyoutItem", automationId: "FlyoutItem_IMPL_multi_blazor_2", text: "Multi-Blazor", isVisible: true, isEnabled: true },
  ]);

  const r = await device.tap({ text: "Blazor" });

  assert.equal(r.ok, false);
  assert.match(r.error, /visible elements match/);
  assert.doesNotMatch(r.error, /ShellItem|ShellSection|ShellContent/);
  assert.deepEqual(device.calls, []);
});


test("fill by an ambiguous automationId is refused", async () => {
  const device = deviceMatching(threeRows);

  const r = await device.fill({ automationId: "DeleteButton" }, "text");

  assert.equal(r.ok, false);
  assert.match(r.error, /ambiguous/i);
  assert.deepEqual(device.calls, []);
});

test("an unambiguous automationId still resolves and taps", async () => {
  const device = deviceMatching([{ id: "AddButton", automationId: "AddButton" }]);

  const r = await device.tap({ automationId: "AddButton" });

  assert.equal(r.ok, true);
  assert.deepEqual(device.calls, [["tap", "AddButton"]]);
});

test("an explicit id is authoritative and skips ambiguity resolution", async () => {
  const device = deviceMatching(threeRows);
  device.query = async () => {
    throw new Error("query must not be consulted when an explicit id is given");
  };

  const r = await device.tap({ id: "DeleteButton_da3ba21a" });

  assert.equal(r.ok, true);
  assert.deepEqual(device.calls, [["tap", "DeleteButton_da3ba21a"]]);
});

test("a selector that matches nothing reports no match, not ambiguity", async () => {
  const device = deviceMatching([]);

  const r = await device.tap({ automationId: "Nope" });

  assert.equal(r.ok, false);
  assert.match(r.error, /no visible element matches/i);
  assert.deepEqual(device.calls, []);
});

test("tap with no target at all is refused with actionable guidance", async () => {
  const device = deviceMatching(threeRows);

  const r = await device.tap({});

  assert.equal(r.ok, false);
  assert.match(r.error, /pass id, automationId, or text/);
  assert.deepEqual(device.calls, []);
});
