// "Sign in with Google" button.
//
// Google's script is fetched on demand rather than in the page head: most visits never
// reach a login screen, and this keeps a third-party request off every other page.
//
// If the script cannot load — an ad blocker, a network that blocks Google, a country
// where it is unreachable — render() returns false instead of throwing, and the login
// screen quietly drops back to email and password. A sign-in page that half-loads and
// then breaks is far worse than one without the extra button.
window.mfGoogle = {
    _loading: null,

    _load() {
        if (window.google?.accounts?.id) return Promise.resolve();
        this._loading ??= new Promise((resolve, reject) => {
            const s = document.createElement('script');
            s.src = 'https://accounts.google.com/gsi/client';
            s.async = true;
            s.defer = true;
            s.onload = () => resolve();
            s.onerror = () => { this._loading = null; reject(new Error('gsi unavailable')); };
            document.head.appendChild(s);
        });
        return this._loading;
    },

    // Returns "ok", or a short reason. A reason is far more use than false: without it
    // a button that silently fails to appear is indistinguishable from one that was
    // never asked for, and the only way to tell them apart is a browser we cannot see.
    async render(host, clientId, dotnetRef, locale) {
        if (!host) return 'no-host';
        if (!clientId) return 'no-client-id';
        try {
            await this._load();
        } catch (e) {
            return 'script-blocked: ' + (e && e.message ? e.message : 'unknown');
        }
        if (!window.google?.accounts?.id) return 'script-loaded-but-empty';
        try {
            google.accounts.id.initialize({
                client_id: clientId,
                // The credential is a token signed by Google. It is worth nothing until the
                // server checks that signature, which is exactly what the API does with it.
                callback: response => dotnetRef.invokeMethodAsync('OnGoogleCredential', response.credential),
                auto_select: false,
                cancel_on_tap_outside: true,
            });
            host.innerHTML = '';
            google.accounts.id.renderButton(host, {
                type: 'standard',
                theme: 'outline',
                size: 'large',
                text: 'continue_with',
                shape: 'pill',
                logo_alignment: 'center',
                // Google rejects anything outside 200-400, and silently draws nothing.
                width: Math.min(Math.max(host.offsetWidth || 320, 200), 400),
                locale: locale || 'en',
            });

            // renderButton does not throw when the origin is not on the client's allowed
            // list — it just quietly draws nothing. Checking afterwards is the only way
            // to catch the single most common misconfiguration.
            if (!host.firstChild) return 'origin-not-allowed-or-empty';
            return 'ok';
        } catch (e) {
            return 'render-failed: ' + (e && e.message ? e.message : 'unknown');
        }
    },

    // True inside an Android app that shows the site in an embedded web view (Majed
    // 2026-10-22: "Google login does nothing" in such an app). Google refuses to sign
    // anyone in there, so the screen points to Chrome instead of a button that cannot work.
    // Android WebView says so in its user agent: "; wv)", or the older "Version/4.0 ... Chrome/".
    embedded() {
        const ua = navigator.userAgent || '';
        if (!/Android/i.test(ua)) return false;
        return /; wv\)/.test(ua) || (/Version\/\d/.test(ua) && /Chrome\/\d/.test(ua));
    },

    // The same page in Chrome, from inside a wrapper app. Most wrappers hand intent:
    // links to Android; the copy button below covers the ones that do not.
    chromeLink() {
        const u = location.href.replace(/^https?:\/\//, '');
        return 'intent://' + u + '#Intent;scheme=https;package=com.android.chrome;end';
    },

    async copyAddress() {
        try { await navigator.clipboard.writeText(location.origin); return true; } catch { return false; }
    },

    // Called when the login screen goes away, so a pending prompt does not outlive it.
    cancel() {
        try { window.google?.accounts?.id?.cancel(); } catch { /* nothing to cancel */ }
    },
};
