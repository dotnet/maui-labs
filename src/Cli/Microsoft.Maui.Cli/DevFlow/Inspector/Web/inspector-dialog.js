// Host-agnostic modal dialog used by browser, VS Code, and Canvas Inspector hosts.
export function confirmModal(message, confirmLabel) {
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
        const edge = event.shiftKey ? cancel : confirm;
        if (document.activeElement === edge) {
          event.preventDefault();
          (event.shiftKey ? confirm : cancel).focus();
        }
      }
    };
    cancel.addEventListener('click', () => finish(false));
    confirm.addEventListener('click', () => finish(true));
    backdrop.addEventListener('click', (event) => { if (event.target === backdrop) finish(false); });
    document.addEventListener('keydown', onKey, true);
    actions.append(cancel, confirm);
    box.append(text, actions);
    backdrop.appendChild(box);
    document.body.appendChild(backdrop);
    confirm.focus();
  });
}