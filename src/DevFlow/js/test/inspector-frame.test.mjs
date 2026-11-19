import assert from "node:assert/strict";
import test from "node:test";
import {
  CoordinateSpace,
  appLogicalToScreenshotPixel,
  appLogicalToWindowLogical,
  buildActionCorrelation,
  clientToAppLogical,
  hasExplicitFrameIdentity,
  inspectorCssToTransformedViewport,
  normalizeFrameDescriptor,
  screenshotPixelToAppLogical,
  shouldCommitFrame,
  transformedViewportToInspectorCss,
  windowLogicalToAppLogical,
} from "../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-frame.js";

const frame = normalizeFrameDescriptor({
  frame: {
    streamId: "stream-a",
    frameId: "frame-a",
    frameRevision: 7,
    logicalWidth: 400,
    logicalHeight: 300,
    pixelWidth: 800,
    pixelHeight: 600,
    rootOffsetX: 12,
    rootOffsetY: 24,
    nativeScale: 3,
    requestedCaptureScale: 2,
    resolutionClamped: true,
    createdAt: "2026-11-04T12:00:00Z",
  },
});

test("normalizes legacy frame state with conservative defaults", () => {
  const legacy = normalizeFrameDescriptor({
    frameId: "legacy",
    viewportWidth: 320,
    viewportHeight: 640,
    rootOffsetX: 3,
    rootOffsetY: 9,
    screenshotUrl: "screenshot.png?frame=legacy",
  });

  assert.equal(legacy.frameId, "legacy");
  assert.equal(legacy.logicalWidth, 320);
  assert.equal(legacy.logicalHeight, 640);
  assert.equal(legacy.pixelWidth, 320);
  assert.equal(legacy.pixelHeight, 640);
  assert.equal(legacy.captureScale, 1);
  assert.equal(legacy.nativeScale, null);
  assert.equal(legacy.requestedCaptureScale, 1);
  assert.equal(legacy.resolutionClamped, null);
  assert.equal(legacy.frameRevision, 0);
  assert.equal(legacy.streamId, null);
});

test("normalizes negotiated screenshot metadata inside the immutable descriptor", () => {
  assert.equal(frame.nativeScale, 3);
  assert.equal(frame.requestedCaptureScale, 2);
  assert.equal(frame.resolutionClamped, true);
});

test("normalization deeply freezes the immutable descriptor", () => {
  assert.equal(Object.isFrozen(frame), true);
  assert.throws(() => {
    frame.logicalWidth = 1;
  }, TypeError);
});

test("frame ordering is scoped to a stream", () => {
  const older = normalizeFrameDescriptor({ frame: { ...frame, frameId: "older", frameRevision: 6 } });
  const newer = normalizeFrameDescriptor({ frame: { ...frame, frameId: "newer", frameRevision: 8 } });
  const restarted = normalizeFrameDescriptor({
    frame: { ...frame, streamId: "stream-b", frameId: "restart", frameRevision: 1 },
  });

  assert.equal(shouldCommitFrame(frame, older), false);
  assert.equal(shouldCommitFrame(frame, frame), false);
  assert.equal(shouldCommitFrame(frame, newer), true);
  assert.equal(shouldCommitFrame(frame, restarted), true);
});

test("legacy states do not inherit ordering identity from a different frame", () => {
  const legacy = normalizeFrameDescriptor({
    frameId: "legacy-next",
    viewportWidth: 400,
    viewportHeight: 300,
  }, frame);

  assert.equal(legacy.streamId, null);
  assert.equal(legacy.frameRevision, 0);
  assert.equal(legacy.pixelWidth, 400);
  assert.equal(legacy.rootOffsetX, 0);
  assert.equal(shouldCommitFrame(frame, legacy), true);
});

