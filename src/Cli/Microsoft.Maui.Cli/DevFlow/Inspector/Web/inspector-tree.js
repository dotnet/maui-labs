export function createElementTreeController(options) {
  const {
    treePanel,
    viewport,
    countElement,
    getSelectedId,
    onSelect,
    onHover,
  } = options;
  const collapsedIds = new Set();
  let lastSignature = '';

  function collectElements() {
    const map = new Map();
    viewport.querySelectorAll('.devflow-element').forEach((element) => {
      const id = element.getAttribute('data-id');
      if (!id) return;
      const automationId = element.getAttribute('data-automationId') || '';
      const text = element.getAttribute('data-text') || '';
      map.set(id, {
        id,
        parentId: element.getAttribute('data-parentId') || null,
        type: element.getAttribute('data-type') || 'Element',
        automationId,
        text,
        name: automationId || text,
        context: '',
        hasSource: element.getAttribute('data-hasSource') === 'true',
        visible: element.getAttribute('data-isVisible') !== 'false',
      });
    });

    const automationIdCounts = new Map();
    map.forEach((node) => {
      if (node.automationId)
        automationIdCounts.set(node.automationId, (automationIdCounts.get(node.automationId) || 0) + 1);
    });
    map.forEach((node) => {
      if (!node.automationId || automationIdCounts.get(node.automationId) < 2) return;
      if (/label/i.test(node.type) && node.text && node.text !== node.automationId) {
        node.context = node.text;
        return;
      }
      const siblingLabel = [...map.values()].find((candidate) =>
        candidate.parentId === node.parentId && /label/i.test(candidate.type) && candidate.text);
      node.context = siblingLabel?.text || (node.text !== node.automationId ? node.text : '');
    });
    return map;
  }

  function signature(map) {
    const parts = [];
    map.forEach((node) => parts.push(
      node.id + '>' + (node.parentId || '') + '|' + node.type + '|' + node.automationId + '|' + node.text
      + '|' + node.visible + '|' + node.hasSource));
    return parts.sort().join(',');
  }

  function typeIcon(type) {
    const normalized = (type || '').toLowerCase();
    if (/shell|page|window|tabbar|flyout/.test(normalized)) return 'i-window';
    if (/collectionview|listview|carousel|tableview/.test(normalized)) return 'i-list';
    if (/button/.test(normalized)) return 'i-button';
    if (/entry|editor|searchbar|picker|stepper|slider/.test(normalized)) return 'i-input';
    if (/checkbox|switch|radiobutton/.test(normalized)) return 'i-check';
    if (/image/.test(normalized)) return 'i-image';
    if (/grid|stack|layout|border|frame|scrollview|contentview|contentpresenter/.test(normalized)) return 'i-layout';
    if (/label|span|text/.test(normalized)) return 'i-text';
    return 'i-node';
  }

  function visibleRows() {
    return [...treePanel.querySelectorAll('.df-tree-node')]
      .filter((row) => !row.closest('.df-tree-children.df-collapsed'));
  }

  function focusRow(row) {
    if (!row) return;
    treePanel.querySelectorAll('.df-tree-node').forEach((candidate) => {
      candidate.tabIndex = candidate === row ? 0 : -1;
    });
    row.focus();
  }

  function setExpanded(row, expanded) {
    if (!row || !row.hasAttribute('aria-expanded')) return;
    const item = row.closest('.df-tree-item');
    const children = item && item.querySelector(':scope > .df-tree-children');
    const twisty = row.querySelector('.df-tree-twisty');
    const id = row.dataset.treeId;
    if (expanded) collapsedIds.delete(id); else collapsedIds.add(id);
    row.setAttribute('aria-expanded', String(expanded));
    if (children) children.classList.toggle('df-collapsed', !expanded);
    if (twisty) twisty.classList.toggle('df-open', expanded);
  }

  function handleKeydown(event) {
    const row = event.currentTarget;
    const rows = visibleRows();
    const index = rows.indexOf(row);
    let target = null;
    if (event.key === 'ArrowDown') target = rows[index + 1] || row;
    else if (event.key === 'ArrowUp') target = rows[index - 1] || row;
    else if (event.key === 'Home') target = rows[0] || row;
    else if (event.key === 'End') target = rows[rows.length - 1] || row;
    else if (event.key === 'ArrowRight') {
      if (row.getAttribute('aria-expanded') === 'false') setExpanded(row, true);
      else if (row.getAttribute('aria-expanded') === 'true') {
        const item = row.closest('.df-tree-item');
        target = item && item.querySelector(':scope > .df-tree-children > .df-tree-item > .df-tree-node');
      }
    } else if (event.key === 'ArrowLeft') {
      if (row.getAttribute('aria-expanded') === 'true') setExpanded(row, false);
      else {
        const parentGroup = row.closest('.df-tree-children');
        const parentItem = parentGroup && parentGroup.closest('.df-tree-item');
        target = parentItem && parentItem.querySelector(':scope > .df-tree-node');
      }
    } else if (event.key === 'Enter' || event.key === ' ') {
      onSelect(row.dataset.treeId);
    } else {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    if (target) focusRow(target);
  }

  function renderNode(id, map, childrenById, depth) {
    const node = map.get(id);
    const childIds = childrenById.get(id) || [];
    const wrapper = document.createElement('div');
    wrapper.className = 'df-tree-item';
    wrapper.setAttribute('role', 'none');

    const row = document.createElement('div');
    row.className = 'df-tree-node';
    row.dataset.treeId = id;
    row.setAttribute('role', 'treeitem');
    row.setAttribute('aria-level', String(depth + 1));
    row.setAttribute('aria-selected', 'false');
    row.tabIndex = -1;
    if (!node.visible) row.classList.add('df-hidden-el');
    row.style.paddingLeft = (depth * 12 + 4) + 'px';

    const twisty = document.createElement('span');
    const hasChildren = childIds.length > 0;
    if (hasChildren) row.setAttribute('aria-expanded', String(!collapsedIds.has(id)));
    twisty.className = 'df-tree-twisty'
      + (hasChildren ? '' : ' df-leaf')
      + (hasChildren && !collapsedIds.has(id) ? ' df-open' : '');
    if (hasChildren) twisty.innerHTML = '<svg class="df-ic-xs"><use href="#i-chevron"/></svg>';

    const label = document.createElement('span');
    label.className = 'df-tree-label';
    const icon = document.createElement('span');
    icon.className = 'df-tree-icon';
    icon.innerHTML = '<svg class="df-ic-xs"><use href="#' + typeIcon(node.type) + '"/></svg>';
    label.appendChild(icon);
    const typeElement = document.createElement('span');
    typeElement.className = 'df-tree-type';
    typeElement.textContent = node.type;
    label.appendChild(typeElement);
    if (node.name) {
      const name = document.createElement('span');
      name.className = 'df-tree-name';
      name.textContent = ' ' + node.name;
      label.appendChild(name);
    }
    if (node.context) {
      const context = document.createElement('span');
      context.className = 'df-tree-context';
      context.textContent = ` “${node.context}”`;
      label.appendChild(context);
    }
    if (node.hasSource) {
      const source = document.createElement('span');
      source.className = 'df-tree-src';
      source.innerHTML = '<svg class="df-ic-xs"><use href="#i-source"/></svg>';
      source.title = 'XAML source available';
      label.appendChild(source);
    }

    row.append(twisty, label);
    wrapper.appendChild(row);

    let childrenWrapper = null;
    if (hasChildren) {
      childrenWrapper = document.createElement('div');
      childrenWrapper.className = 'df-tree-children' + (collapsedIds.has(id) ? ' df-collapsed' : '');
      childrenWrapper.setAttribute('role', 'group');
      childIds.forEach((childId) => childrenWrapper.appendChild(renderNode(childId, map, childrenById, depth + 1)));
      wrapper.appendChild(childrenWrapper);
    }

    twisty.addEventListener('click', (event) => {
      event.stopPropagation();
      if (!hasChildren) return;
      setExpanded(row, collapsedIds.has(id));
    });
    row.addEventListener('click', () => { focusRow(row); onSelect(id); });
    row.addEventListener('keydown', handleKeydown);
    row.addEventListener('mouseenter', () => onHover(id));
    row.addEventListener('mouseleave', () => onHover(null));
    return wrapper;
  }

  function updateSelection() {
    const selectedId = getSelectedId();
    const rows = [...treePanel.querySelectorAll('.df-tree-node')];
    let selectedRow = null;
    for (const row of rows) {
      const selected = !!selectedId && row.dataset.treeId === selectedId;
      row.classList.toggle('df-selected', selected);
      row.setAttribute('aria-selected', String(selected));
      if (selected) selectedRow = row;
    }
    const tabbable = selectedRow || rows.find((row) => row.tabIndex === 0) || rows[0];
    rows.forEach((row) => { row.tabIndex = row === tabbable ? 0 : -1; });
  }

  function build() {
    const map = collectElements();
    lastSignature = signature(map);
    const childrenById = new Map();
    const roots = [];
    map.forEach((node) => {
      if (node.parentId && map.has(node.parentId)) {
        if (!childrenById.has(node.parentId)) childrenById.set(node.parentId, []);
        childrenById.get(node.parentId).push(node.id);
      } else {
        roots.push(node.id);
      }
    });
    treePanel.replaceChildren();
    const fragment = document.createDocumentFragment();
    roots.forEach((id) => fragment.appendChild(renderNode(id, map, childrenById, 0)));
    treePanel.appendChild(fragment);
    if (countElement) countElement.textContent = map.size ? String(map.size) : '';
    updateSelection();
  }

  function syncStructure() {
    if (signature(collectElements()) !== lastSignature) build();
  }

  function reveal(id) {
    const row = [...treePanel.querySelectorAll('.df-tree-node')]
      .find((candidate) => candidate.dataset.treeId === id);
    if (!row) return;
    let parent = row.parentElement;
    while (parent && parent !== treePanel) {
      if (parent.classList.contains('df-tree-children')) {
        const parentItem = parent.closest('.df-tree-item');
        setExpanded(parentItem && parentItem.querySelector(':scope > .df-tree-node'), true);
      }
      parent = parent.parentElement;
    }
    row.scrollIntoView({ block: 'nearest' });
  }

  return Object.freeze({ build, reveal, syncStructure, updateSelection });
}