// Drop-in contract tests for the DevflowDevice adapter over @maui-devflow/client.
// A fake loopback HTTP server plays both the broker (/api/agents) and the agent, so
// these run fully offline. They assert the EXACT return shapes store.mjs/capabilities
// depend on, so the transport swap stays a faithful drop-in.

import { before, after, test } from "node:test";
import assert from "node:assert/strict";
import http from "node:http";
import { unlinkSync } from "node:fs";
import { DevflowDevice } from "../devflow.mjs";

// Minimal valid-enough PNG (8-byte signature + padding) to pass the adapter's magic check.
const PNG = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0, 0, 0]);

let server;
let PORT = 0;
const taps = [];
const fills = [];
const props = [];

before(async () => {
  server = http.createServer((req, res) => {
    const u = new URL(req.url, "http://127.0.0.1");
    const p = u.pathname;
    const json = (o, s = 200) => {
      res.writeHead(s, { "Content-Type": "application/json" });
      res.end(JSON.stringify(o));
    };
    const readBody = (cb) => {
      let b = "";
      req.on("data", (c) => (b += c));
      req.on("end", () => cb(b ? JSON.parse(b) : {}));
    };

    if (req.method === "GET" && p === "/api/agents") {
      return json([{ id: "x", project: "", tfm: "net10.0-windows", platform: "windows", appName: "Fake", port: PORT }]);
    }
    if (req.method === "GET" && p === "/api/v1/agent/status") {
      return json({
        running: true,
        app: { name: "Fake", packageId: "com.fake", version: "1.2" },
        device: { platform: "windows", displayDensity: 2, windowWidth: 390, windowHeight: 844, idiom: "Desktop", deviceType: "virtual" },
        agent: { framework: "maui", frameworkVersion: "10.0" },
        capabilities: { screenshot: true },
      });
    }
    if (req.method === "POST" && p === "/api/v1/agent/lease") {
      return readBody((b) => json({
        ok: true,
        allowed: true,
        youHold: true,
        heldByOther: false,
        leaseId: b.leaseId,
        authority: "agent",
      }));
    }
    if (req.method === "GET" && p === "/api/v1/ui/tree") {
      return json([{ id: "root", type: "Page", fullType: "P", windowBounds: { x: 0, y: 0, width: 390, height: 844 }, children: [{ id: "btn", type: "Button", fullType: "B", automationId: "submit", text: "Go" }] }]);
    }
    if (p.includes("/properties/")) {
      if (req.method === "GET") return json({ value: "Go" });
      if (req.method === "PUT") return readBody((b) => { props.push(b); json({ success: true }); });
    }
    if (req.method === "GET" && p === "/api/v1/ui/hit-test") return json({ elements: [{ id: "btn" }, { id: "root" }] });
    if (req.method === "GET" && p === "/api/v1/ui/elements") return json({ elements: [{ id: "btn", automationId: u.searchParams.get("automationId") || undefined }] });
    if (req.method === "GET" && p.startsWith("/api/v1/ui/elements/")) {
      return json({ id: decodeURIComponent(p.split("/").pop()), type: "Button", fullType: "B" });
    }
    if (req.method === "POST" && p === "/api/v1/ui/actions/tap") return readBody((b) => { taps.push(b.elementId); json({ success: true }); });
    if (req.method === "POST" && p === "/api/v1/ui/actions/fill") return readBody((b) => { fills.push(b); json({ success: true }); });
    if (req.method === "POST" && (p === "/api/v1/ui/actions/back" || p === "/api/v1/ui/actions/navigate" || p === "/api/v1/ui/actions/resize")) {
      return readBody(() => json({ success: true }));
    }
    if (req.method === "GET" && p === "/api/v1/ui/screenshot") {
      res.writeHead(200, { "Content-Type": "image/png" });
      return res.end(PNG);
    }
    if (req.method === "GET" && p === "/api/v1/device/app/theme") return json({ theme: "light", requestedTheme: "light" });
    if (req.method === "PUT" && p === "/api/v1/device/app/theme") return readBody(() => json({ theme: "dark", requestedTheme: "dark", effectiveTheme: "dark", success: true }));
    if (req.method === "GET" && p === "/api/v1/logs") return json([{ level: "info", message: "hi" }]);
    json({ error: "not found" }, 404);
  });
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  PORT = server.address().port;
});

