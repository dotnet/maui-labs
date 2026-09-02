import { test } from "node:test";
import assert from "node:assert/strict";
import { toRoots, buildQuery, directionFromPoints, requests } from "../src/agent.js";

test("toRoots: array passthrough", () => {
  const arr = [{ id: "1", type: "A" }];
  assert.deepEqual(toRoots(arr), arr);
});

test("toRoots: unwraps {elements}, {tree}, {root}", () => {
  assert.equal(toRoots({ elements: [{ id: "1" }] }).length, 1);
  assert.equal(toRoots({ tree: [{ id: "1" }, { id: "2" }] }).length, 2);
  assert.equal(toRoots({ tree: { id: "1" } }).length, 1);
  assert.equal(toRoots({ root: { id: "1" } }).length, 1);
});

test("toRoots: bare node → single-element array", () => {
  assert.equal(toRoots({ id: "x", type: "Button" }).length, 1);
});

test("toRoots: null/empty → []", () => {
  assert.deepEqual(toRoots(null), []);
  assert.deepEqual(toRoots(undefined), []);
  assert.deepEqual(toRoots({}), []);
});

test("buildQuery: skips undefined/null, encodes, prefixes ?", () => {
  assert.equal(buildQuery({ a: 1, b: undefined, c: null, d: "x y" }), "?a=1&d=x%20y");
  assert.equal(buildQuery({}), "");
  assert.equal(buildQuery({ a: undefined }), "");
  assert.equal(buildQuery({ selector: "Button.primary" }), "?selector=Button.primary");
});

test("directionFromPoints: derives swipe direction + distance", () => {
  assert.equal(directionFromPoints([{ x: 0, y: 0 }, { x: 50, y: 2 }])?.direction, "right");
  assert.equal(directionFromPoints([{ x: 50, y: 0 }, { x: 0, y: 2 }])?.direction, "left");
  assert.equal(directionFromPoints([{ x: 0, y: 0 }, { x: 2, y: 50 }])?.direction, "down");
  assert.equal(directionFromPoints([{ x: 0, y: 50 }, { x: 2, y: 0 }])?.direction, "up");
  assert.equal(directionFromPoints([{ x: 0, y: 0 }, { x: 3, y: 4 }])?.distance, 5);
  assert.equal(directionFromPoints([{ x: 0, y: 0 }]), null);
});

test("getProperty parse: value → result → [name] → raw string fallbacks", () => {
  const p = (d: unknown) => requests.getProperty("1", "Text").parse(d);
  assert.equal(p({ value: "a" }), "a");
  assert.equal(p({ result: "b" }), "b");
  assert.equal(p({ Text: "c" }), "c");
  assert.equal(p("raw"), "raw");
  assert.equal(p({ other: 1 }), null);
  assert.equal(p(null), null);
});
