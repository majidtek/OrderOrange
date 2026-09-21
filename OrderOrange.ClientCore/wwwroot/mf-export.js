// Saving a report and printing a report. Both start on the server or in Blazor and
// finish here, because only the browser can put a file on disk or open a print dialog.
window.mfExport = {
    // The workbook arrives as base64 over the SignalR circuit. A Blob keeps it out of
    // the URL — a 2 MB data: URI is refused outright by Safari and truncated by others.
    saveBase64(base64, filename, mime) {
        if (!base64) return false;
        try {
            const bin = atob(base64);
            const bytes = new Uint8Array(bin.length);
            for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            const blob = new Blob([bytes], { type: mime || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename || 'report.xlsx';
            // Must be in the document for Firefox to honour the click.
            document.body.appendChild(a);
            a.click();
            a.remove();
            // Revoked on the next tick: too early and the download never starts.
            setTimeout(() => URL.revokeObjectURL(url), 4000);
            return true;
        } catch (e) {
            console.error('mfExport.saveBase64', e);
            return false;
        }
    },

    // A file the SERVER builds on demand (the guest's invoice PDF): fetched here so the
    // page can show progress while it is made, then handed to the browser as a download.
    // A plain <a target=_blank> to the same URL left a blank tab for 20+ seconds with no
    // sign of life (Majed 2026-09-07: "download invoice pdf not working, no progress").
    // `progress` (optional) is a .NET object with PdfProgress(int): -1 while the server
    // is still making the file, then 0–100 as the bytes arrive.
    async fetchAndSave(url, filename, mime, progress) {
        const report = async pct => { if (!progress) return; try { await progress.invokeMethodAsync('PdfProgress', pct); } catch (e) { } };
        try {
            await report(-1);
            const res = await fetch(url, { credentials: 'omit', cache: 'no-store' });
            if (!res.ok) return false;
            const type = mime || res.headers.get('Content-Type') || 'application/octet-stream';
            const total = parseInt(res.headers.get('Content-Length') || '0', 10);
            let blob;
            if (res.body && res.body.getReader) {
                const reader = res.body.getReader(); const chunks = []; let got = 0, lastPct = -1;
                while (true) {
                    const { done, value } = await reader.read();
                    if (done) break;
                    chunks.push(value); got += value.length;
                    const pct = total > 0 ? Math.min(99, Math.round(got * 100 / total)) : -1;
                    if (pct !== lastPct) { lastPct = pct; await report(pct); }
                }
                blob = new Blob(chunks, { type });
            } else {
                blob = await res.blob();
            }
            await report(100);
            const link = URL.createObjectURL(blob.type === type ? blob : new Blob([blob], { type }));
            const a = document.createElement('a');
            a.href = link;
            a.download = filename || 'file';
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(link), 60000);
            return true;
        } catch (e) {
            console.error('mfExport.fetchAndSave', e);
            return false;
        }
    },

    // Blazor has just rendered the print-only sheet into the DOM. Give the browser one
    // frame to lay it out before print() freezes it, or the first page comes out blank.
    print() {
        return new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(() => {
                    try { window.print(); } catch (e) { console.error('mfExport.print', e); }
                    resolve(true);
                });
            });
        });
    },
};

// ---------- Direct printing (Settings: "print direct") ----------
// The POS drops the invoice page into an invisible frame; that page, seeing ?print=1,
// prints itself. On a till whose Chrome runs --kiosk-printing this is fully silent;
// anywhere else the print dialog opens already aimed at the default printer.
window.mfPrint = {
    direct(url) {
        const frame = document.createElement('iframe');
        frame.style.cssText = 'position:fixed;width:0;height:0;border:0;visibility:hidden';
        frame.src = url;
        document.body.appendChild(frame);
        // The frame has done its job long before this; the timer just tidies up.
        setTimeout(() => frame.remove(), 120000);
    },
    auto() {
        // A beat for the logo and fonts to land on screen before they land on paper.
        setTimeout(() => { try { window.focus(); window.print(); } catch { } }, 600);
    },
};

