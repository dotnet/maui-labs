// Host-agnostic modal dialog used by browser, VS Code, and Canvas Inspector hosts.
export function modalFocusables(root) {
  return [...root.querySelectorAll([
    'button:not([disabled])',
    '[href]',
    'input:not([disabled]):not([type="hidden"])',
    'textarea:not([disabled])',
    'select:not([disabled])',
    '[contenteditable]:not([contenteditable="false"])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(', '))]
    .filter((element) => element instanceof HTMLElement &&
      !element.closest('[hidden], [aria-hidden="true"]') &&
      element.getClientRects().length > 0);
}

export function confirmModal(message, confirmLabel, options = {}) {
  return new Promise((resolve) => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const backdrop = document.createElement('div');
    Object.assign(backdrop.style, {
      position: 'fixed', inset: '0', zIndex: '10002', background: 'rgba(0,0,0,0.45)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
    });
    const box = document.createElement('div');
    box.setAttribute('role', 'dialog');
    box.setAttribute('aria-modal', 'true');
    box.tabIndex = -1;
    Object.assign(box.style, {
      background: 'var(--df-surface, #252526)', color: 'var(--df-fg, #d4d4d4)',
      border: '1px solid var(--df-border, #3c3c3c)', borderRadius: 'var(--df-radius, 5px)',
      padding: '16px 18px', maxWidth: '360px', width: 'calc(100% - 48px)',
      boxShadow: 'var(--df-shadow, 0 8px 30px rgba(0,0,0,0.5))',
      font: '13px var(--df-font, system-ui, sans-serif)',
    });
    const text = document.createElement('div');
    text.id = 'df-confirm-message';
    text.textContent = message;
    box.setAttribute('aria-labelledby', text.id);
    Object.assign(text.style, { marginBottom: '14px', lineHeight: '1.4' });
    const requiredText = typeof options.requiredText === 'string' && options.requiredText.length <= 600
      ? options.requiredText
      : null;
    let confirmationInput = null;
    if (requiredText) {
      const prompt = document.createElement('label');
      prompt.textContent = `Type "${requiredText}" to continue`;
      Object.assign(prompt.style, {
        display: 'grid', gap: '6px', marginBottom: '14px',
        color: 'var(--df-muted, #9d9d9d)', fontSize: '12px',
      });
      confirmationInput = document.createElement('input');
      confirmationInput.type = 'text';
      confirmationInput.autocomplete = 'off';
      confirmationInput.spellcheck = false;
      confirmationInput.setAttribute('aria-label', `Type ${requiredText} to confirm`);
      Object.assign(confirmationInput.style, {
        minHeight: '30px', border: '1px solid var(--df-border, #3c3c3c)',
        borderRadius: 'var(--df-radius-sm, 3px)', padding: '4px 7px',
        background: 'var(--df-bg, #1e1e1e)', color: 'var(--df-fg, #d4d4d4)',
        font: '12px var(--df-mono, monospace)',
      });
      prompt.append(confirmationInput);
      box.append(text, prompt);
    } else {
      box.append(text);
    }
    const actions = document.createElement('div');
    Object.assign(actions.style, { display: 'flex', gap: '8px', justifyContent: 'flex-end' });
    const createButton = (label, primary) => {
      const button = document.createElement('button');
      button.textContent = label;
      Object.assign(button.style, {
        padding: '6px 14px', borderRadius: 'var(--df-radius-sm, 3px)', cursor: 'pointer',
        border: '1px solid var(--df-border, #3c3c3c)',
        background: primary ? 'var(--df-accent, #0e639c)' : 'var(--df-surface-2, #2d2d2d)',
        color: primary ? 'var(--df-accent-fg, #fff)' : 'var(--df-fg, #d4d4d4)',
      });
      if (primary) button.style.borderColor = 'var(--df-accent, #0e639c)';
      return button;
    };
    const cancel = createButton('Cancel', false);
    const confirm = createButton(confirmLabel || 'OK', true);
    if (requiredText) confirm.disabled = true;
    let done = false;
    const finish = (value) => {
      if (done) return;
      done = true;
      document.removeEventListener('keydown', onKey, true);
      backdrop.remove();
      if (previousFocus && previousFocus.isConnected) previousFocus.focus();
      resolve(value);
    };
    const onKey = (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopImmediatePropagation();
        finish(false);
      } else if (event.key === 'Tab') {
        const focusables = modalFocusables(box);
        if (!focusables.length) {
          event.preventDefault();
          box.focus();
          return;
        }
        const index = focusables.indexOf(document.activeElement);
        if (event.shiftKey && (index <= 0 || !box.contains(document.activeElement))) {
          event.preventDefault();
          focusables[focusables.length - 1].focus();
        } else if (!event.shiftKey && index === focusables.length - 1) {
          event.preventDefault();
          focusables[0].focus();
        }
      } else if (event.key === 'Enter' && !confirm.disabled) {
        event.preventDefault();
        finish(true);
      }
    };
    confirmationInput?.addEventListener('input', () => {
      confirm.disabled = confirmationInput.value !== requiredText;
    });
    cancel.addEventListener('click', () => finish(false));
    confirm.addEventListener('click', () => finish(true));
    backdrop.addEventListener('click', (event) => { if (event.target === backdrop) finish(false); });
    document.addEventListener('keydown', onKey, true);
    actions.append(cancel, confirm);
    box.append(actions);
    backdrop.appendChild(box);
    document.body.appendChild(backdrop);
    (confirmationInput || confirm).focus();
  });
}