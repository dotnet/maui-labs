import test from 'node:test';
import assert from 'node:assert/strict';
import {
  SCREENSHOT_PIXEL_BUDGET,
  isDefaultScreenshotScale,
  selectScreenshotScaleBucket,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-resolution.js';

const request = overrides => ({
  logicalWidth: 400,
  logicalHeight: 300,
  renderedWidth: 400,
  renderedHeight: 300,
  devicePixelRatio: 1,
  maxPixels: SCREENSHOT_PIXEL_BUDGET,
  ...overrides,
});

test('resolution buckets combine rendered scale and device pixel ratio', () => {
  assert.equal(selectScreenshotScaleBucket(request({ devicePixelRatio: 2 })), 2);
  assert.equal(selectScreenshotScaleBucket(request({ renderedWidth: 160, renderedHeight: 120 })), 0.5);
  assert.equal(selectScreenshotScaleBucket(request({
    renderedWidth: 220,
    renderedHeight: 165,
    devicePixelRatio: 2,
  })), 1);
});

test('resolution buckets tolerate small changes around an existing quality level', () => {
  assert.equal(selectScreenshotScaleBucket(request({
    renderedWidth: 440,
    renderedHeight: 330,
  })), 1);
  assert.equal(selectScreenshotScaleBucket(request({
    renderedWidth: 480,
    renderedHeight: 360,
  })), 1.5);
});

test('resolution buckets honor the decoded pixel budget', () => {
  assert.equal(selectScreenshotScaleBucket({
    logicalWidth: 4000,
    logicalHeight: 3000,
    renderedWidth: 4000,
    renderedHeight: 3000,
    devicePixelRatio: 2,
    maxPixels: SCREENSHOT_PIXEL_BUDGET,
  }), 0.75);
});

test('resolution buckets use the legacy 1x default for invalid geometry', () => {
  assert.equal(selectScreenshotScaleBucket(request({ renderedWidth: 0 })), 1);
  assert.equal(isDefaultScreenshotScale(1), true);
  assert.equal(isDefaultScreenshotScale(2), false);
});
