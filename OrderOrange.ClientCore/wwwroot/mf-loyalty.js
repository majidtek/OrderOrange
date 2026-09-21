// The loyalty page's motion: numbers that count up, bars that grow, a ring that fills,
// and a burst of confetti when a reward is handed over. Blazor owns every value; this
// file only animates towards what is already on the page, and does nothing when the
// visitor asked for reduced motion.
window.mfLy = {
    reduced() { return matchMedia('(prefers-reduced-motion: reduce)').matches; },

    // Every [data-count] element rolls from where it is to its number. Called after each
    // render, so a redeem visibly drains the balance instead of snapping.
    countUp(root) {
        const scope = root ? document.querySelector(root) : document;
        if (!scope) return;
        scope.querySelectorAll('[data-count]').forEach(el => {
            const to = parseFloat(el.dataset.count);
            if (isNaN(to)) return;
            const decimals = (el.dataset.dec | 0);
            const from = el._mfShown ?? 0;
            if (from === to && el._mfShown !== undefined) return;
            el._mfShown = to;
            if (this.reduced()) { el.textContent = to.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }); return; }
            const t0 = performance.now(), dur = 900;
            const tick = now => {
                const k = Math.min(1, (now - t0) / dur), e = 1 - Math.pow(1 - k, 3);
                const v = from + (to - from) * e;
                el.textContent = v.toLocaleString(undefined, { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
                if (k < 1) requestAnimationFrame(tick);
            };
            requestAnimationFrame(tick);
        });
    },

    // Once the entrance has played, mark the page so a re-render (the live strip polls
    // every ten seconds) cannot restart the rises, the bars or the podium.
    settle() {
        const ly = document.querySelector('.ly');
        if (!ly || ly._mfSettling || ly.classList.contains('lit')) return;
        ly._mfSettling = true;
        setTimeout(() => ly.classList.add('lit'), 2200);
    },

    // The ring's --p is a registered property, so setting it after paint animates it.
    ring(pct) {
        const r = document.querySelector('.ly-member .ring');
        if (!r) return;
        requestAnimationFrame(() => requestAnimationFrame(() => r.style.setProperty('--p', pct)));
    },

    // A short shower of the tier's colours over the member card.
    confetti() {
        if (this.reduced()) return;
        const host = document.querySelector('.ly-member') || document.body;
        const c = document.createElement('canvas');
        c.className = 'ly-confetti';
        const box = host.getBoundingClientRect();
        c.width = box.width; c.height = Math.max(240, box.height);
        host.appendChild(c);
        const ctx = c.getContext('2d');
        const colors = ['#FF5A00', '#FFB273', '#D4A017', '#1C1712', '#1E7A3C', '#fff'];
        const bits = Array.from({ length: 90 }, () => ({
            x: c.width * (0.3 + Math.random() * 0.4), y: c.height * 0.5,
            vx: (Math.random() - 0.5) * 9, vy: -4 - Math.random() * 8,
            w: 5 + Math.random() * 5, h: 3 + Math.random() * 4, r: Math.random() * Math.PI, vr: (Math.random() - 0.5) * 0.3,
            col: colors[(Math.random() * colors.length) | 0],
        }));
        const t0 = performance.now();
        const draw = now => {
            const k = (now - t0) / 1400;
            ctx.clearRect(0, 0, c.width, c.height);
            for (const b of bits) {
                b.x += b.vx; b.vy += 0.28; b.y += b.vy; b.r += b.vr;
                ctx.save(); ctx.translate(b.x, b.y); ctx.rotate(b.r);
                ctx.globalAlpha = Math.max(0, 1 - k);
                ctx.fillStyle = b.col; ctx.fillRect(-b.w / 2, -b.h / 2, b.w, b.h);
                ctx.restore();
            }
            if (k < 1) requestAnimationFrame(draw); else c.remove();
        };
        requestAnimationFrame(draw);
    },

    // Open a WhatsApp chat with the guest, with the message ready to send.
    nudge(phone, text) {
        let d = (phone || '').replace(/\D/g, '');
        if (d.length === 8) d = '968' + d;
        window.open('https://wa.me/' + d + '?text=' + encodeURIComponent(text), '_blank', 'noopener');
    },
};
