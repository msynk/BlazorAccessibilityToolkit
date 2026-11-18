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
    // requestAnimationFrame does not run in background tabs, so a timer bounds the wait.
    await new Promise(resolve => {
        const timer = setTimeout(resolve, 100);
        requestAnimationFrame(() => setTimeout(() => { clearTimeout(timer); resolve(); }, 0));
    });
    const title = (document.title || '').trim();
    if (title) {
        return title;
    }

    const heading = document.querySelector('h1');
    const text = heading ? (heading.textContent || '').replace(/\s+/g, ' ').trim() : '';
    return text || location.pathname;
}

// Roving tabindex for composite widgets (toolbar, tablist, radiogroup, listbox, menu, tree): one tab stop, arrow keys
// move focus between items, Home and End go to the ends. Returns a handle whose dispose() removes the behavior.
export function rovingFocus(container, options) {
    options = Object.assign({ itemSelector: '', orientation: 'horizontal', wrap: true, selectionFollowsFocus: false, typeahead: false }, options || {});
    const typeahead = createTypeahead();
    const items = () => Array.from(container.querySelectorAll(options.itemSelector))
        .filter(el => !el.closest('[hidden], [inert]') && el.getAttribute('aria-disabled') !== 'true' && !el.disabled && el.getClientRects().length > 0);
    const isCurrent = el => ['aria-selected', 'aria-checked', 'aria-pressed'].some(a => el.getAttribute(a) === 'true') || (el.getAttribute('aria-current') || 'false') !== 'false';
    let active = null;

    const apply = () => {
        const list = items();
        if (list.length === 0) {
            return;
        }

        if (!active || !list.includes(active)) {
            active = list.find(isCurrent) || list[0];
        }

        for (const el of list) {
            el.tabIndex = el === active ? 0 : -1;
        }
    };

    const move = (target) => {
        active = target;
        apply();
        target.focus();
        if (options.selectionFollowsFocus && !isCurrent(target)) {
            target.click();
        }
    };

    const keys = {
        horizontal: { next: ['ArrowRight'], previous: ['ArrowLeft'] },
        vertical: { next: ['ArrowDown'], previous: ['ArrowUp'] },
        both: { next: ['ArrowRight', 'ArrowDown'], previous: ['ArrowLeft', 'ArrowUp'] },
    }[options.orientation] || { next: ['ArrowRight'], previous: ['ArrowLeft'] };

    const onKeyDown = event => {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) {
            return;
        }

        const list = items();
        const index = list.indexOf(document.activeElement);
        if (index < 0) {
            return;
        }

        let target = null;
        if (keys.next.includes(event.key)) {
            target = index + 1 < list.length ? list[index + 1] : (options.wrap ? list[0] : null);
        } else if (keys.previous.includes(event.key)) {
            target = index > 0 ? list[index - 1] : (options.wrap ? list[list.length - 1] : null);
        } else if (event.key === 'Home') {
            target = list[0];
        } else if (event.key === 'End') {
            target = list[list.length - 1];
        } else if (options.typeahead && (target = typeahead(event, list, index)) !== undefined) {
            if (!target) {
                return;
            }
        } else {
            return;
        }

        // The key belongs to the widget: do not scroll the page.
        event.preventDefault();
        if (target && target !== document.activeElement) {
            move(target);
        }
    };

    const onFocusIn = event => {
        const list = items();
        const item = list.find(el => el === event.target || el.contains(event.target));
        if (item && item !== active) {
            active = item;
            apply();
        }
    };

    // Blazor re-renders can add, remove or replace items: keep exactly one tab stop.
    const observer = new MutationObserver(() => apply());
    observer.observe(container, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-selected', 'aria-checked', 'aria-pressed', 'aria-current', 'aria-disabled', 'disabled', 'hidden'] });
    container.addEventListener('keydown', onKeyDown);
    container.addEventListener('focusin', onFocusIn);
    apply();

    return {
        dispose() {
            observer.disconnect();
            container.removeEventListener('keydown', onKeyDown);
            container.removeEventListener('focusin', onFocusIn);
        },
    };
}

// Moves focus to the first element that matches the selector (making it focusable for scripts if needed). With
// onlyIfIdle, focus is left alone when the user already put it somewhere (for example while the page was prerendered).
export function focusSelector(selector, onlyIfIdle) {
    const active = document.activeElement;
    if (onlyIfIdle && active && active !== document.body && active !== document.documentElement) {
        return false;
    }

    const target = document.querySelector(selector);
    if (!target) {
        return false;
    }

    if (target.tabIndex < 0 && !target.hasAttribute('tabindex')) {
        target.setAttribute('tabindex', '-1');
    }

    target.focus();
    return document.activeElement === target;
}

// After Blazor replaced the prerendered DOM: focus the new element that matches the one the user focused before.
export function restorePrerenderFocus() {
    const saved = window.__ba11yPrerenderFocus;
    const active = document.activeElement;
    if (!saved || (saved.element && saved.element.isConnected) || (active && active !== document.body && active !== document.documentElement)) {
        return false;
    }

    const matches = el => el && el.localName === saved.tag
        && (el.textContent || '').replace(/\s+/g, ' ').trim() === saved.text
        && el.getAttribute('href') === saved.href && el.getAttribute('name') === saved.name;
    let candidate = saved.id ? document.getElementById(saved.id) : null;
    if (!matches(candidate)) {
        candidate = document.body;
        for (const index of saved.path) {
            candidate = candidate && candidate.children[index];
        }
    }

    if (!matches(candidate)) {
        candidate = Array.from(document.getElementsByTagName(saved.tag)).find(matches) || null;
    }

    window.__ba11yPrerenderFocus = null;
    if (!candidate) {
        return false;
    }

    candidate.focus();
    return document.activeElement === candidate;
}

