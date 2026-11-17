// Focus helpers for BlazorAccessibilityToolkit.Components: skip-link targets, focus traps and page titles.
const tabbableSelector = [
    'a[href]', 'area[href]', 'button:not([disabled])', 'input:not([disabled]):not([type="hidden"])', 'select:not([disabled])',
    'textarea:not([disabled])', 'iframe', 'summary', 'audio[controls]', 'video[controls]', '[contenteditable=""]', '[contenteditable="true"]', '[tabindex]',
].join(',');

function tabbables(container) {
    return Array.from(container.querySelectorAll(tabbableSelector))
        .filter(el => el.tabIndex >= 0 && !el.closest('[inert], [hidden]') && el.getClientRects().length > 0);
}

// Moves focus to the element with the id (making it focusable for scripts if needed) and scrolls it into view.
// Returns false when there is no such element.
export function focusById(id) {
    const target = document.getElementById(id);
    if (!target) {
        return false;
    }

    if (target.tabIndex < 0 && !target.hasAttribute('tabindex')) {
        // Focusable by script only: the target does not become a tab stop.
        target.setAttribute('tabindex', '-1');
    }

    target.focus({ preventScroll: true });
    target.scrollIntoView({ block: 'start' });
    return document.activeElement === target;
}

// Active traps, innermost last. Only the innermost trap handles keys and focus.
const traps = [];

function onKeyDown(event) {
    const trap = traps[traps.length - 1];
    if (!trap || event.key !== 'Tab' || event.defaultPrevented) {
        return;
    }

    if (!trap.container.isConnected) {
        trap.release(false);
        return;
    }

    const items = tabbables(trap.container);
    if (items.length === 0) {
        event.preventDefault();
        trap.container.focus();
        return;
    }

    const first = items[0];
    const last = items[items.length - 1];
    const current = document.activeElement;
    if (!trap.container.contains(current)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && (current === first || current === trap.container)) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && current === last) {
        event.preventDefault();
        first.focus();
    }
}

// Focus that moves out of the trap (a click elsewhere, a script) is brought back to the first element.
function onFocusIn(event) {
    const trap = traps[traps.length - 1];
    if (!trap || !trap.container.isConnected || trap.container.contains(event.target)) {
        return;
    }

    const items = tabbables(trap.container);
    (items[0] || trap.container).focus();
}

// Keeps Tab and Shift+Tab inside the container and returns a handle whose release(restoreFocus) ends the trap.
export function trapFocus(container, options) {
    options = options || {};
    const previous = document.activeElement;
    if (container.tabIndex < 0 && !container.hasAttribute('tabindex')) {
        container.setAttribute('tabindex', '-1');
    }

    const trap = {
        container,
        release(restoreFocus) {
            const index = traps.indexOf(trap);
            if (index < 0) {
                return;
            }

            traps.splice(index, 1);
            if (traps.length === 0) {
                document.removeEventListener('keydown', onKeyDown, true);
                document.removeEventListener('focusin', onFocusIn, true);
            }

            if (restoreFocus && previous && previous.isConnected && typeof previous.focus === 'function') {
                previous.focus();
            }
        },
    };

    traps.push(trap);
    if (traps.length === 1) {
        document.addEventListener('keydown', onKeyDown, true);
        document.addEventListener('focusin', onFocusIn, true);
    }

    if (!container.contains(document.activeElement)) {
        const initial = options.initialFocus ? container.querySelector(options.initialFocus) : null;
        (initial || tabbables(container)[0] || container).focus();
    }

    return trap;
}

// The page title after Blazor's HeadOutlet updated it, then the first heading, then the path.
export async function currentPageName() {
    await new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)));
    const title = (document.title || '').trim();
    if (title) {
        return title;
    }

    const heading = document.querySelector('h1');
    const text = heading ? (heading.textContent || '').replace(/\s+/g, ' ').trim() : '';
    return text || location.pathname;
}
