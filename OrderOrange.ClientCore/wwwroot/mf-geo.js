// Where is this phone right now? One call, one answer — used by the attendance
// buttons ("I'm here" / "Leaving") so every clock carries a place.
//   mfGeo.get() -> { lat, lng, accuracy }            when the browser allows it
//                  { error: 'denied'|'unavailable'|'timeout'|'unsupported' } otherwise
// Geolocation needs https; every OrderOrange host has it.
window.mfGeo = {
    get: function (timeoutMs) {
        return new Promise(function (resolve) {
            if (!navigator.geolocation) return resolve({ error: 'unsupported' });
            navigator.geolocation.getCurrentPosition(
                function (p) { resolve({ lat: p.coords.latitude, lng: p.coords.longitude, accuracy: p.coords.accuracy }); },
                function (e) { resolve({ error: e.code === 1 ? 'denied' : e.code === 3 ? 'timeout' : 'unavailable' }); },
                { enableHighAccuracy: true, timeout: timeoutMs || 15000, maximumAge: 0 });
        });
    }
};
