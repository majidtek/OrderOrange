// The partner portal is responsive everywhere — except the floor plan.
//
// Tables are drawn on a canvas at real coordinates: a salon laid out for a wide
// screen cannot reflow into a phone column without losing the shape of the room.
// So that one page asks the browser for a 1280px-wide layout viewport (exactly what
// Chrome's "Desktop site" toggle does) and the owner pinches to the corner they want.
// Every other page keeps the phone layout, which is what a thumb deserves.
window.ooViewport = (function () {
    var DESKTOP_WIDTH = 1280;
    var RESPONSIVE = "width=device-width, initial-scale=1.0, viewport-fit=cover";

    // The only paths that force the wide layout. Trailing slashes are trimmed first,
    // and a prefix match covers child routes like /tables/3.
    var DESKTOP_PATHS = ["/tables"];

    function viewportMeta() {
        var m = document.querySelector('meta[name="viewport"]');
        if (!m) {
            m = document.createElement("meta");
            m.setAttribute("name", "viewport");
            document.head.appendChild(m);
        }
        return m;
    }

    // Is this an actual handset? `screen` describes the hardware and does not change
    // when we widen the viewport, so this stays true after the switch.
    function isPhone() {
        var shortSide = Math.min(screen.width, screen.height);
        var coarse = window.matchMedia && window.matchMedia("(pointer: coarse)").matches;
        return shortSide <= 820 && coarse;
    }

    function apply() {
        var path = location.pathname.replace(/\/+$/, "") || "/";
        var wantsDesktop = DESKTOP_PATHS.some(function (p) {
            return path === p || path.indexOf(p + "/") === 0;
        });
        var desktop = isPhone() && wantsDesktop;

        viewportMeta().setAttribute(
            "content", desktop ? "width=" + DESKTOP_WIDTH + ", viewport-fit=cover" : RESPONSIVE);
        document.documentElement.classList.toggle("oo-desktop-mode", desktop);
    }

    // Blazor navigates with history.pushState, which fires no event of its own.
    ["pushState", "replaceState"].forEach(function (name) {
        var original = history[name];
        history[name] = function () {
            var result = original.apply(this, arguments);
            setTimeout(apply, 0);
            return result;
        };
    });
    window.addEventListener("popstate", apply);
    window.addEventListener("orientationchange", function () { setTimeout(apply, 120); });
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", apply);
    }
    apply();

    return { apply: apply, isPhone: isPhone };
})();

// Fullscreen toggle for the whole app. Called from a plain DOM onclick (not a
// Blazor round-trip) so the browser still counts it as a real user gesture —
// requestFullscreen is refused otherwise.
window.mfFull = {
    toggle: function () {
        var d = document, el = d.documentElement;
        if (!d.fullscreenElement && !d.webkitFullscreenElement) {
            (el.requestFullscreen || el.webkitRequestFullscreen || function () {}).call(el);
        } else {
            (d.exitFullscreen || d.webkitExitFullscreen || function () {}).call(d);
        }
    }
};

// ---- the nav dialog's "more below" cue ----
// Shows a fade + bouncing chevron while the menu still has rows below the fold,
// and drops both the moment the reader reaches the bottom. Idempotent: safe to
// call on every render while the dialog is open.
window.mfNavHint = {
    attach(attempt) {
        const el = document.querySelector('.pnav-dialog .mud-dialog-content');
        const btn = document.querySelector('.pnav-more-hint');
        const up = document.querySelector('.pnav-up-hint');
        const fade = document.querySelector('.pnav-fade');
        if (!el || !btn) {
            // Blazor mounts the dialog a beat AFTER the layout render that calls us —
            // keep knocking for a few seconds instead of giving up on the first try.
            if ((attempt || 0) < 20) setTimeout(() => this.attach((attempt || 0) + 1), 250);
            return;
        }
        const update = () => {
            const more = el.scrollHeight - el.scrollTop - el.clientHeight > 48;
            btn.classList.toggle('show', more);
            if (fade) fade.classList.toggle('show', more);
            if (up) up.classList.toggle('show', el.scrollTop > 48);
        };
        if (!el.dataset.mfHint) {
            el.dataset.mfHint = '1';
            el.addEventListener('scroll', update, { passive: true });
            new ResizeObserver(update).observe(el);
            btn.addEventListener('click', () =>
                el.scrollBy({ top: el.clientHeight * 0.8, behavior: 'smooth' }));
            if (up) up.addEventListener('click', () =>
                el.scrollBy({ top: -el.clientHeight * 0.8, behavior: 'smooth' }));
        }
        update();
    }
};