// Type-to-select: printable characters typed within half a second form a prefix; returns the next item (after the
// current one) whose text starts with it, null when none matches, or undefined when the key is not a character.
function createTypeahead() {
    let buffer = '';
    let last = 0;
    return (event, list, index) => {
        if (event.key.length !== 1 || event.key === ' ' && buffer === '') {
            return undefined;
        }

        const now = Date.now();
        buffer = (now - last > 500 ? '' : buffer) + event.key.toLowerCase();
        last = now;
        const text = el => (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
        const ordered = list.slice(index + 1).concat(list.slice(0, index + 1));
        // A repeated single letter cycles through the items that start with it.
        const prefix = buffer.split('').every(c => c === buffer[0]) ? buffer[0] : buffer;
        return ordered.find(el => text(el).startsWith(prefix)) || null;
    };
}

let generatedIds = 0;

// aria-activedescendant for listboxes, grids and comboboxes where DOM focus stays on one element (the owner): arrow
// keys, Home and End move the active item, Enter activates (clicks) it. Returns a handle whose dispose() removes it.
export function activeDescendant(container, owner, options) {
    options = Object.assign({ itemSelector: '[role=option]', orientation: 'vertical', wrap: false, selectionFollowsFocus: false, typeahead: true }, options || {});
    owner = owner || container;
    const typeahead = createTypeahead();
    const items = () => Array.from(container.querySelectorAll(options.itemSelector))
        .filter(el => !el.closest('[hidden], [inert]') && el.getAttribute('aria-disabled') !== 'true' && el.getClientRects().length > 0);
    let active = null;

    const setActive = (target) => {
        if (active && active !== target) {
            active.removeAttribute('data-active');
            if (options.selectionFollowsFocus) {
                active.setAttribute('aria-selected', 'false');
            }
        }

        active = target;
        if (!target) {
            owner.removeAttribute('aria-activedescendant');
            return;
        }

        if (!target.id) {
            target.id = 'ba11y-option-' + (++generatedIds);
        }

        target.setAttribute('data-active', '');
        if (options.selectionFollowsFocus) {
            target.setAttribute('aria-selected', 'true');
        }

        owner.setAttribute('aria-activedescendant', target.id);
        target.scrollIntoView({ block: 'nearest' });
    };

    const keys = options.orientation === 'horizontal'
        ? { next: ['ArrowRight'], previous: ['ArrowLeft'] }
        : options.orientation === 'both' ? { next: ['ArrowRight', 'ArrowDown'], previous: ['ArrowLeft', 'ArrowUp'] } : { next: ['ArrowDown'], previous: ['ArrowUp'] };

    const onKeyDown = event => {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) {
            return;
        }

        const list = items();
        if (list.length === 0) {
            // A collapsed popup: the key belongs to the app (for example ArrowDown opens it).
            return;
        }

        const index = list.indexOf(active);
        let target;
        if (keys.next.includes(event.key)) {
            target = index < 0 ? list[0] : index + 1 < list.length ? list[index + 1] : (options.wrap ? list[0] : list[index]);
        } else if (keys.previous.includes(event.key)) {
            target = index < 0 ? list[list.length - 1] : index > 0 ? list[index - 1] : (options.wrap ? list[list.length - 1] : list[index]);
        } else if (event.key === 'Home' && owner === container) {
            target = list[0];
        } else if (event.key === 'End' && owner === container) {
            target = list[list.length - 1];
        } else if (event.key === 'Enter' && active && list.includes(active)) {
            event.preventDefault();
            active.click();
            return;
        } else if (owner === container && options.typeahead && (target = typeahead(event, list, index)) !== undefined) {
            if (!target) {
                return;
            }
        } else {
            return;
        }

        event.preventDefault();
        setActive(target);
    };

    // Items that disappear (a closed popup, new search results) clear the active descendant.
    const observer = new MutationObserver(() => {
        if (active && !items().includes(active)) {
            setActive(null);
        }
    });
    observer.observe(container, { childList: true, subtree: true, attributes: true, attributeFilter: ['hidden', 'aria-disabled', 'class', 'style'] });
    owner.addEventListener('keydown', onKeyDown);

    return {
        dispose() {
            observer.disconnect();
            owner.removeEventListener('keydown', onKeyDown);
            // Do not leave the owner pointing at an option that is about to be removed or re-created.
            setActive(null);
        },
    };
}

// ActiveDescendantGroup: the owner is looked up by id, so it can live outside the group (a combobox input).
export function activeDescendantFor(container, ownerId, options) {
    const owner = ownerId ? document.getElementById(ownerId) : container;
    if (!owner) {
        throw new Error('ActiveDescendantGroup: no element has the id "' + ownerId + '".');
    }

    return activeDescendant(container, owner, options);
}