// ---------- Android direct printing (RawBT bridge) ----------
// An Android browser has no kiosk-printing: window.print() ALWAYS shows the sheet.
// So on Android the receipt goes out as raw ESC/POS bytes through the RawBT app
// (free, one-time setup with the shop's own printer): the page draws the receipt
// to a canvas, packs it as a printer raster, and hands it over. No dialog at all.
(function () {
    const isAndroid = /Android/i.test(navigator.userAgent);

    // Canvas -> ESC/POS raster (GS v 0), 1 bit per pixel, feed + cut at the end.
    function escposRaster(canvas) {
        const ctx = canvas.getContext('2d');
        const w = canvas.width, h = canvas.height;
        const img = ctx.getImageData(0, 0, w, h).data;
        const rowBytes = Math.ceil(w / 8);
        const out = [0x1b, 0x40, 0x1d, 0x76, 0x30, 0x00,
            rowBytes & 255, (rowBytes >> 8) & 255, h & 255, (h >> 8) & 255];
        for (let y = 0; y < h; y++) {
            for (let xb = 0; xb < rowBytes; xb++) {
                let b = 0;
                for (let bit = 0; bit < 8; bit++) {
                    const x = xb * 8 + bit;
                    if (x < w) {
                        const i = (y * w + x) * 4;
                        const lum = 0.299 * img[i] + 0.587 * img[i + 1] + 0.114 * img[i + 2];
                        if (img[i + 3] > 128 && lum < 165) b |= (0x80 >> bit);
                    }
                }
                out.push(b);
            }
        }
        out.push(0x0a, 0x0a, 0x0a, 0x0a, 0x1d, 0x56, 0x01);   // feed, partial cut
        return out;
    }

    async function rawbtPrint() {
        const slip = document.querySelector('.rcpt');
        if (!slip || typeof html2canvas === 'undefined') { window.print(); return; }
        // 576 dots is the printable width of an 80mm thermal head; 58mm heads take 384.
        const dots = slip.classList.contains('p58') ? 384 : 576;
        const canvas = await html2canvas(slip, {
            backgroundColor: '#ffffff',
            scale: dots / slip.offsetWidth,
            logging: false,
        });
        const bytes = escposRaster(canvas);
        let bin = '';
        for (let i = 0; i < bytes.length; i += 8192)
            bin += String.fromCharCode.apply(null, bytes.slice(i, i + 8192));
        // RawBT registers this scheme; Android asks once "open with RawBT?" and
        // remembers. From then on the receipt just comes out of the printer.
        window.location.href = 'rawbt:base64,' + btoa(bin);
    }

    const previousAuto = window.mfPrint && window.mfPrint.auto;
    if (window.mfPrint) {
        window.mfPrint.auto = function () {
            if (!isAndroid) { previousAuto && previousAuto(); return; }
            setTimeout(() => { rawbtPrint().catch(() => { try { window.print(); } catch { } }); }, 600);
        };
        window.mfPrint._escposRaster = escposRaster;   // reachable for tests
        window.mfPrint._rawbt = rawbtPrint;
    }
})();

// The receipt renders in a HIDDEN frame, and Android refuses app-scheme launches
// from frames — so the frame hands the printer bytes UP, and the visible page
// (the POS itself) fires them at RawBT.
(function () {
    if (!window.mfPrint) return;
    const inFrame = (() => { try { return window.parent !== window; } catch { return true; } })();

    const coreRawbt = window.mfPrint._rawbt;
    window.mfPrint._rawbt = async function () {
        const slip = document.querySelector('.rcpt');
        if (!slip || typeof html2canvas === 'undefined') { window.print(); return; }
        const dots = slip.classList.contains('p58') ? 384 : 576;
        const canvas = await html2canvas(slip, { backgroundColor: '#ffffff', scale: dots / slip.offsetWidth, logging: false });
        const bytes = window.mfPrint._escposRaster(canvas);
        let bin = '';
        for (let i = 0; i < bytes.length; i += 8192)
            bin += String.fromCharCode.apply(null, bytes.slice(i, i + 8192));
        const b64 = btoa(bin);
        if (inFrame) window.parent.postMessage({ __ooRawbt: b64 }, '*');
        else window.location.href = 'rawbt:base64,' + b64;
    };
    void coreRawbt;

    if (!inFrame) {
        window.addEventListener('message', e => {
            if (e.data && typeof e.data.__ooRawbt === 'string' && e.data.__ooRawbt.length < 2000000)
                window.location.href = 'rawbt:base64,' + e.data.__ooRawbt;
        });
    }
})();

