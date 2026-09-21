// One-shot GPS fix for the rider app. Returns [lat, lng] or null.
window.mfGeo = {
    get() {
        return new Promise(resolve => {
            if (!navigator.geolocation) { resolve(null); return; }
            navigator.geolocation.getCurrentPosition(
                p => resolve([p.coords.latitude, p.coords.longitude]),
                () => resolve(null),
                { enableHighAccuracy: true, maximumAge: 5000, timeout: 4000 });
        });
    }
};
