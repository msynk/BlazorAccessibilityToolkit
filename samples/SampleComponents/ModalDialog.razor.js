// Focus management for ModalDialog: focus the first control and keep Tab / Shift+Tab inside the dialog.
const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function activate(dialog) {
    if (!dialog) {
        return;
    }

    const items = () => Array.from(dialog.querySelectorAll(focusable));
    const first = items()[0];
    (first ?? dialog).focus();

    if (dialog.__trap) {
        return;
    }

    dialog.__trap = event => {
        if (event.key !== 'Tab') {
            return;
        }

        const list = items();
        if (list.length === 0) {
            event.preventDefault();
            return;
        }

        const index = list.indexOf(document.activeElement);
        if (event.shiftKey && index <= 0) {
            event.preventDefault();
            list[list.length - 1].focus();
        } else if (!event.shiftKey && index === list.length - 1) {
            event.preventDefault();
            list[0].focus();
        }
    };
    dialog.addEventListener('keydown', dialog.__trap);
}