after(() => new Promise((r) => server.close(r)));

const dev = () => new DevflowDevice({ brokerPort: PORT, agentPort: PORT, bootstrapBroker: "never" });

test("getRoots returns {ok, roots} and updates info.window", async () => {
  const d = dev();
  const r = await d.getRoots(0);
  assert.equal(r.ok, true);
  assert.equal(r.roots[0].id, "root");
  assert.equal(d.info().window.width, 390);
  d.dispose();
});

test("refreshInfo maps status → info snapshot", async () => {
  const d = dev();
  const info = await d.refreshInfo();
  assert.equal(info.appName, "Fake");
  assert.equal(info.platform, "windows");
  assert.equal(info.density, 2);
  assert.equal(info.framework, "maui");
  assert.equal(info.appVersion, "1.2");
  assert.equal(info.connected, true);
  assert.deepEqual(info.window, { x: 0, y: 0, width: 390, height: 844 });
  d.dispose();
});

test("getElement / query / hitTest / getProperty return the expected shapes", async () => {
  const d = dev();
  assert.equal((await d.getElement("btn")).id, "btn");
  const q = await d.query({ automationId: "submit" });
  assert.equal(q.ok, true);
  assert.equal(q.elements[0].id, "btn");
  const h = await d.hitTest(5, 5);
  assert.equal(h.ok, true);
  assert.equal(h.element.id, "btn");
  const gp = await d.getProperty("btn", "Text");
  assert.deepEqual(gp, { ok: true, value: "Go" });
  d.dispose();
});

test("mutations return strict {ok:true}; tap resolves an automationId selector to an id", async () => {
  const d = dev();
  taps.length = 0;
  assert.deepEqual(await d.tap({ automationId: "submit" }), { ok: true });
  assert.deepEqual(taps, ["btn"]);
  assert.deepEqual(await d.tap("btn"), { ok: true });
  assert.deepEqual(await d.fill("btn", "hello"), { ok: true });
  assert.deepEqual(await d.setProperty("btn", "Text", "x"), { ok: true });
  assert.deepEqual(await d.back(), { ok: true });
  assert.deepEqual(await d.navigate("//home"), { ok: true });
  assert.deepEqual(await d.resize(400, 800), { ok: true });
  d.dispose();
});

test("themeGet/themeSet return {ok,data} and cache info.theme", async () => {
  const d = dev();
  const g = await d.themeGet();
  assert.equal(g.ok, true);
  assert.equal(g.data.theme, "light");
  const s = await d.themeSet("dark");
  assert.equal(s.ok, true);
  assert.equal(s.data.effectiveTheme, "dark");
  assert.equal(d.info().theme, "dark");
  d.dispose();
});

test("screenshot writes a PNG file; elementShot returns a PNG buffer", async () => {
  const d = dev();
  const s = await d.screenshot();
  assert.equal(s.ok, true);
  assert.ok(s.path.endsWith(".png"));
  const es = await d.elementShot("btn");
  assert.equal(es.ok, true);
  assert.equal(es.mimeType, "image/png");
  assert.ok(Buffer.isBuffer(es.buffer));
  try { unlinkSync(s.path); } catch { /* best effort */ }
  d.dispose();
});

test("listAgents returns an array; whichPort/resolvedAgent/transport", async () => {
  const d = dev();
  const agents = await d.listAgents();
  assert.ok(Array.isArray(agents));
  assert.equal(agents[0].appName, "Fake");
  await d._ensureConnection();
  assert.equal(d.whichPort(), PORT);
  assert.equal(d.transport(), "http");
  assert.equal(d.resolvedAgent()?.appName, "Fake");
  d.dispose();
});

test("logs parses JSON entries", async () => {
  const d = dev();
  const r = await d.logs(10);
  assert.equal(r.ok, true);
  assert.equal(Array.isArray(r.data), true);
  assert.equal(r.data[0].message, "hi");
  d.dispose();
});
