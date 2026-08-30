export function createDataUi(documentLike = document) {
  function element(tag, attributes, ...children) {
    const node = documentLike.createElement(tag);
    for (const [name, value] of Object.entries(attributes || {})) {
      if (value === null || value === undefined) continue;
      if (name === 'class') node.className = value;
      else if (name === 'text') node.textContent = value;
      else if (name === 'disabled') node.disabled = !!value;
      else node.setAttribute(name, String(value));
    }
    for (const child of children) if (child != null) node.append(child);
    return node;
  }

  function icon(id, className = 'df-ic-xs') {
    const svg = documentLike.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', className);
    svg.setAttribute('aria-hidden', 'true');
    const use = documentLike.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', `#${id}`);
    svg.append(use);
    return svg;
  }

  function button(label, options = {}) {
    const node = element(
      'button',
      {
        class: `df-data-button${options.primary ? ' df-data-button-primary' : ''}${options.className ? ` ${options.className}` : ''}`,
        type: 'button',
        disabled: options.disabled,
        title: options.title,
        'aria-label': options.ariaLabel,
      },
      options.icon ? icon(options.icon) : null,
      element('span', { text: label }));
    if (typeof options.onClick === 'function') node.addEventListener('click', options.onClick);
    if (options.commandId) node.dataset.commandId = options.commandId;
    return node;
  }

  function state(options = {}) {
    const kind = options.kind || 'empty';
    const node = element('div', {
      class: `df-data-state df-data-state-${kind}`,
      role: kind === 'error' ? 'alert' : 'status',
    });
    if (options.icon) node.append(icon(options.icon, 'df-data-state-icon'));
    const copy = element('div', { class: 'df-data-state-copy' },
      element('strong', { text: options.title || 'No data' }),
      options.message ? element('span', { text: options.message }) : null);
    node.append(copy);
    if (options.action) node.append(options.action);
    return node;
  }

  function renderActionStrip(host, model) {
    host.replaceChildren();
    host.className = 'df-hidden';
    host.removeAttribute('aria-label');
    host.removeAttribute('data-variant');
    if (!model || model.visible === false) return () => {};

    host.className = 'df-data-action-strip';
    host.dataset.variant = model.variant || 'actions';
    host.setAttribute('aria-label', model.ariaLabel || 'Data actions');

    const context = element('div', { class: 'df-data-action-context' });
    const persistent = element('div', { class: 'df-data-action-persistent' });
    const secondary = element('div', { class: 'df-data-action-secondary' });
    const spacer = element('span', { class: 'df-data-action-spacer', 'aria-hidden': 'true' });
    const primary = element('div', { class: 'df-data-action-primary' });
    for (const item of model.context || []) context.append(item);
    for (const item of model.persistentControls || []) persistent.append(item);
    const secondaryCommands = [...(model.secondaryCommands || [])];
    for (const item of secondaryCommands) secondary.append(item);
    if (model.primaryCommand) primary.append(model.primaryCommand);

    const more = element('details', { class: 'df-data-action-more df-hidden' });
    const moreSummary = element('summary', {
      class: 'df-data-button',
      'aria-label': 'More Data actions',
      title: 'More Data actions',
    }, icon('i-more'));
    const moreMenu = element('div', {
      class: 'df-data-action-more-menu',
      role: 'menu',
      'aria-label': 'More Data actions',
    });
    more.append(moreSummary, moreMenu);
    host.append(primary, secondary, more, spacer, persistent, context);

    let frame = 0;
    function restoreInline() {
      while (moreMenu.firstChild) {
        moreMenu.firstChild.removeAttribute?.('role');
        secondary.append(moreMenu.firstChild);
      }
      more.classList.add('df-hidden');
      more.open = false;
    }
    function reflow() {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const focused = host.contains(documentLike.activeElement)
          ? documentLike.activeElement
          : null;
        restoreInline();
        if (host.scrollWidth <= host.clientWidth || !secondary.children.length) return;
        more.classList.remove('df-hidden');
        while (host.scrollWidth > host.clientWidth && secondary.lastElementChild) {
          const command = secondary.lastElementChild;
          command.setAttribute('role', 'menuitem');
          moreMenu.prepend(command);
        }
        if (!moreMenu.children.length) more.classList.add('df-hidden');
        if (focused && moreMenu.contains(focused)) {
          // A resize can move the focused command into More. Open it before
          // restoring focus so keyboard users never land in a closed disclosure.
          more.open = true;
          requestAnimationFrame(() => {
            if (focused.isConnected) focused.focus({ preventScroll: true });
          });
        }
      });
    }
    function onKeyDown(event) {
      if (!more.open) return;
      if (event.key === 'Escape') {
        event.preventDefault();
        more.open = false;
        moreSummary.focus();
        return;
      }
      if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
      const items = [...moreMenu.querySelectorAll('[role="menuitem"]:not([disabled])')];
      if (!items.length) return;
      event.preventDefault();
      const index = items.indexOf(documentLike.activeElement);
      if (event.key === 'Home') items[0].focus();
      else if (event.key === 'End') items[items.length - 1].focus();
      else if (event.key === 'ArrowDown') items[index < 0 ? 0 : (index + 1) % items.length].focus();
      else items[index < 0 ? items.length - 1 : (index - 1 + items.length) % items.length].focus();
    }
    function onMoreClick(event) {
      if (!event.target.closest?.('[role="menuitem"]')) return;
      more.open = false;
    }
    host.addEventListener('keydown', onKeyDown);
    moreMenu.addEventListener('click', onMoreClick);
    const resizeObserver = typeof ResizeObserver === 'function'
      ? new ResizeObserver(reflow)
      : null;
    resizeObserver?.observe(host);
    window.addEventListener('resize', reflow);
    reflow();

    return () => {
      cancelAnimationFrame(frame);
      resizeObserver?.disconnect();
      window.removeEventListener('resize', reflow);
      host.removeEventListener('keydown', onKeyDown);
      moreMenu.removeEventListener('click', onMoreClick);
    };
  }

  return Object.freeze({ button, element, icon, renderActionStrip, state });
}
