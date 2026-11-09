// Arrow keys open the menu and move between items (handled in MenuButton.razor); Home and End jump inside the menu.
// Keep these keys from also scrolling the page. Blazor cannot cancel only some keys of an @onkeydown handler.
const arrows = new Set(['ArrowUp', 'ArrowDown']);
const jumps = new Set(['Home', 'End']);

export function preventScrollKeys(root) {
    if (!root || root.__preventScrollKeys) {
        return;
    }

    root.__preventScrollKeys = event => {
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) {
            return;
        }

        const inMenu = event.target instanceof Element && event.target.closest('[role="menu"]') !== null;
        if (arrows.has(event.key) || (inMenu && jumps.has(event.key))) {
            event.preventDefault();
        }
    };
    root.addEventListener('keydown', root.__preventScrollKeys);
}
