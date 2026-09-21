// Keeps the <html> element's lang + dir attributes in step with the chosen language,
// so the whole document flips LTR/RTL instantly without a reload.
//
// It also owns the DIGITS: pick Farsi and every rendered 3 becomes ۳, pick Arabic
// and it becomes ٣ — done with a MutationObserver over text nodes, so all four
// apps localize numbers without touching a single page. Inputs, <kbd>, <code>
// and SVG (barcodes!) keep ASCII, because those are typed, scanned or parsed.
//
// Anything else that must stay machine-readable carries class "mf-ascii", and neither
// it nor its children are touched: a printed URL, a barcode's human-readable number,
// an account or reference code. «orderorange.com/restaurant/۵۰۰۰۲۱۴» is not a link
// anybody can type, and a code that disagrees with the bars under it is worse than none.
window.mfLang = (function () {
    const DIGITS = {
        fa: '۰۱۲۳۴۵۶۷۸۹',
        ur: '۰۱۲۳۴۵۶۷۸۹',
        ar: '٠١٢٣٤٥٦٧٨٩',
    };
    const SKIP = { INPUT: 1, TEXTAREA: 1, SELECT: 1, OPTION: 1, SCRIPT: 1, STYLE: 1, KBD: 1, CODE: 1, svg: 1, SVG: 1 };
    const KEEP_ASCII = 'mf-ascii';
    let map = null, observer = null;

    function localize(text) {
        return text.replace(/[0-9]/g, function (d) { return map[d]; });
    }

    /// An element whose digits must be left exactly as rendered.
    function skipped(el) {
        if (SKIP[el.nodeName]) return true;
        // classList is absent on some SVG-adjacent nodes, and className there is an object.
        return !!(el.classList && el.classList.contains(KEEP_ASCII));
    }

    function walk(node) {
        if (node.nodeType === 3) {
            const value = node.nodeValue;
            if (value && /[0-9]/.test(value)) node.nodeValue = localize(value);
            return;
        }
        if (node.nodeType !== 1 || skipped(node)) return;
        for (let child = node.firstChild; child; child = child.nextSibling) walk(child);
    }

    function inSkipped(node) {
        for (let el = node.parentNode; el; el = el.parentNode) {
            if (el.nodeType === 1 && skipped(el)) return true;
        }
        return false;
    }

    function start() {
        walk(document.body);
        if (observer) return;
        observer = new MutationObserver(function (mutations) {
            if (!map) return;
            for (const m of mutations) {
                if (m.type === 'characterData') {
                    const value = m.target.nodeValue;
                    // Writing back only when something changed keeps this from looping.
                    if (value && /[0-9]/.test(value) && !inSkipped(m.target)) m.target.nodeValue = localize(value);
                } else {
                    // inSkipped FIRST. Blazor often creates an element and inserts its
                    // text as a separate mutation, so the added node is a bare text node
                    // whose "mf-ascii" ancestor walk() cannot see from the node itself —
                    // which localized the digits of every IP, barcode and promo code that
                    // arrived after the first render.
                    for (const n of m.addedNodes) if (!inSkipped(n)) walk(n);
                }
            }
        });
        observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    }

    function stop() {
        if (observer) { observer.disconnect(); observer = null; }
        map = null;
    }

    return {
        set(locale, rtl) {
            document.documentElement.lang = locale;
            document.documentElement.dir = rtl ? 'rtl' : 'ltr';
            const digits = DIGITS[locale];
            if (digits) {
                map = {};
                for (let i = 0; i < 10; i++) map[String(i)] = digits[i];
                if (document.body) start();
                else document.addEventListener('DOMContentLoaded', start, { once: true });
            } else {
                stop();
                // Coming back from Farsi to English leaves ۳s behind — a reload-free
                // revert would need a full re-render, so the next navigation fixes it.
            }
        },
    };
})();

// ── The language strip: arrows, and a strip that answers a dragging finger ──
// The row scrolls natively on a touch screen; a mouse gets the same by dragging,
// and the two arrows step it by most of a screenful.
window.mfLangStrip = (() => {
    const strip = () => document.querySelector('.mf-lang-strip');

    return {
        scroll: (direction) => {
            const el = strip();
            if (!el) return;
            el.scrollBy({ left: direction * Math.max(180, el.clientWidth * 0.75), behavior: 'smooth' });
        },
        // Called once when the picker opens. Drag-to-scroll for pointers; a real
        // touch is left to the browser, which already does it better.
        arm: (tries) => {
            const el = strip();
            // The dialog mounts a render AFTER the component that opened it, so the
            // strip may not exist on the first call — look again for a moment.
            if (!el) { if ((tries || 0) < 12) setTimeout(() => window.mfLangStrip.arm((tries || 0) + 1), 120); return; }
            if (el.dataset.armed) return;
            el.dataset.armed = '1';
            let down = false, startX = 0, startLeft = 0, moved = 0;
            el.addEventListener('pointerdown', e => {
                if (e.pointerType === 'touch') return;
                down = true; moved = 0;
                startX = e.clientX; startLeft = el.scrollLeft;
                el.setPointerCapture(e.pointerId);
                el.classList.add('dragging');
            });
            el.addEventListener('pointermove', e => {
                if (!down) return;
                const dx = e.clientX - startX;
                moved = Math.max(moved, Math.abs(dx));
                el.scrollLeft = startLeft - dx;
            });
            const end = () => { down = false; el.classList.remove('dragging'); };
            el.addEventListener('pointerup', end);
            el.addEventListener('pointercancel', end);
            // A drag must not also pick a language.
            el.addEventListener('click', e => { if (moved > 6) { e.stopPropagation(); e.preventDefault(); } }, true);
        },
    };
})();