// The Android path, straightened out: the POS renders the receipt on its OWN page
// (no hidden frame, no second circuit) and fires RawBT at once — Android only lets
// a page launch an app close to the user's tap, and the frame round-trip spent
// that window. This block is the authority; earlier auto()s defer to it.
(function () {
    if (!window.mfPrint) return;
    window.mfPrint.isAndroid = () => /Android/i.test(navigator.userAgent);
    window.mfPrint.rawbtNow = () => window.mfPrint._rawbt();
    const base = window.mfPrint._rawbt;
    // auto() (the hidden-frame path) must use the CURRENT handler, not a stale one.
    window.mfPrint.auto = function () {
        setTimeout(() => {
            try {
                if (window.mfPrint.isAndroid()) window.mfPrint._rawbt().catch(() => { });
                else { window.focus(); window.print(); }
            } catch { }
        }, 600);
    };
    void base;
})();

// Print quality pass: render the slip at DOUBLE resolution and let the browser
// downsample before thresholding — edges come out solid instead of ragged — and
// darken the cut so anti-aliased strokes survive as black. This block replaces
// the raster front-end; the escposRaster packer stays as is.
(function () {
    if (!window.mfPrint || !window.mfPrint._escposRaster) return;
    const inFrame = (() => { try { return window.parent !== window; } catch { return true; } })();

    window.mfPrint._rawbt = async function () {
        const slip = document.querySelector('.rcpt');
        if (!slip || typeof html2canvas === 'undefined') { window.print(); return; }
        const dots = slip.classList.contains('p58') ? 384 : 576;

        // 2x supersample, then downscale to the printer's true width.
        const big = await html2canvas(slip, {
            backgroundColor: '#ffffff',
            scale: (dots * 2) / slip.offsetWidth,
            logging: false,
        });
        const canvas = document.createElement('canvas');
        canvas.width = dots;
        canvas.height = Math.round(big.height / 2);
        const ctx = canvas.getContext('2d');
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(big, 0, 0, canvas.width, canvas.height);

        // Darker threshold: a stroke that is even HALF ink prints as ink.
        const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const d = img.data;
        for (let i = 0; i < d.length; i += 4) {
            const lum = 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2];
            const v = lum < 200 ? 0 : 255;
            d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255;
        }
        ctx.putImageData(img, 0, 0);

        const bytes = window.mfPrint._escposRaster(canvas);
        let bin = '';
        for (let i = 0; i < bytes.length; i += 8192)
            bin += String.fromCharCode.apply(null, bytes.slice(i, i + 8192));
        const b64 = btoa(bin);
        if (inFrame) window.parent.postMessage({ __ooRawbt: b64 }, '*');
        else window.location.href = 'rawbt:base64,' + b64;
    };
})();

