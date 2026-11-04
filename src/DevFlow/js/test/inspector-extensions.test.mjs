import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MAX_STREAM_ROWS,
  buildParameters,
  coerceFieldValue,
  columnValue,
  eventCells,
  eventMatches,
  formatCell,
  formatEventTime,
  normalizeExtensions,
  parseRawParameters,
  schemaFields,
  toolMutates,
} from '../../../Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-extensions.js';

test('a missing schema means the tool takes no parameters', () => {
  assert.deepEqual(schemaFields(undefined), { mode: 'none', fields: [] });
  assert.deepEqual(schemaFields(null), { mode: 'none', fields: [] });
  assert.equal(schemaFields({ type: 'object', properties: {} }).mode, 'none');
});

test('flat object schemas become typed fields with required flags and enums', () => {
  const shape = schemaFields({
    type: 'object',
    required: ['uuid'],
    properties: {
      uuid: { type: 'string', description: 'Peripheral id' },
      timeoutMs: { type: 'integer', default: 5000 },
      rssi: { type: ['number', 'null'] },
      connect: { type: 'boolean' },
      mode: { type: 'string', enum: ['fast', 'slow'] },
      filter: { type: 'object', properties: { name: { type: 'string' } } },
      ids: { type: 'array', items: { type: 'string' } },
    },
  });
  assert.equal(shape.mode, 'fields');
  const byName = Object.fromEntries(shape.fields.map((f) => [f.name, f]));
  assert.equal(byName.uuid.type, 'string');
  assert.equal(byName.uuid.required, true);
  assert.equal(byName.uuid.description, 'Peripheral id');
  assert.equal(byName.timeoutMs.type, 'integer');
  assert.equal(byName.timeoutMs.defaultValue, 5000);
  assert.equal(byName.rssi.type, 'number');
  assert.equal(byName.connect.type, 'boolean');
  assert.equal(byName.mode.type, 'enum');
  assert.deepEqual(byName.mode.enumValues, ['fast', 'slow']);
  assert.equal(byName.filter.type, 'json');
  assert.equal(byName.ids.type, 'json');
  assert.equal(byName.ids.valueType, 'array');
});

test('schemas that are not a flat object fall back to raw JSON', () => {
  assert.equal(schemaFields({ type: 'array', items: {} }).mode, 'json');
  assert.equal(schemaFields({ oneOf: [{ type: 'object' }, { type: 'string' }] }).mode, 'json');
  assert.equal(schemaFields({ type: 'object', additionalProperties: { type: 'string' } }).mode, 'json');
  assert.equal(schemaFields([1, 2]).mode, 'json');
});

test('field values are coerced to their schema types', () => {
  const integer = { name: 'n', type: 'integer', valueType: 'integer', required: false };
  assert.deepEqual(coerceFieldValue(integer, ' 42 '), { value: 42 });
  assert.match(coerceFieldValue(integer, '4.5').error, /whole number/);
  assert.match(coerceFieldValue(integer, 'abc').error, /number/);
  assert.deepEqual(coerceFieldValue(integer, ''), { omit: true });

  const required = { name: 'uuid', type: 'string', valueType: 'string', required: true };
  assert.match(coerceFieldValue(required, '  ').error, /uuid is required/);
  assert.deepEqual(coerceFieldValue(required, 'abc'), { value: 'abc' });

  assert.deepEqual(coerceFieldValue({ name: 'b', type: 'boolean' }, true), { value: true });
  assert.deepEqual(coerceFieldValue({ name: 'b', type: 'boolean' }, undefined), { value: false });
  assert.deepEqual(coerceFieldValue({ name: 'x', type: 'number', valueType: 'number' }, '1.5'), { value: 1.5 });
  assert.deepEqual(coerceFieldValue({ name: 'e', type: 'enum', valueType: 'integer' }, '2'), { value: 2 });
  assert.deepEqual(coerceFieldValue({ name: 'j', type: 'json', valueType: 'object' }, '{"a":1}'), { value: { a: 1 } });
  assert.match(coerceFieldValue({ name: 'j', type: 'json', valueType: 'object' }, '{bad').error, /valid JSON/);
});

