// Development overlay: runs axe-core in the running app after each page change and shows the violations in a small
// panel and in the browser console. Loaded by <AccessibilityDevOverlay /> (or a script tag) from the endpoints that
// MapAccessibilityDevTools() maps. Configuration comes from the data-ba11y-* attributes of the script element.
//
// The overlay stays off under browser automation (navigator.webdriver), so that it never runs axe-core at the same
// time as a test's own audit. localStorage "blazor-a11y-devtools" = "off" turns it off, "on" turns it on even under
// automation.

const storageKey = 'blazor-a11y-devtools';
const hostAttribute = 'data-ba11y-devtools-host';
const defaultTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'];
const impactOrder = { critical: 0, serious: 1, moderate: 2, minor: 3 };

const api = {
    status: 'starting',
    results: null,
    run: () => Promise.resolve(null),
    open: () => { },
    close: () => { },
};

function storedMode() {
    try {
        return localStorage.getItem(storageKey);
    } catch {
        return null;
    }
}

function readConfig() {
    const script = document.querySelector('script[data-ba11y-devtools]');
    const data = script ? script.dataset : {};
    const list = value => (value || '').split(',').map(v => v.trim()).filter(Boolean);
    const delay = Number.parseInt(data.ba11yDelay || '', 10);
    // Selectors can contain commas, so the exclusions are a JSON array (a single selector is accepted as well).
    let exclude = [];
    if (data.ba11yExclude) {
        try {
            const parsed = JSON.parse(data.ba11yExclude);
            exclude = Array.isArray(parsed) ? parsed.filter(s => typeof s === 'string' && s.trim()) : [];
        } catch {
            exclude = [data.ba11yExclude];
        }
    }

    return {
        axeSrc: data.ba11yAxeSrc || new URL('axe.js', import.meta.url).href,
        tags: data.ba11yTags ? list(data.ba11yTags) : defaultTags,
        exclude,
        position: ['bottom-end', 'bottom-start', 'top-end', 'top-start'].includes(data.ba11yPosition) ? data.ba11yPosition : 'bottom-end',
        delay: Number.isFinite(delay) && delay >= 0 ? delay : 1000,
        console: data.ba11yConsole !== 'false',
    };
}

function loadAxe(src) {
    if (window.axe && typeof window.axe.run === 'function') {
        return Promise.resolve(window.axe);
    }

    return new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = src;
        script.setAttribute(hostAttribute, '');
        script.onload = () => window.axe ? resolve(window.axe) : reject(new Error('the script did not define axe'));
        script.onerror = () => reject(new Error('the request failed'));
        document.head.appendChild(script);
    });
}

const styles = `
:host { all: initial; }
* { box-sizing: border-box; }
.root {
    --bg: #ffffff; --fg: #1b1f24; --muted: #4a525c; --border: #6b7480; --accent: #0b57d0;
    --serious: #b3261e; --ok: #146c2e; --focus: #0b57d0; --row: #f3f5f7;
    position: fixed; z-index: 2147483646; max-width: calc(100vw - 16px);
    font: 14px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif; color: var(--fg);
    display: flex; flex-direction: column; gap: 8px;
}
@media (prefers-color-scheme: dark) {
    .root { --bg: #1d2127; --fg: #eef1f4; --muted: #b9c0c8; --border: #8a939e; --accent: #8ab4f8;
            --serious: #ffb4ab; --ok: #7dd99a; --focus: #8ab4f8; --row: #272c33; }
}
.bottom-end { bottom: 8px; inset-inline-end: 8px; align-items: flex-end; flex-direction: column-reverse; }
.bottom-start { bottom: 8px; inset-inline-start: 8px; align-items: flex-start; flex-direction: column-reverse; }
.top-end { top: 8px; inset-inline-end: 8px; align-items: flex-end; }
.top-start { top: 8px; inset-inline-start: 8px; align-items: flex-start; }
button, a { font: inherit; color: inherit; }
button { cursor: pointer; min-height: 32px; min-width: 32px; padding: 4px 10px; border: 1px solid var(--border);
         border-radius: 6px; background: var(--bg); }
button:hover { background: var(--row); }
a { color: var(--accent); }
:focus-visible { outline: 3px solid var(--focus); outline-offset: 2px; }
.badge { font-weight: 600; border-width: 2px; box-shadow: 0 2px 6px rgb(0 0 0 / 0.25); }
.badge[data-state="violations"] { border-color: var(--serious); color: var(--serious); }
.badge[data-state="clean"] { border-color: var(--ok); color: var(--ok); }
.panel { width: min(440px, calc(100vw - 16px)); max-height: min(70vh, 560px); overflow: auto; background: var(--bg);
         border: 1px solid var(--border); border-radius: 8px; box-shadow: 0 4px 16px rgb(0 0 0 / 0.3); padding: 12px; }
.panel[hidden] { display: none; }
.head { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
h2 { font-size: 16px; margin: 0; }
h3 { font-size: 14px; margin: 0 0 4px; }
.status { color: var(--muted); margin: 8px 0; }
.actions { display: flex; gap: 8px; margin-bottom: 8px; }
ul { list-style: none; margin: 0; padding: 0; }
.rule { border-top: 1px solid var(--border); padding: 8px 0; }
.impact { text-transform: capitalize; }
.impact-critical, .impact-serious { color: var(--serious); }
.meta { color: var(--muted); margin: 0 0 4px; }
.node { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 4px 0; }
code { font: 12px/1.4 ui-monospace, Consolas, monospace; overflow-wrap: anywhere; }
.highlight { position: fixed; z-index: 2147483645; pointer-events: none; outline: 3px dashed #d93025;
             outline-offset: 2px; box-shadow: 0 0 0 9999px rgb(0 0 0 / 0.15); }
.highlight[hidden] { display: none; }
.visually-hidden { position: absolute; width: 1px; height: 1px; margin: -1px; padding: 0; overflow: hidden;
                   clip-path: inset(50%); white-space: nowrap; border: 0; }
@media (forced-colors: active) {
    .badge, .panel, button { border-color: CanvasText; }
    .highlight { outline-color: Highlight; box-shadow: none; }
}
`;