// Ink pass: the slip is photographed in BOLD, and every stroke is thickened by one
// dot before packing — thermal heads starve hairlines, so we feed them meat.
(function () {
    if (!window.mfPrint || !window.mfPrint._escposRaster) return;
    const inFrame = (() => { try { return window.parent !== window; } catch { return true; } })();

    window.mfPrint._rawbt = async function () {
        const slip = document.querySelector('.rcpt');
        if (!slip || typeof html2canvas === 'undefined') { window.print(); return; }
        const dots = slip.classList.contains('p58') ? 384 : 576;

        const big = await html2canvas(slip, {
            backgroundColor: '#ffffff',
            scale: (dots * 2) / slip.offsetWidth,
            logging: false,
            onclone: doc => doc.querySelectorAll('.rcpt').forEach(r => r.classList.add('rawbt-bold')),
        });
        const canvas = document.createElement('canvas');
        canvas.width = dots;
        canvas.height = Math.round(big.height / 2);
        const ctx = canvas.getContext('2d');
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(big, 0, 0, canvas.width, canvas.height);

        const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const d = img.data, w = canvas.width, h = canvas.height;
        // Threshold to pure black/white first…
        const black = new Uint8Array(w * h);
        for (let p = 0, i = 0; p < w * h; p++, i += 4) {
            const lum = 0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2];
            black[p] = lum < 200 ? 1 : 0;
        }
        // …then grow every stroke by one dot to the right and one down.
        for (let y = h - 1; y >= 0; y--) {
            for (let x = w - 1; x >= 0; x--) {
                const p = y * w + x;
                if (!black[p]) {
                    if ((x > 0 && black[p - 1]) || (y > 0 && black[p - w])) black[p] = 2;
                }
            }
        }
        for (let p = 0, i = 0; p < w * h; p++, i += 4) {
            const v = black[p] ? 0 : 255;
            d[i] = d[i + 1] = d[i + 2] = v; d[i + 3] = 255;
        }
        ctx.putImageData(img, 0, 0);

        const bytes = window.mfPrint._escposRaster(canvas);
        let bin = '';
        for (let i = 0; i < bytes.length; i += 8192)
            bin += String.fromCharCode.apply(null, bytes.slice(i, i + 8192));
        const b64 = btoa(bin);
        if (inFrame) window.parent.postMessage({ __ooRawbt: b64 }, '*');
        else window.location.href = 'rawbt:base64,' + b64;
    };
})();

// ---------- Browser notifications (partner portal) ----------
// New orders, guest chats, stock alarms — the bell's items, surfaced as SYSTEM
// notifications so a backgrounded tab (or a tablet on the counter) still rings.
// Android Chrome only allows notifications through a service worker; desktop
// takes them either way.
window.mfNotify = {
    async init() {
        try {
            if (!('Notification' in window)) return 'unsupported';
            if ('serviceWorker' in navigator) {
                try { await navigator.serviceWorker.register('/notify-sw.js'); } catch { }
            }
            if (Notification.permission === 'default') return await Notification.requestPermission();
            return Notification.permission;
        } catch { return 'error'; }
    },
    async show(title, body, url) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') return;
            const opts = {
                body: body || '',
                icon: '/_content/OrderOrange.ClientCore/logo.svg',
                badge: '/_content/OrderOrange.ClientCore/logo.svg',
                tag: 'oo-' + (title || '') + (body || ''),
                data: { url: url || '/' },
            };
            if ('serviceWorker' in navigator) {
                const reg = await navigator.serviceWorker.getRegistration();
                if (reg) { await reg.showNotification(title, opts); return; }
            }
            const n = new Notification(title, opts);
            n.onclick = () => { try { window.focus(); if (url) location.href = url; n.close(); } catch { } };
        } catch { }
    },
};

// ---------- Web Push subscription (partner) ----------
// Called with the store's VAPID public key once permission is granted: subscribes this
// browser and returns the subscription for the API to keep. From then on the SERVER
// rings the device — no open tab required.
window.mfNotify.subscribePush = async function (vapidPublicKey) {
    try {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) return null;
        if (Notification.permission !== 'granted') return null;
        const reg = await navigator.serviceWorker.ready;
        const toKey = (s) => {
            const pad = '='.repeat((4 - s.length % 4) % 4);
            const raw = atob((s + pad).replace(/-/g, '+').replace(/_/g, '/'));
            return Uint8Array.from(raw, c => c.charCodeAt(0));
        };
        let sub = await reg.pushManager.getSubscription();
        sub ??= await reg.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: toKey(vapidPublicKey),
        });
        const j = sub.toJSON();
        return { endpoint: sub.endpoint, p256dh: j.keys.p256dh, auth: j.keys.auth };
    } catch (e) { return null; }
};
