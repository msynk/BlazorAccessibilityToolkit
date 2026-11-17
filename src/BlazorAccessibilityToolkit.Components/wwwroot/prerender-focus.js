// Remembers the element the user focuses while a page is prerendered. When the page becomes interactive, Blazor
// replaces the prerendered DOM and focus falls back to the document body; NavigationFocus then restores focus to the
// equivalent element of the new DOM. A classic script, so that it runs as soon as the prerendered markup is parsed.
(function () {
    if (window.__ba11yPrerenderFocus !== undefined) {
        return;
    }

    window.__ba11yPrerenderFocus = null;

    function describe(el) {
        const path = [];
        for (let node = el; node && node !== document.body && node.parentElement; node = node.parentElement) {
            path.unshift(Array.prototype.indexOf.call(node.parentElement.children, node));
        }

        return {
            element: el,
            id: el.id || null,
            tag: el.localName,
            text: (el.textContent || '').replace(/\s+/g, ' ').trim(),
            href: el.getAttribute('href'),
            name: el.getAttribute('name'),
            path: path,
        };
    }

    document.addEventListener('focusin', function (event) {
        if (event.target && event.target !== document.body && event.target.localName) {
            window.__ba11yPrerenderFocus = describe(event.target);
        }
    }, true);
})();