function element(tag, attributes = {}, text) {
    const node = document.createElement(tag);
    for (const [name, value] of Object.entries(attributes)) {
        node.setAttribute(name, value);
    }

    if (text !== undefined) {
        node.textContent = text;
    }

    return node;
}

function wcagCriteria(tags) {
    return tags
        .map(tag => /^wcag(\d)(\d)(\d+)$/.exec(tag))
        .filter(Boolean)
        .map(match => `${match[1]}.${match[2]}.${match[3]}`);
}

function targetText(target) {
    return target.map(part => Array.isArray(part) ? part.join(' >>> ') : part).join(' >> ');
}

function resolveTarget(target) {
    // axe-core targets: a selector per frame or shadow root. Only same-document targets can be highlighted.
    let root = document;
    let found = null;
    for (let i = 0; i < target.length; i++) {
        const selectors = Array.isArray(target[i]) ? target[i] : [target[i]];
        found = null;
        for (const selector of selectors) {
            found = (found && found.shadowRoot ? found.shadowRoot : root).querySelector(selector);
            if (!found) {
                return null;
            }
        }

        if (i < target.length - 1) {
            // The next part is inside this frame; a cross-origin frame has no readable document.
            try {
                root = found.contentDocument;
            } catch {
                return null;
            }

            if (!root) {
                return null;
            }
        }
    }

    return found;
}

