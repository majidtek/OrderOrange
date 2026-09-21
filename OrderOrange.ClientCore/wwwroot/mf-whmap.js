// The warehouse map's hands. Blazor owns the places and the lines between them;
// this file drags nodes with mouse or finger, keeps the lines glued to them while
// they move, and reports where a node was dropped in canvas percentages (the
// node's CENTER), so every screen size agrees on the layout.
window.mfWh = {
    wire(dotnetRef) {
        const canvas = document.querySelector('.whm-canvas');
        if (!canvas) return;
        canvas._mfRef = dotnetRef;
        if (!canvas._mfResize) {
            canvas._mfResize = () => window.mfWh.fit();
            window.addEventListener('resize', canvas._mfResize);
        }
        window.mfWh.fit();

        canvas.querySelectorAll('.whm-node[data-id]').forEach(el => {
            if (el._mfWired) return;
            el._mfWired = true;
            const id = +el.dataset.id;
            let d = null;

            el.addEventListener('pointerdown', e => {
                // buttons inside the node (link, edit) are clicks, not drags
                if (e.button !== 0 || e.target.closest('button')) return;
                d = { x: e.clientX, y: e.clientY, l: el.offsetLeft, t: el.offsetTop, moved: false };
                el.setPointerCapture(e.pointerId);
            });
            el.addEventListener('pointermove', e => {
                if (!d) return;
                // pointer pixels are screen pixels; the drawing may be scaled down
                const k = canvas._k || 1;
                const dx = (e.clientX - d.x) / k, dy = (e.clientY - d.y) / k;
                if (!d.moved && Math.hypot(dx, dy) < 4) return;
                d.moved = true;
                el.classList.add('drag');
                const w = el.offsetWidth, h = el.offsetHeight;
                // the node is translated by -50%, so offsetLeft IS the centre; keep it inside
                const cx = Math.min(Math.max(d.l + dx, w / 2), canvas.offsetWidth - w / 2);
                const cy = Math.min(Math.max(d.t + dy, h / 2), canvas.offsetHeight - h / 2);
                el.style.left = cx / canvas.offsetWidth * 100 + '%';
                el.style.top = cy / canvas.offsetHeight * 100 + '%';
                window.mfWh.draw();
                e.preventDefault();
            });
            const end = e => {
                if (!d) return;
                const was = d; d = null;
                el.classList.remove('drag');
                if (!was.moved) return;
                // swallow the click that follows a real drag
                el.addEventListener('click', ev => ev.stopPropagation(), { capture: true, once: true });
                const x = el.offsetLeft / canvas.offsetWidth * 100;
                const y = el.offsetTop / canvas.offsetHeight * 100;
                canvas._mfRef.invokeMethodAsync('OnNodeMoved', id, x, y);
            };
            el.addEventListener('pointerup', end);
            el.addEventListener('pointercancel', end);
        });

        window.mfWh.draw();
    },

    // The drawing is 1000x520. Shrink it to the wrapper's width, but never below 0.72 —
    // past that the wrapper scrolls sideways instead, so the text stays readable.
    fit() {
        const canvas = document.querySelector('.whm-canvas');
        const wrap = canvas && canvas.parentElement;
        if (!wrap) return;
        const k = Math.max(0.72, Math.min(1, wrap.clientWidth / 1000));
        canvas._k = k;
        canvas.style.transform = `scale(${k})`;
        // a transform leaves the layout box at full size; pull the margins in so the
        // wrapper scrolls exactly as far as the picture goes and no further
        canvas.style.marginRight = -Math.round(1000 * (1 - k)) + 'px';
        canvas.style.marginBottom = -Math.round(520 * (1 - k)) + 'px';
        wrap.style.height = Math.round(520 * k) + 'px';
        window.mfWh.draw();
    },

    // Every line runs from the edge of one node to the edge of the next, with the
    // arrow head landing just outside the target; the label sits at the midpoint.
    draw() {
        const canvas = document.querySelector('.whm-canvas');
        if (!canvas) return;
        const svg = canvas.querySelector('svg.whm-lines');
        if (!svg) return;
        const W = canvas.offsetWidth, H = canvas.offsetHeight;
        svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
        const box = id => {
            const el = canvas.querySelector(`.whm-node[data-id="${id}"]`);
            if (!el) return null;
            return { cx: el.offsetLeft, cy: el.offsetTop, hw: el.offsetWidth / 2, hh: el.offsetHeight / 2 };
        };
        // where a ray from the centre leaves a rounded box, padded a little
        const edge = (b, dx, dy, pad) => {
            const ax = Math.abs(dx), ay = Math.abs(dy);
            if (ax < 1e-6 && ay < 1e-6) return { x: b.cx, y: b.cy };
            const t = Math.min((b.hw + pad) / (ax || 1e-9), (b.hh + pad) / (ay || 1e-9));
            return { x: b.cx + dx * t, y: b.cy + dy * t };
        };
        svg.querySelectorAll('path[data-from]').forEach(p => {
            const a = box(p.dataset.from), b = box(p.dataset.to);
            const lbl = canvas.querySelector(`.whm-lbl[data-from="${p.dataset.from}"][data-to="${p.dataset.to}"]`);
            if (!a || !b) { p.setAttribute('d', ''); if (lbl) lbl.hidden = true; return; }
            const dx = b.cx - a.cx, dy = b.cy - a.cy;
            const s = edge(a, dx, dy, 6), t = edge(b, -dx, -dy, 10);
            // a gentle curve reads as a route rather than a wire
            const mx = (s.x + t.x) / 2, my = (s.y + t.y) / 2;
            const bend = Math.min(40, Math.hypot(dx, dy) / 6);
            const nx = -dy / (Math.hypot(dx, dy) || 1) * bend, ny = dx / (Math.hypot(dx, dy) || 1) * bend;
            p.setAttribute('d', `M${s.x},${s.y} Q${mx + nx},${my + ny} ${t.x},${t.y}`);
            if (lbl) {
                lbl.hidden = false;
                lbl.style.left = (mx + nx / 2) / W * 100 + '%';
                lbl.style.top = (my + ny / 2) / H * 100 + '%';
            }
        });
    },
};
