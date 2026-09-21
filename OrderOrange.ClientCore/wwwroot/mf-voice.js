// The chat's little signal: a two-tone ding for a new message. Pure WebAudio,
// no sound file to load or cache-bust.
window.mfDing = () => {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = 'sine';
        osc.frequency.setValueAtTime(880, ctx.currentTime);
        osc.frequency.setValueAtTime(1174, ctx.currentTime + 0.12);
        gain.gain.setValueAtTime(0.0001, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.3, ctx.currentTime + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.5);
        osc.start();
        osc.stop(ctx.currentTime + 0.55);
    } catch { /* muted tab or autoplay policy — the badge still shows */ }
};

// Big payloads cross the Blazor bridge in 50KB slices — one huge interop string
// trips SignalR's message cap, fifty small ones stroll through.
window.mfBlob = {
    _store: {},
    _seq: 1,
    put(data) {
        const id = 'b' + this._seq++;
        this._store[id] = data;
        return { id, size: data.length };
    },
    chunk(id, offset, len) {
        const d = this._store[id];
        return d ? d.substr(offset, len) : '';
    },
    end(id) { delete this._store[id]; },
};

// A file dialog that is CANCELLED fires no change event, so a picker that only
// listens for change never resolves — the caller's "uploading…" flag then stays
// on and the button is dead until the page reloads (Majed, 2026-09-06: "i can not
// upload file when i cancel dialog"). Modern browsers fire `cancel` on the input;
// older ones give nothing but the window regaining focus with no file chosen.
// Either way the promise settles with null and the button comes back.
function mfArmCancel(input, resolve) {
    let settled = false;
    const finish = v => { if (settled) return; settled = true; cleanup(); resolve(v); };
    const onCancel = () => finish(null);
    const onFocus = e => {
        // Only the WINDOW getting focus back means the dialog closed — a control inside
        // the page taking focus is ordinary life and must not cancel the pick.
        if (e.target !== window && e.target !== document) return;
        // The chosen file arrives a beat AFTER focus returns — give it that beat.
        setTimeout(() => { if (!input.files || !input.files.length) finish(null); }, 600);
    };
    const cleanup = () => {
        input.removeEventListener('cancel', onCancel);
        window.removeEventListener('focus', onFocus, true);
    };
    input.addEventListener('cancel', onCancel, { once: true });
    // Armed a tick late, so the focus shuffle of the click that opened us is not heard.
    setTimeout(() => { if (!settled) window.addEventListener('focus', onFocus, true); }, 0);
    return finish;
}

// Photo messages: pick, shrink on-device, park the data URL for chunked pickup.
// Recompresses until the result fits under ~2MB of real bytes.
window.mfImage = {
    // maxPx caps the longer edge (default 1600); product photos ask for 1024.
    pick(fromCamera, limitChars, maxPx) {
        return new Promise(settle => {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = 'image/*';
            if (fromCamera) input.capture = 'environment';
            const resolve = mfArmCancel(input, settle);
            input.onchange = () => {
                const file = input.files && input.files[0];
                if (!file) return resolve(null);
                // A 5MB+ original is a burden even to compress — refuse it loudly.
                if (file.size > 5242880) return resolve('huge');
                const img = new Image();
                img.onload = () => {
                    URL.revokeObjectURL(img.src);
                    const limit = limitChars || 2800000; // ≈2MB binary as base64 text unless told tighter
                    let max = maxPx > 0 ? maxPx : 1600, quality = 0.85, data = null;
                    for (let i = 0; i < 6; i++) {
                        let w = img.width, h = img.height;
                        if (Math.max(w, h) > max) {
                            const s = max / Math.max(w, h);
                            w = Math.round(w * s);
                            h = Math.round(h * s);
                        }
                        const canvas = document.createElement('canvas');
                        canvas.width = w;
                        canvas.height = h;
                        canvas.getContext('2d').drawImage(img, 0, 0, w, h);
                        data = canvas.toDataURL('image/jpeg', quality);
                        if (data.length <= limit) break;
                        max = Math.round(max * 0.75);
                        quality = Math.max(0.6, quality - 0.08);
                    }
                    if (!data || data.length > limit) return resolve('too-big');
                    // Park a copy OUTSIDE the app: if the camera app killed the page,
                    // the reloaded page finds the photo here and still sends it.
                    try { sessionStorage.setItem('mf-pending-img', data); } catch { }
                    resolve(JSON.stringify(window.mfBlob.put(data)));
                };
                img.onerror = () => resolve(null);
                img.src = URL.createObjectURL(file);
            };
            input.click();
        });
    },

    // The reloaded page asks: did a photo survive the camera round-trip?
    takePending() {
        try {
            const data = sessionStorage.getItem('mf-pending-img');
            if (!data) return null;
            sessionStorage.removeItem('mf-pending-img');
            return JSON.stringify(window.mfBlob.put(data));
        } catch { return null; }
    },

    clearPending() {
        try { sessionStorage.removeItem('mf-pending-img'); } catch { }
    },
};