test('buildParameters omits empty optional fields and reports every error', () => {
  const { fields } = schemaFields({
    type: 'object',
    required: ['a'],
    properties: { a: { type: 'string' }, b: { type: 'integer' }, c: { type: 'boolean' } },
  });
  assert.deepEqual(buildParameters(fields, { a: 'x', b: '', c: true }), { parameters: { a: 'x', c: true }, errors: [] });
  const failed = buildParameters(fields, { a: '', b: 'nope' });
  assert.equal(failed.errors.length, 2);
});

test('raw parameters must be a JSON object', () => {
  assert.deepEqual(parseRawParameters(''), { parameters: undefined, errors: [] });
  assert.deepEqual(parseRawParameters('{"x":1}'), { parameters: { x: 1 }, errors: [] });
  assert.equal(parseRawParameters('[1]').errors.length, 1);
  assert.equal(parseRawParameters('{').errors.length, 1);
});

test('only non-GET tools that are not read-only need the mutation lease', () => {
  assert.equal(toolMutates({ method: 'GET' }), false);
  assert.equal(toolMutates({ method: 'post' }), true);
  assert.equal(toolMutates({ method: 'POST', annotations: { readOnly: true } }), false);
  assert.equal(toolMutates({ method: 'DELETE', annotations: { destructive: true } }), true);
});

test('stream rows use the declared columns, falling back to compact JSON', () => {
  const data = { Kind: 'scan', peripheral: { name: 'HR', rssi: -60 }, detail: null };
  assert.equal(columnValue(data, 'kind'), 'scan');
  assert.deepEqual(eventCells(data, ['kind', 'peripheral', 'detail', 'missing']),
    ['scan', '{"name":"HR","rssi":-60}', '', '']);
  assert.deepEqual(eventCells('plain text', []), ['plain text']);
  assert.deepEqual(eventCells({ a: 1 }, undefined), ['{"a":1}']);
  assert.equal(formatCell(3), '3');
  assert.equal(formatCell(false), 'false');
});

test('the text filter matches any cell case-insensitively', () => {
  const cells = ['Connected', 'Polar H10', ''];
  assert.equal(eventMatches(cells, ''), true);
  assert.equal(eventMatches(cells, 'polar'), true);
  assert.equal(eventMatches(cells, 'disconnected'), false);
});

test('event times render as local wall-clock time and ignore bad timestamps', () => {
  assert.match(formatEventTime('2026-10-31T14:05:06.007Z'), /^\d{2}:\d{2}:\d{2}\.\d{3}$/);
  assert.equal(formatEventTime('not a date'), '');
  assert.equal(formatEventTime(undefined), '');
});

test('extension descriptors are normalized and sorted, with load errors kept', () => {
  const { extensions, errors } = normalizeExtensions({
    ok: true,
    extensions: {
      'org.shiny.ble': {
        version: '1.0.0',
        description: 'BLE monitor',
        state: 'ready',
        toolPrefix: 'ble',
        tools: [{ name: 'status', method: 'GET', path: '/api/v1/ext/org.shiny.ble/status' }, { description: 'no name' }],
        streams: [{ name: 'events', columns: ['kind', 'peripheral', 'detail'] }],
      },
      'com.example.broken': { state: 'faulted', error: 'no radio' },
    },
    extensionErrors: [{ extension: 'Contoso.Plugin', error: 'ctor threw' }],
  });
  assert.deepEqual(extensions.map((e) => e.namespace), ['com.example.broken', 'org.shiny.ble']);
  assert.equal(extensions[0].state, 'faulted');
  assert.equal(extensions[0].error, 'no radio');
  assert.equal(extensions[1].tools.length, 1);
  assert.equal(extensions[1].streams[0].name, 'events');
  assert.deepEqual(errors, [{ extension: 'Contoso.Plugin', error: 'ctor threw' }]);
  assert.deepEqual(normalizeExtensions(null), { extensions: [], errors: [] });
});

test('stream tables are bounded', () => {
  assert.ok(MAX_STREAM_ROWS >= 100 && MAX_STREAM_ROWS <= 5000);
});
