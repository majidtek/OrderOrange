// Live position for a shop's OWN courier.
//
// The platform's riders push their location from the rider app; a shop that
// delivers for itself has no rider app, so the phone that carries the food runs
// this instead. watchPosition keeps a single subscription open and reports each
// fix to Blazor, which stores it on the order — the customer's tracking map then
// reads it through exactly the same endpoint a rider's position uses.
//
// Fixes are throttled to one every 10 seconds: a delivery van does not need
// per-second precision, and the battery does need the rest.
window.mfCourier = {
    _watchId: null,
    _lastSent: 0,

    async start(dotnetRef) {
        if (!navigator.geolocation) return false;
        this.stop();

        // Ask once up front: a refusal should be reported now, not silently later.
        const allowed = await new Promise(resolve => {
            navigator.geolocation.getCurrentPosition(
                p => { this._report(dotnetRef, p, true); resolve(true); },
                () => resolve(false),
                { enableHighAccuracy: true, timeout: 8000 });
        });
        if (!allowed) return false;

        this._watchId = navigator.geolocation.watchPosition(
            p => this._report(dotnetRef, p, false),
            () => { },
            { enableHighAccuracy: true, maximumAge: 5000, timeout: 20000 });
        return true;
    },

    _report(dotnetRef, position, force) {
        const now = Date.now();
        if (!force && now - this._lastSent < 10000) return;
        this._lastSent = now;
        dotnetRef.invokeMethodAsync('OnFix', position.coords.latitude, position.coords.longitude);
    },

    stop() {
        if (this._watchId !== null) {
            navigator.geolocation.clearWatch(this._watchId);
            this._watchId = null;
        }
        this._lastSent = 0;
    }
};
