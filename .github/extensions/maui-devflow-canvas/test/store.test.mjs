import assert from "node:assert/strict";
import test from "node:test";
import { LiveStore } from "../store.mjs";

const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

class FakeDevice {
  constructor() {
    this.opts = {};
    this.port = 1;
    this.rootCalls = 0;
    this.activeRoots = 0;
    this.maxActiveRoots = 0;
    this.beforeRootReturn = null;
    this._info = {
      appName: "Fake",
      platform: "windows",
      connected: true,
      theme: "light",
      window: { x: 0, y: 0, width: 100, height: 100 },
    };
  }

  async _ensureConnection() {}

  async getRoots() {
    const call = ++this.rootCalls;
    this.activeRoots += 1;
    this.maxActiveRoots = Math.max(this.maxActiveRoots, this.activeRoots);
    if (this.beforeRootReturn) await this.beforeRootReturn({ call });
    else await delay(15);
    this.activeRoots -= 1;
    return {
      ok: true,
      roots: [{
        id: `root-${call}`,
        type: "ContentPage",
        windowBounds: { x: 0, y: 0, width: 100, height: 100 },
        children: [],
      }],
      window: this._info.window,
    };
  }

  async refreshInfo() {
    return this._info;
  }

  info() {
    return this._info;
  }

  async themeGet() {
    return { ok: true, data: { theme: this._info.theme } };
  }

  async themeSet(theme) {
    this._info = { ...this._info, theme };
    return { ok: true, data: { effectiveTheme: theme } };
  }

  async screenshot() {
    return { ok: false, error: "disabled in test" };
  }

  async listAgents() {
    return [];
  }

  whichPort() {
    return this.port;
  }

  retarget({ platform, agentPort }) {
    this.port = agentPort;
    this._info = { ...this._info, platform: platform || this._info.platform };
  }

  dispose() {}
}

function liveStore(device) {
  const store = new LiveStore({ bootstrapBroker: "never" });
  store.device.dispose();
  store.device = device;
  store.state.info = device.info();
  return store;
}

test("refresh serializes overlapping pulls and preserves the newest snapshot", async () => {
  const device = new FakeDevice();
  const store = liveStore(device);
  try {
    await Promise.all([store.refresh({ shot: false }), store.refresh({ shot: false })]);
    assert.equal(device.maxActiveRoots, 1);
    assert.equal(store.state.roots[0].id, "root-2");
    assert.equal(store.state.busy, false);
  } finally {
    store.dispose();
  }
});

test("selectAgent supersedes an in-flight theme settle", async () => {
  const device = new FakeDevice();
  let markStarted;
  let releaseFirst;
  const started = new Promise((resolve) => {
    markStarted = resolve;
  });
  const firstGate = new Promise((resolve) => {
    releaseFirst = resolve;
  });
  device.beforeRootReturn = ({ call }) => {
    if (call !== 1) return delay(15);
    markStarted();
    return firstGate;
  };
  const store = liveStore(device);
  let settles = 0;
  store._settleThemeShot = () => {
    settles += 1;
  };

  try {
    const theme = store.setTheme("dark");
    await started;
    const selected = store.selectAgent({ platform: "android", port: 2 });
    releaseFirst();
    const [result] = await Promise.all([theme, selected]);

    assert.equal(result.superseded, true);
    assert.equal(device.rootCalls, 2);
    assert.equal(store.state.roots[0].id, "root-2");
    assert.equal(settles, 0);
    assert.equal(store.state.info.platform, "android");
  } finally {
    store.dispose();
  }
});
