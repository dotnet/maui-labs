const ENUMS = Object.freeze({
  LayoutOptions: ['Start', 'Center', 'End', 'Fill'],
  TextAlignment: ['Start', 'Center', 'End'],
  FontAttributes: ['None', 'Bold', 'Italic'],
  LineBreakMode: ['NoWrap', 'WordWrap', 'CharacterWrap', 'HeadTruncation', 'MiddleTruncation', 'TailTruncation'],
});

const PROPERTY_DESCRIPTORS = Object.freeze({
  '*': [['IsVisible', 'bool'], ['IsEnabled', 'bool'], ['Opacity', 'number'], ['BackgroundColor', 'color']],
  Label: [['Text', 'text'], ['TextColor', 'color'], ['FontSize', 'number'], ['FontAttributes', 'enum', ENUMS.FontAttributes], ['HorizontalTextAlignment', 'enum', ENUMS.TextAlignment], ['LineBreakMode', 'enum', ENUMS.LineBreakMode]],
  Button: [['Text', 'text'], ['TextColor', 'color'], ['FontSize', 'number']],
  Entry: [['Text', 'text'], ['Placeholder', 'text'], ['TextColor', 'color']],
  Editor: [['Text', 'text'], ['Placeholder', 'text'], ['TextColor', 'color']],
  SearchBar: [['Text', 'text'], ['Placeholder', 'text']],
  CheckBox: [['IsChecked', 'bool'], ['Color', 'color']],
  Switch: [['IsToggled', 'bool'], ['OnColor', 'color']],
  Frame: [['BorderColor', 'color'], ['CornerRadius', 'number'], ['HasShadow', 'bool']],
  StackLayout: [['Spacing', 'number']],
});

// Keep the older-agent fallback aligned with DevFlowAgentService.GetPropertyConstraints.
const PROPERTY_CONSTRAINTS = Object.freeze({
  Opacity: Object.freeze({ min: 0, max: 1, step: 0.05 }),
  FontSize: Object.freeze({ min: 0.1 }),
});

function descriptorsFor(type) {
  const specific = PROPERTY_DESCRIPTORS[type]
    || (/StackLayout$/.test(type) ? PROPERTY_DESCRIPTORS.StackLayout : []);
  return [...specific, ...PROPERTY_DESCRIPTORS['*']].map(([name, kind, choices]) => ({
    name,
    kind,
    choices: choices || null,
    writable: true,
    persistable: true,
    ...(PROPERTY_CONSTRAINTS[name] || {}),
  }));
}

function normalizeDescriptors(value) {
  if (!value || value.supported !== true || !Array.isArray(value.properties)) return null;
  const allowedKinds = new Set(['bool', 'enum', 'text', 'color', 'number']);
  const seen = new Set();
  const descriptors = [];
  for (const property of value.properties) {
    if (!property || typeof property.name !== 'string' || !allowedKinds.has(property.kind)) continue;
    const name = property.name.trim();
    const key = name.toLowerCase();
    if (!name || seen.has(key)) continue;
    seen.add(key);
    descriptors.push({
      name,
      kind: property.kind,
      value: Object.prototype.hasOwnProperty.call(property, 'value') ? property.value : null,
      choices: Array.isArray(property.choices) ? property.choices.map(String) : null,
      writable: property.writable === true,
      persistable: property.persistable === true,
      min: Number.isFinite(property.min) ? property.min : null,
      max: Number.isFinite(property.max) ? property.max : null,
      step: Number.isFinite(property.step) ? property.step : null,
    });
  }
  return descriptors;
}

