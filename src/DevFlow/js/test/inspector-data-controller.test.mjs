import assert from 'node:assert/strict';
import test from 'node:test';

import { createDataDockController } from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-data-controller.js';

function bodyWith(children = []) {
  return {
    childElementCount: children.length,
    children,
    attributes: new Map(),
    classes: new Set(),
    classList: {
      toggle(name, enabled) {
        if (enabled) this.owner.classes.add(name);
        else this.owner.classes.delete(name);
      },
      remove(name) { this.owner.classes.delete(name); },
      owner: null,
    },
    setAttribute(name, value) { this.attributes.set(name, value); },
    replaceChildren(...next) {
      this.children = next;
      this.childElementCount = next.length;
    },
  };
}

function controller(body) {
  body.classList.owner = body;
  const metadata = { text: '', note: '' };
  return {
    metadata,
    value: createDataDockController({
      panel: null,
      actionStrip: null,
      body,
      renderActionStrip: () => () => {},
      renderState: (state) => state,
      setMeta: (text) => { metadata.text = text; },
      setMetaNote: (text) => { metadata.note = text; },
    }),
  };
}

test('initial loading replaces the body with a contextual state', () => {
  const body = bodyWith();
  const { value } = controller(body);

  value.begin('network');

  assert.equal(body.attributes.get('aria-busy'), 'true');
  assert.equal(body.children[0].kind, 'loading');
});

test('late generations cannot replace the active action strip', () => {
  const body = bodyWith();
  const strip = bodyWith();
  const models = [];
  body.classList.owner = body;
  strip.classList.owner = strip;
  const value = createDataDockController({
    panel: null,
    actionStrip: strip,
    body,
    renderActionStrip: (_, model) => {
      models.push(model);
      return () => {};
    },
    renderState: (state) => state,
    setMeta: () => {},
    setMetaNote: () => {},
  });

  value.begin('layout', { generation: 1, view: 'findings' });
  value.begin('sensors', { generation: 2, view: 'list' });

  assert.equal(value.setActionStrip('layout', 1, { visible: true }, 'findings'), false);
  assert.equal(value.setActionStrip('sensors', 2, { visible: true }, 'list'), true);
  assert.deepEqual(models.at(-1), { visible: true });
});

test('refresh keeps successful content and a failed refresh marks it stale', () => {
  const previous = { kind: 'network-table' };
  const body = bodyWith([previous]);
  const { value, metadata } = controller(body);

  value.begin('network', { refresh: true });
  assert.equal(body.children[0], previous);
  assert.equal(body.classes.has('df-data-refreshing'), true);

  value.fail('network', 'offline', { retain: true });
  assert.equal(body.children[0], previous);
  assert.equal(body.attributes.get('aria-busy'), 'false');
  assert.equal(metadata.text, 'stale · refresh failed');
});
