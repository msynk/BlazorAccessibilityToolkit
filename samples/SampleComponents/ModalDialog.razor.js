// Focus management for ModalDialog: move focus into the dialog and keep it there while the dialog is open.
// Tab and Shift+Tab wrap inside the dialog, focus that left it (for example after a click on the backdrop) is brought
// back on the next Tab, and pointer presses outside the dialog do not take focus away from it.
const candidates = [
    'a[href]',
    'area[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    'iframe',
    'summary',
    '[contenteditable=""]',
    '[contenteditable="true"]',
    '[tabindex]',
].join(',');

let active = null;

function tabbables(container) {
    return Array.from(container.querySelectorAll(candidates))
        .filter(element => element.tabIndex >= 0 && !element.closest('[inert], [hidden]') && element.getClientRects().length > 0);
}

export function activate(dialog) {
    if (!dialog) {
        return;
    }

    // Modal dialogs do not stack: opening one releases the previous trap.
    deactivate();

    const onKeyDown = event => {
        if (!dialog.isConnected) {
            deactivate();
            return;
        }

        if (event.key !== 'Tab') {
            return;
        }

        const items = tabbables(dialog);
        if (items.length === 0) {
            event.preventDefault();
            dialog.focus();
            return;
        }

        const first = items[0];
        const last = items[items.length - 1];
        const current = document.activeElement;
        if (!dialog.contains(current)) {
            event.preventDefault();
            (event.shiftKey ? last : first).focus();
        } else if (event.shiftKey && (current === first || current === dialog)) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && current === last) {
            event.preventDefault();
            first.focus();
        }
    };

    const onPointerDown = event => {
        if (dialog.isConnected && !dialog.contains(event.target)) {
            event.preventDefault();
        }
    };

    document.addEventListener('keydown', onKeyDown, true);
    document.addEventListener('mousedown', onPointerDown, true);
    active = { onKeyDown, onPointerDown };

    (tabbables(dialog)[0] ?? dialog).focus();
}

export function deactivate() {
    if (!active) {
        return;
    }

    document.removeEventListener('keydown', active.onKeyDown, true);
    document.removeEventListener('mousedown', active.onPointerDown, true);
    active = null;
}
