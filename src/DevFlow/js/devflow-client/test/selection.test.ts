import { test } from "node:test";
import assert from "node:assert/strict";
import {
  selectRegistration,
  selectLive,
  isProjectInRoot,
  candidatePorts,
  androidRegistrationPorts,
  androidForwardingPolicy,
  Resolver,
} from "../src/resolve.js";
import type { AgentRegistration } from "../src/types.js";
import type { LiveAgent } from "../src/resolve.js";

function reg(p: Partial<AgentRegistration> & { port: number }): AgentRegistration {
  return { id: "", project: "", tfm: "", platform: "", appName: "", ...p };
}

test("selectRegistration: empty → none", () => {
  assert.equal(selectRegistration([], {}).kind, "none");
});

test("selectRegistration: single → unique", () => {
  const out = selectRegistration([reg({ port: 100, appName: "A" })], {});
  assert.equal(out.kind, "unique");
});

test("selectRegistration: pinned port found → unique, missing → none", () => {
  const agents = [reg({ port: 100 }), reg({ port: 200 })];
  assert.equal(selectRegistration(agents, { agentPort: 200 }).kind, "unique");
  assert.equal(selectRegistration(agents, { agentPort: 999 }).kind, "none");
});

test("selectRegistration: projectRoot narrows", () => {
  const agents = [
    reg({ port: 1, project: "D:/apps/foo/foo.csproj" }),
    reg({ port: 2, project: "D:/apps/bar/bar.csproj" }),
  ];
  const out = selectRegistration(agents, { projectRoot: "D:/apps/foo" });
  assert.equal(out.kind, "unique");
  assert.equal(out.kind === "unique" && out.agent.port, 1);
});

test("selectRegistration: platform/tfm narrows", () => {
  const agents = [
    reg({ port: 1, platform: "android", tfm: "net10.0-android" }),
    reg({ port: 2, platform: "windows", tfm: "net10.0-windows10.0.19041.0" }),
  ];
  const out = selectRegistration(agents, { platform: "android" });
  assert.equal(out.kind === "unique" && out.agent.port, 1);
});

test("selectRegistration: ambiguous returns candidates (no silent pick)", () => {
  const agents = [reg({ port: 1, appName: "A" }), reg({ port: 2, appName: "B" })];
  const out = selectRegistration(agents, {});
  assert.equal(out.kind, "ambiguous");
  assert.equal(out.kind === "ambiguous" && out.candidates.length, 2);
});

test("selectRegistration: allowAmbiguousMostRecent picks newest connectedAt", () => {
  const agents = [
    reg({ port: 1, connectedAt: "2020-01-01T00:00:00Z" }),
    reg({ port: 2, connectedAt: "2030-01-01T00:00:00Z" }),
  ];
  const out = selectRegistration(agents, { allowAmbiguousMostRecent: true });
  assert.equal(out.kind === "unique" && out.agent.port, 2);
});

test("selectLive: running preferred, ambiguous otherwise", () => {
  const live: LiveAgent[] = [
    { port: 1, status: { running: false }, running: false, platform: "windows" },
    { port: 2, status: { running: true }, running: true, platform: "windows" },
  ];
  const out = selectLive(live, {});
  assert.equal(out.kind === "unique" && out.agent.port, 2);

  const bothRunning: LiveAgent[] = [
    { port: 1, status: { running: true }, running: true },
    { port: 2, status: { running: true }, running: true },
  ];
  assert.equal(selectLive(bothRunning, {}).kind, "ambiguous");
});

test("isProjectInRoot", () => {
  assert.equal(isProjectInRoot("D:/a/b/b.csproj", "D:/a/b"), true);
  assert.equal(isProjectInRoot("D:/a/b", "D:/a/b"), true);
  assert.equal(isProjectInRoot("D:/a/c/c.csproj", "D:/a/b"), false);
  assert.equal(isProjectInRoot("", "D:/a"), false);
  assert.equal(isProjectInRoot("D:/a", ""), false);
});

test("candidatePorts: includes pinned, default, scan range; deduped", () => {
  const ports = candidatePorts(9999);
  assert.ok(ports.includes(9999));
  assert.ok(ports.includes(9223));
  assert.ok(ports.includes(10223));
  assert.ok(ports.includes(10242));
  assert.equal(new Set(ports).size, ports.length);
});

test("androidRegistrationPorts trusts broker platform metadata over stale hints", () => {
  const registrations = [
    reg({ port: 10223, platform: "android", tfm: "net10.0-android" }),
    reg({ port: 10224, platform: "windows", tfm: "net10.0-windows10.0.19041.0" }),
    reg({ port: 10225, platform: "", tfm: "" }),
  ];

  assert.deepEqual(androidRegistrationPorts(registrations, false), [10223]);
  assert.deepEqual(androidRegistrationPorts(registrations, true), [10223, 10225]);
});

test("androidForwardingPolicy: auto supports broker Android without blind desktop ADB scans", () => {
  assert.deepEqual(androidForwardingPolicy({}), { enabled: true, fallbackScan: false });
  assert.deepEqual(androidForwardingPolicy({ platform: "android" }), { enabled: true, fallbackScan: true });
  assert.deepEqual(androidForwardingPolicy({ device: "emulator-5554" }), { enabled: true, fallbackScan: true });
  assert.deepEqual(androidForwardingPolicy({ platform: "windows", device: "emulator-5554" }), { enabled: true, fallbackScan: false });
  assert.deepEqual(androidForwardingPolicy({ adb: true }), { enabled: true, fallbackScan: true });
  assert.deepEqual(androidForwardingPolicy({ platform: "android", adb: false }), { enabled: false, fallbackScan: false });
});

test("Resolver.retarget recomputes Android fallback policy", () => {
  const resolver = new Resolver({
    bootstrapBroker: "never",
    allowAmbiguousMostRecent: false,
    adbMode: undefined,
    adbEnabled: true,
    adbFallbackScan: false,
    probeTimeoutMs: 1,
  });

  resolver.retarget({ platform: "android" });
  assert.equal(resolver.config.adbFallbackScan, true);

  resolver.retarget({ platform: "windows" });
  assert.equal(resolver.config.adbFallbackScan, false);
});
