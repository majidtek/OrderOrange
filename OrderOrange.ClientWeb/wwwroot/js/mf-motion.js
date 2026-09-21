// OrderOrange customer app — Motion (framer-motion vanilla engine) animation layer.
// Uses window.Motion from js/motion.min.js (motion@12 UMD build).
// Blazor Server re-renders DOM at any time, so a MutationObserver re-scans and
// animates only nodes it hasn't seen (marked with data-mfm).
(function () {
    'use strict';

    var REDUCED = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Card-like things that gently fade-up as they enter the viewport.
    var REVEAL = '.mf-rest-card, .mf-promo-card, .mf-again-card, .mf-menu-item, ' +
                 '.mf-search-hit, .mf-address-card, .mud-card';
    // Small round things that pop in with a stagger.
    var POP = '.mf-cuisine-item';
    // Hero banners that slide in once per page view.
    var HERO = '.mf-hero, .mf-vert-hero, .mf-track-hero';
    // Things that press down while tapped.
    var PRESS = '.mf-rest-card, .mf-again-card, .mf-promo-card, .mf-cuisine-item, ' +
                '.mf-menu-item, .mf-basket-bar, .mud-button-root';

    function motion() { return window.Motion || null; }

    function reveal(el, i) {
        var M = motion();
        if (!M) return;
        el.style.opacity = '0';
        M.animate(el,
            { opacity: [0, 1], transform: ['translateY(16px)', 'translateY(0px)'] },
            { duration: 0.45, delay: Math.min(i * 0.05, 0.4), ease: [0.21, 0.8, 0.35, 1] });
    }

    function pop(el, i) {
        var M = motion();
        if (!M) return;
        el.style.opacity = '0';
        M.animate(el,
            { opacity: [0, 1], transform: ['scale(.6)', 'scale(1.06)', 'scale(1)'] },
            { duration: 0.5, delay: Math.min(i * 0.04, 0.5), ease: 'easeOut' });
    }

    function hero(el) {
        var M = motion();
        if (!M) return;
        M.animate(el,
            { opacity: [0, 1], transform: ['translateY(-10px) scale(.985)', 'translateY(0px) scale(1)'] },
            { duration: 0.55, ease: [0.21, 0.8, 0.35, 1] });
    }

    function basket(el) {
        var M = motion();
        if (!M) return;
        M.animate(el,
            { opacity: [0, 1], transform: ['translateY(70px) scale(.92)', 'translateY(-6px) scale(1.02)', 'translateY(0px) scale(1)'] },
            { duration: 0.55, ease: 'easeOut' });
    }

    function pressable(el) {
        el.addEventListener('pointerdown', function () {
            var M = motion();
            if (M && !REDUCED) M.animate(el, { scale: 0.97 }, { duration: 0.1 });
        });
        var up = function () {
            var M = motion();
            if (M && !REDUCED) M.animate(el, { scale: 1 }, { duration: 0.25, ease: 'easeOut' });
        };
        el.addEventListener('pointerup', up);
        el.addEventListener('pointerleave', up);
        el.addEventListener('pointercancel', up);
    }

    function seen(el, tag) {
        if (el.dataset['mfm' + tag]) return true;
        el.dataset['mfm' + tag] = '1';
        return false;
    }

    var revealQueue = 0;
    function scan(root) {
        var M = motion();
        if (!M) return;
        var scope = root && root.querySelectorAll ? root : document;

        if (!REDUCED) {
            scope.querySelectorAll(HERO).forEach(function (el) {
                if (!seen(el, 'H')) hero(el);
            });
            var i = revealQueue;
            scope.querySelectorAll(REVEAL).forEach(function (el) {
                if (seen(el, 'R')) return;
                // Only intro-animate what is near the viewport; everything else
                // reveals on scroll via inView below.
                var r = el.getBoundingClientRect();
                if (r.top < window.innerHeight + 120) {
                    reveal(el, i++ - revealQueue);
                } else {
                    el.style.opacity = '0';
                    M.inView(el, function () { reveal(el, 0); }, { margin: '0px 0px -40px 0px' });
                }
            });
            var p = 0;
            scope.querySelectorAll(POP).forEach(function (el) {
                if (!seen(el, 'P')) pop(el, p++);
            });
            scope.querySelectorAll('.mf-basket-bar').forEach(function (el) {
                if (!seen(el, 'B')) basket(el);
            });
        }
        scope.querySelectorAll(PRESS).forEach(function (el) {
            if (!seen(el, 'T')) pressable(el);
        });
    }

    function boot() {
        if (!motion()) { setTimeout(boot, 150); return; }
        scan(document);
        var mo = new MutationObserver(function (muts) {
            for (var m = 0; m < muts.length; m++) {
                for (var n = 0; n < muts[m].addedNodes.length; n++) {
                    var node = muts[m].addedNodes[n];
                    if (node.nodeType === 1) scan(node);
                }
            }
        });
        mo.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
