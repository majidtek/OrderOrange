// Chat helpers: voice-note recording (MediaRecorder → data URL) and a one-shot GPS fix.
window.mfChat = {
    _recorder: null,
    _chunks: [],

    // The attach action lives in a dropdown now — it opens the hidden InputFile.
    pickFile(id) {
        var el = document.getElementById(id);
        if (el) el.click();
    },

    // The message input is uncontrolled (fast typing); clear it after a send.
    clearInput(id) {
        var el = document.getElementById(id);
        if (el) { el.value = ''; try { el.focus(); } catch { } }
    },

    // Opens the picker; images are downscaled (max 1400px) and re-encoded as
    // JPEG with stepped quality until they fit maxKb. Other files pass through
    // with a hard 1.5 MB cap.
    pickCompressed(inputId, maxKb) {
        return new Promise(resolve => {
            const input = document.getElementById(inputId);
            if (!input) { resolve(null); return; }
            input.value = '';

            const done = v => { cleanup(); resolve(v); };
            const onCancel = () => done(null);
            const onChange = async () => {
                const file = input.files && input.files[0];
                if (!file) { done(null); return; }

                if (!file.type.startsWith('image/')) {
                    if (file.size > 1_500_000) { done({ error: 'TOO_BIG' }); return; }
                    const r = new FileReader();
                    r.onloadend = () => done({ dataUrl: r.result, name: file.name, image: false });
                    r.readAsDataURL(file);
                    return;
                }

                try {
                    const bmp = await createImageBitmap(file);
                    const maxDim = 1400;
                    let w = bmp.width, h = bmp.height;
                    const scale = Math.min(1, maxDim / Math.max(w, h));
                    w = Math.round(w * scale); h = Math.round(h * scale);
                    const canvas = document.createElement('canvas');
                    canvas.width = w; canvas.height = h;
                    canvas.getContext('2d').drawImage(bmp, 0, 0, w, h);

                    const budget = (maxKb || 300) * 1024;
                    let dataUrl = null;
                    for (const q of [0.82, 0.68, 0.55, 0.44, 0.34]) {
                        dataUrl = canvas.toDataURL('image/jpeg', q);
                        if (dataUrl.length * 0.75 <= budget) break;
                    }
                    // Still over? halve the dimensions once more.
                    if (dataUrl.length * 0.75 > budget) {
                        const c2 = document.createElement('canvas');
                        c2.width = Math.round(w / 2); c2.height = Math.round(h / 2);
                        c2.getContext('2d').drawImage(canvas, 0, 0, c2.width, c2.height);
                        dataUrl = c2.toDataURL('image/jpeg', 0.6);
                    }
                    const name = (file.name || 'photo').replace(/\.\w+$/, '') + '.jpg';
                    done({ dataUrl, name, image: true });
                } catch {
                    done(null);
                }
            };
            const cleanup = () => {
                input.removeEventListener('change', onChange);
                input.removeEventListener('cancel', onCancel);
            };
            input.addEventListener('change', onChange, { once: true });
            input.addEventListener('cancel', onCancel, { once: true });
            input.click();
        });
    },

    async startVoice() {
        try {
            // A microphone is only offered on a secure page; saying so beats a
            // silent "nothing happened".
            if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
                this._voiceError = 'unsupported';
                return false;
            }
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            this._chunks = [];

            // Browsers disagree on what they can record: Chrome/Firefox do WebM/Opus,
            // Safari and iOS do MP4/AAC. Ask for the first one THIS browser admits to
            // supporting, and remember it so the blob is labelled truthfully.
            const wanted = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus'];
            const supported = wanted.find(t => MediaRecorder.isTypeSupported?.(t));
            this._recorder = supported
                ? new MediaRecorder(stream, { mimeType: supported })
                : new MediaRecorder(stream);
            this._voiceType = this._recorder.mimeType || supported || 'audio/webm';
            this._recorder.ondataavailable = e => { if (e.data && e.data.size) this._chunks.push(e.data); };
            // Timeslice: some mobile browsers only hand over data as it goes.
            this._recorder.start(250);
            // Live analyser so the waveform shows the REAL microphone signal.
            try {
                this._audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                const src = this._audioCtx.createMediaStreamSource(stream);
                this._analyser = this._audioCtx.createAnalyser();
                this._analyser.fftSize = 64;
                this._analyser.smoothingTimeConstant = .55;
                src.connect(this._analyser);
            } catch { this._analyser = null; }
            this._voiceError = null;
            return true;
        } catch (err) {
            // Denied, in use, or no device — each deserves its own answer upstairs.
            this._voiceError = err?.name === 'NotAllowedError' ? 'denied'
                : err?.name === 'NotFoundError' ? 'nomic'
                : 'failed';
            return false;
        }
    },

    /// Why the last startVoice() said no.
    voiceError() { return this._voiceError || 'failed'; },

    // Draws live amplitude bars onto the composer canvas while recording.
    startViz(canvasId) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        const draw = () => {
            if (!this._recorder) { ctx.clearRect(0, 0, canvas.width, canvas.height); return; }
            requestAnimationFrame(draw);
            const w = canvas.width, h = canvas.height;
            ctx.clearRect(0, 0, w, h);
            if (!this._analyser) return;
            const data = new Uint8Array(this._analyser.frequencyBinCount);
            this._analyser.getByteFrequencyData(data);
            const bars = 24, gap = 3;
            const bw = (w - gap * (bars - 1)) / bars;
            for (let i = 0; i < bars; i++) {
                const v = data[Math.floor(i * data.length / bars)] / 255;
                const bh = Math.max(3, v * h);
                const x = i * (bw + gap), y = (h - bh) / 2;
                const g = ctx.createLinearGradient(0, y, 0, y + bh);
                g.addColorStop(0, '#FF7E33');
                g.addColorStop(1, '#FF5A00');
                ctx.fillStyle = g;
                ctx.beginPath();
                ctx.roundRect(x, y, bw, bh, bw / 2);
                ctx.fill();
            }
        };
        draw();
    },

    stopVoice() {
        return new Promise(resolve => {
            const rec = this._recorder;
            if (!rec) { resolve(null); return; }
            // The blob must be labelled with what was ACTUALLY recorded — calling an
            // MP4 recording "webm" produced a note that uploaded fine and then
            // refused to play.
            const type = (this._voiceType || rec.mimeType || 'audio/webm').split(';')[0];
            rec.onstop = () => {
                rec.stream.getTracks().forEach(t => t.stop());
                this._recorder = null;
                try { this._audioCtx?.close(); } catch { }
                this._audioCtx = null;
                this._analyser = null;
                const blob = new Blob(this._chunks, { type });
                if (!blob.size) { resolve(null); return; }        // nothing was captured
                if (blob.size > 1_500_000) { resolve('TOO_BIG'); return; }
                const reader = new FileReader();
                reader.onloadend = () => resolve(reader.result);
                reader.onerror = () => resolve(null);
                reader.readAsDataURL(blob);
            };
            if (rec.state === 'inactive') { rec.onstop(); return; }
            rec.stop();
        });
    },

    getLocation() {
        return new Promise(resolve => {
            if (!navigator.geolocation) { resolve(null); return; }
            navigator.geolocation.getCurrentPosition(
                p => resolve([p.coords.latitude, p.coords.longitude]),
                () => resolve(null),
                { enableHighAccuracy: true, timeout: 5000 });
        });
    },

    scrollToBottom(el) {
        if (el) el.scrollTop = el.scrollHeight;
    },

    /// Copies a promo code. Falls back to a hidden textarea because the async
    /// Clipboard API is refused on insecure origins and in some in-app browsers.
    async copy(text) {
        if (!text) return false;
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch {
            try {
                const ta = document.createElement('textarea');
                ta.value = text;
                ta.setAttribute('readonly', '');
                ta.style.cssText = 'position:fixed;top:-1000px;opacity:0';
                document.body.appendChild(ta);
                ta.select();
                const ok = document.execCommand('copy');
                document.body.removeChild(ta);
                return ok;
            } catch {
                return false;
            }
        }
    }
};

