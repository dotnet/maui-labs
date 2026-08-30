export function createDataDockController({
  panel,
  actionStrip,
  body,
  renderActionStrip,
  renderState,
  setMeta,
  setMetaNote,
}) {
  let activeTab = null;
  let activeView = 'default';
  let activeGeneration = 0;
  let activeStripModel = null;
  let disposeStrip = () => {};

  function activate(tab, options = {}) {
    activeTab = tab;
    activeView = options.view || 'default';
    activeGeneration = Number.isInteger(options.generation) ? options.generation : activeGeneration + 1;
    if (panel) {
      panel.setAttribute('aria-labelledby', `df-tab-${tab}`);
      panel.dataset.activeTab = tab;
      panel.dataset.view = activeView;
    }
    body.setAttribute('aria-label', `${options.label || tab} data`);
    return activeGeneration;
  }

  function begin(tab, options = {}) {
    activate(tab, options);
    body.setAttribute('aria-busy', 'true');
    body.classList.toggle('df-data-refreshing', options.refresh === true);
    if (options.refresh !== true) setActionStrip(tab, activeGeneration, null, activeView);
    if (options.refresh === true && body.childElementCount > 0) return;
    body.replaceChildren(renderState({
      kind: 'loading',
      icon: 'i-refresh',
      title: 'Loading',
      message: 'Reading the latest app data.',
    }));
    return activeGeneration;
  }

  function isCurrent(tab, generation, view = activeView) {
    return tab === activeTab &&
      generation === activeGeneration &&
      view === activeView;
  }

  function setActionStrip(tab, generation, model, view = activeView) {
    if (!actionStrip || !isCurrent(tab, generation, view)) return false;
    const documentLike = actionStrip.ownerDocument ||
      (typeof document === 'undefined' ? null : document);
    const focused = documentLike && actionStrip.contains?.(documentLike.activeElement)
      ? documentLike.activeElement
      : null;
    const focusedCommand = focused?.dataset?.commandId || null;
    disposeStrip();
    activeStripModel = model || null;
    disposeStrip = renderActionStrip(actionStrip, model);
    if (!model && focused) {
      const tabButton = document.getElementById(`df-tab-${tab}`);
      requestAnimationFrame(() => tabButton?.focus());
    } else if (model && focused) {
      requestAnimationFrame(() => {
        const replacement = focusedCommand
          ? [...actionStrip.querySelectorAll('[data-command-id]')]
              .find((item) => item.dataset.commandId === focusedCommand)
          : actionStrip.querySelector('.df-data-action-primary button, .df-data-action-primary summary, button, summary');
        replacement?.focus();
      });
    }
    return true;
  }

  function complete(tab, meta, note, options = {}) {
    const generation = Number.isInteger(options.generation) ? options.generation : activeGeneration;
    const view = options.view || activeView;
    if (!isCurrent(tab, generation, view)) return false;
    body.setAttribute('aria-busy', 'false');
    body.classList.remove('df-data-refreshing');
    setMeta(meta || '');
    setMetaNote(note || '');
    return true;
  }

  function fail(tab, message, options = {}) {
    const generation = Number.isInteger(options.generation) ? options.generation : activeGeneration;
    const view = options.view || activeView;
    if (!isCurrent(tab, generation, view)) return false;
    body.setAttribute('aria-busy', 'false');
    body.classList.remove('df-data-refreshing');
    if (!options.retain || body.childElementCount === 0) {
      body.replaceChildren(renderState({
        kind: 'error',
        icon: 'i-refresh',
        title: 'Could not load data',
        message: message || 'Try refreshing this tab.',
      }));
    }
    setMeta(options.retain ? 'stale · refresh failed' : '');
    return true;
  }

  function current() {
    return {
      tab: activeTab,
      view: activeView,
      generation: activeGeneration,
      stripModel: activeStripModel,
    };
  }

  function dispose() {
    disposeStrip();
    disposeStrip = () => {};
  }

  return Object.freeze({ activate, begin, complete, current, dispose, fail, isCurrent, setActionStrip });
}
