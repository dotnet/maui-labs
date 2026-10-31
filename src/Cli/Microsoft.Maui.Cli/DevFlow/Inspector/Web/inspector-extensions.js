// Extensions panel: a generic UI over the DevFlow plugins an app hosts. Everything is driven by the
// agent's extension descriptors (tools with JSON-schema parameters, event streams with columns), so a
// plugin needs no Inspector-specific code. All plugin-controlled text is rendered with textContent.
//
// The pure helpers (schema → fields, value coercion, event formatting) are exported for unit tests;
// the DOM controller is created with createExtensionsPanel and receives its dependencies as options.

export const MAX_STREAM_ROWS = 1000;
const STREAM_RETRY_MS = [1000, 2000, 5000, 10000];
const POLL_INTERVAL_MS = 1000;
const WS_FAILURES_BEFORE_POLLING = 3;
const POLL_MODE_DURATION_MS = 30000;
const SCALAR_TYPES = new Set(['string', 'number', 'integer', 'boolean']);

const text = (value) => (typeof value === 'string' ? value : '');

function schemaType(schema) {
  if (!schema || typeof schema !== 'object') return '';
  if (typeof schema.type === 'string') return schema.type;
  // ["string", "null"] style nullable types.
  if (Array.isArray(schema.type)) {
    const concrete = schema.type.filter((t) => t !== 'null');
    return concrete.length === 1 ? concrete[0] : '';
  }
  if (Array.isArray(schema.enum) && schema.enum.length) return 'string';
  return '';
}

/**
 * Turns a tool's JSON-schema parameters into form fields. Returns { mode: 'none' } when the tool takes
 * no parameters, { mode: 'fields', fields } for a flat object schema, and { mode: 'json' } when the
 * schema can't be represented as simple fields (the form then offers a raw JSON textarea).
 */
export function schemaFields(schema) {
  if (schema == null) return { mode: 'none', fields: [] };
  if (typeof schema !== 'object' || Array.isArray(schema)) return { mode: 'json', fields: [] };
  const rootType = schemaType(schema);
  const properties = schema.properties;
  if (rootType && rootType !== 'object') return { mode: 'json', fields: [] };
  if (properties == null) {
    const composite = schema.oneOf || schema.anyOf || schema.allOf || schema.additionalProperties;
    return composite ? { mode: 'json', fields: [] } : { mode: 'none', fields: [] };
  }
  if (typeof properties !== 'object' || Array.isArray(properties)) return { mode: 'json', fields: [] };
  const required = new Set(Array.isArray(schema.required) ? schema.required.filter((r) => typeof r === 'string') : []);
  const fields = Object.keys(properties).map((name) => {
    const property = properties[name] && typeof properties[name] === 'object' ? properties[name] : {};
    const type = schemaType(property);
    const enumValues = Array.isArray(property.enum) && property.enum.every((v) => typeof v === 'string' || typeof v === 'number')
      ? property.enum
      : null;
    return {
      name,
      type: enumValues ? 'enum' : (SCALAR_TYPES.has(type) ? type : 'json'),
      valueType: type || 'string',
      required: required.has(name),
      description: text(property.description) || text(property.title),
      enumValues,
      defaultValue: property.default,
    };
  });
  return { mode: fields.length ? 'fields' : 'none', fields };
}

/**
 * Coerces one form input to its JSON value. Returns { omit: true } for an empty optional value, or
 * { value } / { error }.
 */
export function coerceFieldValue(field, input) {
  if (field.type === 'boolean') return { value: input === true || input === 'true' };
  const raw = typeof input === 'string' ? input : (input == null ? '' : String(input));
  if (raw.trim() === '') {
    return field.required ? { error: `${field.name} is required.` } : { omit: true };
  }
  if (field.type === 'integer' || field.type === 'number' || (field.type === 'enum' && field.valueType !== 'string')) {
    const number = Number(raw.trim());
    if (!Number.isFinite(number)) return { error: `${field.name} must be a number.` };
    if ((field.type === 'integer' || field.valueType === 'integer') && !Number.isInteger(number))
      return { error: `${field.name} must be a whole number.` };
    return { value: number };
  }
  if (field.type === 'json') {
    try { return { value: JSON.parse(raw) }; }
    catch { return { error: `${field.name} must be valid JSON.` }; }
  }
  return { value: raw };
}