// A profile picture is a small square, not a photograph. Crop to the centre square
// first, scale to 256px, then squeeze until it fits the budget — a 4MB camera shot
// leaves the browser as a few KB, and the network never carries the original.
window.mfAvatar = {
    pick(inputId, px, maxKb) {
        return new Promise(resolve => {
            const input = document.getElementById(inputId);
            if (!input) { resolve(null); return; }
            input.value = '';

            const done = v => { cleanup(); resolve(v); };
            const onCancel = () => done(null);
            const onChange = async () => {
                const file = input.files && input.files[0];
                if (!file) { done(null); return; }
                if (!file.type.startsWith('image/')) { done({ error: 'NOT_IMAGE' }); return; }

                try {
                    const bmp = await createImageBitmap(file);
                    const side = Math.min(bmp.width, bmp.height);      // the centre square
                    const sx = (bmp.width - side) / 2;
                    const sy = (bmp.height - side) / 2;
                    const out = px || 256;

                    const canvas = document.createElement('canvas');
                    canvas.width = out; canvas.height = out;
                    const ctx = canvas.getContext('2d');
                    ctx.imageSmoothingQuality = 'high';
                    ctx.drawImage(bmp, sx, sy, side, side, 0, 0, out, out);

                    const budget = (maxKb || 60) * 1024;
                    let dataUrl = canvas.toDataURL('image/jpeg', 0.85);
                    for (const q of [0.72, 0.6, 0.5, 0.4]) {
                        if (dataUrl.length * 0.75 <= budget) break;
                        dataUrl = canvas.toDataURL('image/jpeg', q);
                    }
                    done({ dataUrl, bytes: Math.round(dataUrl.length * 0.75), original: file.size });
                } catch (e) {
                    done({ error: 'FAILED' });
                }
            };
            const cleanup = () => {
                input.removeEventListener('change', onChange);
                window.removeEventListener('focus', onFocus, true);
            };
            // A cancelled file dialog fires no change event — the window regaining
            // focus with nothing chosen is the only signal we get.
            let focused = false;
            const onFocus = () => {
                if (focused) return; focused = true;
                setTimeout(() => { if (!input.files || !input.files.length) onCancel(); }, 400);
            };
            input.addEventListener('change', onChange, { once: true });
            window.addEventListener('focus', onFocus, true);
            input.click();
        });
    }
};
