// Optimistic basket steppers.
//
// Blazor Server owns the basket, so the authoritative number only arrives after a
// round-trip — and on a connection forced onto long-polling that is hundreds of
// milliseconds. Waiting for it makes the button feel broken, so the browser paints
// the answer itself the moment the finger lands and the server's render simply
// confirms it a moment later.
//
// A clear progress bar runs across the card while that confirmation is in flight,
// and gives up after 4s so a dropped connection never leaves it spinning forever.
window.mfQty = {
    // MUST run on click, never on pointerdown: hiding the pressed button between
    // pointerdown and pointerup cancels its click, so Blazor never received the
    // decrement — the basket kept the item while the card showed a bare +.
    tap(el, delta, ev) {
        if (!el) return;
        const box = el.closest('.mf-qty-inline');
        if (box && delta) {
            const label = box.querySelector('b');
            if (label) {
                const now = parseInt(label.textContent, 10);
                if (!isNaN(now)) {
                    const next = Math.max(0, now + delta);
                    label.textContent = next;          // instant, no server needed
                    // data-qty drives the looks: 1 shows the bin, 0 collapses the
                    // stepper to the bare +, exactly like the server will render it.
                    box.dataset.qty = next;
                    // Belt AND braces: the attribute drives the stylesheet, and the
                    // same states are forced inline in case that stylesheet is a
                    // cached older copy. settled() wipes the inline part the moment
                    // the server's own render lands, so nothing can stay stuck.
                    const show = (sel, on) => {
                        const el = box.querySelector(sel);
                        if (el) el.style.display = on ? '' : 'none';
                    };
                    show('.qty-less', next > 1);
                    show('.qty-bin', next === 1);
                    show('b', next > 0);
                }
            }
        }
        this.busy(el, ev);
        this.pending(el);
    },

    /// While the server confirms, the whole stepper steps aside: every control in
    /// it hides and one centered ring holds the space, so no half-state is ever
    /// visible. settled() (or the 4s failsafe) brings the controls back.
    busy(el, ev) {
        const box = (el && el.closest?.('.mf-qty-inline')) || el;
        if (!box) return;
        box.classList.add('qty-wait');
        clearTimeout(box._busyTimer);
        box._busyTimer = setTimeout(() => box.classList.remove('qty-wait'), 4000);
    },

    /// The LOOK comes from data-qty + the stylesheet (0 → only the +, 1 → bin,
    /// 2+ → ordinary stepper). This only clears inline display left behind by an
    /// older build, which could otherwise keep a control hidden after Blazor
    /// re-used the element for a fuller basket.
    paint(box) {
        for (const el of box.querySelectorAll('.qty-less, .qty-bin, b'))
            if (el.style.display) el.style.display = '';
    },

    /// Shows the working bar on the whole card — far easier to see than a hairline.
    pending(el) {
        const card = el.closest('.mf-menu-item') || el.closest('.mf-qty-inline');
        if (!card) return;
        card.classList.add('qty-pending');
        clearTimeout(card._qtyTimer);
        card._qtyTimer = setTimeout(() => card.classList.remove('qty-pending'), 4000);
    },

    /// Called from Blazor after the real render lands: every bar stands down, and
    /// every stepper is repainted from the SERVER's data-qty. Blazor reuses these
    /// DOM nodes, so an inline style left over from an optimistic 0 would otherwise
    /// keep the minus hidden even after the basket refilled.
    settled() {
        document.querySelectorAll('.qty-pending').forEach(el => {
            clearTimeout(el._qtyTimer);
            el.classList.remove('qty-pending');
        });
        document.querySelectorAll('.qty-wait, .qty-busy').forEach(el => {
            clearTimeout(el._busyTimer);
            el.classList.remove('qty-wait', 'qty-busy');
        });
        document.querySelectorAll('.mf-qty-inline').forEach(box => this.paint(box));
    }
};
