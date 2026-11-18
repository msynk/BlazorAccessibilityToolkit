// Blazor Accessibility Toolkit — in-page helpers.
// Runs inside the page under test. Accessible names and roles are computed with axe-core's implementation
// (axe.commons), which must be loaded before these helpers are called.
(() => {
  'use strict';
  const VERSION = '10';
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

  // Style properties that commonly carry a focus indicator. Comparing them with the element's unfocused style
  // (recorded by recordFocusBaselines) detects indicators made from borders, backgrounds, text decoration, pseudo
  // elements or the parent (:focus-within), not only outlines and box-shadows.
  const focusBaselines = new WeakMap();

  // The visible appearance of a box, normalized so that properties without a visible effect (an outline-offset or
  // outline-color while outline-style is none, a border color of a zero-width border) do not count as a change.
  function appearance(cs, pseudo) {
    if (pseudo && (cs.content === 'none' || cs.content === 'normal' || cs.display === 'none')) return 'none';
    const outline = cs.outlineStyle === 'none' || parseFloat(cs.outlineWidth) === 0 ? 'none' : [cs.outlineStyle, cs.outlineWidth, cs.outlineColor, cs.outlineOffset].join(' ');
    const border = ['Top', 'Right', 'Bottom', 'Left'].map((side) => {
      const style = cs['border' + side + 'Style'];
      const width = cs['border' + side + 'Width'];
      return style === 'none' || style === 'hidden' || parseFloat(width) === 0 ? 'none' : [style, width, cs['border' + side + 'Color']].join(' ');
    }).join(',');
    const decoration = !cs.textDecorationLine || cs.textDecorationLine === 'none' ? 'none' : [cs.textDecorationLine, cs.textDecorationStyle, cs.textDecorationColor, cs.textDecorationThickness].join(' ');
    const parts = [outline, border, decoration, cs.boxShadow, cs.backgroundColor, cs.backgroundImage, cs.color, cs.fontWeight, cs.transform, cs.filter, cs.opacity, cs.visibility];
    if (pseudo) parts.push(cs.content, cs.width, cs.height);
    return parts.join('|');
  }

  function styleSignature(el) {
    const sig = { self: appearance(getComputedStyle(el), false) };
    try { sig.before = appearance(getComputedStyle(el, '::before'), true); } catch (e) { sig.before = ''; }
    try { sig.after = appearance(getComputedStyle(el, '::after'), true); } catch (e) { sig.after = ''; }
    sig.parent = el.parentElement ? appearance(getComputedStyle(el.parentElement), false) : '';
    return sig;
  }

  // Records the unfocused style of every focusable candidate (except the focused element) so that focusIndicator can
  // tell whether focusing changes anything visible.
  function recordFocusBaselines(rootSelector) {
    const root = rootSelector ? document.querySelector(rootSelector) : document;
    if (!root) return 0;
    const active = activeElementDeep();
    let n = 0;
    for (const el of root.querySelectorAll(TABBABLE_CANDIDATES)) {
      if (el === active || el.hasAttribute('data-a11y-focus-sentinel')) continue;
      focusBaselines.set(el, styleSignature(el));
      n++;
    }
    return n;
  }

  function focusIndicator(el) {
    const cs = getComputedStyle(el);
    const outline = cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) > 0;
    const shadow = !!cs.boxShadow && cs.boxShadow !== 'none';
    let focusVisible = false;
    try { focusVisible = el.matches(':focus-visible'); } catch (e) { /* unsupported */ }
    const base = focusBaselines.get(el);
    if (!base) {
      return { indicator: outline || shadow ? 'detected' : 'not-detected', focusVisible, method: 'outline' };
    }
    const now = styleSignature(el);
    const changed = ['self', 'before', 'after', 'parent'].filter((k) => now[k] !== base[k]);
    return { indicator: changed.length ? 'detected' : 'not-detected', focusVisible, method: 'style-change', changed };
  }

  // Whether the element is hidden behind other content (WCAG 2.4.11/2.4.12). Samples nine points of each visible box
  // and asks the browser which element is on top there. Only author content that covers the element counts; points
  // outside the viewport are ignored, and an element entirely outside it is reported as such.
  function obscuration(el) {
    try {
      if (getComputedStyle(el).pointerEvents === 'none') return { state: 'unknown' };
      const doc = el.ownerDocument;
      const win = doc.defaultView;
      const root = el.getRootNode();
      const probe = root && typeof root.elementFromPoint === 'function' ? root : doc;
      const rects = Array.from(el.getClientRects()).filter((r) => r.width > 0 && r.height > 0);
      if (!rects.length) return { state: 'unknown' };
      let samples = 0;
      let hits = 0;
      let cover = null;
      for (const r of rects) {
        const left = Math.max(r.left, 0);
        const right = Math.min(r.right, win.innerWidth);
        const top = Math.max(r.top, 0);
        const bottom = Math.min(r.bottom, win.innerHeight);
        if (right - left < 1 || bottom - top < 1) continue;
        for (const fx of [0.1, 0.5, 0.9]) {
          for (const fy of [0.1, 0.5, 0.9]) {
            const hit = probe.elementFromPoint(left + (right - left) * fx, top + (bottom - top) * fy);
            samples++;
            if (!hit || hit === el || el.contains(hit) || hit.contains(el)) hits++;
            else if (!cover) cover = hit;
          }
        }
      }
      if (samples === 0) return { state: 'outside-viewport' };
      if (hits === samples) return { state: 'none' };
      return { state: hits === 0 ? 'entire' : 'partial', coveredBy: cover ? selectorFor(cover) : null };
    } catch (e) {
      return { state: 'unknown' };
    }
  }

  function focusInfo() {
    const el = activeElementDeep();
    if (!el) return { hasFocus: document.hasFocus(), element: null };
    return withAxe((axe) => {
      const info = describe(axe, el);
      const modal = modalContainer(el);
      const fi = focusIndicator(el);
      const ob = obscuration(el);
      const r = el.getBoundingClientRect();
      return {
        bounds: { x: r.left + window.scrollX, y: r.top + window.scrollY, width: r.width, height: r.height },
        rtl: getComputedStyle(document.documentElement).direction === 'rtl',
        hasFocus: document.hasFocus(),
        element: info,
        modal: modal ? selectorFor(modal) : null,
        indicator: fi.indicator,
        indicatorMethod: fi.method,
        focusVisible: fi.focusVisible,
        obscured: ob.state,
        obscuredBy: ob.coveredBy || null,
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
        // A trigger that is not rendered states nothing to anyone, for example a menu button that only appears on
        // narrow screens while the navigation it controls is always shown on wide ones.
        if (!isRendered(el)) continue;
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

      // The current page link of a navigation (Blazor NavLink adds class="active") should expose aria-current.
      for (const el of all) {
        if (el.localName !== 'a' || !el.hasAttribute('href') || !el.classList.contains('active')) continue;
        if (!el.closest('nav, [role="navigation"]') || !isRendered(el)) continue;
        const current = el.getAttribute('aria-current');
        if (current && current !== 'false') continue;
        report('blazor-navlink-missing-aria-current', el, { href: el.getAttribute('href') || '' });
      }

      // Fields that collect personal data should identify their purpose (autocomplete).
      for (const el of all) {
        const purpose = personalDataPurpose(el);
        if (!purpose || el.hasAttribute('autocomplete') || !isRendered(el)) continue;
        report('blazor-input-purpose-missing', el, { suggested: purpose },
          'This field appears to collect "' + purpose + '" but has no autocomplete attribute. Add autocomplete="' + purpose + '" (or the token that matches its purpose).');
      }

      // Required fields must not be marked as required only visually.
      for (const el of all) {
        if (!isFormControl(el) || el.required || el.getAttribute('aria-required') === 'true' || !isRendered(el)) continue;
        const text = labelTexts(el).find(isRequiredMarker);
        if (text) report('blazor-required-not-exposed', el, { label: truncate(text, 120) });
      }

      // Names: links with the same name but different destinations (2.4.4), and names that repeat the role.
      const linksByName = new Map();
      for (const el of all) {
        const isLink = el.localName === 'a' && el.hasAttribute('href');
        const isButton = el.localName === 'button' || el.getAttribute('role') === 'button';
        if ((!isLink && !isButton) || !isVisible(el) || isHiddenForAT(axe, el)) continue;
        const d = describe(axe, el);
        const name = norm(d.name);
        if (!name) continue;
        if (d.role === 'button' && /(^|\s)button$/i.test(name) && !/^button$/i.test(name)) {
          report('blazor-role-in-name', el, { name, role: 'button' }, 'The name "' + name + '" ends with "button", which screen readers announce again ("' + name + ', button"). Remove the role word.');
        } else if (d.role === 'link' && /^link(\s+to)?\s|\slink$/i.test(name)) {
          report('blazor-role-in-name', el, { name, role: 'link' }, 'The name "' + name + '" contains the word "link", which screen readers announce again. Remove it.');
        }
        if (!isLink || d.role !== 'link') continue;
        let destination;
        try { const u = new URL(el.getAttribute('href'), document.baseURI); destination = u.origin + u.pathname + u.search; } catch (e) { destination = (el.getAttribute('href') || '').split('#')[0]; }
        const key = name.toLowerCase();
        if (!linksByName.has(key)) linksByName.set(key, []);
        linksByName.get(key).push({ el, destination });
      }
      for (const [, links] of linksByName) {
        const destinations = new Set(links.map((l) => l.destination));
        if (destinations.size < 2) continue;
        const name = norm(describe(axe, links[0].el).name);
        report('blazor-ambiguous-link-name', links[0].el, { name, links: String(links.length), destinations: String(destinations.size) },
          links.length + ' links are named "' + name + '" but lead to ' + destinations.size + ' different destinations. Make each name say where it goes, for example with visually hidden text.');
      }

      // <Virtualize> renders only the visible items between two aria-hidden spacers ("height: Npx; flex-shrink: 0").
      // When a spacer stands for omitted items, each item should expose aria-posinset and aria-setsize.
      const isSpacer = (el) => el.localName === 'div' && el.getAttribute('aria-hidden') === 'true' && el.children.length === 0
        && /height:\s*[\d.]+px;\s*flex-shrink:\s*0/.test(el.getAttribute('style') || '');
      const virtualContainers = new Set();
      for (const el of all) {
        if (isSpacer(el) && el.parentElement && parseFloat((/height:\s*([\d.]+)px/.exec(el.getAttribute('style')) || [])[1] || '0') > 0) virtualContainers.add(el.parentElement);
      }
      for (const container of virtualContainers) {
        if (!inScope(container, root, exclude)) continue;
        const items = Array.from(container.children).filter((c) => !isSpacer(c) && !/^(script|template|style)$/.test(c.localName));
        if (items.length === 0) continue;
        const positioned = (item) => item.hasAttribute('aria-setsize') && item.hasAttribute('aria-posinset')
          || !!item.querySelector('[aria-setsize][aria-posinset]');
        const missing = items.filter((item) => !positioned(item)).length;
        if (missing > 0) {
          report('blazor-virtualized-set-size', container, { rendered: String(items.length), withoutPosition: String(missing) },
            'This virtualized list renders ' + items.length + ' items, ' + missing + ' of them without aria-posinset and aria-setsize, so screen readers announce only the rendered items. Add aria-setsize="@total" and aria-posinset="@(index + 1)" to each item.');
        }
      }

      // Two interactive elements with the same box: one target announced twice (Android ATF DuplicateClickableBounds).
      const interactiveSelector = 'a[href], button, input:not([type=hidden]), select, textarea, summary, [role=button], [role=link], [role=checkbox], [role=menuitem], [role=option], [role=tab], [tabindex]:not([tabindex="-1"])';
      for (const el of all) {
        if (!el.matches(interactiveSelector) || !isVisible(el)) continue;
        const outer = el.parentElement && el.parentElement.closest(interactiveSelector);
        if (!outer || !isVisible(outer)) continue;
        const a = el.getBoundingClientRect(), b = outer.getBoundingClientRect();
        if (Math.abs(a.left - b.left) <= 1 && Math.abs(a.top - b.top) <= 1 && Math.abs(a.width - b.width) <= 1 && Math.abs(a.height - b.height) <= 1) {
          report('blazor-duplicate-clickable-bounds', el, { outer: selectorFor(outer) },
            'This element and the interactive element around it (' + startTag(outer) + ') have the same size and position. Keep only one of them interactive.');
        }
      }

      // The .NET reconnect modal: a named dialog whose status changes are announced.
      const reconnect = document.getElementById('components-reconnect-modal');
      if (reconnect && inScope(reconnect, root, exclude)) {
        const problems = [];
        const isDialog = reconnect.localName === 'dialog' || /^(alert)?dialog$/.test(reconnect.getAttribute('role') || '');
        // The dialog is usually closed while audited, so its name is judged from the markup, not computed.
        const labelledBy = (reconnect.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean).map((id) => byId(reconnect, id)).filter(Boolean);
        const named = norm(reconnect.getAttribute('aria-label')) || labelledBy.some((ref) => norm(ref.textContent)) || norm(reconnect.getAttribute('title'));
        if (isDialog && !named) problems.push('the dialog has no accessible name (add aria-labelledby that points at its message)');
        const live = reconnect.matches('[role=status], [role=alert], [aria-live]:not([aria-live=off])') || reconnect.querySelector('[role=status], [role=alert], [aria-live]:not([aria-live=off])');
        if (!live) problems.push('its status messages (rejoining, failed, paused) are not in a live region, so changes are not announced (wrap them in role="status")');
        if (problems.length) {
          report('blazor-reconnect-ui-not-accessible', reconnect, { problems: String(problems.length) }, '#components-reconnect-modal: ' + problems.join('; ') + '.');
        }
      }

      // In-page links that resolve to another document: with Blazor's <base href="/">, href="#main" opens the home page.
      for (const el of all) {
        if (el.localName !== 'a') continue;
        const raw = el.getAttribute('href') || '';
        if (raw.length < 2 || raw[0] !== '#') continue;
        let target;
        try { target = new URL(el.href); } catch (e) { continue; }
        if (target.origin + target.pathname + target.search !== location.origin + location.pathname + location.search) {
          report('blazor-fragment-link-leaves-page', el, { href: raw, resolved: target.pathname + target.search + target.hash },
            '<a href="' + raw + '"> resolves to ' + target.pathname + target.hash + ' (the document base URL), so it leaves this page instead of moving to ' + raw + '. Include the path in the link, or use the SkipLink component.');
        }
      }

      // WCAG 2.2: accessible authentication (3.3.8), redundant entry (3.3.7) and dragging (2.5.7).
      if (opts.wcag22 !== false) {
        for (const el of all) {
          if (el.localName !== 'input' || !isRendered(el)) continue;
          const type = (el.getAttribute('type') || 'text').toLowerCase();
          const autocomplete = (el.getAttribute('autocomplete') || '').toLowerCase().split(/\s+/).filter(Boolean);
          if (type === 'password' && autocomplete.length === 1 && autocomplete[0] === 'off') {
            report('blazor-paste-blocked', el, { reason: 'autocomplete-off', credential: 'True' },
              '<input type="password" autocomplete="off"> asks password managers not to fill the field. Use autocomplete="current-password" or "new-password".');
          }
          const credential = type === 'password' || autocomplete.some((t) => t === 'username' || t === 'current-password' || t === 'new-password' || t === 'one-time-code');
          if (credential && opts.pasteProbe !== false && !el.disabled && !el.readOnly && pasteIsBlocked(el)) {
            report('blazor-paste-blocked', el, { reason: 'paste', credential: 'True' },
              'Pasting into this field is cancelled (an event handler calls preventDefault, for example @onpaste:preventDefault). Allow pasting: people who cannot memorize or transcribe passwords and codes rely on it.');
          }
          const purpose = redundantEntryPurpose(el, type);
          if (purpose) report('blazor-redundant-entry', el, { purpose });
        }
        const draggableGroups = new Map();
        for (const el of all) {
          if (el.getAttribute('draggable') !== 'true' || !isRendered(el) || isHiddenForAT(axe, el)) continue;
          const group = draggableGroups.get(el.parentElement);
          if (group) group.count++; else draggableGroups.set(el.parentElement, { first: el, count: 1 });
        }
        for (const { first, count } of draggableGroups.values()) report('blazor-dragging-alternative', first, { draggable: String(count) });
      }

      // The Blazor error notification must be announced and dismissible with the keyboard. It is hidden until an error
      // occurs, so it is checked whether or not it is rendered.
      const errorUi = document.getElementById('blazor-error-ui');
      if (errorUi && inScope(errorUi, root, exclude)) {
        const problems = [];
        const role = errorUi.getAttribute('role');
        const live = errorUi.getAttribute('aria-live');
        if (role !== 'alert' && role !== 'status' && (!live || live === 'off')) problems.push('it is not a live region, so the error is not announced when it appears (add role="alert")');
        for (const dismiss of errorUi.querySelectorAll('.dismiss')) {
          const operable = dismiss.localName === 'button' || (dismiss.localName === 'a' && dismiss.hasAttribute('href'))
            || (dismiss.getAttribute('role') === 'button' && dismiss.hasAttribute('tabindex') && dismiss.tabIndex >= 0);
          if (!operable) {
            problems.push('its <' + dismiss.localName + ' class="dismiss"> cannot be reached or activated with the keyboard (use <button type="button" class="dismiss" aria-label="Dismiss">)');
            continue;
          }
          const name = (dismiss.getAttribute('aria-label') || '') + ' ' + (dismiss.getAttribute('aria-labelledby') ? 'x' : '') + ' ' + dismiss.textContent;
          if (!/[\p{L}\p{N}]/u.test(name)) problems.push('its dismiss control has no text name (add aria-label="Dismiss")');
        }
        if (problems.length) {
          report('blazor-error-ui-not-accessible', errorUi, { problems: String(problems.length) },
            '#blazor-error-ui: ' + problems.join('; ') + '.');
        }
      }

      return results;
    });
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Form heuristics.
  // ---------------------------------------------------------------------------------------------------------------
  const PURPOSE_BY_TYPE = { email: 'email', tel: 'tel', password: 'current-password' };
  const PURPOSE_BY_NAME = [
    [/^(e?mail|emailaddress|useremail)$/, 'email'],
    [/^(phone|phonenumber|telephone|telephonenumber|tel|mobile|mobilephone|mobilenumber|cellphone|cellphonenumber)$/, 'tel'],
    [/^(firstname|givenname|forename)$/, 'given-name'],
    [/^(lastname|familyname|surname)$/, 'family-name'],
    [/^(fullname|yourname)$/, 'name'],
    [/^(username|userid|login|loginname)$/, 'username'],
    [/^(password|currentpassword)$/, 'current-password'],
    [/^(newpassword|confirmpassword|passwordconfirmation|repeatpassword)$/, 'new-password'],
    [/^(zip|zipcode|postalcode|postcode)$/, 'postal-code'],
    [/^(street|streetaddress|address|address1|addressline1)$/, 'address-line1'],
    [/^(address2|addressline2)$/, 'address-line2'],
    [/^(birthdate|dateofbirth|dob|bday|birthday)$/, 'bday'],
  ];
  const NON_TEXT_TYPES = new Set(['hidden', 'submit', 'button', 'reset', 'image', 'checkbox', 'radio', 'file', 'range', 'color']);

  function personalDataPurpose(el) {
    if (el.localName !== 'input' && el.localName !== 'textarea' && el.localName !== 'select') return null;
    const type = el.localName === 'input' ? (el.getAttribute('type') || 'text').toLowerCase() : el.localName;
    if (NON_TEXT_TYPES.has(type)) return null;
    // Blazor forms name fields after the bound model path (for example "Model.Email" or "Input.PhoneNumber").
    const tokens = [el.getAttribute('name'), el.id].filter(Boolean)
      .map((v) => (v.split(/[.[\]]/).filter(Boolean).pop() || '').toLowerCase().replace(/[^a-z0-9]/g, ''));
    for (const token of tokens) {
      for (const [pattern, purpose] of PURPOSE_BY_NAME) {
        if (pattern.test(token)) return purpose;
      }
    }
    return PURPOSE_BY_TYPE[type] || null;
  }

  // Help mechanisms in document order, with the landmark that contains them (WCAG 3.2.6).
  function helpMechanisms(opts) {
    opts = opts || {};
    const pattern = new RegExp(opts.pattern || '\\b(help|contact|support|faq|chat)\\b', 'i');
    const extra = opts.selector ? Array.from(document.querySelectorAll(opts.selector)) : [];
    return withAxe((axe) => {
      const found = [];
      for (const el of document.querySelectorAll('a[href], button, [role=button], [role=link]')) {
        if (!isVisible(el) || isHiddenForAT(axe, el)) continue;
        const href = (el.getAttribute('href') || '').toLowerCase();
        const name = norm(describe(axe, el).name);
        const kind = href.startsWith('mailto:') ? 'email' : href.startsWith('tel:') ? 'phone' : pattern.test(name) ? 'link' : null;
        if (!kind && !extra.includes(el)) continue;
        found.push({ el, name, kind: kind || 'custom' });
      }
      for (const el of extra) {
        if (!found.some((f) => f.el === el) && isVisible(el)) found.push({ el, name: norm(describe(axe, el).name || el.textContent), kind: 'custom' });
      }
      found.sort((a, b) => (a.el.compareDocumentPosition(b.el) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1));
      return found.map((f) => {
        const landmark = f.el.closest('header, footer, nav, main, aside, [role=banner], [role=contentinfo], [role=navigation], [role=main], [role=complementary]');
        let region = 'none';
        if (landmark) {
          try { region = axe.commons.aria.getRole(landmark) || landmark.localName; } catch (e) { region = landmark.localName; }
        }
        return { key: f.kind + ':' + f.name.toLowerCase(), name: f.name, kind: f.kind, region, selector: selectorFor(f.el) };
      });
    });
  }

  // Interactive elements smaller than the minimum size, except inline links in text (WCAG 2.5.5 exception).
  function targetSizeReport(opts) {
    opts = opts || {};
    const min = opts.min || 44;
    const exclude = opts.exclude || [];
    const max = opts.max || 20;
    const selector = TABBABLE_CANDIDATES + ', [role=button], [role=link], [role=checkbox], [role=radio], [role=switch], [role=tab], [role=menuitem], [role=option]';
    return withAxe((axe) => {
      const offenders = [];
      for (const el of document.querySelectorAll(selector)) {
        if (offenders.length >= max) break;
        if (!inScope(el, null, exclude) || !isVisible(el) || isHiddenForAT(axe, el) || el.matches(':disabled')) continue;
        if (el.localName === 'input' && el.type === 'hidden') continue;
        if (el.tabIndex < 0 && !el.getAttribute('role')) continue;
        // Inline links inside a sentence are exempt.
        if (el.localName === 'a' && getComputedStyle(el).display === 'inline' && el.parentElement && norm(el.parentElement.textContent).length > norm(el.textContent).length + 1) continue;
        // A wrapping label enlarges the target of a checkbox or radio button.
        const label = el.closest('label');
        const rect = (label && /^(checkbox|radio)$/.test(el.type) ? label : el).getBoundingClientRect();
        if (rect.width >= min && rect.height >= min) continue;
        const d = describe(axe, el);
        offenders.push({ selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal, width: Math.round(rect.width), height: Math.round(rect.height) });
      }
      return offenders;
    });
  }

  // Dispatches a synthetic, empty paste event and reports whether a handler cancelled it. Blazor implements
  // @onpaste:preventDefault in its event delegation, which is invisible in the DOM, so this is the only way to see it.
  function pasteIsBlocked(el) {
    let event;
    try {
      event = new ClipboardEvent('paste', { bubbles: true, cancelable: true, composed: true, clipboardData: new DataTransfer() });
    } catch (e) {
      event = new Event('paste', { bubbles: true, cancelable: true, composed: true });
    }
    const value = el.value;
    try {
      el.dispatchEvent(event);
    } catch (e) {
      return false;
    }
    if (el.value !== value) el.value = value;
    return event.defaultPrevented;
  }

  // "ConfirmEmail", "Model.EmailConfirmation", "repeat-email": a second field for information already asked for.
  // Repeated passwords are exempt (re-entering a new password is accepted for security).
  const REDUNDANT_ENTRY = /^(?:(?:confirm|repeat|verify|retype|reenter)(e?mail|emailaddress|phone|phonenumber|username)|(e?mail|emailaddress|phone|phonenumber|username)(?:confirm|confirmation|again|repeat|verify|verification))$/;
  function redundantEntryPurpose(el, type) {
    if (NON_TEXT_TYPES.has(type) || type === 'password') return null;
    for (const value of [el.getAttribute('name'), el.id]) {
      if (!value) continue;
      const token = (value.split(/[.[\]]/).filter(Boolean).pop() || '').toLowerCase().replace(/[^a-z0-9]/g, '');
      const match = REDUNDANT_ENTRY.exec(token);
      if (match) return match[1] || match[2];
    }
    return null;
  }

  // Native form controls that can be required.
  function isFormControl(el) {
    if (el.localName === 'select' || el.localName === 'textarea') return true;
    if (el.localName !== 'input') return false;
    return !/^(hidden|submit|button|reset|image|range|color)$/.test((el.getAttribute('type') || 'text').toLowerCase());
  }

  function labelTexts(el) {
    const texts = [];
    if (el.labels) for (const label of el.labels) texts.push(norm(label.textContent));
    for (const id of (el.getAttribute('aria-labelledby') || '').split(/\s+/).filter(Boolean)) {
      const ref = byId(el, id);
      if (ref) texts.push(norm(ref.textContent));
    }
    return texts.filter(Boolean);
  }

  // "Email *", "* Email", "Email (required)" or "Email - required".
  function isRequiredMarker(text) {
    return /^\*|\*\s*:?$|\(\s*required\s*\)|[:\-–]\s*required$/i.test(text);
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Adaptation checks: reflow (WCAG 1.4.10) and text spacing (WCAG 1.4.12).
  // ---------------------------------------------------------------------------------------------------------------
  // Parts of a page that may need a two-dimensional layout (WCAG 1.4.10 exception).
  const TWO_DIMENSIONAL = 'table, [role="table"], [role="grid"], [role="treegrid"], [role="toolbar"], [role="application"], pre, svg, canvas, video, iframe, img[usemap], map, [data-a11y-allow-horizontal-scroll]';

  // The nearest ancestor that clips or scrolls the element on the given axis (so the element cannot widen the page).
  function clippedByAncestor(el, axis) {
    for (let p = el.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement) {
      const cs = getComputedStyle(p);
      if ((axis === 'x' ? cs.overflowX : cs.overflowY) !== 'visible') return p;
    }
    return null;
  }

  function reflowReport(opts) {
    opts = opts || {};
    const exclude = opts.exclude || [];
    const doc = document.documentElement;
    const viewport = doc.clientWidth;
    const scrollWidth = Math.max(doc.scrollWidth, document.body ? document.body.scrollWidth : 0);
    const rtl = getComputedStyle(doc).direction === 'rtl';
    const overflowing = (el) => {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) return false;
      return rtl ? r.left < -1 : r.right > viewport + 1;
    };
    const offenders = [];
    if (scrollWidth <= viewport + 1 || !document.body) return { viewportWidth: viewport, scrollWidth, offenders };
    return withAxe((axe) => {
      for (const el of document.body.querySelectorAll('*')) {
        if (offenders.length >= (opts.max || 20)) break;
        if (!inScope(el, null, exclude) || !isVisible(el) || !overflowing(el)) continue;
        // Report where the overflow starts: skip elements whose parent already overflows.
        const parent = el.parentElement;
        if (parent && parent !== document.body && isVisible(parent) && overflowing(parent)) continue;
        if (clippedByAncestor(el, 'x')) continue;
        const r = el.getBoundingClientRect();
        const d = describe(axe, el);
        offenders.push({ selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal, exempt: !!el.closest(TWO_DIMENSIONAL), width: Math.round(r.width), right: Math.round(r.right) });
      }
      return { viewportWidth: viewport, scrollWidth, offenders };
    });
  }

  const TEXT_SPACING_CSS = '* { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; } p { margin-bottom: 2em !important; }';

  function clippingState(el) {
    const cs = getComputedStyle(el);
    const clips = (o) => o === 'hidden' || o === 'clip';
    const x = clips(cs.overflowX) && el.scrollWidth > el.clientWidth + 1;
    const y = clips(cs.overflowY) && el.scrollHeight > el.clientHeight + 1;
    const clamped = !!cs.webkitLineClamp && cs.webkitLineClamp !== 'none' && el.scrollHeight > el.clientHeight + 1;
    return { clipped: x || y || clamped, x, y: y || clamped };
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Accessibility tree for virtual screen-reader transcripts (same shape as the bUnit tree in ComponentTesting).
  // ---------------------------------------------------------------------------------------------------------------
  const ATOMIC_ROLES = new Set(['button', 'link', 'checkbox', 'radio', 'switch', 'img', 'textbox', 'searchbox', 'combobox', 'slider', 'spinbutton',
    'progressbar', 'meter', 'scrollbar', 'separator', 'option', 'tab', 'menuitem', 'menuitemcheckbox', 'menuitemradio', 'heading', 'columnheader', 'rowheader']);
  const TREE_STATES = ['level', 'checked', 'pressed', 'expanded', 'selected', 'current', 'haspopup', 'disabled', 'readonly', 'required', 'invalid', 'busy', 'valuetext', 'valuenow'];

  function mergeTree(nodes) {
    const result = [];
    let text = [];
    const flush = () => { if (text.length) { result.push({ role: 'text', name: norm(text.join(' ')) }); text = []; } };
    for (const n of nodes) {
      if (n.role === 'text') text.push(n.name);
      else { flush(); if (n.role !== 'break') result.push(n); }
    }
    flush();
    return result.filter((n) => n.role !== 'text' || n.name);
  }

  function accessibilityTree(arg) {
    const root = arg && arg.element ? arg.element : document.body;
    if (!root) throw new Error('The page has no body.');
    return withAxe((axe) => {
      const children = (el) => {
        const out = [];
        for (const c of (el.shadowRoot ? el.shadowRoot.childNodes : el.childNodes)) build(c, out);
        return out;
      };
      const build = (node, out) => {
        if (node.nodeType === Node.TEXT_NODE) {
          const t = norm(node.textContent);
          if (t) out.push({ role: 'text', name: t });
          return;
        }
        if (node.nodeType !== Node.ELEMENT_NODE) return;
        const el = node;
        if (/^(script|style|template|noscript)$/.test(el.localName) || isHiddenForAT(axe, el)) return;
        // A label that wraps its control is read as part of the control, not twice.
        if (el.localName === 'label' && el.querySelector('input:not([type=hidden]), select, textarea, meter, progress, output')) {
          for (const n of children(el)) if (n.role !== 'text') out.push(n);
          return;
        }
        const d = describe(axe, el);
        const role = d.role;
        if (!role || role === 'none' || role === 'presentation' || role === 'generic') {
          const block = el.localName === 'br' || /^(block|flex|grid|list-item|table)/.test(getComputedStyle(el).display);
          if (block) out.push({ role: 'break' });
          out.push(...children(el));
          if (block) out.push({ role: 'break' });
          return;
        }
        const states = {};
        for (const key of TREE_STATES) {
          if (d.states[key] != null && d.states[key] !== '') states[key] = String(d.states[key]).trim();
        }
        out.push({ role, name: d.name, description: d.description || '', states, children: ATOMIC_ROLES.has(role) ? [] : mergeTree(children(el)) });
      };
      const out = [];
      // The page tree starts below <body>, which axe-core gives the role "document".
      if (arg && arg.element) build(root, out);
      else for (const c of root.childNodes) build(c, out);
      return mergeTree(out);
    });
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Motion (WCAG 2.2.2, 2.3.3) and orientation (WCAG 1.3.4).
  // ---------------------------------------------------------------------------------------------------------------
  const MOVING_PROPERTIES = /^(transform|translate|rotate|scale|left|right|top|bottom|margin.*|inset.*|background-?position.*|offset.*|width|height)$/i;
  // Loading indicators are commonly accepted as essential motion.
  const LOADING_INDICATOR = '[role=progressbar], [role=status], [aria-busy=true], [class*=spinner], [class*=loading], [class*=loader]';

  // Animations and auto-playing media still running (call it with prefers-reduced-motion: reduce emulated).
  function motionReport(opts) {
    opts = opts || {};
    const exclude = opts.exclude || [];
    const max = opts.max || 20;
    const offenders = [];
    return withAxe((axe) => {
      const seen = new Set();
      const animations = typeof document.getAnimations === 'function' ? document.getAnimations() : [];
      for (const animation of animations) {
        if (offenders.length >= max) break;
        if (animation.playState !== 'running' || !animation.effect) continue;
        const target = animation.effect.target;
        if (!target || seen.has(target) || !target.isConnected || !inScope(target, null, exclude) || !isVisible(target) || target.closest(LOADING_INDICATOR)) continue;
        const timing = animation.effect.getComputedTiming();
        const duration = Number(timing.duration) || 0;
        const iterations = timing.iterations;
        const total = iterations === Infinity ? Infinity : duration * (iterations || 1);
        const keyframes = typeof animation.effect.getKeyframes === 'function' ? animation.effect.getKeyframes() : [];
        const properties = Array.from(new Set(keyframes.flatMap((k) => Object.keys(k)).filter((p) => !/^(offset|easing|composite|computedOffset)$/.test(p))));
        const moves = properties.some((p) => MOVING_PROPERTIES.test(p.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())));
        // Report motion that moves things, and anything that runs longer than five seconds.
        if (!moves && total <= 5000) continue;
        seen.add(target);
        const d = describe(axe, target);
        offenders.push({
          selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal,
          kind: 'animation',
          name2: animation.animationName || animation.id || '',
          properties: properties.join(' '),
          duration: Math.round(duration),
          infinite: iterations === Infinity,
        });
      }
      for (const media of document.querySelectorAll('video, audio')) {
        if (offenders.length >= max) break;
        if (media.paused || media.ended || !inScope(media, null, exclude) || (media.localName === 'video' && !isVisible(media))) continue;
        const length = isFinite(media.duration) ? media.duration * 1000 : Infinity;
        if (!media.loop && length <= 5000) continue;
        const d = describe(axe, media);
        offenders.push({ selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal, kind: 'media', name2: '', properties: '', duration: isFinite(length) ? Math.round(length) : -1, infinite: !!media.loop, controls: media.controls });
      }
      return offenders;
    });
  }

  // What is visible in the current orientation: the amount of text, whether the root is rotated, and visible
  // messages that ask to rotate the device.
  function orientationSnapshot() {
    const transform = (el) => (el ? getComputedStyle(el).transform : 'none');
    const rotated = (t) => {
      if (!t || t === 'none') return false;
      const m = /matrix\(([^)]+)\)/.exec(t);
      if (!m) return /rotate/.test(t);
      const [a, b] = m[1].split(',').map(Number);
      return Math.abs(Math.round(Math.atan2(b, a) * 180 / Math.PI)) % 180 === 90;
    };
    const rotateMessages = Array.from(document.body ? document.body.querySelectorAll('*') : [])
      .filter((el) => el.children.length < 5 && isVisible(el) && /\b(rotate|turn) (your|the) (device|phone|screen|tablet)\b|\b(landscape|portrait) (mode|orientation) (only|is required)\b|\bplease use (landscape|portrait)\b/i.test(el.textContent || ''))
      .map((el) => selectorFor(el));
    return {
      text: norm(document.body ? document.body.innerText : '').length,
      rotated: rotated(transform(document.documentElement)) || rotated(transform(document.body)),
      messages: rotateMessages.slice(0, 5),
    };
  }

  // Measures which text containers clip their content, applies the WCAG text-spacing values, measures again and
  // reports containers that clip only with the new spacing. The style is removed again before returning.
  function textSpacingReport(opts) {
    opts = opts || {};
    const exclude = opts.exclude || [];
    const candidates = Array.from(document.body ? document.body.querySelectorAll('*') : [])
      .filter((el) => el.localName !== 'script' && el.localName !== 'style' && inScope(el, null, exclude) && isVisible(el) && norm(el.textContent));
    const before = new Map(candidates.map((el) => [el, clippingState(el).clipped]));
    const style = document.createElement('style');
    style.setAttribute('data-a11y-text-spacing', '');
    style.textContent = TEXT_SPACING_CSS;
    (document.head || document.documentElement).append(style);
    try {
      return withAxe((axe) => {
        const found = [];
        // Innermost containers first, so that an ancestor clipping the same text is not reported again.
        for (const el of candidates.slice().reverse()) {
          if (found.length >= (opts.max || 20)) break;
          if (before.get(el)) continue;
          const state = clippingState(el);
          if (!state.clipped || found.some((f) => el.contains(f.el))) continue;
          const d = describe(axe, el);
          found.push({ el, selector: d.selector, html: d.html, role: d.role, name: d.name, ordinal: d.ordinal, axis: state.x && state.y ? 'both' : state.x ? 'horizontal' : 'vertical', text: truncate(norm(el.textContent), 120) });
        }
        return found.reverse().map(({ el, ...rest }) => rest);
      });
    } finally {
      style.remove();
    }
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
      // Text nodes changed several times in one callback: each record's value is the old value of the next change of
      // the same node (characterDataOldValue), and the last one is the node's current text. Reading textContent for
      // every record would report the final text several times and lose the intermediate updates.
      const nextOldValue = new Map();
      for (let i = mutations.length - 1; i >= 0; i--) {
        const m = mutations[i];
        if (m.type === 'characterData') {
          m.__value = nextOldValue.has(m.target) ? nextOldValue.get(m.target) : m.target.textContent;
          nextOldValue.set(m.target, m.oldValue);
        }
      }
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
          if (region) entry(region).added.push(norm(m.__value));
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
      state.observer.observe(document, { subtree: true, childList: true, characterData: true, characterDataOldValue: true, attributes: true, attributeFilter: ['aria-live', 'role'] });
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
    reflowReport,
    textSpacingReport,
    motionReport,
    targetSizeReport,
    helpMechanisms,
    accessibilityTree,
    orientationSnapshot,
    recordFocusBaselines,
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
