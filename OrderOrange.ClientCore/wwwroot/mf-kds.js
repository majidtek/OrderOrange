// The kitchen screen's three small favours from the browser: keep the display awake
// through a quiet hour, go full screen on a tap, and a ding that survives a kiosk's
// autoplay rules by unlocking one audio context on the first touch.
window.mfKds = {
    _wake: null,
    _ctx: null,

    /** Holds the screen on. Silently does nothing where the API is missing (Safari, older Android). */
    async keepAwake() {
        try {
            if (!('wakeLock' in navigator)) return false;
            this._wake = await navigator.wakeLock.request('screen');
            // A tab that goes to the background loses the lock; take it back on return.
            document.addEventListener('visibilitychange', async () => {
                try {
                    if (document.visibilityState === 'visible' && !this._wake)
                        this._wake = await navigator.wakeLock.request('screen');
                } catch { }
            });
            return true;
        } catch { return false; }
    },

    async release() {
        try { await this._wake?.release(); this._wake = null; } catch { }
    },

    toggleFullscreen() {
        try {
            if (document.fullscreenElement) document.exitFullscreen();
            else document.documentElement.requestFullscreen();
            return !!document.fullscreenElement;
        } catch { return false; }
    },

    /** One context, created on a real gesture, reused for every ding after it. */
    unlock() {
        try {
            this._ctx ??= new (window.AudioContext || window.webkitAudioContext)();
            if (this._ctx.state === 'suspended') this._ctx.resume();
            return true;
        } catch { return false; }
    },

    /** Two short rising notes — audible over an extractor fan, not startling. */
    ding(times) {
        try {
            this.unlock();
            const ctx = this._ctx;
            if (!ctx) return;
            const at = ctx.currentTime;
            for (let i = 0; i < (times || 1); i++) {
                const t = at + i * 0.42;
                const osc = ctx.createOscillator();
                const gain = ctx.createGain();
                osc.type = 'sine';
                osc.frequency.setValueAtTime(784, t);
                osc.frequency.setValueAtTime(1046, t + 0.13);
                gain.gain.setValueAtTime(0.0001, t);
                gain.gain.exponentialRampToValueAtTime(0.35, t + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.34);
                osc.connect(gain).connect(ctx.destination);
                osc.start(t);
                osc.stop(t + 0.36);
            }
        } catch { }
    },
};