test("only explicit frame identity enforces immutable commit ordering", () => {
  assert.equal(hasExplicitFrameIdentity({
    viewportWidth: 400,
    viewportHeight: 300,
    treeRevision: "legacy-refresh",
    elements: "<div></div>",
  }), false);
  assert.equal(hasExplicitFrameIdentity({ screenshotUrl: "screenshot.png" }), false);
  assert.equal(hasExplicitFrameIdentity({ frame: {} }), true);
  assert.equal(hasExplicitFrameIdentity({ frameId: "frame-a" }), true);
  assert.equal(hasExplicitFrameIdentity({ screenshotUrl: "screenshot.png?frame=frame-a" }), true);
  assert.equal(shouldCommitFrame(frame, normalizeFrameDescriptor({ frame: {} }, frame)), false);
});

test("identity-less refreshes drop stale correlation identity but keep current geometry defaults", () => {
  const legacy = normalizeFrameDescriptor({
    screenshotUrl: "screenshot.png",
    rootOffsetX: 30,
    treeRevision: "legacy-refresh",
  }, frame);

  assert.equal(legacy.frameId, null);
  assert.equal(legacy.streamId, null);
  assert.equal(legacy.frameRevision, 0);
  assert.equal(legacy.rootOffsetX, 30);
  assert.equal(legacy.logicalWidth, 400);
  assert.equal(legacy.treeRevision, "legacy-refresh");
  assert.equal(buildActionCorrelation(legacy), null);
});

test("identity-less refreshes prefer current viewport dimensions over fallback geometry", () => {
  const identified = normalizeFrameDescriptor({
    frame: {
      ...frame,
      windowId: 7,
      treeRevision: "tree-a",
      diagnosticInputsRevision: "layout-a",
      captureScale: 3,
    },
  });
  const legacy = normalizeFrameDescriptor({
    viewportWidth: 800,
    viewportHeight: 600,
  }, identified);

  assert.equal(legacy.logicalWidth, 800);
  assert.equal(legacy.logicalHeight, 600);
  assert.equal(legacy.pixelWidth, 800);
  assert.equal(legacy.pixelHeight, 600);
  assert.equal(legacy.captureScale, 1);
  assert.equal(legacy.windowId, null);
  assert.equal(legacy.treeRevision, "");
  assert.equal(legacy.diagnosticInputsRevision, null);
});

test("coordinate conversions round-trip root offsets and capture scale", () => {
  const app = { x: 25, y: 40 };
  const window = appLogicalToWindowLogical(app, frame);
  assert.deepEqual(window, { x: 37, y: 64 });
  assert.deepEqual(windowLogicalToAppLogical(window, frame), app);

  const pixel = appLogicalToScreenshotPixel(app, frame);
  assert.deepEqual(pixel, { x: 50, y: 80 });
  assert.deepEqual(screenshotPixelToAppLogical(pixel, frame), app);
});

test("client conversion covers fit, 1:1, and translated viewport rectangles", () => {
  assert.deepEqual(
    clientToAppLogical(210, 170, { left: 10, top: 20, width: 400, height: 300 }, frame),
    { x: 200, y: 150 },
  );
  assert.deepEqual(
    clientToAppLogical(110, 95, { left: 10, top: 20, width: 200, height: 150 }, frame),
    { x: 200, y: 150 },
  );
  assert.deepEqual(
    clientToAppLogical(360, 245, { left: 260, top: 170, width: 200, height: 150 }, frame),
    { x: 200, y: 150 },
  );
});

test("viewport scale and translation round-trip without UI policy", () => {
  const transform = {
    originX: 100,
    originY: 50,
    scale: 1.5,
    translateX: 30,
    translateY: -10,
  };
  const css = { x: 80, y: 40 };
  const transformed = inspectorCssToTransformedViewport(css, transform);
  assert.deepEqual(transformed, { x: 250, y: 100 });
  assert.deepEqual(transformedViewportToInspectorCss(transformed, transform), css);
});

test("action correlation references the committed frame without duplicating coordinates", () => {
  const correlation = buildActionCorrelation(
    frame,
    CoordinateSpace.appLogical,
    "inspector",
    "action-1",
  );

  assert.deepEqual(correlation, {
    actionId: "action-1",
    source: "inspector",
    streamId: "stream-a",
    frameId: "frame-a",
    frameRevision: 7,
    coordinateSpace: "appLogical",
  });
  assert.equal(Object.isFrozen(correlation), true);
});