/** Builds the parameters object for a tool call from { fieldName: inputValue }. */
export function buildParameters(fields, inputs) {
  const parameters = {};
  const errors = [];
  for (const field of fields) {
    const result = coerceFieldValue(field, inputs[field.name]);
    if (result.error) errors.push(result.error);
    else if (!result.omit) parameters[field.name] = result.value;
  }
  return { parameters, errors };
}

/** Parses the raw-JSON fallback; empty means "no parameters". */
export function parseRawParameters(raw) {
  const trimmed = typeof raw === 'string' ? raw.trim() : '';
  if (!trimmed) return { parameters: undefined, errors: [] };
  try {
    const value = JSON.parse(trimmed);
    if (!value || typeof value !== 'object' || Array.isArray(value))
      return { errors: ['Parameters must be a JSON object.'] };
    return { parameters: value, errors: [] };
  } catch {
    return { errors: ['Parameters must be valid JSON.'] };
  }
}

/** A tool mutates the app (and needs the lease) unless it is a GET or marked read-only. */
export function toolMutates(tool) {
  const method = text(tool && tool.method).toUpperCase() || 'GET';
  return method !== 'GET' && !(tool && tool.annotations && tool.annotations.readOnly === true);
}

/** Text for one stream cell: a string as-is, anything else as compact JSON. */
export function formatCell(value) {
  if (value === undefined || value === null) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  try { return JSON.stringify(value); } catch { return String(value); }
}

/** Reads a column from event data, matching the plugin's casing first, then case-insensitively. */
export function columnValue(data, column) {
  if (!data || typeof data !== 'object' || Array.isArray(data)) return undefined;
  if (Object.prototype.hasOwnProperty.call(data, column)) return data[column];
  const lower = column.toLowerCase();
  const key = Object.keys(data).find((k) => k.toLowerCase() === lower);
  return key === undefined ? undefined : data[key];
}

/** The cells a stream row shows: one per declared column, or the whole payload as compact JSON. */
export function eventCells(data, columns) {
  if (Array.isArray(columns) && columns.length) return columns.map((c) => formatCell(columnValue(data, c)));
  return [formatCell(data)];
}

/** Case-insensitive substring filter over the rendered cells. */
export function eventMatches(cells, filter) {
  const needle = text(filter).trim().toLowerCase();
  if (!needle) return true;
  return cells.some((cell) => cell.toLowerCase().includes(needle));
}

