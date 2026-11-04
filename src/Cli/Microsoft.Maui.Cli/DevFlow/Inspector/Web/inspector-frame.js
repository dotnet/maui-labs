export const CoordinateSpace = Object.freeze({
  appLogical: 'appLogical',
  windowLogical: 'windowLogical',
  screenshotPixel: 'screenshotPixel',
  inspectorCss: 'inspectorCss',
  transformedViewport: 'transformedViewport',
});

const coordinateSpaces = new Set(Object.values(CoordinateSpace));
const actionSources = new Set(['inspector', 'cli', 'mcp', 'replay']);

function objectOrEmpty(value) {
  return value && typeof value === 'object' ? value : {};
}

function finiteNumber(...values) {
  for (const value of values) {
    const number = Number(value);
    if (Number.isFinite(number)) return number;
  }
  return 0;
}

function positiveNumber(...values) {
  for (const value of values) {
    const number = Number(value);
    if (Number.isFinite(number) && number > 0) return number;
  }
  return 0;
}

function safeInteger(value, minimum) {
  const number = Number(value);
  return Number.isSafeInteger(number) && number >= minimum ? number : null;
}

function optionalString(value) {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function frameIdFromScreenshotUrl(url) {
  if (typeof url !== 'string' || url.length === 0) return null;
  try {
    return new URL(url, 'http://localhost/').searchParams.get('frame');
  } catch {
    return null;
  }
}

export function hasExplicitFrameIdentity(state) {
  const stateObject = objectOrEmpty(state);
  return (stateObject.frame && typeof stateObject.frame === 'object')
    || optionalString(stateObject.frameId) !== null
    || optionalString(stateObject.screenshotUrl) !== null;
}

function deepFreeze(value, seen = new WeakSet()) {
  if (!value || typeof value !== 'object' || seen.has(value)) return value;
  seen.add(value);
  for (const child of Object.values(value)) deepFreeze(child, seen);
  return Object.freeze(value);
}

export function normalizeFrameDescriptor(state, fallback = {}) {
  const stateObject = objectOrEmpty(state);
  const fallbackObject = objectOrEmpty(fallback);
  const raw = objectOrEmpty(stateObject.frame || stateObject);
  const fallbackFrame = objectOrEmpty(fallbackObject.frame || fallbackObject);
  const explicitFrameId = optionalString(
    raw.frameId
    ?? stateObject.frameId
    ?? frameIdFromScreenshotUrl(stateObject.screenshotUrl),
  );
  const fallbackFrameId = optionalString(
    fallbackFrame.frameId
    ?? fallbackObject.frameId
    ?? frameIdFromScreenshotUrl(fallbackObject.screenshotUrl),
  );
  const canInheritFrameMetadata = !explicitFrameId || explicitFrameId === fallbackFrameId;

  const pixelWidth = positiveNumber(
    raw.pixelWidth,
    stateObject.pixelWidth,
    raw.logicalWidth,
    stateObject.viewportWidth,
    canInheritFrameMetadata ? fallbackFrame.pixelWidth : null,
    canInheritFrameMetadata ? fallbackObject.pixelWidth : null,
    canInheritFrameMetadata ? fallbackFrame.logicalWidth : null,
    canInheritFrameMetadata ? fallbackObject.viewportWidth : null,
  ) || 1;
  const pixelHeight = positiveNumber(
    raw.pixelHeight,
    stateObject.pixelHeight,
    raw.logicalHeight,
    stateObject.viewportHeight,
    canInheritFrameMetadata ? fallbackFrame.pixelHeight : null,
    canInheritFrameMetadata ? fallbackObject.pixelHeight : null,
    canInheritFrameMetadata ? fallbackFrame.logicalHeight : null,
    canInheritFrameMetadata ? fallbackObject.viewportHeight : null,
  ) || 1;
  const logicalWidth = positiveNumber(
    raw.logicalWidth,
    stateObject.logicalWidth,
    stateObject.viewportWidth,
    canInheritFrameMetadata ? fallbackFrame.logicalWidth : null,
    canInheritFrameMetadata ? fallbackObject.logicalWidth : null,
    canInheritFrameMetadata ? fallbackObject.viewportWidth : null,
    pixelWidth,
  ) || 1;
  const logicalHeight = positiveNumber(
    raw.logicalHeight,
    stateObject.logicalHeight,
    stateObject.viewportHeight,
    canInheritFrameMetadata ? fallbackFrame.logicalHeight : null,
    canInheritFrameMetadata ? fallbackObject.logicalHeight : null,
    canInheritFrameMetadata ? fallbackObject.viewportHeight : null,
    pixelHeight,
  ) || 1;
  const scaleX = pixelWidth / logicalWidth;
  const scaleY = pixelHeight / logicalHeight;
  const captureScale = positiveNumber(
    raw.captureScale,
    stateObject.captureScale,
    canInheritFrameMetadata ? fallbackFrame.captureScale : null,
    canInheritFrameMetadata ? fallbackObject.captureScale : null,
    Math.abs(scaleX - scaleY) <= Math.max(scaleX, scaleY) * 0.005 ? scaleX : 1,
  ) || 1;

  return deepFreeze({
    streamId: optionalString(
      raw.streamId
      ?? stateObject.streamId
      ?? (canInheritFrameMetadata ? fallbackFrame.streamId ?? fallbackObject.streamId : null),
    ),
    frameId: explicitFrameId ?? fallbackFrameId,
    frameRevision: safeInteger(
      raw.frameRevision
      ?? stateObject.frameRevision
      ?? (canInheritFrameMetadata ? fallbackFrame.frameRevision ?? fallbackObject.frameRevision : null),
      0,
    ) ?? 0,
    captureEpoch: safeInteger(
      raw.captureEpoch
      ?? stateObject.captureEpoch
      ?? (canInheritFrameMetadata ? fallbackFrame.captureEpoch ?? fallbackObject.captureEpoch : null),
      1,
    ),
    registryGeneration: safeInteger(
      raw.registryGeneration
      ?? stateObject.registryGeneration
      ?? (canInheritFrameMetadata
        ? fallbackFrame.registryGeneration ?? fallbackObject.registryGeneration
        : null),
      0,
    ),
    windowId: safeInteger(
      raw.windowId
      ?? stateObject.windowId
      ?? (canInheritFrameMetadata ? fallbackFrame.windowId ?? fallbackObject.windowId : null),
      0,
    ),
    treeRevision: optionalString(
      raw.treeRevision
      ?? stateObject.treeRevision
      ?? (canInheritFrameMetadata ? fallbackFrame.treeRevision ?? fallbackObject.treeRevision : null),
    ) ?? '',
    diagnosticInputsRevision: optionalString(
      raw.diagnosticInputsRevision
      ?? stateObject.diagnosticInputsRevision
      ?? (canInheritFrameMetadata
        ? fallbackFrame.diagnosticInputsRevision ?? fallbackObject.diagnosticInputsRevision
        : null),
    ),
    logicalWidth,
    logicalHeight,
    pixelWidth,
    pixelHeight,
    rootOffsetX: finiteNumber(
      raw.rootOffsetX,
      stateObject.rootOffsetX,
      canInheritFrameMetadata ? fallbackFrame.rootOffsetX : null,
      canInheritFrameMetadata ? fallbackObject.rootOffsetX : null,
    ),
    rootOffsetY: finiteNumber(
      raw.rootOffsetY,
      stateObject.rootOffsetY,
      canInheritFrameMetadata ? fallbackFrame.rootOffsetY : null,
      canInheritFrameMetadata ? fallbackObject.rootOffsetY : null,
    ),
    captureScale,
    createdAt: optionalString(
      raw.createdAt
      ?? stateObject.createdAt
      ?? (canInheritFrameMetadata ? fallbackFrame.createdAt ?? fallbackObject.createdAt : null),
    ),
  });
}

export function shouldCommitFrame(committed, candidate) {
  if (!candidate) return false;
  if (!committed) return true;

  if (candidate.streamId && committed.streamId) {
    if (candidate.streamId !== committed.streamId) return true;
    if (candidate.frameRevision !== committed.frameRevision) {
      return candidate.frameRevision > committed.frameRevision;
    }
    return false;
  }

  if (candidate.frameId && committed.frameId) {
    return candidate.frameId !== committed.frameId;
  }

  return true;
}

export function appLogicalToWindowLogical(point, frame) {
  return {
    x: Number(point.x) + frame.rootOffsetX,
    y: Number(point.y) + frame.rootOffsetY,
  };
}

export function windowLogicalToAppLogical(point, frame) {
  return {
    x: Number(point.x) - frame.rootOffsetX,
    y: Number(point.y) - frame.rootOffsetY,
  };
}

export function appLogicalToScreenshotPixel(point, frame) {
  return {
    x: Number(point.x) * frame.pixelWidth / frame.logicalWidth,
    y: Number(point.y) * frame.pixelHeight / frame.logicalHeight,
  };
}

export function screenshotPixelToAppLogical(point, frame) {
  return {
    x: Number(point.x) * frame.logicalWidth / frame.pixelWidth,
    y: Number(point.y) * frame.logicalHeight / frame.pixelHeight,
  };
}

export function inspectorCssToAppLogical(point) {
  return { x: Number(point.x), y: Number(point.y) };
}

export function appLogicalToInspectorCss(point) {
  return { x: Number(point.x), y: Number(point.y) };
}

export function transformedViewportToInspectorCss(point, transform) {
  const scale = positiveNumber(transform?.scale) || 1;
  const originX = finiteNumber(transform?.originX);
  const originY = finiteNumber(transform?.originY);
  const translateX = finiteNumber(transform?.translateX);
  const translateY = finiteNumber(transform?.translateY);
  return {
    x: (Number(point.x) - originX - translateX) / scale,
    y: (Number(point.y) - originY - translateY) / scale,
  };
}

export function inspectorCssToTransformedViewport(point, transform) {
  const scale = positiveNumber(transform?.scale) || 1;
  const originX = finiteNumber(transform?.originX);
  const originY = finiteNumber(transform?.originY);
  const translateX = finiteNumber(transform?.translateX);
  const translateY = finiteNumber(transform?.translateY);
  return {
    x: originX + translateX + Number(point.x) * scale,
    y: originY + translateY + Number(point.y) * scale,
  };
}

export function clientToAppLogical(clientX, clientY, renderedViewport, frame) {
  const width = positiveNumber(renderedViewport?.width) || frame.logicalWidth;
  const height = positiveNumber(renderedViewport?.height) || frame.logicalHeight;
  return {
    x: (Number(clientX) - finiteNumber(renderedViewport?.left)) * frame.logicalWidth / width,
    y: (Number(clientY) - finiteNumber(renderedViewport?.top)) * frame.logicalHeight / height,
  };
}

export function buildActionCorrelation(
  frame,
  coordinateSpace = CoordinateSpace.appLogical,
  source = 'inspector',
  actionId = null,
) {
  if (!frame?.frameId) return null;
  const normalizedSource = actionSources.has(source) ? source : 'inspector';
  const normalizedSpace = coordinateSpaces.has(coordinateSpace)
    ? coordinateSpace
    : CoordinateSpace.appLogical;
  const generatedActionId = optionalString(actionId)
    ?? globalThis.crypto?.randomUUID?.()
    ?? `action-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;

  const correlation = {
    actionId: generatedActionId,
    source: normalizedSource,
    frameId: frame.frameId,
    coordinateSpace: normalizedSpace,
  };
  if (frame.streamId) correlation.streamId = frame.streamId;
  if (frame.frameRevision > 0) correlation.frameRevision = frame.frameRevision;
  return deepFreeze(correlation);
}
