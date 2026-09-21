// Clicking the menu entry of the page you are ALREADY on does nothing in Blazor —
// the URL doesn't change, so no navigation fires and the screen looks frozen.
// People click the menu precisely to get a fresh page, so give them one.
document.addEventListener('click', e => {
    const link = e.target.closest('a.mud-nav-link');
    if (!link) return;
    const href = link.getAttribute('href');
    if (!href) return;
    try {
        const target = new URL(link.href, location.origin);
        if (target.pathname === location.pathname && target.search === location.search) {
            e.preventDefault();
            location.reload();
        }
    } catch { /* malformed href — let it be */ }
});