/** Local wall-clock time for an event timestamp. */
export function formatEventTime(ts) {
  const date = ts ? new Date(ts) : null;
  if (!date || Number.isNaN(date.getTime())) return '';
  const pad = (n, w = 2) => String(n).padStart(w, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)}`;
}

/** Normalizes the /api/extensions response into a sorted list. */
export function normalizeExtensions(response) {
  const map = response && response.extensions && typeof response.extensions === 'object' ? response.extensions : {};
  const extensions = Object.keys(map).sort((a, b) => a.localeCompare(b)).map((ns) => {
    const e = map[ns] || {};
    return {
      namespace: ns,
      version: text(e.version),
      description: text(e.description),
      state: text(e.state) || 'ready',
      error: text(e.error),
      toolPrefix: text(e.toolPrefix),
      tools: Array.isArray(e.tools) ? e.tools.filter((t) => t && text(t.name)) : [],
      streams: Array.isArray(e.streams) ? e.streams.filter((s) => s && text(s.name)) : [],
    };
  });
  const errors = Array.isArray(response && response.extensionErrors)
    ? response.extensionErrors.map((e) => ({ extension: text(e && e.extension) || 'unknown', error: text(e && e.error) }))
    : [];
  return { extensions, errors };
}

/**
 * Creates the Extensions panel controller.
 * options: {
 *   document,
 *   api: { postDetailed(path, body) },     // token-aware POST (inspector-api.js)
 *   streamSocketUrl(ns, stream, since),    // WebSocket URL for the /ws/extensions proxy
 *   canDrive(): boolean,                   // ensures the mutation lease UI state (shows hints itself)
 *   confirm(message, label): Promise<bool>,
 *   status(message),
 *   onAvailabilityChange(hasAny),          // show/hide the panel entry point
 * }
 */
export function createExtensionsPanel(options) {
  const doc = options.document;
  const api = options.api;
  const root = el('div', { class: 'df-ext' });
  const rail = el('div', { class: 'df-ext-rail', role: 'listbox', 'aria-label': 'Extensions' });
  const detail = el('div', { class: 'df-ext-detail' });
  root.append(rail, detail);

  let model = { extensions: [], errors: [] };
  let signature = '';
  let selected = null;
  let connected = true;
  let loadedOnce = false;
  let mounted = false;      // streams connect only once the panel has been shown
  let loadRetryTimer = 0;
  let unavailable = false;
  const streamViews = new Map();   // `${ns}\n${stream}` -> StreamView
  const toolState = new Map();     // `${ns}\n${tool}` -> { inputs, raw, open }

  function el(tag, attrs, ...children) {
    const node = doc.createElement(tag);
    if (attrs) {
      for (const key of Object.keys(attrs)) {
        const value = attrs[key];
        if (value == null || value === false) continue;
        if (key === 'text') node.textContent = value;
        else if (key === 'class') node.className = value;
        else node.setAttribute(key, value === true ? '' : value);
      }
    }
    for (const child of children) if (child != null) node.append(child);
    return node;
  }

  async function refresh() {
    const result = await api.postDetailed('/api/extensions', {});
    if (!result.ok || !result.body || result.body.ok !== true) {
      unavailable = true;
      // Until the first successful read, keep probing so the entry point appears once the app is up.
      if (!loadedOnce && !loadRetryTimer && result.status !== 403)
        loadRetryTimer = setTimeout(() => { loadRetryTimer = 0; refresh().catch(() => {}); }, 10000);
      if (!loadedOnce) renderEmpty(result.status === 403
        ? 'The Inspector token was rejected; reload the page.'
        : 'Extensions are unavailable while the app is disconnected.');
      return model;
    }
    unavailable = false;
    loadedOnce = true;
    const next = normalizeExtensions(result.body);
    const nextSignature = JSON.stringify(next);
    if (nextSignature !== signature) {
      signature = nextSignature;
      model = next;
      if (!selected || !model.extensions.some((e) => e.namespace === selected))
        selected = model.extensions.length ? model.extensions[0].namespace : null;
      disposeVanishedStreams();
      render();
    }
    options.onAvailabilityChange?.(model.extensions.length > 0 || model.errors.length > 0);
    return model;
  }

  function disposeVanishedStreams() {
    const live = new Set();
    for (const extension of model.extensions)
      for (const stream of extension.streams) live.add(`${extension.namespace}\n${stream.name}`);
    for (const [key, view] of streamViews) {
      if (!live.has(key)) { view.dispose(); streamViews.delete(key); }
    }
  }

  function renderEmpty(message) {
    rail.replaceChildren();
    detail.replaceChildren(el('div', { class: 'df-empty', text: message }));
    root.classList.add('df-ext-empty');
  }

  function render() {
    if (!mounted) return;
    if (!model.extensions.length && !model.errors.length) {
      renderEmpty('This app hosts no DevFlow extensions. Plugins added with AgentOptions.AddExtension appear here.');
      return;
    }
    root.classList.remove('df-ext-empty');
    renderRail();
    renderDetail();
  }

  function renderRail() {
    const items = [];
    for (const extension of model.extensions) {
      const isSelected = extension.namespace === selected;
      const item = el('button', {
        type: 'button',
        class: 'df-ext-rail-item' + (isSelected ? ' df-active' : ''),
        role: 'option',
        'aria-selected': String(isSelected),
        title: extension.description || extension.namespace,
      },
      el('span', { class: 'df-ext-dot df-ext-state-' + (extension.state === 'faulted' ? 'faulted' : 'ready'), 'aria-hidden': 'true' }),
      el('span', { class: 'df-ext-rail-name', text: extension.namespace }));
      item.addEventListener('click', () => { selected = extension.namespace; renderRail(); renderDetail(); });
      items.push(item);
    }
    if (model.errors.length) {
      const isSelected = selected === '\u0000errors';
      const item = el('button', {
        type: 'button',
        class: 'df-ext-rail-item df-ext-rail-errors' + (isSelected ? ' df-active' : ''),
        role: 'option',
        'aria-selected': String(isSelected),
      },
      el('span', { class: 'df-ext-dot df-ext-state-faulted', 'aria-hidden': 'true' }),
      el('span', { class: 'df-ext-rail-name', text: `Load errors (${model.errors.length})` }));
      item.addEventListener('click', () => { selected = '\u0000errors'; renderRail(); renderDetail(); });
      items.push(item);
    }
    if (!selected && items.length) selected = model.extensions[0]?.namespace ?? '\u0000errors';
    rail.replaceChildren(...items);
  }

  function renderDetail() {
    if (selected === '\u0000errors' || (!model.extensions.length && model.errors.length)) {
      const list = el('div', { class: 'df-ext-section' },
        el('div', { class: 'df-section-title', text: 'Plugins that could not be loaded' }));
      for (const error of model.errors) {
        list.append(el('div', { class: 'df-ext-error' },
          el('strong', { text: error.extension }),
          el('span', { text: error.error || 'Unknown error.' })));
      }
      detail.replaceChildren(list);
      return;
    }
    const extension = model.extensions.find((e) => e.namespace === selected);
    if (!extension) { detail.replaceChildren(); return; }

    const header = el('div', { class: 'df-ext-header' },
      el('span', { class: 'df-ext-title', text: extension.namespace }),
      extension.version ? el('span', { class: 'df-ext-chip', text: 'v' + extension.version }) : null,
      el('span', {
        class: 'df-ext-chip df-ext-chip-' + (extension.state === 'faulted' ? 'faulted' : 'ready'),
        text: extension.state,
      }),
      extension.toolPrefix ? el('span', { class: 'df-ext-chip', title: 'MCP tool prefix', text: extension.toolPrefix + '_*' }) : null);
    const parts = [header];
    if (extension.description) parts.push(el('div', { class: 'df-ext-desc', text: extension.description }));
    if (extension.error) parts.push(el('div', { class: 'df-ext-error', role: 'alert' }, el('strong', { text: 'Faulted' }), el('span', { text: extension.error })));

    const streamToolPaths = new Set(extension.streams.map((s) => s.path));
    const tools = extension.tools.filter((t) => !streamToolPaths.has(t.path));
    if (tools.length) {
      const section = el('div', { class: 'df-ext-section' }, el('div', { class: 'df-section-title', text: 'Tools' }));
      for (const tool of tools) section.append(renderTool(extension, tool));
      parts.push(section);
    }
    if (extension.streams.length) {
      const section = el('div', { class: 'df-ext-section' }, el('div', { class: 'df-section-title', text: 'Streams' }));
      for (const stream of extension.streams) section.append(streamView(extension, stream).element);
      parts.push(section);
    }
    if (!tools.length && !extension.streams.length && !extension.error)
      parts.push(el('div', { class: 'df-empty', text: 'This extension exposes no tools or streams.' }));
    detail.replaceChildren(...parts);
  }

  // ── Tools ──

  function renderTool(extension, tool) {
    const key = `${extension.namespace}\n${tool.name}`;
    const state = toolState.get(key) || { inputs: {}, raw: '', open: false };
    toolState.set(key, state);
    const method = text(tool.method).toUpperCase() || 'GET';
    const annotations = tool.annotations || {};
    const mutates = toolMutates(tool);

    const details = el('details', { class: 'df-ext-tool' });
    details.open = state.open;
    details.addEventListener('toggle', () => { state.open = details.open; });
    const summary = el('summary', null,
      el('span', { class: 'df-ext-method df-ext-method-' + method.toLowerCase(), text: method }),
      el('span', { class: 'df-ext-tool-name', text: tool.name }),
      annotations.readOnly ? el('span', { class: 'df-ext-chip', text: 'read-only' }) : null,
      annotations.destructive ? el('span', { class: 'df-ext-chip df-ext-chip-faulted', text: 'destructive' }) : null,
      mutates && !annotations.destructive ? el('span', { class: 'df-ext-chip', title: 'Requires driving the app (mutation lease)', text: 'mutates' }) : null,
      tool.description ? el('span', { class: 'df-ext-tool-desc', text: tool.description }) : null);
    details.append(summary);

    // novalidate: coercion errors are reported inline instead of by native constraint bubbles.
    const form = el('form', { class: 'df-ext-form', novalidate: true });
    const shape = schemaFields(tool.parameters);
    const readInputs = [];
    if (shape.mode === 'fields') {
      for (const field of shape.fields) {
        const id = `df-ext-${hash(key)}-${field.name.replace(/[^A-Za-z0-9_-]/g, '_')}`;
        const label = el('label', { class: 'df-ext-label', for: id, text: field.name + (field.required ? ' *' : '') });
        let input;
        if (field.type === 'boolean') {
          input = el('input', { type: 'checkbox', id, class: 'df-field' });
          input.checked = state.inputs[field.name] !== undefined ? state.inputs[field.name] === true : field.defaultValue === true;
          readInputs.push(() => { state.inputs[field.name] = input.checked; });
        } else if (field.type === 'enum') {
          input = el('select', { id, class: 'df-field' });
          if (!field.required) input.append(el('option', { value: '', text: '' }));
          for (const value of field.enumValues) input.append(el('option', { value: String(value), text: String(value) }));
          input.value = state.inputs[field.name] ?? (field.defaultValue != null ? String(field.defaultValue) : '');
          readInputs.push(() => { state.inputs[field.name] = input.value; });
        } else if (field.type === 'json') {
          input = el('textarea', { id, class: 'df-field df-ext-json', rows: '2', spellcheck: 'false', placeholder: field.valueType === 'array' ? '[ ]' : '{ }' });
          input.value = state.inputs[field.name] ?? (field.defaultValue !== undefined ? JSON.stringify(field.defaultValue) : '');
          readInputs.push(() => { state.inputs[field.name] = input.value; });
        } else {
          input = el('input', {
            id,
            class: 'df-field',
            type: field.type === 'string' ? 'text' : 'number',
            step: field.type === 'integer' ? '1' : (field.type === 'number' ? 'any' : null),
            placeholder: field.type,
            spellcheck: 'false',
          });
          input.value = state.inputs[field.name] ?? (field.defaultValue != null ? String(field.defaultValue) : '');
          readInputs.push(() => { state.inputs[field.name] = input.value; });
        }
        if (field.required) input.setAttribute('aria-required', 'true');
        if (field.description) input.title = field.description;
        const row = el('div', { class: 'df-ext-field' + (field.type === 'boolean' ? ' df-ext-field-bool' : '') }, label, input);
        if (field.description) row.append(el('div', { class: 'df-ext-hint', text: field.description }));
        form.append(row);
      }
    } else if (shape.mode === 'json') {
      const id = `df-ext-${hash(key)}-raw`;
      const input = el('textarea', { id, class: 'df-field df-ext-json', rows: '3', spellcheck: 'false', placeholder: '{ }' });
      input.value = state.raw;
      readInputs.push(() => { state.raw = input.value; });
      form.append(el('div', { class: 'df-ext-field' },
        el('label', { class: 'df-ext-label', for: id, text: 'Parameters (JSON)' }), input));
    }

    const run = el('button', { type: 'submit', class: 'df-dock-btn df-ext-run', text: 'Run' });
    const result = el('div', { class: 'df-ext-result', 'aria-live': 'polite' });
    form.append(el('div', { class: 'df-ext-actions' }, run));
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      for (const read of readInputs) read();
      const built = shape.mode === 'fields'
        ? buildParameters(shape.fields, state.inputs)
        : shape.mode === 'json' ? parseRawParameters(state.raw) : { parameters: undefined, errors: [] };
      if (built.errors.length) {
        result.replaceChildren(el('div', { class: 'df-ext-error', text: built.errors.join(' ') }));
        return;
      }
      if (mutates && !options.canDrive()) return;
      if (annotations.destructive) {
        const ok = await options.confirm(
          `Run the destructive tool "${tool.name}" from ${extension.namespace} against the LIVE app?`,
          'Run tool');
        if (!ok) return;
      }
      run.disabled = true;
      result.replaceChildren(el('div', { class: 'df-ext-meta', text: 'Running…' }));
      try {
        const response = await api.postDetailed('/api/extensions/call', {
          namespace: extension.namespace,
          tool: tool.name,
          parameters: built.parameters ?? null,
        });
        result.replaceChildren(...renderToolResult(response));
      } finally {
        run.disabled = false;
      }
    });
    details.append(form, result);
    return details;
  }

  function renderToolResult(response) {
    const body = response && response.body;
    if (!response || !body) {
      return [el('div', { class: 'df-ext-error', text: (response && response.error) || 'The Inspector could not reach the agent.' })];
    }
    if (response.status === 409 && body.reason === 'writer') {
      return [el('div', { class: 'df-ext-error', text: 'Read-only — another session is driving this app. Take control to run this tool.' })];
    }
    if (!response.ok || body.status === undefined) {
      return [el('div', { class: 'df-ext-error', text: text(body.error) || `Request failed (HTTP ${response.status}).` })];
    }
    const statusText = `${body.status} ${body.ok ? 'OK' : 'error'} · ${body.durationMs ?? '?'} ms${body.truncated ? ' · truncated at 1 MB' : ''}`;
    const payload = body.body;
    const formatted = typeof payload === 'string' ? payload : JSON.stringify(payload, null, 2);
    return [
      el('div', { class: 'df-ext-meta ' + (body.ok ? 'df-status-ok' : 'df-status-err'), text: statusText }),
      el('pre', { class: 'df-ext-output', text: formatted === undefined ? '' : formatted }),
    ];
  }

  // ── Streams ──

  function streamView(extension, stream) {
    const key = `${extension.namespace}\n${stream.name}`;
    let view = streamViews.get(key);
    if (!view || view.signature !== JSON.stringify(stream.columns || [])) {
      view?.dispose();
      view = createStreamView(extension.namespace, stream);
      streamViews.set(key, view);
    }
    return view;
  }

  function createStreamView(ns, stream) {
    const columns = Array.isArray(stream.columns) ? stream.columns.filter((c) => typeof c === 'string' && c) : [];
    const rows = [];          // { seq, ts, cells, marker? }
    const pending = [];       // events received while paused
    let lastSeq = 0;
    let droppedTotal = 0;
    let paused = false;
    let filter = '';
    let socket = null;
    let retryTimer = 0;
    let retryIndex = 0;
    let wsFailures = 0;
    let pollUntil = 0;
    let pollTimer = 0;
    let disposed = false;
    let state = 'connecting';
    let gotMessageOnSocket = false;

    const statusEl = el('span', { class: 'df-ext-live', role: 'status' });
    const droppedEl = el('span', { class: 'df-ext-dropped df-hidden', role: 'status' });
    const countEl = el('span', { class: 'df-ext-meta' });
    const pauseBtn = el('button', { type: 'button', class: 'df-dock-btn', 'aria-pressed': 'false', text: 'Pause' });
    const clearBtn = el('button', { type: 'button', class: 'df-dock-btn', text: 'Clear' });
    const filterInput = el('input', { type: 'search', class: 'df-field df-ext-filter', placeholder: 'Filter events', 'aria-label': `Filter ${stream.name} events` });
    const tbody = el('tbody');
    const headRow = el('tr', null, el('th', { text: '#' }), el('th', { text: 'Time' }));
    for (const column of (columns.length ? columns : ['data'])) headRow.append(el('th', { text: column }));
    const table = el('table', { class: 'df-ext-table' }, el('thead', null, headRow), tbody);
    const scroller = el('div', { class: 'df-ext-table-wrap' }, table);
    const emptyEl = el('div', { class: 'df-empty', text: 'Waiting for events…' });

    const element = el('div', { class: 'df-ext-stream' },
      el('div', { class: 'df-ext-stream-head' },
        el('span', { class: 'df-ext-tool-name', text: stream.name }),
        statusEl,
        stream.description ? el('span', { class: 'df-ext-tool-desc', text: stream.description }) : null),
      el('div', { class: 'df-ext-toolbar' }, pauseBtn, clearBtn, filterInput, countEl, droppedEl),
      scroller,
      emptyEl);

    pauseBtn.addEventListener('click', () => {
      paused = !paused;
      pauseBtn.textContent = paused ? 'Resume' : 'Pause';
      pauseBtn.setAttribute('aria-pressed', String(paused));
      if (!paused && pending.length) { for (const evt of pending.splice(0)) append(evt); renderRows(); }
      renderStatus();
    });
    clearBtn.addEventListener('click', () => {
      rows.length = 0;
      pending.length = 0;
      droppedTotal = 0;
      renderRows();
    });
    filterInput.addEventListener('input', () => { filter = filterInput.value; renderRows(); });

    function eventCount() { return rows.filter((r) => !r.marker).length; }

    function renderStatus() {
      const labels = {
        connecting: 'connecting…',
        live: 'live',
        polling: 'polling',
        reconnecting: 'reconnecting…',
        unavailable: 'stream unavailable',
      };
      statusEl.textContent = paused ? 'paused' : labels[state] || state;
      statusEl.className = 'df-ext-live df-ext-live-' + (paused ? 'paused' : state);
      const shown = rows.filter(isVisible).length;
      countEl.textContent = filter
        ? `${shown} of ${rows.filter((r) => !r.marker).length} events`
        : `${eventCount()} event${eventCount() === 1 ? '' : 's'}${pending.length ? ` · ${pending.length} pending` : ''}`;
      droppedEl.classList.toggle('df-hidden', droppedTotal === 0);
      droppedEl.textContent = droppedTotal ? `${droppedTotal} event${droppedTotal === 1 ? '' : 's'} dropped` : '';
      droppedEl.title = 'The app published faster than the Inspector read; older events were discarded.';
    }

    // Markers (dropped / restarted) show in the unfiltered view; a filter shows matching events only.
    function isVisible(row) { return row.marker ? !text(filter).trim() : eventMatches(row.cells, filter); }

    function rowElement(row) {
      if (row.marker) {
        const cell = el('td', { class: 'df-ext-marker', colspan: String(2 + Math.max(columns.length, 1)), text: row.marker });
        return el('tr', null, cell);
      }
      const tr = el('tr', null, el('td', { class: 'df-ext-seq', text: String(row.seq) }), el('td', { class: 'df-log-time', text: row.time }));
      for (const cell of row.cells) tr.append(el('td', { text: cell }));
      return tr;
    }

    function renderRows() {
      const visible = rows.filter(isVisible);
      const stick = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 24;
      tbody.replaceChildren(...visible.map(rowElement));
      emptyEl.classList.toggle('df-hidden', visible.length > 0);
      emptyEl.textContent = rows.length ? 'No events match the filter.' : 'Waiting for events…';
      if (stick) scroller.scrollTop = scroller.scrollHeight;
      renderStatus();
    }

    function append(evt) {
      const row = { seq: evt.seq, time: formatEventTime(evt.ts), cells: eventCells(evt.data, columns) };
      rows.push(row);
      if (rows.length > MAX_STREAM_ROWS) rows.splice(0, rows.length - MAX_STREAM_ROWS);
      return row;
    }

    function receive(evt) {
      if (!evt || typeof evt !== 'object') return;
      let structural = false;   // a marker row was added, so re-render instead of appending
      const dropped = Number(evt.dropped) || 0;
      if (dropped > 0) {
        droppedTotal += dropped;
        rows.push({ marker: `${dropped} event${dropped === 1 ? '' : 's'} dropped` });
        structural = true;
      }
      if (typeof evt.seq === 'number') {
        if (evt.seq <= lastSeq) {
          // Sequences restart with the app: the agent replays its buffer from the start.
          rows.push({ marker: 'Stream restarted (the app was relaunched)' });
          structural = true;
        }
        lastSeq = evt.seq;
        if (paused) {
          pending.push(evt);
          if (pending.length > MAX_STREAM_ROWS) pending.shift();
        } else {
          const row = append(evt);
          if (!structural && (!filter || eventMatches(row.cells, filter))) {
            const stick = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 24;
            tbody.append(rowElement(row));
            while (tbody.childElementCount > MAX_STREAM_ROWS) tbody.firstElementChild.remove();
            emptyEl.classList.add('df-hidden');
            if (stick) scroller.scrollTop = scroller.scrollHeight;
          }
        }
      }
      if (structural && !paused) renderRows();
      else renderStatus();
    }

    function setState(next) { state = next; renderStatus(); }

    function connect() {
      if (disposed || socket) return;
      clearTimeout(retryTimer);
      retryTimer = 0;
      if (Date.now() < pollUntil) { startPolling(); return; }
      stopPolling();
      gotMessageOnSocket = false;
      let opened = false;
      try {
        socket = new WebSocket(options.streamSocketUrl(ns, stream.name, lastSeq));
      } catch {
        socket = null;
        scheduleReconnect();
        return;
      }
      socket.onopen = () => { opened = true; wsFailures = 0; retryIndex = 0; setState('live'); };
      socket.onmessage = (message) => {
        gotMessageOnSocket = true;
        if (typeof message.data !== 'string') return;
        try { receive(JSON.parse(message.data)); } catch { /* ignore malformed frames */ }
      };
      socket.onerror = () => { try { socket && socket.close(); } catch { /* onclose reconnects */ } };
      socket.onclose = () => {
        socket = null;
        if (disposed) return;
        if (!opened) wsFailures += 1;
        if (wsFailures >= WS_FAILURES_BEFORE_POLLING) {
          wsFailures = 0;
          pollUntil = Date.now() + POLL_MODE_DURATION_MS;
        }
        scheduleReconnect();
      };
    }

    function scheduleReconnect() {
      if (disposed || retryTimer) return;
      setState(state === 'unavailable' ? 'unavailable' : 'reconnecting');
      const delay = STREAM_RETRY_MS[Math.min(retryIndex, STREAM_RETRY_MS.length - 1)];
      retryIndex += 1;
      retryTimer = setTimeout(() => { retryTimer = 0; connect(); }, delay);
    }

    // Polling fallback for hosts/proxies where the WebSocket upgrade keeps failing.
    function startPolling() {
      if (pollTimer || disposed) return;
      setState('polling');
      const tick = async () => {
        pollTimer = 0;
        if (disposed) return;
        if (Date.now() >= pollUntil) { connect(); return; }
        const response = await api.postDetailed('/api/extensions/events', { namespace: ns, stream: stream.name, since: lastSeq, limit: 200 });
        if (disposed) return;
        const body = response && response.body;
        if (response && response.ok && body && Array.isArray(body.events)) {
          if (body.dropped) receive({ dropped: body.dropped });
          for (const evt of body.events) receive(evt);
          if (!body.events.length && typeof body.nextSequence === 'number' && body.nextSequence < lastSeq) {
            rows.push({ marker: 'Stream restarted (the app was relaunched)' });
            lastSeq = body.nextSequence;
            renderRows();
          }
          setState('polling');
        } else {
          setState(response && response.status === 404 ? 'unavailable' : 'reconnecting');
        }
        pollTimer = setTimeout(tick, POLL_INTERVAL_MS);
      };
      pollTimer = setTimeout(tick, 0);
    }

    function stopPolling() { clearTimeout(pollTimer); pollTimer = 0; }

    function reconnectNow() {
      if (disposed) return;
      retryIndex = 0;
      if (socket && socket.readyState <= 1 && gotMessageOnSocket) return;
      try { socket && socket.close(); } catch { /* ignore */ }
      socket = null;
      clearTimeout(retryTimer);
      retryTimer = 0;
      connect();
    }

    function dispose() {
      disposed = true;
      clearTimeout(retryTimer);
      stopPolling();
      try { socket && socket.close(); } catch { /* ignore */ }
      socket = null;
    }

    renderRows();
    connect();
    return { element, dispose, reconnectNow, signature: JSON.stringify(stream.columns || []) };
  }

  function hash(value) {
    let h = 0;
    for (let i = 0; i < value.length; i++) h = ((h << 5) - h + value.charCodeAt(i)) | 0;
    return (h >>> 0).toString(36);
  }

  return Object.freeze({
    element: root,
    refresh,
    /** Mounts the panel into the Data dock body. */
    activate(container) {
      container.replaceChildren(root);
      const firstMount = !mounted;
      mounted = true;
      if (!loadedOnce) renderEmpty('Loading extensions…');
      else if (firstMount) render();
      return refresh();
    },
    deactivate() {},
    /** The app (re)connected or disconnected: reload descriptors and resume streams promptly. */
    async onConnectionChange(isConnected) {
      const wasConnected = connected;
      connected = isConnected;
      if (!isConnected || wasConnected) return;
      await refresh();
      for (const view of streamViews.values()) view.reconnectNow();
    },
    get hasExtensions() { return model.extensions.length > 0 || model.errors.length > 0; },
    get unavailable() { return unavailable; },
    dispose() {
      for (const view of streamViews.values()) view.dispose();
      streamViews.clear();
    },
  });
}
