import {
  inspectorCssToTransformedViewport,
  transformedViewportToInspectorCss,
} from './inspector-frame.js';
import {
  VIEWPORT_MAX_SCALE,
  VIEWPORT_MIN_SCALE,
  centerViewportTransform,
  clampViewportScale,
  clampViewportTransform,
  fitViewportScale,
  preserveViewportCenter,
  stepViewportScale,
} from './inspector-viewport.js';

const PAN_STEP = 40;
const PAN_STEP_LARGE = 120;
const ZOOM_EPSILON = 0.0001;

function sameTransform(left, right) {
  return Math.abs(left.scale - right.scale) < ZOOM_EPSILON
    && Math.abs(left.translateX - right.translateX) < ZOOM_EPSILON
    && Math.abs(left.translateY - right.translateY) < ZOOM_EPSILON;
}

function sameBounds(left, right) {
  return left.contentWidth === right.contentWidth
    && left.contentHeight === right.contentHeight
    && left.viewportWidth === right.viewportWidth
    && left.viewportHeight === right.viewportHeight;
}

function isEditableTarget(target, windowObject) {
  if (!(target instanceof windowObject.HTMLElement)) return false;
  return !!target.closest('input, textarea, select, button, [contenteditable="true"], [role="button"]');
}

export function createViewportController(options) {
  const {
    document,
    window,
    viewport,
    stage,
    wrap,
    fitButton,
    zoomOutButton,
    zoomInButton,
    zoomPreset,
    zoomIndicator,
    zoomAnnouncement,
    isEditing = () => false,
    isCommandEnabled = () => true,
    canNavigate = () => true,
    onNavigationBlocked = () => {},
    onTransformChanged = () => {},
  } = options;

  const customOption = zoomPreset?.querySelector('option[value="custom"]') || null;
  const view = {
    mode: 'fit',
    transform: { scale: 1, translateX: 0, translateY: 0 },
  };
  let lastBounds = null;
  let layoutFrame = 0;
  let announcementTimer = 0;
  let spacePanArmed = false;
  let panSession = null;
  let suppressedClick = null;

  function readBounds() {
    return {
      contentWidth: parseFloat(viewport.dataset.width) || viewport.offsetWidth || 1,
      contentHeight: parseFloat(viewport.dataset.height) || viewport.offsetHeight || 1,
      viewportWidth: wrap.clientWidth || 1,
      viewportHeight: wrap.clientHeight || 1,
    };
  }

  function snapToDevicePixel(value) {
    const density = Number(window.devicePixelRatio) || 1;
    return Math.round(value * density) / density;
  }

  function renderedTransform() {
    const origin = stage.getBoundingClientRect();
    const left = snapToDevicePixel(origin.left + view.transform.translateX);
    const top = snapToDevicePixel(origin.top + view.transform.translateY);
    return {
      originX: origin.left,
      originY: origin.top,
      scale: view.transform.scale,
      translateX: left - origin.left,
      translateY: top - origin.top,
    };
  }

  function announceZoom() {
    if (!zoomAnnouncement) return;
    if (announcementTimer) window.clearTimeout(announcementTimer);
    zoomAnnouncement.textContent = '';
    announcementTimer = window.setTimeout(() => {
      announcementTimer = 0;
      const percent = Math.round(view.transform.scale * 100);
      zoomAnnouncement.textContent = view.mode === 'fit'
        ? `Fit view, ${percent} percent`
        : `Viewport zoom ${percent} percent`;
    }, 140);
  }

  function commandEnabled(id) {
    return isCommandEnabled(id) !== false;
  }

  function syncActionButton(button, limited = false) {
    if (!button) return;
    button.setAttribute('aria-disabled', String(!commandEnabled('df-toggle-fit') || limited));
  }

  function syncControls() {
    const percent = Math.round(view.transform.scale * 100);
    if (zoomIndicator && zoomIndicator.textContent !== `${percent}%`) {
      zoomIndicator.textContent = `${percent}%`;
    }

    if (fitButton) {
      fitButton.classList.toggle('df-active', view.mode === 'fit');
      fitButton.setAttribute('aria-pressed', String(view.mode === 'fit'));
    }

    if (zoomPreset) {
      zoomPreset.disabled = !commandEnabled('df-toggle-fit');
      if (view.mode === 'fit') {
        if (customOption) customOption.hidden = true;
        zoomPreset.value = 'fit';
      } else {
        const preset = [0.5, 1, 2].find(scale =>
          Math.abs(view.transform.scale - scale) < ZOOM_EPSILON);
        if (preset) {
          zoomPreset.value = String(preset);
          if (customOption) customOption.hidden = true;
        } else if (customOption) {
          customOption.hidden = false;
          customOption.textContent = `${percent}%`;
          zoomPreset.value = 'custom';
        }
      }
    }

    syncActionButton(
      zoomOutButton,
      view.transform.scale <= VIEWPORT_MIN_SCALE + ZOOM_EPSILON);
    syncActionButton(
      zoomInButton,
      view.transform.scale >= VIEWPORT_MAX_SCALE - ZOOM_EPSILON);
  }

  function render(announce = false) {
    const bounds = readBounds();
    stage.style.width = `${bounds.viewportWidth}px`;
    stage.style.height = `${bounds.viewportHeight}px`;
    const rendered = renderedTransform();
    viewport.style.transform =
      `translate(${rendered.translateX}px, ${rendered.translateY}px) scale(${rendered.scale})`;
    syncControls();
    onTransformChanged({
      bounds,
      mode: view.mode,
      transform: { ...view.transform },
    });
    if (announce) announceZoom();
  }

  function applyLayout({ preserveCenter = true, announce = false } = {}) {
    const bounds = readBounds();
    if (!canNavigate() && lastBounds) {
      if (!sameBounds(bounds, lastBounds)) onNavigationBlocked();
      if (!canNavigate()) return false;
    }
    if (view.mode === 'fit') {
      view.transform = centerViewportTransform(fitViewportScale(bounds), bounds);
    } else if (preserveCenter && lastBounds) {
      view.transform = preserveViewportCenter(
        view.transform,
        lastBounds,
        bounds,
        { centerSmallContent: false });
    } else {
      view.transform = clampViewportTransform(
        view.transform,
        bounds,
        { centerSmallContent: false });
    }
    lastBounds = bounds;
    render(announce);
    return true;
  }

  function scheduleLayout(options = {}) {
    if (layoutFrame) window.cancelAnimationFrame(layoutFrame);
    layoutFrame = window.requestAnimationFrame(() => {
      layoutFrame = 0;
      applyLayout(options);
    });
  }

  function commitTransform(mode, transform, bounds, announce) {
    view.mode = mode;
    view.transform = transform;
    lastBounds = bounds;
    render(announce);
    return true;
  }

  function setFit({ announce = true } = {}) {
    if (!canNavigate()) return false;
    const bounds = readBounds();
    const next = centerViewportTransform(fitViewportScale(bounds), bounds);
    if (view.mode === 'fit' && sameTransform(next, view.transform)) return false;
    return commitTransform('fit', next, bounds, announce);
  }

  function setScaleAtClient(scale, clientX, clientY, { announce = true } = {}) {
    if (!canNavigate()) return false;
    const nextScale = clampViewportScale(scale);
    const currentTransform = renderedTransform();
    const anchor = { x: clientX, y: clientY };
    const appPoint = transformedViewportToInspectorCss(anchor, currentTransform);
    const projected = inspectorCssToTransformedViewport(
      appPoint,
      {
        originX: currentTransform.originX,
        originY: currentTransform.originY,
        scale: nextScale,
        translateX: 0,
        translateY: 0,
      });
    const bounds = readBounds();
    const next = clampViewportTransform({
      scale: nextScale,
      translateX: anchor.x - projected.x,
      translateY: anchor.y - projected.y,
    }, bounds, { centerSmallContent: false });
    if (view.mode === 'custom' && sameTransform(next, view.transform)) return false;
    return commitTransform('custom', next, bounds, announce);
  }

  function setScaleAtCenter(scale, options = {}) {
    const rect = stage.getBoundingClientRect();
    setScaleAtClient(scale, rect.left + rect.width / 2, rect.top + rect.height / 2, options);
  }

  function stepScale(direction) {
    const next = stepViewportScale(view.transform.scale, direction);
    if ((direction > 0 && next <= view.transform.scale + ZOOM_EPSILON)
      || (direction < 0 && next >= view.transform.scale - ZOOM_EPSILON))
      return;
    setScaleAtCenter(next);
  }

  function panBy(deltaX, deltaY, { announce = false } = {}) {
    if (!canNavigate()) return false;
    const bounds = readBounds();
    const next = clampViewportTransform({
      scale: view.transform.scale,
      translateX: view.transform.translateX + deltaX,
      translateY: view.transform.translateY + deltaY,
    }, bounds, { centerSmallContent: view.mode === 'fit' });
    if (sameTransform(next, view.transform)) return false;
    return commitTransform('custom', next, bounds, announce);
  }

  function setPanArmed(armed) {
    spacePanArmed = armed;
    document.body.classList.toggle('df-viewport-pan-armed', armed && !panSession);
  }

  function clearSuppressedClick() {
    if (suppressedClick?.timer) window.clearTimeout(suppressedClick.timer);
    suppressedClick = null;
  }

  function armClickSuppression(pointerId, pointerType) {
    clearSuppressedClick();
    suppressedClick = { pointerId, pointerType, timer: 0 };
  }

  function clickMatchesSuppressedPointer(event) {
    if (!suppressedClick) return false;
    if (Number.isFinite(event.pointerId) && event.pointerId > 0)
      return event.pointerId === suppressedClick.pointerId;
    return suppressedClick.pointerType === 'mouse' && event.detail > 0;
  }

  function finishPan(event = null, { expectClick = false } = {}) {
    if (!panSession) return false;
    if (event && event.pointerId !== panSession.pointerId) return false;
    const session = panSession;
    panSession = null;
    document.body.classList.remove('df-viewport-panning');
    document.body.classList.toggle('df-viewport-pan-armed', spacePanArmed);
    if (wrap.hasPointerCapture?.(session.pointerId)) {
      try { wrap.releasePointerCapture(session.pointerId); } catch {}
    }
    if (expectClick && suppressedClick?.pointerId === session.pointerId) {
      suppressedClick.timer = window.setTimeout(clearSuppressedClick, 700);
    } else if (suppressedClick?.pointerId === session.pointerId) {
      clearSuppressedClick();
    }
    return true;
  }

  function cancelActivePan() {
    setPanArmed(false);
    if (!panSession || panSession.cancelled) return;
    panSession.cancelled = true;
    document.body.classList.remove('df-viewport-panning');
  }

  function onPointerDown(event) {
    if (panSession?.cancelled) {
      if (event.pointerType !== panSession.pointerType) {
        event.preventDefault();
        return;
      }
      finishPan(null);
    }
    const middleButton = event.pointerType !== 'touch' && event.button === 1;
    const spaceDrag = event.pointerType !== 'touch' && event.button === 0 && spacePanArmed;
    if (!middleButton && !spaceDrag) {
      if (event.button === 0 && !isEditing() && !isEditableTarget(event.target, window))
        wrap.focus({ preventScroll: true, focusVisible: false });
      return;
    }
    if (isEditing() || !commandEnabled('df-toggle-fit') || !canNavigate()) {
      event.preventDefault();
      return;
    }
    event.preventDefault();
    wrap.focus({ preventScroll: true, focusVisible: false });
    panSession = {
      pointerId: event.pointerId,
      pointerType: event.pointerType,
      lastClientX: event.clientX,
      lastClientY: event.clientY,
      cancelled: false,
    };
    armClickSuppression(event.pointerId, event.pointerType);
    document.body.classList.remove('df-viewport-pan-armed');
    document.body.classList.add('df-viewport-panning');
    wrap.setPointerCapture(event.pointerId);
  }

  function onPointerMove(event) {
    if (!panSession || event.pointerId !== panSession.pointerId) return;
    event.preventDefault();
    if (panSession.cancelled) return;
    const deltaX = event.clientX - panSession.lastClientX;
    const deltaY = event.clientY - panSession.lastClientY;
    panSession.lastClientX = event.clientX;
    panSession.lastClientY = event.clientY;
    panBy(deltaX, deltaY);
  }

  function onPointerEnd(event) {
    if (!panSession || event.pointerId !== panSession.pointerId) return;
    event.preventDefault();
    finishPan(event, { expectClick: event.type === 'pointerup' });
  }

  function onClick(event) {
    if (!clickMatchesSuppressedPointer(event)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    clearSuppressedClick();
  }

  function onAuxClick(event) {
    if (event.button !== 1 || !clickMatchesSuppressedPointer(event)) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    clearSuppressedClick();
  }

  function onWheel(event) {
    if (!event.ctrlKey && !event.metaKey) return;
    event.preventDefault();
    if (!commandEnabled('df-toggle-fit') || !canNavigate()) return;
    const unit = event.deltaMode === 1
      ? 16
      : (event.deltaMode === 2 ? Math.max(1, wrap.clientHeight) : 1);
    const delta = event.deltaY * unit;
    if (!delta) return;
    const requestedScale = view.transform.scale * Math.exp(-delta * 0.002);
    const nextScale = clampViewportScale(requestedScale);
    if ((delta > 0 && nextScale >= view.transform.scale - ZOOM_EPSILON)
      || (delta < 0 && nextScale <= view.transform.scale + ZOOM_EPSILON))
      return;
    setScaleAtClient(nextScale, event.clientX, event.clientY);
  }

  function onDocumentKeyDown(event) {
    if (event.key === 'Escape' && (panSession || spacePanArmed)) {
      event.preventDefault();
      event.stopImmediatePropagation();
      cancelActivePan();
      return;
    }
    if (event.code !== 'Space' || event.repeat || event.ctrlKey || event.metaKey || event.altKey)
      return;
    if (!wrap.contains(document.activeElement)
      || isEditing()
      || !canNavigate()
      || isEditableTarget(event.target, window))
      return;
    event.preventDefault();
    setPanArmed(true);
  }

  function onDocumentKeyUp(event) {
    if (event.code !== 'Space' || !spacePanArmed) return;
    event.preventDefault();
    setPanArmed(false);
  }

  function onViewportKeyDown(event) {
    if (isEditing() || !canNavigate() || isEditableTarget(event.target, window)
      || event.altKey || event.ctrlKey || event.metaKey
      || !commandEnabled('df-toggle-fit'))
      return;
    let handled = true;
    if (event.key === '+' || event.key === '=' || event.key === 'Add') {
      stepScale(1);
    } else if (event.key === '-' || event.key === '_' || event.key === 'Subtract') {
      stepScale(-1);
    } else if (event.key === '0') {
      setFit();
    } else if (event.key === '1') {
      setScaleAtCenter(1);
    } else if (event.key === 'ArrowLeft') {
      panBy(event.shiftKey ? PAN_STEP_LARGE : PAN_STEP, 0);
    } else if (event.key === 'ArrowRight') {
      panBy(event.shiftKey ? -PAN_STEP_LARGE : -PAN_STEP, 0);
    } else if (event.key === 'ArrowUp') {
      panBy(0, event.shiftKey ? PAN_STEP_LARGE : PAN_STEP);
    } else if (event.key === 'ArrowDown') {
      panBy(0, event.shiftKey ? -PAN_STEP_LARGE : -PAN_STEP);
    } else {
      handled = false;
    }
    if (handled) event.preventDefault();
  }

  fitButton?.addEventListener('click', () => {
    if (!commandEnabled('df-toggle-fit')) return;
    setFit();
  });
  zoomOutButton?.addEventListener('click', () => {
    if (zoomOutButton.getAttribute('aria-disabled') !== 'true') stepScale(-1);
  });
  zoomInButton?.addEventListener('click', () => {
    if (zoomInButton.getAttribute('aria-disabled') !== 'true') stepScale(1);
  });
  zoomPreset?.addEventListener('change', () => {
    if (!commandEnabled('df-toggle-fit')) return;
    if (zoomPreset.value === 'fit') {
      setFit();
      return;
    }
    const scale = Number(zoomPreset.value);
    if (Number.isFinite(scale)) setScaleAtCenter(scale);
  });

  wrap.addEventListener('pointerdown', onPointerDown, true);
  wrap.addEventListener('pointermove', onPointerMove, true);
  wrap.addEventListener('pointerup', onPointerEnd, true);
  wrap.addEventListener('pointercancel', onPointerEnd, true);
  wrap.addEventListener('lostpointercapture', onPointerEnd, true);
  wrap.addEventListener('click', onClick, true);
  wrap.addEventListener('auxclick', onAuxClick, true);
  wrap.addEventListener('wheel', onWheel, { capture: true, passive: false });
  wrap.addEventListener('keydown', onViewportKeyDown);
  document.addEventListener('keydown', onDocumentKeyDown, true);
  document.addEventListener('keyup', onDocumentKeyUp, true);
  window.addEventListener('blur', cancelActivePan);
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) cancelActivePan();
  });

  if (window.ResizeObserver) {
    try {
      new window.ResizeObserver(() => {
        const bounds = readBounds();
        if (!canNavigate() && lastBounds && !sameBounds(bounds, lastBounds))
          onNavigationBlocked();
        scheduleLayout();
      }).observe(wrap);
    } catch {}
  }
  applyLayout({ preserveCenter: false });

  function revealElement(element, padding = 24) {
    if (!(element instanceof window.HTMLElement) || !viewport.contains(element)) return false;
    const bounds = readBounds();
    const scale = view.transform.scale;
    const horizontalPadding = Math.min(padding, bounds.viewportWidth / 4);
    const verticalPadding = Math.min(padding, bounds.viewportHeight / 4);
    const left = view.transform.translateX + element.offsetLeft * scale;
    const top = view.transform.translateY + element.offsetTop * scale;
    const right = left + element.offsetWidth * scale;
    const bottom = top + element.offsetHeight * scale;
    let deltaX = 0;
    let deltaY = 0;
    if (left < horizontalPadding) deltaX = horizontalPadding - left;
    else if (right > bounds.viewportWidth - horizontalPadding)
      deltaX = bounds.viewportWidth - horizontalPadding - right;
    if (top < verticalPadding) deltaY = verticalPadding - top;
    else if (bottom > bounds.viewportHeight - verticalPadding)
      deltaY = bounds.viewportHeight - verticalPadding - bottom;
    return panBy(deltaX, deltaY);
  }

  return Object.freeze({
    applyLayout,
    scheduleLayout,
    syncControls,
    revealElement,
  });
}
