// The theme switcher (Components/Layout/ThemeSwitcher.razor) is a group of radio buttons named "theme". This listener
// applies a choice at once and stores it in a cookie, so the server renders every later page in that theme (App.razor)
// and nothing flashes. It works before the Blazor circuit connects and on statically rendered pages such as /error.
document.addEventListener('change', event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement) || input.name !== 'theme' || !['light', 'dark', 'system'].includes(input.value)) {
        return;
    }

    document.documentElement.dataset.theme = input.value;
    document.cookie = `theme=${input.value}; path=/; max-age=31536000; samesite=lax`;
});
