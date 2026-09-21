// Helper for uncontrolled inputs (the smart-search box): Blazor never writes
// their value attribute (that would break mobile IME composition), so chip
// clicks / clear buttons set the DOM value through here instead.
// The greeting must follow the USER'S clock, not the server's.
window.mfLocalHour = function () { return new Date().getHours(); };

window.mfInput = {
    set: function (el, value) {
        if (!el) return;
        el.value = value || '';
        try { el.focus(); } catch (e) { /* focus is a nicety */ }
    }
};