// Voice notes for the table chat: MediaRecorder in, data URL out. HTTPS only —
// the browser will not open a microphone on plain HTTP, and neither should we.
window.mfVoice = {
    _rec: null,
    _chunks: [],

    async start() {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            this._chunks = [];
            const mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
                ? 'audio/webm;codecs=opus'
                : (MediaRecorder.isTypeSupported('audio/mp4') ? 'audio/mp4' : '');
            const rec = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
            rec.ondataavailable = e => { if (e.data.size > 0) this._chunks.push(e.data); };
            rec.start(250);
            this._rec = rec;
            return true;
        } catch {
            return false; // no permission, no mic, or not HTTPS
        }
    },

    stop() {
        return new Promise(resolve => {
            const rec = this._rec;
            this._rec = null;
            if (!rec) return resolve(null);
            rec.onstop = () => {
                rec.stream.getTracks().forEach(t => t.stop());
                const blob = new Blob(this._chunks, { type: rec.mimeType || 'audio/webm' });
                this._chunks = [];
                if (blob.size < 800) return resolve(null);          // a tap, not a message
                if (blob.size > 600000) return resolve('too-big');  // ~1 min of opus
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.readAsDataURL(blob);
            };
            try { rec.stop(); } catch { resolve(null); }
        });
    },

    cancel() {
        const rec = this._rec;
        this._rec = null;
        this._chunks = [];
        if (rec) {
            rec.onstop = null;
            rec.stream.getTracks().forEach(t => t.stop());
            try { rec.stop(); } catch { }
        }
    },
};

// Document attachments: any file up to 2MB, parked for chunked pickup.
window.mfFile = {
    pick() {
        return new Promise(settle => {
            const input = document.createElement('input');
            input.type = 'file';
            const resolve = mfArmCancel(input, settle);
            input.onchange = () => {
                const file = input.files && input.files[0];
                if (!file) return resolve(null);
                if (file.size > 2097152) return resolve('too-big');
                const reader = new FileReader();
                reader.onload = () => {
                    const parked = window.mfBlob.put(reader.result);
                    resolve(JSON.stringify({ id: parked.id, size: parked.size, name: file.name }));
                };
                reader.readAsDataURL(file);
            };
            input.click();
        });
    },
};
// In-page camera: live preview and shutter INSIDE the page, so the browser never
// yields to a camera app — no backgrounding, no reload, no lost photo.
window.mfCam = {
    open() {
        return new Promise(resolve => {
            navigator.mediaDevices.getUserMedia({
                video: { facingMode: 'environment', width: { ideal: 1920 }, height: { ideal: 1080 } },
                audio: false,
            }).then(stream => {
                const veil = document.createElement('div');
                veil.className = 'mfcam';
                const video = document.createElement('video');
                video.autoplay = true;
                video.playsInline = true;
                video.muted = true;
                video.srcObject = stream;
                const shoot = document.createElement('button');
                shoot.type = 'button';
                shoot.className = 'shoot';
                const close = document.createElement('button');
                close.type = 'button';
                close.className = 'close';
                close.textContent = '\u2715';
                const done = out => {
                    stream.getTracks().forEach(t => t.stop());
                    veil.remove();
                    resolve(out);
                };
                close.onclick = () => done(null);
                shoot.onclick = () => {
                    const limit = 2800000;
                    let max = 1600, quality = 0.85, data = null;
                    for (let i = 0; i < 6; i++) {
                        let w = video.videoWidth, h = video.videoHeight;
                        if (!w || !h) return;
                        if (Math.max(w, h) > max) {
                            const s = max / Math.max(w, h);
                            w = Math.round(w * s);
                            h = Math.round(h * s);
                        }
                        const canvas = document.createElement('canvas');
                        canvas.width = w;
                        canvas.height = h;
                        canvas.getContext('2d').drawImage(video, 0, 0, w, h);
                        data = canvas.toDataURL('image/jpeg', quality);
                        if (data.length <= limit) break;
                        max = Math.round(max * 0.75);
                        quality = Math.max(0.6, quality - 0.08);
                    }
                    done(data && data.length <= limit ? JSON.stringify(window.mfBlob.put(data)) : 'too-big');
                };
                veil.append(video, shoot, close);
                document.body.append(veil);
            }).catch(() => resolve('no-cam'));
        });
    },
};

