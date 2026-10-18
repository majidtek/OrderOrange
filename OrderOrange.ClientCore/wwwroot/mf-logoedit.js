// Logo editor (Majed 2026-10-14: "client in upload logo can edit logo").
// A square canvas: drag to move, slider to zoom, 90° rotation, transparent or white
// background, optional round preview. export() redraws the same framing at the output size.
window.mfLogoEdit = (function () {
    const st = {};

    function checker(ctx, W) {
        const n = 12, sz = W / n;
        for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) {
            ctx.fillStyle = (x + y) % 2 ? '#eeeae4' : '#ffffff';
            ctx.fillRect(x * sz, y * sz, sz, sz);
        }
    }

    function render(ctx, W, s) {
        const img = s.img;
        const fit = W / Math.max(img.naturalWidth, img.naturalHeight);
        const sc = fit * s.zoom;
        ctx.save();
        ctx.translate(W / 2 + s.x * W, W / 2 + s.y * W);
        ctx.rotate(s.rot * Math.PI / 180);
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(img, -img.naturalWidth * sc / 2, -img.naturalHeight * sc / 2, img.naturalWidth * sc, img.naturalHeight * sc);
        ctx.restore();
    }

    function draw(id) {
        const s = st[id]; if (!s) return;
        const c = s.canvas, ctx = c.getContext('2d'), W = c.width;
        ctx.clearRect(0, 0, W, W);
        if (s.bg === 'white') { ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, W, W); } else checker(ctx, W);
        render(ctx, W, s);
        if (s.round) {
            ctx.save();
            ctx.fillStyle = 'rgba(30,20,10,.45)';
            ctx.beginPath();
            ctx.rect(0, 0, W, W);
            ctx.arc(W / 2, W / 2, W / 2 - 1, 0, Math.PI * 2, true);
            ctx.fill('evenodd');
            ctx.restore();
        }
    }

    function init(id, src) {
        return new Promise((resolve) => {
            const canvas = document.getElementById(id);
            if (!canvas) { resolve(false); return; }
            const ratio = Math.min(window.devicePixelRatio || 1, 2);
            const css = canvas.clientWidth || 280;
            canvas.width = Math.round(css * ratio);
            canvas.height = Math.round(css * ratio);
            const img = new Image();
            img.onload = () => {
                const s = st[id] = { canvas, img, zoom: 1, rot: 0, x: 0, y: 0, bg: 'clear', round: false, drag: null };
                canvas.onpointerdown = (e) => { canvas.setPointerCapture(e.pointerId); s.drag = { px: e.clientX, py: e.clientY, x: s.x, y: s.y }; };
                canvas.onpointermove = (e) => {
                    if (!s.drag) return;
                    const w = canvas.clientWidth || css;
                    s.x = s.drag.x + (e.clientX - s.drag.px) / w;
                    s.y = s.drag.y + (e.clientY - s.drag.py) / w;
                    draw(id);
                };
                canvas.onpointerup = canvas.onpointercancel = () => { s.drag = null; };
                canvas.onwheel = (e) => {
                    e.preventDefault();
                    s.zoom = Math.min(5, Math.max(0.3, s.zoom * (e.deltaY < 0 ? 1.08 : 1 / 1.08)));
                    draw(id);
                    if (s.onZoom) s.onZoom.invokeMethodAsync('ZoomChanged', s.zoom);
                };
                draw(id);
                resolve(true);
            };
            img.onerror = () => resolve(false);
            img.src = src;
        });
    }

    return {
        init,
        watchZoom: (id, ref) => { if (st[id]) st[id].onZoom = ref; },
        zoom: (id, z) => { const s = st[id]; if (s) { s.zoom = z; draw(id); } },
        rotate: (id, deg) => { const s = st[id]; if (s) { s.rot = (s.rot + deg + 360) % 360; draw(id); } },
        bg: (id, mode) => { const s = st[id]; if (s) { s.bg = mode; draw(id); } },
        round: (id, on) => { const s = st[id]; if (s) { s.round = !!on; draw(id); } },
        reset: (id) => { const s = st[id]; if (s) { s.zoom = 1; s.rot = 0; s.x = 0; s.y = 0; draw(id); } },
        // The edited logo as a data URL; steps the size/quality down until it fits maxChars.
        export: (id, size, mime, maxChars) => {
            const s = st[id]; if (!s) return null;
            let out = size, q = 0.92, url = null;
            for (let i = 0; i < 8; i++) {
                const c = document.createElement('canvas');
                c.width = c.height = out;
                const ctx = c.getContext('2d');
                if (s.bg === 'white' || mime === 'image/jpeg') { ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, out, out); }
                render(ctx, out, s);
                url = c.toDataURL(mime, q);
                if (!maxChars || url.length <= maxChars) return url;
                if (q > 0.6 && mime !== 'image/png') q -= 0.12; else out = Math.round(out * 0.82);
            }
            return url;
        },
        dispose: (id) => { delete st[id]; },
    };
})();
