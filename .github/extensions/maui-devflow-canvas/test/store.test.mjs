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

test("page filtering preserves nested navigation content and a side-by-side flyout", async () => {
  const el = (id, type, x, y, width, height, children = []) => ({
    id, type, isVisible: true, windowBounds: { x, y, width, height }, children,
  });
  const device = new FakeDevice();
  device._info.window = { x: 0, y: 0, width: 1000, height: 900 };
  device.getRoots = async () => ({
    ok: true,
    window: device._info.window,
    roots: [el("root", "MainShell", 0, 0, 1000, 900, [
      el("menu", "ContentPage", 0, 0, 240, 900, [
        el("menu-item", "Label", 20, 200, 200, 40),
      ]),
      el("navigation", "NavigationPage", 244, 100, 756, 800, [
        el("detail", "DetailPage", 244, 100, 756, 800, [
          el("detail-label", "Label", 260, 200, 200, 40),
          el("detail-button", "Button", 260, 300, 200, 40),
        ]),
      ]),
    ])],
  });
  const store = liveStore(device);
  try {
    await store.refresh({ shot: false });
    const ids = new Set();
    const visit = (element) => {
      ids.add(element.id);
      for (const child of element.children || []) visit(child);
    };
    store.state.roots.forEach(visit);
    for (const id of ["menu", "menu-item", "navigation", "detail", "detail-label", "detail-button"])
      assert.ok(ids.has(id), `${id} must not be mistaken for an inactive page`);
  } finally {
    store.dispose();
  }
});

test("hidden Shell tabs prefer the selected current page over a populated cached page", async () => {
  const device = new FakeDevice();
  device._info.window = { x: 0, y: 0, width: 400, height: 900 };
  const el = (id, type, children = []) => ({
    id, type, isVisible: true, windowBounds: { x: 0, y: 100, width: 400, height: 800 }, children,
  });
  const label = id => ({ id, type: "Label", windowBounds: { x: 20, y: 200, width: 100, height: 40 } });
  device.getRoots = async () => ({
    ok: true,
    window: device._info.window,
    roots: [el("shell", "AppShell", [
      el("home-wrapper", "ShellContent", [el("home", "MainPage", [label("old1"), label("old2"), label("old3")])]),
      el("catalog-wrapper", "ShellContent", [{
        ...el("catalog", "CatalogPage", [label("current")]), state: { selected: true },
      }]),
    ])],
  });
  const store = liveStore(device);
  try {
    await store.refresh({ shot: false });
    const json = JSON.stringify(store.state.roots);
    assert.match(json, /"id":"current"/);
    assert.doesNotMatch(json, /"id":"home"/);
  } finally {
    store.dispose();
  }
});

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

test("live-sync applies its tree when no refresh intervened", async () => {
  const device = new FakeDevice();
  const store = liveStore(device);
  try {
    await store._syncNow();
    assert.equal(store.state.roots[0].id, "root-1");
  } finally {
    store.dispose();
  }
});

test("live-sync discards a tree fetched before a refresh published a newer one", async () => {
  // The entry guard only proves no refresh was running when the sync STARTED. Here a
  // mutation's refresh lands mid-flight, so the sync's snapshot is stale by the time it
  // resumes; re-applying it would regress the panel to the pre-mutation tree under a
  // higher rev (which the browser accepts, since it only drops older revs).
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
    if (call !== 1) return delay(5);
    markStarted();
    return firstGate;
  };
  const store = liveStore(device);
  try {
    const sync = store._syncNow(); // fetches root-1, then parks
    await started;

    await store.refresh({ shot: false }); // the post-mutation pull: applies root-2
    assert.equal(store.state.roots[0].id, "root-2");
    const afterRefresh = store._rev;

    releaseFirst();
    await sync;

    assert.equal(store.state.roots[0].id, "root-2", "stale sync must not regress the tree");
    assert.equal(store._rev, afterRefresh, "a discarded sync must not publish a new revision");
  } finally {
    store.dispose();
  }
});