function start() {
    const config = readConfig();
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    const host = element('div', { [hostAttribute]: '' });
    const shadow = host.attachShadow({ mode: 'open' });
    const style = document.createElement('style');
    style.textContent = styles;

    const root = element('aside', { class: `root ${config.position}`, 'aria-label': 'Accessibility dev tools' });
    const badge = element('button', { type: 'button', class: 'badge', 'aria-expanded': 'false', 'aria-controls': 'ba11y-panel', 'data-state': 'running' }, 'Checking accessibility…');
    const panel = element('section', { id: 'ba11y-panel', class: 'panel', 'aria-labelledby': 'ba11y-title' });
    panel.hidden = true;
    const head = element('div', { class: 'head' });
    const title = element('h2', { id: 'ba11y-title' }, 'Accessibility issues');
    const close = element('button', { type: 'button' }, 'Close');
    head.append(title, close);
    const status = element('p', { class: 'status' });
    const actions = element('div', { class: 'actions' });
    const rerun = element('button', { type: 'button' }, 'Check again');
    actions.append(rerun);
    const list = element('ul', { class: 'rules', 'aria-label': 'Violations by rule' });
    panel.append(head, status, actions, list);
    const highlight = element('div', { class: 'highlight', 'aria-hidden': 'true' });
    highlight.hidden = true;
    // Announces a changed count to screen reader users who are elsewhere on the page; unchanged results stay quiet.
    const announcer = element('span', { class: 'visually-hidden', role: 'status' });
    root.append(badge, panel, announcer);
    shadow.append(style, root, highlight);
    document.body.appendChild(host);

    let axe = null;
    let axeLoading = null;
    let running = false;
    let pending = false;
    let timer = 0;
    let highlighted = null;
    let frame = 0;
    let announcedCount = null;
    let logged = new Set();
    let lastSignature = null;

    function setOpen(open, focusBadge) {
        panel.hidden = !open;
        badge.setAttribute('aria-expanded', String(open));
        if (!open) {
            hideHighlight();
            if (focusBadge) {
                badge.focus();
            }
        }
    }

    function positionHighlight() {
        frame = 0;
        if (!highlighted) {
            return;
        }

        if (!highlighted.isConnected) {
            hideHighlight();
            return;
        }

        const rect = highlighted.getBoundingClientRect();
        Object.assign(highlight.style, { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px` });
        frame = requestAnimationFrame(positionHighlight);
    }

    function show(target) {
        const node = resolveTarget(target);
        if (!node) {
            status.textContent = `The element ${targetText(target)} is no longer on the page, or is in a frame from another origin.`;
            return;
        }

        node.scrollIntoView({ block: 'center', inline: 'nearest', behavior: reducedMotion ? 'auto' : 'smooth' });
        highlighted = node;
        highlight.hidden = false;
        if (!frame) {
            frame = requestAnimationFrame(positionHighlight);
        }
        console.log('[a11y] element:', node);
    }

    function hideHighlight() {
        cancelAnimationFrame(frame);
        frame = 0;
        highlighted = null;
        highlight.hidden = true;
    }

    function render(results) {
        const violations = [...results.violations].sort((a, b) =>
            (impactOrder[a.impact] ?? 4) - (impactOrder[b.impact] ?? 4) || a.id.localeCompare(b.id));
        const count = violations.reduce((sum, v) => sum + v.nodes.length, 0);
        badge.dataset.state = count > 0 ? 'violations' : 'clean';
        badge.textContent = count === 0 ? 'No a11y issues' : `${count} a11y ${count === 1 ? 'issue' : 'issues'}`;
        if (count !== announcedCount) {
            announcer.textContent = announcedCount === null && count === 0 ? '' : `Accessibility check: ${badge.textContent}`;
            announcedCount = count;
        }

        const time = new Date().toLocaleTimeString();
        status.textContent = count === 0
            ? `No violations found at ${time} (axe-core ${axe.version}). Automated checks find only some problems.`
            : `${count} ${count === 1 ? 'element' : 'elements'} in ${violations.length} ${violations.length === 1 ? 'rule' : 'rules'} at ${time} (axe-core ${axe.version}).`;

        // Rebuild the list only when the violations changed, and keep keyboard focus on the same button when they did.
        const signature = violations.map(v => `${v.id}:${v.nodes.map(n => targetText(n.target)).join(',')}`).join('|');
        if (config.console) {
            log(violations);
        }

        if (signature === lastSignature) {
            return;
        }

        lastSignature = signature;
        const focused = shadow.activeElement;
        const focusedKey = focused && list.contains(focused) ? focused.dataset.key ?? '' : null;
        list.replaceChildren(...violations.map(violation => {
            const item = element('li', { class: 'rule' });
            const heading = element('h3');
            heading.append(element('span', { class: `impact impact-${violation.impact}` }, violation.impact || 'unknown'), `: ${violation.help}`);
            const criteria = wcagCriteria(violation.tags);
            const meta = element('p', { class: 'meta' }, `${violation.id}${criteria.length ? ` · WCAG ${criteria.join(', ')}` : ''} · `);
            const link = element('a', { href: violation.helpUrl, target: '_blank', rel: 'noopener noreferrer' }, 'Rule help');
            link.setAttribute('aria-label', `Rule help for ${violation.id} (opens in a new tab)`);
            meta.append(link);
            const nodes = element('ul', { 'aria-label': `Elements failing ${violation.id}` });
            nodes.append(...violation.nodes.map((node, index) => {
                const row = element('li', { class: 'node' });
                const target = targetText(node.target);
                row.append(element('code', {}, target));
                const button = element('button', { type: 'button', 'aria-label': `Show element ${index + 1} failing ${violation.id}`, 'data-key': `${violation.id}|${target}` }, 'Show');
                button.addEventListener('click', () => show(node.target));
                row.append(button);
                return row;
            }));
            item.append(heading, meta, nodes);
            return item;
        }));

        if (focusedKey !== null) {
            // The focused element was replaced: focus the same Show button, or "Check again" when it is gone.
            const same = focusedKey && [...list.querySelectorAll('button[data-key]')].find(b => b.dataset.key === focusedKey);
            (same || rerun).focus();
        }
    }

    function log(violations) {
        const current = new Set();
        for (const violation of violations) {
            const fresh = violation.nodes.filter(node => {
                const key = `${violation.id}|${targetText(node.target)}`;
                current.add(key);
                return !logged.has(key);
            });
            if (fresh.length === 0) {
                continue;
            }

            console.groupCollapsed(`%c[a11y] ${violation.impact}%c ${violation.id}: ${violation.help} (${fresh.length})`,
                'color: #b3261e; font-weight: bold', 'font-weight: normal');
            console.log(violation.helpUrl);
            for (const node of fresh) {
                console.log(resolveTarget(node.target) ?? targetText(node.target), node.failureSummary ?? '');
            }

            console.groupEnd();
        }

        const fixed = [...logged].filter(key => !current.has(key)).length;
        if (fixed > 0) {
            console.info(`[a11y] ${fixed} earlier ${fixed === 1 ? 'violation is' : 'violations are'} gone.`);
        }

        logged = current;
    }

    async function run() {
        if (running) {
            pending = true;
            return api.results;
        }

        if (axe && axe._running) {
            // Another axe-core run with the same instance (a browser extension, a test) is in progress: try again later.
            pending = true;
            schedule();
            return api.results;
        }

        running = true;
        pending = false;
        badge.dataset.state = 'running';
        host.setAttribute('data-state', 'running');
        api.status = 'running';
        try {
            // Loaded once: after a failed load, page changes do not request the script again ("Check again" does).
            axeLoading = axeLoading ?? loadAxe(config.axeSrc);
            axe = await axeLoading;
            const context = { exclude: [[`[${hostAttribute}]`], ...config.exclude.map(selector => [selector])] };
            const results = await axe.run(context, { runOnly: { type: 'tag', values: config.tags }, resultTypes: ['violations'] });
            api.results = results;
            render(results);
            api.status = 'idle';
        } catch (error) {
            const message = error && error.message ? error.message : String(error);
            if (/already running/i.test(message)) {
                // Another axe-core run (a browser extension, a test) is in progress: try again later.
                pending = true;
            } else {
                badge.dataset.state = 'error';
                badge.textContent = 'a11y check failed';
                status.textContent = `axe-core could not run: ${message}. Is app.MapAccessibilityDevTools() mapped, or does data-ba11y-axe-src point to axe-core?`;
                console.warn('[a11y] axe-core could not run:', error);
                api.status = 'error';
            }
        } finally {
            running = false;
            host.setAttribute('data-state', api.status);
            if (pending) {
                schedule();
            }
        }

        return api.results;
    }

    function schedule() {
        // Debounced: a burst of changes (a render, a navigation) leads to one check after the page settles.
        clearTimeout(timer);
        timer = setTimeout(() => {
            if (document.hidden) {
                pending = true;
                return;
            }

            run();
        }, config.delay);
        if (!running) {
            api.status = 'scheduled';
            host.setAttribute('data-state', 'scheduled');
        }
    }

    // Changes to the overlay itself (its host element and the axe-core script it adds) do not start a new check.
    const own = node => node instanceof Element && node.hasAttribute(hostAttribute);
    const relevant = record => !own(record.target)
        && !(record.type === 'childList' && [...record.addedNodes, ...record.removedNodes].every(own));
    const observer = new MutationObserver(records => {
        if (records.some(relevant)) {
            schedule();
        }
    });
    observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true, characterData: true });
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden && pending && !running) {
            schedule();
        }
    });

    badge.addEventListener('click', () => setOpen(panel.hidden, false));
    close.addEventListener('click', () => setOpen(false, true));
    rerun.addEventListener('click', () => {
        if (!axe) {
            axeLoading = null;
        }

        run();
    });
    // Escape closes the panel when focus is in the overlay, or on the page body (Safari does not focus buttons on click).
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !panel.hidden && (event.composedPath().includes(host) || document.activeElement === document.body)) {
            event.stopPropagation();
            setOpen(false, true);
        }
    });

    api.run = run;
    api.open = () => setOpen(true, false);
    api.close = () => setOpen(false, false);
    run();
}

if (!window.blazorA11yDevTools) {
    window.blazorA11yDevTools = api;
    const mode = storedMode();
    if (mode === 'off') {
        api.status = 'off';
    } else if (navigator.webdriver && mode !== 'on') {
        api.status = 'off: automation';
    } else if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    }
}
