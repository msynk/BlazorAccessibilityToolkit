// Blazor Accessibility Toolkit — in-page helpers.
// Runs inside the page under test. Accessible names and roles are computed with axe-core's implementation
// (axe.commons), which must be loaded before these helpers are called.
(() => {
  'use strict';
  const VERSION = '8';
  if (window.__blazorA11y && window.__blazorA11y.version === VERSION) {
    return;
  }

  const ORD_ATTR = 'data-a11y-ord';
  const LIVE_ROLES = new Set(['status', 'alert', 'log', 'marquee', 'timer']);
  const IDREF_ATTRS = ['aria-labelledby', 'aria-describedby', 'aria-controls', 'aria-owns', 'aria-errormessage', 'aria-activedescendant', 'aria-details', 'aria-flowto'];
  const STATE_ATTRS = ['aria-expanded', 'aria-checked', 'aria-selected', 'aria-pressed', 'aria-disabled', 'aria-required', 'aria-invalid', 'aria-current', 'aria-haspopup', 'aria-level', 'aria-valuenow', 'aria-valuetext', 'aria-modal', 'aria-busy', 'aria-readonly', 'aria-multiselectable', 'aria-orientation', 'aria-sort'];
  const TABBABLE_CANDIDATES = 'a[href], area[href], button, input, select, textarea, iframe, summary, [tabindex], [contenteditable]:not([contenteditable="false"]), audio[controls], video[controls]';

  let keyCounter = 0;
  const keys = new WeakMap();
  const keyOf = (el) => {
    let k = keys.get(el);
    if (!k) {
      k = ++keyCounter;
      keys.set(el, k);
    }
    return k;
  };

  const norm = (s) => (s == null ? '' : String(s).replace(/\s+/g, ' ').trim());
  const truncate = (s, n) => (s && s.length > n ? s.slice(0, n - 1) + '…' : s);

  function requireAxe() {
    if (typeof window.axe === 'undefined' || !window.axe.commons) {
      throw new Error('axe-core is not loaded in the page.');
    }
    return window.axe;
  }

  // Builds a fresh axe virtual tree for the duration of fn so that names and roles reflect the current DOM.
  function withAxe(fn) {
    const axe = requireAxe();
    try { axe.teardown(); } catch (e) { /* not set up */ }
    axe.setup(document);
    try {
      return fn(axe);
    } finally {
      try { axe.teardown(); } catch (e) { /* ignore */ }
    }
  }

  function startTag(el) {
    if (!el || !el.outerHTML) return null;
    const html = el.outerHTML;
    const end = html.indexOf('>');
    const open = end >= 0 ? html.slice(0, end + 1) : html;
    const text = norm(el.textContent);
    return truncate(open + (text ? truncate(text, 80) : '') + (el.children.length || text ? '…' : ''), 400);
  }

  function isUnique(root, sel, el) {
    try {
      const found = root.querySelectorAll(sel);
      return found.length === 1 && found[0] === el;
    } catch (e) {
      return false;
    }
  }

  function simpleSelector(el) {
    const root = el.getRootNode();
    if (el.id) {
      const idSel = '#' + CSS.escape(el.id);
      if (isUnique(root, idSel, el)) return idSel;
    }
    let sel = el.localName;
    const classes = Array.from(el.classList).filter((c) => !/^b-[a-z0-9]{10}$/.test(c)).slice(0, 2);
    if (classes.length) sel += '.' + classes.map((c) => CSS.escape(c)).join('.');
    const parent = el.parentElement;
    if (parent) {
      const siblings = Array.from(parent.children);
      if (siblings.filter((c) => { try { return c.matches(sel); } catch (e) { return false; } }).length > 1) {
        sel += ':nth-child(' + (siblings.indexOf(el) + 1) + ')';
      }
    }
    return sel;
  }

  function selectorFor(el) {
    if (!(el instanceof Element)) return null;
    const root = el.getRootNode();
    const parts = [];
    let node = el;
    while (node && node.nodeType === 1) {
      const part = simpleSelector(node);
      parts.unshift(part);
      const candidate = parts.join(' > ');
      if (isUnique(root, candidate, el)) return candidate;
      if (part.startsWith('#') || node === document.documentElement) break;
      node = node.parentElement;
    }
    return parts.join(' > ');
  }

  function cssVisible(el) {
    if (typeof el.checkVisibility === 'function') {
      return el.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true });
    }
    const cs = getComputedStyle(el);
    return !(cs.display === 'none' || cs.visibility === 'hidden' || cs.opacity === '0');
  }

  // Visible with a non-empty box: what a sighted keyboard user can see (used for focus targets and tab order).
  function isVisible(el) {
    if (!(el instanceof Element) || !el.isConnected) return false;
    if (!cssVisible(el)) return false;
    const rect = el.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) return true;
    // display: contents and similar: visible when any child box is visible.
    return getComputedStyle(el).display === 'contents' && Array.from(el.children).some(isVisible);
  }

  // Rendered and not hidden by CSS, regardless of size. An empty element (a live region waiting for its first
  // message, a panel that is still loading) is rendered even though its box has no area.
  function isRendered(el) {
    if (!(el instanceof Element) || !el.isConnected) return false;
    const cs = getComputedStyle(el);
    if (cs.display === 'contents') {
      // No box of its own (checkVisibility() reports false): rendered when its parent is.
      return cs.visibility !== 'hidden' && (!el.parentElement || isRendered(el.parentElement));
    }
    if (!cssVisible(el)) return false;
    return el.getClientRects().length > 0;
  }

  function isHiddenForAT(axe, el) {
    try {
      const v = axe.utils.getNodeFromTree(el);
      if (v && axe.commons.dom.isVisibleToScreenReaders) return !axe.commons.dom.isVisibleToScreenReaders(v);
    } catch (e) { /* fall through */ }
    return !!el.closest('[aria-hidden="true"], [hidden], [inert]');
  }

  function describe(axe, el) {
    let role = null;
    let name = '';
    let description = '';
    try { role = axe.commons.aria.getRole(el) || null; } catch (e) { role = el.getAttribute('role'); }
    try {
      const v = axe.utils.getNodeFromTree(el);
      name = norm(v ? axe.commons.text.accessibleTextVirtual(v) : axe.commons.text.accessibleText(el));
    } catch (e) {
      name = norm(el.getAttribute('aria-label') || el.textContent);
    }
    try {
      const ids = (el.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
      const parts = [];
      for (const id of ids) {
        const ref = el.getRootNode().getElementById ? el.getRootNode().getElementById(id) : document.getElementById(id);
        if (ref) parts.push(norm(ref.textContent));
      }
      description = norm(parts.join(' '));
      if (!description && el.title && norm(el.title) !== name) description = norm(el.title);
    } catch (e) { /* ignore */ }

    const states = {};
    for (const a of STATE_ATTRS) {
      if (el.hasAttribute(a)) states[a.slice(5)] = el.getAttribute(a);
    }
    if (el.matches(':disabled')) states.disabled = 'true';
    if ((el.type === 'checkbox' || el.type === 'radio') && el.localName === 'input') states.checked = el.indeterminate ? 'mixed' : String(el.checked);
    if (el.localName === 'option') states.selected = String(el.selected);
    if (el.required) states.required = 'true';
    if (el.localName === 'details') states.expanded = String(el.open);
    if (el.localName === 'summary' && el.parentElement && el.parentElement.localName === 'details') states.expanded = String(el.parentElement.open);
    if (/^h[1-6]$/.test(el.localName) && !states.level) states.level = el.localName.slice(1);

    let focusable = false;
    try { focusable = axe.commons.dom.isFocusable(el); } catch (e) { focusable = el.tabIndex >= 0; }

    return {
      key: keyOf(el),
      tagName: el.localName,
      role,
      name,
      description,
      states,
      selector: selectorFor(el),
      tabIndex: el.hasAttribute('tabindex') || focusable ? el.tabIndex : null,
      focusable,
      visible: isVisible(el),
      hiddenForAT: isHiddenForAT(axe, el),
      html: startTag(el),
      ordinal: typeof el.__a11yOrd === 'number' ? el.__a11yOrd : null,
    };
  }

  function activeElementDeep() {
    let a = document.activeElement;
    if (a && a.hasAttribute && a.hasAttribute('data-a11y-focus-sentinel')) return null;
    for (let i = 0; i < 50 && a; i++) {
      if (a.shadowRoot && a.shadowRoot.activeElement) {
        a = a.shadowRoot.activeElement;
        continue;
      }
      if (a.localName === 'iframe') {
        try {
          const inner = a.contentDocument && a.contentDocument.activeElement;
          if (inner && inner !== a.contentDocument.body) { a = inner; continue; }
        } catch (e) { /* cross-origin */ }
      }
      break;
    }
    if (!a || a === document.body || a === document.documentElement) return null;
    return a;
  }

  function modalContainer(el) {
    let node = el;
    while (node) {
      if (node instanceof Element) {
        try { if (node.matches(':modal')) return node; } catch (e) { /* :modal unsupported */ }
        if (node.getAttribute('aria-modal') === 'true' && /^(dialog|alertdialog)$/.test(node.getAttribute('role') || '')) return node;
      }
      node = node.parentNode || (node.host ? node.host : null);
    }
    return null;
  }

  function focusIndicator(el) {
    const cs = getComputedStyle(el);
    const outline = cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0;
    const shadow = cs.boxShadow && cs.boxShadow !== 'none';
    let focusVisible = false;
    try { focusVisible = el.matches(':focus-visible'); } catch (e) { /* unsupported */ }
    return { indicator: outline || shadow ? 'detected' : 'not-detected', focusVisible };
  }

  function focusInfo() {
    const el = activeElementDeep();
    if (!el) return { hasFocus: document.hasFocus(), element: null };
    return withAxe((axe) => {
      const info = describe(axe, el);
      const modal = modalContainer(el);
      const fi = focusIndicator(el);
      return {
        hasFocus: document.hasFocus(),
        element: info,
        modal: modal ? selectorFor(modal) : null,
        indicator: fi.indicator,
        focusVisible: fi.focusVisible,
      };
    });
  }

  function matchText(actual, m) {
    if (!m) return true;
    const a = norm(actual);
    if (m.pattern != null) {
      try { return new RegExp(m.pattern, m.flags || '').test(a); } catch (e) { return false; }
    }
    return m.exact ? a === norm(m.text) : a.toLowerCase().includes(norm(m.text).toLowerCase());
  }

  function focusMatches(arg) {
    const info = focusInfo();
    if (!info.element) return null;
    if (arg.role && info.element.role !== arg.role) return null;
    if (arg.name && !matchText(info.element.name, arg.name)) return null;
    return info;
  }

  function isTabbable(el) {
    if (el.matches(':disabled')) return false;
    if (el.tabIndex < 0) return false;
    if (el.closest('[inert]')) return false;
    if (el.localName === 'input' && el.type === 'hidden') return false;
    const details = el.closest('details:not([open])');
    if (details && !(el.localName === 'summary' && el.parentElement === details)) return false;
    return isVisible(el);
  }

  // Tabbable candidates in DOM order (heuristic). excludeLinks mirrors WebKit's default of skipping links on Tab.
  function tabbables(rootSelector, excludeLinks) {
    const root = rootSelector ? document.querySelector(rootSelector) : document;
    if (!root) return [];
    return Array.from(root.querySelectorAll(TABBABLE_CANDIDATES))
      .filter((el) => !el.hasAttribute('data-a11y-focus-sentinel'))
      .filter((el) => !(excludeLinks && (el.localName === 'a' || el.localName === 'area') && !el.hasAttribute('tabindex')))
      .filter(isTabbable)
      .map((el) => ({ key: keyOf(el), selector: selectorFor(el), tabIndex: el.tabIndex }));
  }

  // Moves keyboard focus to "the start of the document" so the next Tab reaches the first element of the tab order
  // (including positive tabindex values), or, with reverse=true, so the next Shift+Tab reaches the last one.
  // blur() alone is not enough: browsers keep the blurred element as the sequential navigation starting point.
  // A temporary sentinel (tabindex=1 first in the DOM, or tabindex=0 last in the DOM) holds focus and removes
  // itself as soon as focus leaves it.
  function blurActive(reverse) {
    for (const old of document.querySelectorAll('[data-a11y-focus-sentinel]')) old.remove();
    const a = document.activeElement;
    if (a && a !== document.body && typeof a.blur === 'function') a.blur();
    const sentinel = document.createElement('span');
    sentinel.setAttribute('data-a11y-focus-sentinel', '');
    sentinel.tabIndex = reverse ? 0 : 1;
    sentinel.addEventListener('focusout', () => setTimeout(() => sentinel.remove(), 0), { once: true });
    const parent = document.body || document.documentElement;
    if (reverse) parent.append(sentinel); else parent.prepend(sentinel);
    sentinel.focus({ preventScroll: true });
    return true;
  }

  function inScope(el, root, exclude) {
    if (root && !(root === el || root.contains(el))) return false;
    if (el.hasAttribute('data-a11y-focus-sentinel')) return false;
    for (const sel of exclude) {
      try { if (el.closest(sel)) return false; } catch (e) { /* invalid selector */ }
    }
    return true;
  }

  function byId(el, id) {
    const root = el.getRootNode();
    return root && root.getElementById ? root.getElementById(id) : document.getElementById(id);
  }

  function referencedIds() {
    const ids = new Set();
    for (const attr of IDREF_ATTRS) {
      for (const el of document.querySelectorAll('[' + attr + ']')) {
        for (const id of (el.getAttribute(attr) || '').split(/\s+/).filter(Boolean)) ids.add(id);
      }
    }
    for (const label of document.querySelectorAll('label[for]')) ids.add(label.getAttribute('for'));
    return ids;
  }

  // Toolkit diagnostics that complement axe-core. Each result identifies a toolkit rule; metadata lives in .NET.
  function runDiagnostics(opts) {
    opts = opts || {};
    const root = opts.root ? document.querySelector(opts.root) : null;
    if (opts.root && !root) throw new Error('Diagnostics scope "' + opts.root + '" matched no element.');
    const exclude = opts.exclude || [];
    const results = [];

    return withAxe((axe) => {
      const report = (ruleId, el, data, message) => {
        const d = describe(axe, el);
        results.push({ ruleId, selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal, data: data || {}, message: message || null });
      };
      const scope = root || document;
      const all = Array.from(scope.querySelectorAll('*')).filter((el) => inScope(el, root, exclude));

      // Duplicate ids that are not referenced (referenced duplicates are covered by axe-core duplicate-id-aria).
      const counts = new Map();
      for (const el of document.querySelectorAll('[id]')) {
        const id = el.id;
        if (id) counts.set(id, (counts.get(id) || 0) + 1);
      }
      const referenced = referencedIds();
      const firstSeen = new Set();
      for (const el of document.querySelectorAll('[id]')) {
        const id = el.id;
        if (!id || counts.get(id) < 2) continue;
        if (!firstSeen.has(id)) { firstSeen.add(id); continue; }
        if (referenced.has(id) || !inScope(el, root, exclude)) continue;
        report('blazor-duplicate-id', el, { id, count: String(counts.get(id)) });
      }

      // Broken id references.
      for (const el of all) {
        for (const attr of IDREF_ATTRS) {
          if (!el.hasAttribute(attr)) continue;
          const tokens = (el.getAttribute(attr) || '').split(/\s+/).filter(Boolean);
          const missing = tokens.filter((id) => !byId(el, id));
          if (missing.length) report('blazor-idref-missing-target', el, { attribute: attr, missing: missing.join(' ') }, attr + ' references missing id(s): ' + missing.join(', ') + '. Render the referenced element or remove the reference.');
        }
        if (el.localName === 'label' && el.htmlFor && !byId(el, el.htmlFor)) {
          report('blazor-idref-missing-target', el, { attribute: 'for', missing: el.htmlFor }, 'label for="' + el.htmlFor + '" does not match any element id.');
        }
      }

      // aria-expanded must match the visibility of the controlled element.
      for (const el of all) {
        const expanded = el.getAttribute('aria-expanded');
        const controls = el.getAttribute('aria-controls');
        if ((expanded !== 'true' && expanded !== 'false') || !controls) continue;
        for (const id of controls.split(/\s+/).filter(Boolean)) {
          const target = byId(el, id);
          if (!target) continue;
          const shown = isRendered(target);
          if ((expanded === 'true') !== shown) {
            report('blazor-expanded-state-mismatch', el, { expanded, controls: id, controlledVisible: String(shown) },
              'aria-expanded="' + expanded + '" but the controlled element #' + id + ' is ' + (shown ? 'visible' : 'hidden') + '. Update aria-expanded in the same render that shows or hides the content.');
          }
        }
      }

      // Tabs: selection state must be consistent with the panels it controls.
      for (const list of all.filter((el) => el.getAttribute('role') === 'tablist')) {
        const tabs = Array.from(list.querySelectorAll('[role="tab"]')).filter((t) => t.closest('[role="tablist"]') === list);
        if (tabs.length === 0) continue;
        const selected = tabs.filter((t) => t.getAttribute('aria-selected') === 'true');
        if (list.getAttribute('aria-multiselectable') !== 'true' && selected.length !== 1) {
          report('blazor-tab-selection-mismatch', list, { selectedTabs: String(selected.length) },
            selected.length === 0 ? 'No tab has aria-selected="true". Mark the active tab as selected.' : selected.length + ' tabs have aria-selected="true" in a single-select tablist.');
        }
        for (const tab of tabs) {
          const panelId = (tab.getAttribute('aria-controls') || '').split(/\s+/).filter(Boolean)[0];
          const panel = panelId ? byId(tab, panelId) : null;
          if (!panel) continue;
          const isSelected = tab.getAttribute('aria-selected') === 'true';
          const panelShown = isRendered(panel);
          if (isSelected !== panelShown) {
            report('blazor-tab-selection-mismatch', tab, { tab: describe(axe, tab).name, panelVisible: String(panelShown) },
              isSelected ? 'The selected tab controls a hidden panel.' : 'An unselected tab controls a visible panel.');
          }
        }
      }

      // Blazor ValidationMessage output must be associated with the control.
      const describedIds = new Set();
      for (const el of document.querySelectorAll('[aria-describedby], [aria-errormessage]')) {
        for (const attr of ['aria-describedby', 'aria-errormessage']) {
          for (const id of (el.getAttribute(attr) || '').split(/\s+/).filter(Boolean)) describedIds.add(id);
        }
      }
      for (const el of all) {
        if (!el.classList.contains('validation-message') || !norm(el.textContent)) continue;
        if (el.id && describedIds.has(el.id)) continue;
        report('blazor-validation-message-association', el, { text: truncate(norm(el.textContent), 120) });
      }

      // Invalid controls should reference an error message.
      for (const el of all) {
        const invalid = el.getAttribute('aria-invalid');
        if (invalid == null || invalid === 'false' || invalid === '') continue;
        const refs = ['aria-describedby', 'aria-errormessage']
          .flatMap((a) => (el.getAttribute(a) || '').split(/\s+/).filter(Boolean))
          .filter((id) => byId(el, id));
        if (!refs.length) report('blazor-invalid-without-error-reference', el, { ariaInvalid: invalid });
      }

      // Radio groups need a group label.
      const groups = new Map();
      for (const el of all) {
        if (el.localName !== 'input' || el.type !== 'radio' || !el.name || !isVisible(el)) continue;
        const key = (el.form ? keyOf(el.form) : 0) + '|' + el.name;
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(el);
      }
      for (const radios of groups.values()) {
        if (radios.length < 2) continue;
        const labelled = (container) => {
          if (!container) return false;
          if (container.localName === 'fieldset') {
            const legend = container.querySelector(':scope > legend');
            return !!(legend && norm(legend.textContent)) || !!norm(describe(axe, container).name);
          }
          return !!norm(describe(axe, container).name);
        };
        const container = radios[0].closest('fieldset, [role="radiogroup"], [role="group"]');
        if (container && radios.every((r) => container.contains(r)) && labelled(container)) continue;
        report('blazor-radio-group-ungrouped', radios[0], { name: radios[0].name, count: String(radios.length) });
      }

      // Fieldsets need a legend.
      for (const el of all) {
        if (el.localName !== 'fieldset') continue;
        const legend = el.querySelector(':scope > legend');
        if (legend && norm(legend.textContent)) continue;
        if (el.hasAttribute('aria-label') || el.hasAttribute('aria-labelledby')) continue;
        report('blazor-fieldset-missing-legend', el, {});
      }

      return results;
    });
  }

  // Role, name and ordinal for selectors reported by axe-core (single-document selectors only).
  function describeSelectors(selectors) {
    return withAxe((axe) => selectors.map((sel) => {
      let el = null;
      try { el = typeof sel === 'string' ? document.querySelector(sel) : null; } catch (e) { el = null; }
      if (!el) return null;
      const d = describe(axe, el);
      return { role: d.role, name: d.name, ordinal: d.ordinal };
    }));
  }

  function inspectSelector(selector) {
    const el = document.querySelector(selector);
    if (!el) return null;
    return withAxe((axe) => describe(axe, el));
  }

  function inspectElement(el) {
    return withAxe((axe) => describe(axe, el));
  }

  // Moves data-a11y-ord attributes into an expando so they do not appear in evidence or affect rules.
  function adoptOrdinals() {
    let n = 0;
    for (const el of document.querySelectorAll('[' + ORD_ATTR + ']')) {
      el.__a11yOrd = parseInt(el.getAttribute(ORD_ATTR), 10);
      el.removeAttribute(ORD_ATTR);
      n++;
    }
    return n;
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Live regions: records DOM evidence of live-region updates. This is NOT screen-reader output.
  // ---------------------------------------------------------------------------------------------------------------
  function isLiveRegionElement(el) {
    const live = el.getAttribute('aria-live');
    const role = el.getAttribute('role');
    return (!!live && live !== 'off') || (!!role && LIVE_ROLES.has(role)) || el.localName === 'output';
  }

  function liveRegionOf(node) {
    let el = node && node.nodeType === 1 ? node : node && node.parentElement;
    while (el) {
      const live = el.getAttribute && el.getAttribute('aria-live');
      const role = el.getAttribute && el.getAttribute('role');
      if ((live && live !== 'off') || (role && LIVE_ROLES.has(role)) || el.localName === 'output') return el;
      if (live === 'off') return null;
      el = el.parentElement;
    }
    return null;
  }

  function regionInfo(el) {
    const role = el.getAttribute('role');
    const live = el.getAttribute('aria-live');
    const implicit = role === 'alert' ? 'assertive' : role === 'status' || role === 'log' || el.localName === 'output' ? 'polite' : 'off';
    return {
      selector: selectorFor(el),
      role: role || (el.localName === 'output' ? 'status' : null),
      politeness: live || implicit,
      ariaLive: live,
      atomic: el.getAttribute('aria-atomic') === 'true' || ((role === 'alert' || role === 'status') && el.getAttribute('aria-atomic') !== 'false'),
      relevant: el.getAttribute('aria-relevant') || 'additions text',
      busy: el.getAttribute('aria-busy') === 'true',
      hidden: !isRendered(el) || !!el.closest('[aria-hidden="true"]'),
      text: truncate(norm(el.textContent), 500),
    };
  }

  function installLiveRegionObserver() {
    if (window.__blazorA11yLive) return true;
    // documentId tells records of this document apart from those of a previous one, so a cursor taken before a
    // navigation does not hide the records of the new page (sequence numbers restart with every document).
    const state = { seq: 0, records: [], known: new WeakSet(), observer: null, documentId: performance.timeOrigin + ':' + Math.random().toString(36).slice(2) };
    window.__blazorA11yLive = state;
    const markKnown = () => {
      for (const el of document.querySelectorAll('[aria-live], [role="status"], [role="alert"], [role="log"], [role="marquee"], [role="timer"], output')) state.known.add(el);
    };

    state.observer = new MutationObserver((mutations) => {
      const batch = new Map();
      const entry = (region) => {
        if (!batch.has(region)) batch.set(region, { added: [], removed: [], inserted: false });
        return batch.get(region);
      };
      for (const m of mutations) {
        if (m.type === 'childList') {
          // Resolve the region from the mutation target: an added node may already be detached again when the
          // callback runs (for example a status text replaced by the next render in the same batch).
          const targetRegion = liveRegionOf(m.target);
          for (const n of m.addedNodes) {
            if (n.nodeType === 1 && isLiveRegionElement(n)) {
              const e = entry(n);
              if (!state.known.has(n)) e.inserted = true;
              else e.added.push(norm(n.textContent));
              continue;
            }
            if (targetRegion) {
              entry(targetRegion).added.push(norm(n.textContent));
              continue;
            }
            // A newly inserted subtree may contain live regions.
            if (n.nodeType === 1) {
              for (const inner of n.querySelectorAll('[aria-live], [role="status"], [role="alert"], [role="log"], output')) {
                if (!state.known.has(inner)) entry(inner).inserted = true;
              }
            }
          }
          if (targetRegion) {
            for (const n of m.removedNodes) entry(targetRegion).removed.push(norm(n.textContent));
          }
        } else if (m.type === 'characterData') {
          const region = liveRegionOf(m.target);
          if (region) entry(region).added.push(norm(m.target.textContent));
        } else if (m.type === 'attributes') {
          // An existing element that becomes a live region starts being tracked; its current text is not an update.
          if (liveRegionOf(m.target) === m.target) state.known.add(m.target);
        }
      }
      const now = performance.now();
      for (const [region, e] of batch) {
        const info = regionInfo(region);
        const added = e.added.filter(Boolean);
        const removed = e.removed.filter(Boolean).join(' ');
        const wasPresent = state.known.has(region);
        state.known.add(region);
        if (!e.inserted && added.length === 0 && !removed && wasPresent) continue;
        // textIsCurrent: info.text is the region text after this change. It is false for all but the last of several
        // updates delivered in one callback, because only the final text can still be read.
        const push = (kind, addedText, removedText, textIsCurrent) => {
          state.records.push({
            sequence: ++state.seq,
            timestamp: now,
            kind,
            regionWasPresent: wasPresent,
            addedText: truncate(addedText, 500),
            removedText: truncate(removedText, 500),
            textIsCurrent,
            ...info,
          });
          if (state.records.length > 1000) state.records.shift();
        };
        if (!wasPresent) {
          push('inserted', added.join(' '), removed, true);
        } else if (added.length === 0) {
          push('content-removed', '', removed, true);
        } else {
          // Several updates can arrive in one observer callback (for example two quick Blazor renders); keep one record
          // per added text so that each update can be asserted on its own.
          added.forEach((text, i) => push('content-added', text, i === 0 ? removed : '', i === added.length - 1));
        }
      }
    });

    const start = () => {
      markKnown();
      // Observe the document node rather than document.documentElement, which does not exist yet when an init script
      // installs the observer on a new document.
      state.observer.observe(document, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['aria-live', 'role'] });
    };
    if (document.readyState === 'loading') {
      // Installed by an init script before the page was parsed: content that arrives with the server response is not
      // an update, so observation starts once parsing is complete.
      document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
      start();
    }
    return true;
  }

  // Sequence numbers restart with every document: a cursor from another document (taken before a navigation) means
  // "from the beginning" in this one.
  function liveAfter(s, documentId, sequence) {
    return documentId && documentId !== s.documentId ? 0 : (sequence || 0);
  }

  function liveRecords(afterSequence, documentId) {
    const s = window.__blazorA11yLive;
    if (!s) return null;
    const after = liveAfter(s, documentId, afterSequence);
    return s.records.filter((r) => r.sequence > after);
  }

  function liveCursor() {
    const s = window.__blazorA11yLive;
    return s ? { documentId: s.documentId, sequence: s.seq } : null;
  }

  // Finds the first record after the cursor whose announced text matches. Only text that a live region would present
  // counts: the added text, or the whole region text when the region was inserted or is atomic. Removals and updates
  // of hidden regions are not presented, so they never match.
  function findLiveRecord(arg) {
    const s = window.__blazorA11yLive;
    if (!s) return null;
    const after = liveAfter(s, arg.documentId, arg.after);
    for (const r of s.records) {
      if (r.sequence <= after) continue;
      if (r.kind === 'content-removed' || r.hidden) continue;
      if (arg.politeness && r.politeness !== arg.politeness) continue;
      const candidates = [r.addedText];
      if (r.kind === 'inserted' || (r.atomic && r.textIsCurrent)) candidates.push(r.text);
      if (candidates.some((c) => !!c && matchText(c, arg.match))) return Object.assign({ documentId: s.documentId }, r);
    }
    return null;
  }

  function liveRegions() {
    return Array.from(document.querySelectorAll('[aria-live], [role="status"], [role="alert"], [role="log"], [role="marquee"], [role="timer"], output')).map(regionInfo);
  }

  window.__blazorA11y = {
    version: VERSION,
    selectorFor,
    isVisible,
    focusInfo,
    focusMatches,
    tabbables,
    blurActive,
    runDiagnostics,
    describeSelectors,
    inspectSelector,
    inspectElement,
    adoptOrdinals,
    installLiveRegionObserver,
    liveRecords,
    liveCursor,
    findLiveRecord,
    liveRegions,
    activeElementDeep,
  };
})();
