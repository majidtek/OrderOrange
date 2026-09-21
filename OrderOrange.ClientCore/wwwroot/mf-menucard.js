// Draws a menu poster for Instagram in the browser and hands back a JPEG data URL.
// Canvas rather than the server: the fonts, the shaping of Arabic and Persian names and the
// emoji all come free from the browser that is already showing them.
window.mfMenuCard = {
    /**
     * @param {object} p
     *   title      shop name
     *   subtitle   line under it (area, phone…)
     *   footer     the address printed at the bottom
     *   logo       data URI or URL, optional
     *   items      [{ name, price, note }]
     *   theme      'orange' | 'dark' | 'light'
     *   rtl        true for Arabic/Persian/Urdu
     */
    // Hands the drawn picture to the browser as a file.
    download(dataUrl, name) {
        const a = document.createElement('a');
        a.href = dataUrl; a.download = name || 'menu-poster.jpg';
        document.body.appendChild(a); a.click(); a.remove();
    },

    render: async function (p) {
        const W = 1080, H = 1350;
        const c = document.createElement('canvas');
        c.width = W; c.height = H;
        const g = c.getContext('2d');

        const themes = {
            orange: { bg1: '#FF7A2A', bg2: '#C43B08', ink: '#FFFFFF', soft: 'rgba(255,255,255,.82)', card: 'rgba(255,255,255,.12)', line: 'rgba(255,255,255,.25)', price: '#FFD9BF' },
            dark: { bg1: '#241C15', bg2: '#12100D', ink: '#FFF7F0', soft: 'rgba(255,247,240,.72)', card: 'rgba(255,255,255,.06)', line: 'rgba(255,255,255,.14)', price: '#FF9A4D' },
            light: { bg1: '#FFFFFF', bg2: '#FBF1E6', ink: '#1C1712', soft: '#6B5D4F', card: 'rgba(255,90,0,.06)', line: 'rgba(28,23,18,.10)', price: '#C44300' }
        };
        const t = themes[p.theme] || themes.orange;

        // ground
        const grad = g.createLinearGradient(0, 0, W, H);
        grad.addColorStop(0, t.bg1); grad.addColorStop(1, t.bg2);
        g.fillStyle = grad; g.fillRect(0, 0, W, H);

        // a soft glow so a flat gradient does not look printed
        const glow = g.createRadialGradient(W * 0.85, -80, 40, W * 0.85, -80, 700);
        glow.addColorStop(0, 'rgba(255,255,255,.22)'); glow.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = glow; g.fillRect(0, 0, W, H);

        const dir = p.rtl ? 'rtl' : 'ltr';
        g.direction = dir;
        const L = p.rtl ? W - 80 : 80;            // text origin on the reading side
        const R = p.rtl ? 80 : W - 80;            // the far side (prices)
        g.textAlign = p.rtl ? 'right' : 'left';

        // logo
        let y = 96;
        if (p.logo) {
            try {
                const img = await this._load(p.logo);
                const s = 92;
                const x = p.rtl ? W - 80 - s : 80;
                this._rounded(g, x, y - 18, s, s, 22);
                g.save(); g.clip();
                g.fillStyle = '#fff'; g.fillRect(x, y - 18, s, s);
                g.drawImage(img, x, y - 18, s, s);
                g.restore();
                y += 96;
            } catch { /* a missing logo is not a reason to fail */ }
        }

        // title
        g.fillStyle = t.ink;
        g.font = '800 62px Manrope, system-ui, "Segoe UI", Arial, sans-serif';
        y = this._wrap(g, p.title || '', L, y + 46, W - 160, 66, 2);
        if (p.subtitle) {
            g.fillStyle = t.soft;
            g.font = '600 28px Manrope, system-ui, Arial, sans-serif';
            g.fillText(p.subtitle, L, y + 14);
            y += 46;
        }

        // rule
        y += 22;
        g.strokeStyle = t.line; g.lineWidth = 2;
        g.beginPath(); g.moveTo(80, y); g.lineTo(W - 80, y); g.stroke();
        y += 26;

        // items: name on the reading side, price on the other, a dotted lead between them
        const items = (p.items || []).slice(0, 12);
        // Rows share whatever is left between the rule and the footer, so a poster of four
        // dishes fills the frame as honestly as one of twelve.
        const room = (H - 150) - y;
        const rowH = Math.max(72, Math.min(150, Math.floor(room / Math.max(1, items.length))));
        for (const it of items) {
            const top = y;
            this._rounded(g, 68, top, W - 136, rowH - 12, 18);
            g.fillStyle = t.card; g.fill();

            g.fillStyle = t.ink;
            g.font = '700 ' + (rowH > 118 ? 40 : rowH > 90 ? 36 : 32) + 'px Manrope, system-ui, Arial, sans-serif';
            const nameY = top + (rowH - 12) / 2 + (it.note ? -6 : 10);
            const priceText = it.price || '';
            g.textAlign = p.rtl ? 'right' : 'left';
            this._clip(g, it.name || '', L + (p.rtl ? -20 : 20), nameY, W - 300);

            if (it.note) {
                g.fillStyle = t.soft;
                g.font = '500 22px Manrope, system-ui, Arial, sans-serif';
                this._clip(g, it.note, L + (p.rtl ? -20 : 20), nameY + 30, W - 320);
            }

            g.fillStyle = t.price;
            g.font = '800 ' + (rowH > 118 ? 40 : rowH > 90 ? 36 : 32) + 'px Manrope, system-ui, Arial, sans-serif';
            g.textAlign = p.rtl ? 'left' : 'right';
            g.fillText(priceText, R + (p.rtl ? 20 : -20), top + (rowH - 12) / 2 + 10);

            y += rowH;
            if (y > H - 200) break;
        }

        // footer
        g.textAlign = 'center';
        g.fillStyle = t.soft;
        g.font = '700 30px Manrope, system-ui, Arial, sans-serif';
        g.fillText(p.footer || 'orderorange.com', W / 2, H - 74);

        return c.toDataURL('image/jpeg', 0.92);
    },

    _load: function (src) {
        return new Promise((ok, no) => {
            const i = new Image();
            i.crossOrigin = 'anonymous';
            i.onload = () => ok(i);
            i.onerror = no;
            i.src = src;
        });
    },

    _rounded: function (g, x, y, w, h, r) {
        g.beginPath();
        g.moveTo(x + r, y);
        g.arcTo(x + w, y, x + w, y + h, r);
        g.arcTo(x + w, y + h, x, y + h, r);
        g.arcTo(x, y + h, x, y, r);
        g.arcTo(x, y, x + w, y, r);
        g.closePath();
    },

    /** Draws text, shrinking it until it fits the width — a long dish name must not spill. */
    _clip: function (g, text, x, y, max) {
        const original = g.font;
        let size = parseInt(original.match(/(\d+)px/)[1], 10);
        while (g.measureText(text).width > max && size > 16) {
            size -= 2;
            g.font = original.replace(/\d+px/, size + 'px');
        }
        g.fillText(text, x, y);
        g.font = original;
    },

    /** Word-wraps a headline over at most `lines` lines; returns the new y. */
    _wrap: function (g, text, x, y, max, lh, lines) {
        const words = String(text).split(/\s+/);
        let line = '', drawn = 0;
        for (let i = 0; i < words.length; i++) {
            const test = line ? line + ' ' + words[i] : words[i];
            if (g.measureText(test).width > max && line) {
                g.fillText(line, x, y); y += lh; line = words[i]; drawn++;
                if (drawn >= lines - 1) break;
            } else line = test;
        }
        if (line) { g.fillText(line, x, y); y += lh; }
        return y;
    }
};
