export const VIEWPORT_MIN_SCALE = 0.25;
export const VIEWPORT_MAX_SCALE = 4;
export const VIEWPORT_SCALE_STEPS = Object.freeze([
  0.25,
  0.5,
  0.75,
  1,
  1.25,
  1.5,
  2,
  3,
  4,
]);

function finiteOr(value, fallback) {
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function positiveOr(value, fallback) {
  const number = finiteOr(value, fallback);
  return number > 0 ? number : fallback;
}

export function clampViewportScale(scale) {
  return Math.min(VIEWPORT_MAX_SCALE, Math.max(VIEWPORT_MIN_SCALE, finiteOr(scale, 1)));
}

export function fitViewportScale(bounds) {
  const contentWidth = positiveOr(bounds?.contentWidth, 1);
  const contentHeight = positiveOr(bounds?.contentHeight, 1);
  const viewportWidth = positiveOr(bounds?.viewportWidth, contentWidth);
  const viewportHeight = positiveOr(bounds?.viewportHeight, contentHeight);
  const scale = Math.min(viewportWidth / contentWidth, viewportHeight / contentHeight, 1);
  return Number.isFinite(scale) && scale > 0 ? scale : 1;
}

function clampAxisTranslation(contentSize, viewportSize, scale, translation, centerSmallContent) {
  const scaledContentSize = contentSize * scale;
  if (scaledContentSize <= viewportSize) {
    const availableSpace = viewportSize - scaledContentSize;
    return centerSmallContent
      ? availableSpace / 2
      : Math.min(availableSpace, Math.max(0, translation));
  }
  return Math.min(0, Math.max(viewportSize - scaledContentSize, translation));
}

export function clampViewportTransform(transform, bounds, options = {}) {
  const contentWidth = positiveOr(bounds?.contentWidth, 1);
  const contentHeight = positiveOr(bounds?.contentHeight, 1);
  const viewportWidth = positiveOr(bounds?.viewportWidth, contentWidth);
  const viewportHeight = positiveOr(bounds?.viewportHeight, contentHeight);
  const scale = positiveOr(transform?.scale, 1);
  const translateX = finiteOr(transform?.translateX, 0);
  const translateY = finiteOr(transform?.translateY, 0);
  const centerSmallContent = options.centerSmallContent !== false;
  return {
    scale,
    translateX: clampAxisTranslation(
      contentWidth,
      viewportWidth,
      scale,
      translateX,
      centerSmallContent),
    translateY: clampAxisTranslation(
      contentHeight,
      viewportHeight,
      scale,
      translateY,
      centerSmallContent),
  };
}

export function centerViewportTransform(scale, bounds) {
  const contentWidth = positiveOr(bounds?.contentWidth, 1);
  const contentHeight = positiveOr(bounds?.contentHeight, 1);
  const viewportWidth = positiveOr(bounds?.viewportWidth, contentWidth);
  const viewportHeight = positiveOr(bounds?.viewportHeight, contentHeight);
  return clampViewportTransform({
    scale,
    translateX: (viewportWidth - contentWidth * scale) / 2,
    translateY: (viewportHeight - contentHeight * scale) / 2,
  }, bounds);
}

export function preserveViewportCenter(transform, previousBounds, nextBounds, options = {}) {
  const scale = positiveOr(transform?.scale, 1);
  const previousWidth = positiveOr(previousBounds?.viewportWidth, 1);
  const previousHeight = positiveOr(previousBounds?.viewportHeight, 1);
  const nextWidth = positiveOr(nextBounds?.viewportWidth, previousWidth);
  const nextHeight = positiveOr(nextBounds?.viewportHeight, previousHeight);
  const contentCenterX = (previousWidth / 2 - finiteOr(transform?.translateX, 0)) / scale;
  const contentCenterY = (previousHeight / 2 - finiteOr(transform?.translateY, 0)) / scale;
  return clampViewportTransform({
    scale,
    translateX: nextWidth / 2 - contentCenterX * scale,
    translateY: nextHeight / 2 - contentCenterY * scale,
  }, nextBounds, options);
}

export function stepViewportScale(scale, direction) {
  const numericScale = Number(scale);
  const current = Number.isFinite(numericScale) && numericScale > 0 ? numericScale : 1;
  if (direction > 0) {
    return VIEWPORT_SCALE_STEPS.find(step => step > current + 0.0001) ?? VIEWPORT_MAX_SCALE;
  }
  if (direction < 0) {
    return [...VIEWPORT_SCALE_STEPS].reverse().find(step => step < current - 0.0001)
      ?? VIEWPORT_MIN_SCALE;
  }
  return current;
}
