// Arrow keys, Home and End move between tabs (handled in Tabs.razor). Keep them from also scrolling the page.
// Blazor cannot cancel only some keys of an @onkeydown handler, so the default action is prevented here.
const navigationKeys = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End']);

export function preventScrollKeys(tablist) {
    if (!tablist || tablist.__preventScrollKeys) {
        return;
    }

    tablist.__preventScrollKeys = event => {
        if (navigationKeys.has(event.key) && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
            event.preventDefault();
        }
    };
    tablist.addEventListener('keydown', tablist.__preventScrollKeys);
}
