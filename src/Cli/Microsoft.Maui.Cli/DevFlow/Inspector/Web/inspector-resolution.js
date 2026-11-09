export const SCREENSHOT_SCALE_BUCKETS = Object.freeze([0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4]);
export const SCREENSHOT_PIXEL_BUDGET = 8_388_608;

const DEFAULT_SCALE = 1;
const SCALE_TOLERANCE = 1.1;

export function selectScreenshotScaleBucket({
  logicalWidth,
  logicalHeight,
  renderedWidth,
  renderedHeight,
  devicePixelRatio = 1,
  maxPixels = SCREENSHOT_PIXEL_BUDGET,
}) {
  if (![logicalWidth, logicalHeight, renderedWidth, renderedHeight, devicePixelRatio, maxPixels]
    .every(value => Number.isFinite(value) && value > 0)) {
    return DEFAULT_SCALE;
  }

  const renderedScale = Math.min(renderedWidth / logicalWidth, renderedHeight / logicalHeight);
  const idealScale = renderedScale * devicePixelRatio;
  const budgetScale = Math.sqrt(maxPixels / (logicalWidth * logicalHeight));
  const maximumScale = Math.min(SCREENSHOT_SCALE_BUCKETS.at(-1), budgetScale);
  const toleratedScale = Math.min(maximumScale, idealScale / SCALE_TOLERANCE);

  let selected = SCREENSHOT_SCALE_BUCKETS[0];
  for (const bucket of SCREENSHOT_SCALE_BUCKETS) {
    if (bucket > maximumScale) break;
    selected = bucket;
    if (bucket >= toleratedScale) break;
  }
  return selected;
}

export function selectNativeAwareScreenshotScaleBucket(desiredScale, nativeMaximumScale) {
  if (![desiredScale, nativeMaximumScale]
    .every(value => Number.isFinite(value) && value > 0)) {
    return DEFAULT_SCALE;
  }

  const maximumScale = Math.min(desiredScale, nativeMaximumScale);
  return SCREENSHOT_SCALE_BUCKETS.find(bucket => bucket >= maximumScale)
    ?? SCREENSHOT_SCALE_BUCKETS.at(-1);
}

export function isDefaultScreenshotScale(scale) {
  return scale === DEFAULT_SCALE;
}
