// OrderOrange customer app — modern horizontal scroller.
// Enhances every .mf-hscroll row with: frosted arrow buttons (auto hide at
// the edges), spring-animated scrolling, a velocity "lean" on the items
// while the row moves, vertical-wheel capture, and desktop drag-to-scroll.
// Survives Blazor Server re-renders via MutationObserver.
(function () {
    'use strict';

    var REDUCED = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    var RTL = function () { return (document.documentElement.getAttribute('dir') || '').toLowerCase() === 'rtl'; };

    var CHEVRON_L = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"/></svg>';
    var CHEVRON_R = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M9 6l6 6-6 6"/></svg>';

    function pos(el) { return Math.abs(el.scrollLeft); }
    function maxPos(el) { return Math.max(0, el.scrollWidth - el.clientWidth); }

    function setPos(el, p) {
        el.scrollLeft = RTL() ? -p : p;
    }

    // Spring-animated scroll; falls back to native smooth scrolling.
    function springTo(el, target) {
        target = Math.max(0, Math.min(maxPos(el), target));
        var M = window.Motion;
        if (el._mfAnim && el._mfAnim.stop) el._mfAnim.stop();
        if (M && M.animate && !REDUCED) {
            try {
                el._mfAnim = M.animate(pos(el), target, {
                    type: 'spring', stiffness: 150, damping: 24, mass: .9,
                    onUpdate: function (v) { setPos(el, v); }
                });
                return;
            } catch (e) { /* fall through to native */ }
        }
        el.scrollTo({ left: RTL() ? -target : target, behavior: REDUCED ? 'auto' : 'smooth' });
    }

    function update(el, bar) {
        var m = maxPos(el), p = pos(el);
        var canPrev = p > 4, canNext = p < m - 4;
        bar.prevBtn.classList.toggle('off', !canPrev);
        bar.nextBtn.classList.toggle('off', !canNext);
        el.classList.toggle('fade-both', canPrev && canNext);
        el.classList.toggle('fade-start', canPrev && !canNext);
        el.classList.toggle('fade-end', !canPrev && canNext);
        bar.el.style.display = m > 8 ? '' : 'none';
    }

    function align(el, bar) {
        bar.el.style.top = el.offsetTop + 'px';
        bar.el.style.height = el.offsetHeight + 'px';
        // Rows of labelled circles: center the arrows on the circles, not on
        // the full row (the labels below would drag them too low).
        var c = el.querySelector('.mf-cuisine-circle');
        var t = '50%';
        if (c) {
            t = (c.getBoundingClientRect().top - el.getBoundingClientRect().top + c.offsetHeight / 2) + 'px';
        }
        bar.prevBtn.style.top = t;
        bar.nextBtn.style.top = t;
    }

    function makeButton(cls, svg) {
        var b = document.createElement('button');
        b.type = 'button';
        b.className = 'mf-sbtn ' + cls;
        b.setAttribute('aria-label', cls === 'prev' ? 'Scroll back' : 'Scroll forward');
        b.innerHTML = svg;
        return b;
    }

    function enhance(el) {
        if (el.dataset.mfsc) return;
        el.dataset.mfsc = '1';

        var parent = el.parentElement;
        if (!parent) return;
        parent.classList.add('mf-scroll-anchor');

        var bar = { el: document.createElement('div') };
        bar.el.className = 'mf-sbar';
        bar.el._mfRow = el;   // linkage for orphan cleanup after Blazor re-renders
        bar.prevBtn = makeButton('prev', CHEVRON_L);
        bar.nextBtn = makeButton('next', CHEVRON_R);
        bar.el.appendChild(bar.prevBtn);
        bar.el.appendChild(bar.nextBtn);
        parent.insertBefore(bar.el, el.nextSibling);

        var page = function () { return Math.max(140, el.clientWidth * 0.75); };
        bar.prevBtn.addEventListener('click', function () { springTo(el, pos(el) - page()); });
        bar.nextBtn.addEventListener('click', function () { springTo(el, pos(el) + page()); });

        // ----- velocity lean: items tilt with fast motion, spring back on rest
        var lastP = pos(el), lastT = performance.now(), idle = null, ticking = false;
        el.addEventListener('scroll', function () {
            if (!ticking) {
                ticking = true;
                requestAnimationFrame(function () {
                    ticking = false;
                    var now = performance.now();
                    var dp = pos(el) - lastP, dt = Math.max(8, now - lastT);
                    lastP = pos(el); lastT = now;
                    if (!REDUCED) {
                        var v = dp / dt; // px per ms
                        var lean = Math.max(-5, Math.min(5, v * 5));
                        var dir = RTL() ? -1 : 1;
                        el.style.setProperty('--mf-lean', (-lean * dir) + 'deg');
                        el.style.setProperty('--mf-shift', (-Math.max(-8, Math.min(8, v * 8)) * dir) + 'px');
                    }
                    update(el, bar);
                    clearTimeout(idle);
                    idle = setTimeout(function () {
                        el.style.setProperty('--mf-lean', '0deg');
                        el.style.setProperty('--mf-shift', '0px');
                    }, 90);
                });
            }
        }, { passive: true });

        // ----- vertical mouse wheel scrolls the row while hovering it
        el.addEventListener('wheel', function (e) {
            if (Math.abs(e.deltaY) <= Math.abs(e.deltaX)) return;   // trackpad horizontal pans pass through
            if (maxPos(el) <= 8) return;
            var p = pos(el);
            if ((e.deltaY > 0 && p >= maxPos(el) - 2) || (e.deltaY < 0 && p <= 2)) return; // let the page scroll at the ends
            e.preventDefault();
            if (el._mfAnim && el._mfAnim.stop) el._mfAnim.stop();
            setPos(el, p + e.deltaY);
        }, { passive: false });

        // ----- desktop drag-to-scroll (clicks still work below 8px of travel)
        var drag = null;
        el.addEventListener('mousedown', function (e) {
            if (e.button !== 0 || maxPos(el) <= 8) return;
            drag = { x: e.clientX, start: pos(el), moved: false };
            if (el._mfAnim && el._mfAnim.stop) el._mfAnim.stop();
        });
        window.addEventListener('mousemove', function (e) {
            if (!drag) return;
            var dx = e.clientX - drag.x;
            if (Math.abs(dx) > 8) { drag.moved = true; el.classList.add('dragging'); }
            if (drag.moved) setPos(el, drag.start - dx * (RTL() ? -1 : 1));
        });
        window.addEventListener('mouseup', function () {
            if (!drag) return;
            var moved = drag.moved;
            drag = null;
            el.classList.remove('dragging');
            if (moved) {
                // swallow the click that follows a drag so items don't open
                el.addEventListener('click', function block(ev) {
                    ev.stopPropagation(); ev.preventDefault();
                    el.removeEventListener('click', block, true);
                }, true);
            }
        });

        window.addEventListener('resize', function () { align(el, bar); update(el, bar); });
        align(el, bar);
        update(el, bar);
        // Re-measure once the entrance animations settle and fonts load.
        setTimeout(function () { align(el, bar); update(el, bar); }, 450);
    }

    function scanAll() {
        // Remove orphaned arrow overlays: Blazor swaps pages underneath us and
        // leaves injected bars behind when their row is gone or moved away.
        document.querySelectorAll('.mf-sbar').forEach(function (b) {
            if (!b._mfRow || !document.body.contains(b._mfRow) || b.previousElementSibling !== b._mfRow) {
                b.remove();
            }
        });
        document.querySelectorAll('.mf-hscroll').forEach(enhance);
        // Blazor re-render may have dropped an overlay while keeping the row's
        // dataset flag — restore any row whose bar is gone.
        document.querySelectorAll('.mf-hscroll[data-mfsc]').forEach(function (el) {
            var sib = el.nextElementSibling;
            if (!sib || !sib.classList || !sib.classList.contains('mf-sbar')) {
                delete el.dataset.mfsc;
                enhance(el);
            }
        });
    }

    function boot() {
        scanAll();
        new MutationObserver(function () {
            clearTimeout(boot._t);
            boot._t = setTimeout(scanAll, 60);
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