// Dashboard numbers roll up from zero — a page that feels alive on arrival.
window.mfCount = {
    sweep() {
        document.querySelectorAll('[data-count]').forEach(el => {
            const target = parseFloat(el.getAttribute('data-count')) || 0;
            if (target <= 0) return;
            const t0 = performance.now(), dur = 750;
            const step = now => {
                const p = Math.min(1, (now - t0) / dur);
                el.textContent = Math.round(target * (1 - Math.pow(1 - p, 3)));
                if (p < 1) requestAnimationFrame(step);
            };
            requestAnimationFrame(step);
        });
    },
};

// The map picker: GOOGLE MAPS tiles everywhere (Leaflet is only the thin pane
// of glass that makes them touchable), loaded only when someone actually
// needs to drop a pin. Tap or drag the marker, confirm, get "lat,lng" back.
window.mfMap = {
    _lib: null,
    _load() {
        if (this._lib) return this._lib;
        this._lib = new Promise((resolve, reject) => {
            const css = document.createElement('link');
            css.rel = 'stylesheet';
            css.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
            document.head.append(css);
            const script = document.createElement('script');
            script.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
            script.onload = () => resolve();
            script.onerror = () => { this._lib = null; reject(); };
            document.head.append(script);
        });
        return this._lib;
    },
    /// A normal map pin — the classic teardrop — just dressed in OrderOrange orange.
    _icon() {
        return L.divIcon({
            className: 'mf-pin-wrap',
            html: '<svg width="34" height="48" viewBox="0 0 34 48">' +
                '<path d="M17 1C8.2 1 1 8.2 1 17c0 11.9 16 30 16 30s16-18.1 16-30C33 8.2 25.8 1 17 1z" ' +
                'fill="#f4511e" stroke="#c73a10" stroke-width="1"/>' +
                '<circle cx="17" cy="17" r="6" fill="#fff"/></svg>',
            iconSize: [34, 48],
            iconAnchor: [17, 47],
        });
    },

    /// The customer's address map: tap to drop the pin, the form fills by hand.
    /// (The dialog has always called init/locate/dispose — these ARE that API.)
    async init(elId, _apiKey, lat, lng, ref) {
        try { await this._load(); } catch { return false; }
        const el = document.getElementById(elId);
        if (!el) return false;
        if (el._map) { try { el._map.remove(); } catch { } el._map = null; }
        const has = typeof lat === 'number' && typeof lng === 'number' && (lat || lng);
        const start = has ? [lat, lng] : [23.588, 58.4059]; // Muscat until told better
        const map = L.map(el, { zoomControl: false, attributionControl: false })
            .setView(start, has ? 16 : 11);
        L.tileLayer('https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}',
            { subdomains: ['mt0', 'mt1', 'mt2', 'mt3'], maxZoom: 20 }).addTo(map);
        el.style.position = 'relative';
        const credit = document.createElement('span');
        credit.textContent = '© Google';
        credit.style.cssText = 'position:absolute;bottom:6px;left:8px;z-index:1000;font-size:10px;' +
            'color:#5c5348;background:rgba(255,255,255,.75);border-radius:6px;padding:1px 6px;pointer-events:none;';
        el.appendChild(credit);
        let marker = has ? L.marker(start, { icon: this._icon() }).addTo(map) : null;
        map.on('click', e => {
            if (!marker) marker = L.marker(e.latlng, { icon: this._icon() }).addTo(map);
            else marker.setLatLng(e.latlng);
            ref.invokeMethodAsync('OnMapPick', e.latlng.lat, e.latlng.lng, null, null);
        });
        el._map = map;
        // the dialog is still animating open — measure again once it settles
        setTimeout(() => map.invalidateSize(), 150);
        setTimeout(() => map.invalidateSize(), 500);
        // A NEW address starts where the customer is standing: ask once, pin it.
        if (!has && navigator.geolocation) {
            navigator.geolocation.getCurrentPosition(p => {
                const here = L.latLng(p.coords.latitude, p.coords.longitude);
                map.setView(here, 16);
                map.fire('click', { latlng: here });
            }, () => { }, { timeout: 6000 });
        }
        return true;
    },

    locate(elId) {
        const el = document.getElementById(elId);
        const map = el && el._map;
        if (!map || !navigator.geolocation) return false;
        return new Promise(resolve => navigator.geolocation.getCurrentPosition(p => {
            const here = L.latLng(p.coords.latitude, p.coords.longitude);
            map.setView(here, 16);
            map.fire('click', { latlng: here }); // same path as a tap: pin + report
            resolve(true);
        }, () => resolve(false), { timeout: 6000 }));
    },

    dispose(elId) {
        const el = document.getElementById(elId);
        if (el && el._map) { try { el._map.remove(); } catch { } el._map = null; }
    },

    /// The customer's live tracking map: the destination pinned, the rider following.
    ///
    /// This lives here rather than in the customer app's own mf-maps.js because that
    /// file also assigns window.mfMap — and it is loaded FIRST, so this object replaced
    /// it wholesale and took `live`/`updateDriver` down with it. The tracking map was
    /// silently dead as a result. Reimplemented on Leaflet so the whole app draws maps
    /// one way, with no Maps JS key to keep alive.
    async live(elId, _apiKey, destLat, destLng) {
        try { await this._load(); } catch { return false; }
        const el = document.getElementById(elId);
        if (!el) return false;
        if (el._map) { try { el._map.remove(); } catch { } el._map = null; }

        const hasDest = typeof destLat === 'number' && typeof destLng === 'number' && (destLat || destLng);
        const dest = hasDest ? [destLat, destLng] : [23.588, 58.4059];
        const map = L.map(el, { zoomControl: false, attributionControl: false }).setView(dest, 14);
        L.tileLayer('https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}',
            { subdomains: ['mt0', 'mt1', 'mt2', 'mt3'], maxZoom: 20 }).addTo(map);

        if (hasDest) el._dest = L.marker(dest, { icon: this._icon() }).addTo(map);
        el._map = map;

        // Leaflet measures the container on creation; inside a panel that was still
        // laying out, that measurement is zero and the tiles never paint.
        setTimeout(() => { try { map.invalidateSize(); } catch { } }, 60);
        return true;
    },

    /// Moves the rider. Called on every poll, so it creates the marker once and then
    /// only repositions it — and keeps both ends of the journey in view.
    updateDriver(elId, lat, lng) {
        const el = document.getElementById(elId);
        const map = el && el._map;
        if (!map || typeof lat !== 'number' || typeof lng !== 'number') return false;

        const here = L.latLng(lat, lng);
        if (el._rider) {
            el._rider.setLatLng(here);
        } else {
            el._rider = L.marker(here, {
                icon: L.divIcon({
                    className: 'mf-rider-wrap',
                    html: '<div style="font-size:26px;line-height:1;filter:drop-shadow(0 2px 4px rgba(0,0,0,.35))">🛵</div>',
                    iconSize: [30, 30],
                    iconAnchor: [15, 15],
                }),
            }).addTo(map);
        }

        try {
            map.fitBounds(el._dest ? L.latLngBounds([here, el._dest.getLatLng()]) : L.latLngBounds([here, here]),
                { padding: [40, 40], maxZoom: 16 });
        } catch { map.setView(here, 15); }
        return true;
    },

    /// A quiet little proof under the form: the chosen spot, pinned, not draggable.
    async preview(id, lat, lng) {
        try { await this._load(); } catch { return; }
        const el = document.getElementById(id);
        if (!el) return;
        if (el._map) { try { el._map.remove(); } catch { } el._map = null; }
        const map = L.map(el, {
            zoomControl: false, dragging: false, scrollWheelZoom: false,
            doubleClickZoom: false, boxZoom: false, keyboard: false,
            attributionControl: false,
        }).setView([lat, lng], 15);
        L.tileLayer('https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}',
            { subdomains: ['mt0', 'mt1', 'mt2', 'mt3'], maxZoom: 20 }).addTo(map);
        L.marker([lat, lng], { icon: this._icon() }).addTo(map);
        el._map = map;
        setTimeout(() => map.invalidateSize(), 80);
    },

    async open(lat, lng, confirmText, searchText, linkText) {
        try { await this._load(); } catch { return null; }
        return new Promise(resolve => {
            const veil = document.createElement('div');
            veil.className = 'mfmap';
            const box = document.createElement('div');
            box.className = 'box';
            const mapEl = document.createElement('div');
            mapEl.className = 'map';

            // The toolbox: close + search share the first row, results slide out
            // beneath them, the link box stands last. One column, nothing overlaps.
            const top = document.createElement('div');
            top.className = 'mtop';
            const row = document.createElement('div');
            row.className = 'mrow';
            const x = document.createElement('button');
            x.type = 'button';
            x.className = 'mx';
            x.textContent = '✕';
            const q = document.createElement('input');
            q.type = 'text';
            q.className = 'mq';
            q.placeholder = searchText || 'Search…';
            row.append(x, q);

            // Search results: powered by Google's Places API, through the site.
            const res = document.createElement('div');
            res.className = 'mres';

            // A pasted Google Maps link jumps the pin straight to its spot.
            const lk = document.createElement('input');
            lk.type = 'text';
            lk.className = 'mlk';
            lk.placeholder = linkText || 'Google Maps link…';

            const ok = document.createElement('button');
            ok.type = 'button';
            ok.className = 'ok';
            ok.textContent = '✓ ' + (confirmText || 'OK');
            top.append(row, res, lk);
            box.append(mapEl, top, ok);
            veil.append(box);
            document.body.append(veil);

            const has = typeof lat === 'number' && typeof lng === 'number' && (lat || lng);
            const start = has ? [lat, lng] : [23.588, 58.4059]; // Muscat until told better
            const map = L.map(mapEl, { zoomControl: false, attributionControl: false }).setView(start, has ? 16 : 11);
            const credit = document.createElement('span');
            credit.className = 'credit';
            credit.textContent = '© Google';
            box.append(credit);
            // Google's own tiles: the map people already know, street names and all.
            L.tileLayer('https://{s}.google.com/vt/lyrs=m&x={x}&y={y}&z={z}', {
                subdomains: ['mt0', 'mt1', 'mt2', 'mt3'],
                attribution: '© Google',
                maxZoom: 20,
            }).addTo(map);
            // Everyone picks like a taxi app: the pin stands still in the middle
            // and the MAP is what moves — phone and PC alike.
            const centerMode = true;
            let marker = null;
            if (centerMode) {
                const pinEl = document.createElement('div');
                pinEl.className = 'mf-centerpin';
                pinEl.innerHTML = '<svg width="34" height="48" viewBox="0 0 34 48">' +
                    '<path d="M17 1C8.2 1 1 8.2 1 17c0 11.9 16 30 16 30s16-18.1 16-30C33 8.2 25.8 1 17 1z" ' +
                    'fill="#f4511e" stroke="#c73a10" stroke-width="1"/>' +
                    '<circle cx="17" cy="17" r="6" fill="#fff"/></svg>';
                box.append(pinEl);
            } else {
                marker = L.marker(start, { draggable: true, icon: this._icon() }).addTo(map);
                map.on('click', e => marker.setLatLng(e.latlng));
            }
            if (!has && navigator.geolocation) {
                navigator.geolocation.getCurrentPosition(p => {
                    const here = [p.coords.latitude, p.coords.longitude];
                    map.setView(here, 16);
                    if (marker) marker.setLatLng(here);
                }, () => { }, { timeout: 4000 });
            }

            const search = async () => {
                const text = q.value.trim();
                if (text.length < 2) return;
                res.innerHTML = '<i>…</i>';
                try {
                    const lang = document.documentElement.lang || 'en';
                    const r = await fetch('/placesearch?lang=' + encodeURIComponent(lang) +
                        '&ft=' + encodeURIComponent(window.ooFT || '') +
                        '&q=' + encodeURIComponent(text));
                    const list = await r.json();
                    res.innerHTML = '';
                    if (!list.length) { res.innerHTML = '<i>—</i>'; return; }
                    list.forEach((place, i) => {
                        const b = document.createElement('button');
                        b.type = 'button';
                        b.className = 'hit';
                        b.style.animationDelay = (i * 45) + 'ms';
                        const ic = document.createElement('span');
                        ic.className = 'pin';
                        ic.textContent = '📍';
                        const tx = document.createElement('span');
                        tx.className = 'tx';
                        const nm = document.createElement('b');
                        nm.textContent = place.name;
                        tx.append(nm);
                        if (place.address) {
                            const ad = document.createElement('small');
                            ad.textContent = place.address;
                            tx.append(ad);
                        }
                        b.append(ic, tx);
                        b.onclick = () => {
                            map.setView([place.lat, place.lng], 17);
                            if (marker) marker.setLatLng([place.lat, place.lng]);
                            res.innerHTML = '';
                            q.value = '';
                        };
                        res.append(b);
                    });
                } catch { res.innerHTML = ''; }
            };
            let searchTimer = null;
            q.addEventListener('input', () => {
                clearTimeout(searchTimer);
                if (q.value.trim().length >= 3) searchTimer = setTimeout(search, 450);
                else res.innerHTML = '';
            });
            q.addEventListener('keydown', e => {
                if (e.key === 'Enter') { e.preventDefault(); clearTimeout(searchTimer); search(); }
                e.stopPropagation();
            });

            const jump = ll => {
                map.setView(ll, 17);
                if (marker) marker.setLatLng(ll);
                // Found: just move the map — the input stays a plain input.
                lk.classList.remove('busy', 'bad');
            };
            const tryLink = async (strict) => {
                const raw = lk.value.trim();
                if (!raw) return;
                const m = raw.match(/@(-?\d{1,2}\.\d+),(-?\d{1,3}\.\d+)/) ||
                          raw.match(/[?&](?:q|ll|query|destination)=(-?\d{1,2}\.\d+)(?:,|%2C)(-?\d{1,3}\.\d+)/) ||
                          raw.match(/!3d(-?\d{1,2}\.\d+)!4d(-?\d{1,3}\.\d+)/);
                if (m) return jump([parseFloat(m[1]), parseFloat(m[2])]);
                // A short goo.gl link carries no coordinates — our own server unrolls it.
                if (/^https?:\/\//i.test(raw)) {
                    lk.classList.add('busy');
                    try {
                        const r = await fetch('/maplink?ft=' + encodeURIComponent(window.ooFT || '') + '&url=' + encodeURIComponent(raw));
                        if (r.ok) {
                            const p = await r.json();
                            return jump([p.lat, p.lng]);
                        }
                    } catch { }
                    lk.classList.remove('busy');
                    lk.classList.add('bad');
                    setTimeout(() => lk.classList.remove('bad'), 900);
                }
                else if (strict) {
                    lk.classList.add('bad');
                    setTimeout(() => lk.classList.remove('bad'), 900);
                }
            };
            lk.addEventListener('input', () => tryLink(false));
            lk.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); tryLink(true); } e.stopPropagation(); });

            const done = out => { map.remove(); veil.remove(); resolve(out); };
            x.onclick = () => done(null);
            ok.onclick = () => {
                const p = marker ? marker.getLatLng() : map.getCenter();
                done(p.lat.toFixed(6) + ',' + p.lng.toFixed(6));
            };
            setTimeout(() => map.invalidateSize(), 80);
        });
    },
};
// ---- Support tickets: hand the browser a data-URL to save (attachments) ----
window.mfSupport = {
    download(dataUrl, filename) {
        try {
            const a = document.createElement('a');
            a.href = dataUrl;
            a.download = filename || 'file';
            document.body.appendChild(a);
            a.click();
            setTimeout(() => a.remove(), 0);
        } catch (e) { console.warn('mfSupport.download', e); }
    }
};
// Support tab bar: our own scroll arrows. MudBlazor's own arrows drive a physical
// translateX that points the wrong way in RTL, so the bar scrolls natively and these
// two buttons nudge it. They only show when there is something to scroll to.
window.mfSupport = window.mfSupport || {};
window.mfSupport.scrollTabs = function (dir) {
    const el = document.querySelector('.tk-tabs .mud-tabs-tabbar-content');
    if (!el) return;
    const rtl = getComputedStyle(el).direction === 'rtl';
    const step = Math.max(140, el.clientWidth * 0.6);
    el.scrollBy({ left: (rtl ? -dir : dir) * step, behavior: 'smooth' });
};
window.mfSupport.watchTabs = function () {
    const el = document.querySelector('.tk-tabs .mud-tabs-tabbar-content');
    const bar = document.querySelector('.tk-tabbar');
    if (!el || !bar) return;
    const update = () => bar.classList.toggle('scrolls', el.scrollWidth > el.clientWidth + 2);
    update();
    if (!el.dataset.ooTabWatch) {
        el.dataset.ooTabWatch = '1';
        try { new ResizeObserver(update).observe(el); } catch (e) { window.addEventListener('resize', update); }
        el.addEventListener('scroll', update, { passive: true });
    }
};

