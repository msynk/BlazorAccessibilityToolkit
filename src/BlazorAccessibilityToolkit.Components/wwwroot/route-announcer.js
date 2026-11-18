// Announces enhanced navigation between statically rendered pages, where RouteAnnouncer cannot run .NET code: after
// each "enhancedload" event, the [data-ba11y-route-announcer] region receives "Navigated to <page>". An interactive
// RouteAnnouncer marks its region with data-ba11y-interactive and announces from .NET instead.
import { currentPageName } from './accessibility.js';

let registered = false;

async function announce() {
    const region = document.querySelector('[data-ba11y-route-announcer]');
    if (!region || region.dataset.ba11yInteractive === 'true') {
        return;
    }

    const name = await currentPageName();
    const text = (region.dataset.ba11yFormat || 'Navigated to {0}').replace('{0}', () => name);
    if (region.textContent === text) {
        region.textContent = '';
        await new Promise(resolve => setTimeout(resolve, 150));
    }

    region.textContent = text;
}

function register() {
    if (registered) {
        return true;
    }

    if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
        window.Blazor.addEventListener('enhancedload', announce);
        registered = true;
    }

    return registered;
}

// blazor.web.js usually runs before this module; poll briefly in case it loads later.
if (!register()) {
    let attempts = 0;
    const timer = setInterval(() => {
        if (register() || ++attempts > 50) {
            clearInterval(timer);
        }
    }, 100);
}