function parseHexColor(value) {
  if (value == null) return null;
  const normalized = String(value).trim().replace(/^#/, '');
  if (/^[0-9a-fA-F]{8}$/.test(normalized)) {
    return { rgb: '#' + normalized.slice(0, 6), alpha: normalized.slice(6).toUpperCase() };
  }
  if (/^[0-9a-fA-F]{6}$/.test(normalized)) return { rgb: '#' + normalized, alpha: 'FF' };
  return null;
}

function shortFile(path) {
  const parts = String(path).split(/[\\/]/);
  return parts[parts.length - 1] || path;
}

export function createPropertyGridController(options) {
  const {
    pane,
    body,
    labelElement,
    closeButton,
    api,
    getIsWriter,
    prepareOpen,
    syncPaneChrome,
    setStatus,
    onRuntimeChange,
    onOpen,
    onClose,
  } = options;
  let loadToken = 0;

  function updateWriterState() {
    for (const field of body.querySelectorAll('.df-field:not(.df-prop-filter)')) {
      field.disabled = !getIsWriter()
        || field.dataset.writable === 'false'
        || field.dataset.sourceBusy === 'true';
    }
    for (const button of body.querySelectorAll('.df-prop-source')) {
      button.disabled = !getIsWriter()
        || button.dataset.busy === 'true'
        || button.dataset.valueValid === 'false'
        || button.dataset.dirty !== 'true';
    }
  }

  function close() {
    const wasOpen = !pane.classList.contains('df-hidden');
    pane.classList.add('df-hidden');
    body.replaceChildren();
    labelElement.textContent = '';
    loadToken++;
    syncPaneChrome();
    if (wasOpen && onClose) onClose();
  }

  async function open(targetElement) {
    const elementId = targetElement.getAttribute('data-id');
    if (!elementId) return;

    prepareOpen();
    const type = targetElement.dataset.type || 'Element';
    labelElement.textContent = options.labelFor(targetElement);
    pane.classList.remove('df-hidden');
    syncPaneChrome();
    if (onOpen) onOpen();
    body.replaceChildren();
    const currentLoad = ++loadToken;
    const described = normalizeDescriptors(await api.post('/api/getProperties', { elementId }));
    const descriptors = described || descriptorsFor(type);
    if (!described) {
      const values = await Promise.all(descriptors.map(({ name }) =>
        api.post('/api/getProperty', { elementId, name })));
      descriptors.forEach((descriptor, index) => {
        const result = values[index];
        descriptor.value = result && Object.prototype.hasOwnProperty.call(result, 'value')
          ? result.value
          : null;
      });
    }
    if (currentLoad !== loadToken) return;

    if (described && descriptors.length === 0) {
      body.replaceChildren(document.createTextNode('No editable properties reported by this control.'));
      updateWriterState();
      return;
    }

    const filter = document.createElement('input');
    filter.id = 'df-prop-filter';
    filter.type = 'search';
    filter.className = 'df-field df-prop-filter';
    filter.placeholder = 'Filter properties';
    filter.setAttribute('aria-label', 'Filter properties');
    const noResults = document.createElement('div');
    noResults.className = 'df-prop-no-results';
    noResults.textContent = 'No matching properties.';
    noResults.hidden = true;
    filter.addEventListener('input', () => {
      const query = filter.value.trim().toLowerCase();
      let matches = 0;
      for (const propertyRow of body.querySelectorAll('.df-prop-row')) {
        const propertyName = propertyRow.dataset.propertyName || '';
        propertyRow.hidden = !!query && !propertyName.includes(query);
        if (!propertyRow.hidden) matches++;
      }
      noResults.hidden = !query || matches > 0;
    });
    body.append(filter, noResults);

    for (const descriptor of descriptors) {
      const { name, kind, choices } = descriptor;
      const hasValue = descriptor.value != null;
      const value = hasValue ? descriptor.value : null;

      const row = document.createElement('label');
      row.className = 'df-prop-row';
      row.dataset.propertyName = name.toLowerCase();
      const nameElement = document.createElement('span');
      nameElement.className = 'df-prop-name';
      nameElement.textContent = name;
      nameElement.title = name;
      const fieldWrapper = document.createElement('span');
      fieldWrapper.className = 'df-prop-field';

      let editor;
      let readValue;
      let valueEdited = false;
      let unsetColorControl = null;
      let unsetColorLabel = null;
      if (kind === 'bool') {
        editor = document.createElement('input');
        editor.type = 'checkbox';
        editor.className = 'df-field';
        editor.checked = String(value).toLowerCase() === 'true';
        readValue = () => String(editor.checked);
      } else if (kind === 'enum') {
        editor = document.createElement('select');
        editor.className = 'df-field';
        const options = (choices || []).slice();
        if (value != null && !options.includes(String(value))) options.unshift(String(value));
        for (const choice of options) {
          const option = document.createElement('option');
          option.value = choice;
          option.textContent = choice;
          editor.appendChild(option);
        }
        if (value != null) editor.value = String(value);
        readValue = () => editor.value;
      } else if (kind === 'color') {
        editor = document.createElement('input');
        editor.type = 'color';
        editor.className = 'df-field';
        const color = parseHexColor(value);
        if (color) {
          editor.value = color.rgb;
          editor.dataset.alpha = color.alpha;
        } else {
          editor.dataset.representable = 'false';
          editor.setAttribute('aria-label', `${name}: unset; activate to choose a color`);
          unsetColorControl = document.createElement('span');
          unsetColorControl.className = 'df-color-unset';
          unsetColorLabel = document.createElement('span');
          unsetColorLabel.className = 'df-color-unset-label';
          unsetColorLabel.textContent = 'Unset';
        }
        editor.title = value != null ? String(value) : '';
        readValue = () => {
          const alpha = editor.dataset.alpha || 'FF';
          return alpha === 'FF' ? editor.value : '#' + alpha + editor.value.slice(1);
        };
      } else if (kind === 'text') {
        editor = document.createElement('textarea');
        editor.rows = String(value ?? '').includes('\n') ? 3 : 1;
        editor.className = 'df-field df-text-field';
        const originalValue = value == null ? '' : String(value);
        editor.value = originalValue;
        readValue = () => valueEdited ? editor.value : originalValue;
      } else {
        editor = document.createElement('input');
        editor.type = 'number';
        editor.className = 'df-field';
        editor.required = true;
        if (descriptor.min != null) editor.min = String(descriptor.min);
        if (descriptor.max != null) editor.max = String(descriptor.max);
        editor.step = descriptor.step != null ? String(descriptor.step) : 'any';
        if (value != null) editor.value = value;
        readValue = () => editor.value;
      }
      if (!hasValue) {
        editor.dataset.representable = 'false';
        editor.title = 'Value unavailable. Enter a value explicitly before applying it to XAML.';
      }
      editor.dataset.writable = descriptor.writable === false ? 'false' : 'true';
      if (descriptor.writable === false) editor.title = editor.title || 'This property is read-only.';

      const errorElement = document.createElement('span');
      errorElement.className = 'df-prop-error';
      errorElement.setAttribute('role', 'alert');
      const setFieldError = (message) => {
        editor.classList.toggle('df-invalid', !!message);
        editor.setAttribute('aria-invalid', String(!!message));
        errorElement.textContent = message || '';
      };

      let sourceButton = null;
      const setSourceState = (state) => {
        if (!sourceButton) return;
        const details = {
          clean: { icon: '#i-source', label: `Change ${name} to enable Apply to XAML` },
          pending: { icon: '#i-edit', label: `Apply ${name} live before saving it to XAML` },
          dirty: { icon: '#i-save', label: `Apply changed ${name} to XAML source` },
          busy: { icon: '#i-refresh', label: `Applying ${name} to XAML source` },
          saved: { icon: '#i-check', label: `${name} is saved to XAML source` },
        }[state];
        sourceButton.dataset.state = state;
        sourceButton.dataset.dirty = state === 'dirty' || state === 'busy' ? 'true' : 'false';
        sourceButton.dataset.busy = state === 'busy' ? 'true' : 'false';
        editor.dataset.sourceBusy = state === 'busy' ? 'true' : 'false';
        sourceButton.classList.toggle('df-saved', state === 'saved');
        sourceButton.querySelector('use')?.setAttribute('href', details.icon);
        sourceButton.title = details.label;
        sourceButton.setAttribute('aria-label', details.label);
        updateWriterState();
      };
      const syncSourceValidity = () => {
        if (!sourceButton) return;
        sourceButton.dataset.valueValid = editor.checkValidity()
          && editor.dataset.representable !== 'false' ? 'true' : 'false';
        updateWriterState();
      };
      editor.addEventListener('input', () => {
        valueEdited = true;
        editor.dataset.representable = 'true';
        setSourceState('pending');
        setFieldError('');
        if (unsetColorControl) {
          fieldWrapper.insertBefore(editor, unsetColorControl);
          unsetColorControl.remove();
          unsetColorControl = null;
          unsetColorLabel = null;
          editor.removeAttribute('aria-label');
        }
        syncSourceValidity();
      });
      editor.addEventListener('change', async () => {
        valueEdited = true;
        editor.dataset.representable = 'true';
        if (!editor.checkValidity()) {
          editor.reportValidity();
          const message = `Enter a valid ${name} value.`;
          setSourceState('pending');
          setFieldError(message);
          setStatus(message);
          syncSourceValidity();
          return;
        }
        const nextValue = readValue();
        const response = await api.postDetailed('/api/setProperty', { elementId, name, value: nextValue });
        if (response.ok) {
          setFieldError('');
          setSourceState('dirty');
          onRuntimeChange({ elementId, name, value: nextValue });
        } else {
          setSourceState('pending');
          const message = response.status === 0
            ? 'Could not reach the running app. Reconnect and try again.'
            : ((response.body && response.body.error) || `The running app rejected ${name}.`);
          setFieldError(message);
          setStatus(message);
        }
        syncSourceValidity();
      });

        if (targetElement.getAttribute('data-hasSource') === 'true'
          && descriptor.persistable !== false
          && descriptor.writable !== false) {
        sourceButton = document.createElement('button');
        sourceButton.type = 'button';
        sourceButton.className = 'df-prop-source';
        sourceButton.innerHTML = '<svg class="df-ic"><use href="#i-source"/></svg>';
        fieldWrapper.classList.add('df-has-source');
        sourceButton.addEventListener('click', async () => {
          if (!getIsWriter()) {
            setStatus('Read-only — take control before updating XAML source.');
            return;
          }
          if (!editor.checkValidity() || editor.dataset.representable === 'false') {
            editor.reportValidity();
            setStatus(`Enter a valid ${name} value before updating XAML source.`);
            syncSourceValidity();
            return;
          }
          if (sourceButton.dataset.dirty !== 'true') {
            setStatus(`Change ${name} in the running app before updating XAML source.`);
            return;
          }

          setSourceState('busy');
          try {
            const response = await api.postDetailed('/api/persistProperty', {
              elementId,
              name,
              value: readValue(),
            });
            if (response.ok && response.body && response.body.ok) {
              if (sourceButton.dataset.state === 'busy') {
                setSourceState('saved');
                setStatus(`Saved ${name} to ${shortFile(response.body.file || 'XAML source')}.`);
              }
            } else {
              setSourceState('dirty');
              setStatus(response.body && response.body.error
                ? response.body.error
                : 'Could not update the XAML source.');
            }
          } finally {
            if (sourceButton.dataset.state === 'busy') setSourceState('dirty');
          }
        });
        setSourceState('clean');
        syncSourceValidity();
      }

      row.appendChild(nameElement);
      if (unsetColorControl) {
        unsetColorControl.append(unsetColorLabel, editor);
        fieldWrapper.appendChild(unsetColorControl);
      } else {
        fieldWrapper.appendChild(editor);
      }
      if (sourceButton) fieldWrapper.appendChild(sourceButton);
      fieldWrapper.appendChild(errorElement);
      row.appendChild(fieldWrapper);
      body.appendChild(row);
    }
    updateWriterState();
  }

  if (closeButton) closeButton.addEventListener('click', close);
  return Object.freeze({ close, open, updateWriterState });
}