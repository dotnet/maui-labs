import assert from 'node:assert/strict';
import test from 'node:test';
import {
  VIEWPORT_MAX_SCALE,
  VIEWPORT_MIN_SCALE,
  centerViewportTransform,
  clampViewportScale,
  clampViewportTransform,
  fitViewportScale,
  preserveViewportCenter,
  stepViewportScale,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-viewport.js';

test('fit scale preserves the current no-upscale behavior', () => {
  assert.equal(fitViewportScale({
    contentWidth: 400,
    contentHeight: 800,
    viewportWidth: 200,
    viewportHeight: 600,
  }), 0.5);
  assert.equal(fitViewportScale({
    contentWidth: 200,
    contentHeight: 300,
    viewportWidth: 1000,
    viewportHeight: 1000,
  }), 1);
  assert.equal(fitViewportScale({
    contentWidth: 0,
    contentHeight: Number.NaN,
    viewportWidth: 0,
    viewportHeight: Number.NaN,
  }), 1);
});

test('custom zoom stays inside supported bounds and follows stable steps', () => {
  assert.equal(clampViewportScale(0), VIEWPORT_MIN_SCALE);
  assert.equal(clampViewportScale(10), VIEWPORT_MAX_SCALE);
  assert.equal(stepViewportScale(1, 1), 1.25);
  assert.equal(stepViewportScale(1, -1), 0.75);
  assert.equal(stepViewportScale(0.9, 1), 1);
  assert.equal(stepViewportScale(0.9, -1), 0.75);
  assert.equal(stepViewportScale(0.1, 1), VIEWPORT_MIN_SCALE);
  assert.equal(stepViewportScale(0.1, -1), VIEWPORT_MIN_SCALE);
  assert.equal(stepViewportScale(VIEWPORT_MAX_SCALE, 1), VIEWPORT_MAX_SCALE);
  assert.equal(stepViewportScale(VIEWPORT_MIN_SCALE, -1), VIEWPORT_MIN_SCALE);
});

test('translation centers small content and clamps enlarged content to every edge', () => {
  const bounds = {
    contentWidth: 400,
    contentHeight: 300,
    viewportWidth: 800,
    viewportHeight: 600,
  };
  assert.deepEqual(clampViewportTransform({
    scale: 1,
    translateX: -500,
    translateY: 500,
  }, bounds), {
    scale: 1,
    translateX: 200,
    translateY: 150,
  });
  assert.deepEqual(clampViewportTransform({
    scale: 1,
    translateX: 120,
    translateY: 40,
  }, bounds, { centerSmallContent: false }), {
    scale: 1,
    translateX: 120,
    translateY: 40,
  });

  const enlarged = { ...bounds, viewportWidth: 300, viewportHeight: 200 };
  assert.deepEqual(clampViewportTransform({
    scale: 2,
    translateX: 100,
    translateY: 100,
  }, enlarged), {
    scale: 2,
    translateX: 0,
    translateY: 0,
  });
  assert.deepEqual(clampViewportTransform({
    scale: 2,
    translateX: -1000,
    translateY: -1000,
  }, enlarged), {
    scale: 2,
    translateX: -500,
    translateY: -400,
  });
});

test('centering and resize preservation keep the same content point visible', () => {
  const previousBounds = {
    contentWidth: 1000,
    contentHeight: 800,
    viewportWidth: 500,
    viewportHeight: 400,
  };
  const centered = centerViewportTransform(1, previousBounds);
  assert.deepEqual(centered, { scale: 1, translateX: -250, translateY: -200 });

  const contentX = (previousBounds.viewportWidth / 2 - centered.translateX) / centered.scale;
  const contentY = (previousBounds.viewportHeight / 2 - centered.translateY) / centered.scale;
  const nextBounds = { ...previousBounds, viewportWidth: 700, viewportHeight: 500 };
  const resized = preserveViewportCenter(centered, previousBounds, nextBounds);
  assert.equal((nextBounds.viewportWidth / 2 - resized.translateX) / resized.scale, contentX);
  assert.equal((nextBounds.viewportHeight / 2 - resized.translateY) / resized.scale, contentY);
});

test('custom placement is stable across unchanged frame commits', () => {
  const bounds = {
    contentWidth: 400,
    contentHeight: 300,
    viewportWidth: 900,
    viewportHeight: 700,
  };
  const anchored = { scale: 1.5, translateX: 120, translateY: 80 };
  assert.deepEqual(
    preserveViewportCenter(
      anchored,
      bounds,
      bounds,
      { centerSmallContent: false }),
    anchored);
});