// Table-QR page: the group pills scroll natively; two physical arrows nudge them and
// the row learns whether it overflows and which end it is at (Majed 2026-09-07:
// "make group better ui and add arrow left and right").
window.mfQm = {
    // The search box types locally; the text goes to Blazor once per pause (or on Enter),
    // so a phone on 3G sends one message per word instead of one per letter.
    bindSearch(id, dotnetRef) {
        const input = document.getElementById(id);
        if (!input || input.dataset.ooBound) return;
        input.dataset.ooBound = '1';
        let timer = null, last = null;
        const send = () => { timer = null; const v = input.value; if (v === last) return; last = v; dotnetRef.invokeMethodAsync('SetQuery', v).catch(() => { }); };
        input.addEventListener('input', () => { if (timer) clearTimeout(timer); timer = setTimeout(send, 220); });
        input.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); if (timer) clearTimeout(timer); send(); input.blur(); } });
        input.addEventListener('search', () => { if (timer) clearTimeout(timer); send(); }); // the native ✕ on some keyboards
    },
    clearSearch(id) {
        const input = document.getElementById(id);
        if (input) { input.value = ''; }
    },
    scrollCats(dir) {
        const el = document.querySelector('.qm-cats-row .qm-cats');
        if (!el) return;
        el.scrollBy({ left: dir * Math.max(120, el.clientWidth * 0.6), behavior: 'smooth' });
    },
    watchCats() {
        const el = document.querySelector('.qm-cats-row .qm-cats');
        const row = el && el.parentElement;
        if (!el || !row) return;
        const update = () => {
            const max = el.scrollWidth - el.clientWidth;
            row.classList.toggle('scrolls', max > 2);
            // scrollLeft is negative in RTL — use its magnitude for "how far along"
            const x = Math.abs(el.scrollLeft);
            const rtl = getComputedStyle(el).direction === 'rtl';
            const atStart = x <= 2, atEnd = x >= max - 2;
            // physical ends: in RTL the row starts at the RIGHT
            row.classList.toggle('at-left', rtl ? atEnd : atStart);
            row.classList.toggle('at-right', rtl ? atStart : atEnd);
        };
        update();
        if (!el.dataset.ooCatWatch) {
            el.dataset.ooCatWatch = '1';
            try { new ResizeObserver(update).observe(el); } catch (e) { window.addEventListener('resize', update); }
            el.addEventListener('scroll', update, { passive: true });
        }
    },
};