// Google Maps interop for the address picker.
// The Maps JS script is injected lazily so pages without a map never pay for it.
window.mfMap = {
    _loader: null,
    _instances: {},

    _load(key) {
        if (this._loader) return this._loader;
        this._loader = new Promise((resolve, reject) => {
            if (window.google?.maps) { resolve(); return; }
            const s = document.createElement('script');
            s.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key)}&v=weekly`;
            s.async = true;
            s.onload = () => resolve();
            s.onerror = () => reject(new Error('Google Maps failed to load'));
            document.head.appendChild(s);
        });
        return this._loader;
    },

    // Creates the picker map. Tapping or dragging the pin reverse-geocodes and
    // reports (lat, lng, formattedAddress, area) back to the Blazor dialog.
    async init(elementId, key, lat, lng, dotnetRef) {
        try {
            await this._load(key);
        } catch {
            const el = document.getElementById(elementId);
            if (el) el.innerHTML = '<div style="padding:16px;color:#77706A;font-size:13px;">Map could not load — check the API key and its allowed referrers.</div>';
            return false;
        }

        const hasPin = typeof lat === 'number' && typeof lng === 'number' && !(lat === 0 && lng === 0);
        const center = hasPin ? { lat, lng } : { lat: 23.5880, lng: 58.3829 }; // Muscat

        const map = new google.maps.Map(document.getElementById(elementId), {
            center,
            zoom: hasPin ? 16 : 11,
            streetViewControl: false,
            mapTypeControl: false,
            fullscreenControl: false,
            clickableIcons: false
        });
        const marker = new google.maps.Marker({ position: center, map, draggable: true });
        const geocoder = new google.maps.Geocoder();

        const report = (pos) => {
            geocoder.geocode({ location: pos }, (results, status) => {
                let formatted = null, area = null;
                if (status === 'OK' && results?.[0]) {
                    formatted = results[0].formatted_address;
                    const component = results[0].address_components.find(c =>
                        c.types.includes('sublocality') || c.types.includes('neighborhood') || c.types.includes('locality'));
                    area = component?.long_name ?? null;
                }
                dotnetRef.invokeMethodAsync('OnMapPick', pos.lat, pos.lng, formatted, area);
            });
        };

        map.addListener('click', e => {
            const pos = { lat: e.latLng.lat(), lng: e.latLng.lng() };
            marker.setPosition(pos);
            report(pos);
        });
        marker.addListener('dragend', () => {
            const p = marker.getPosition();
            report({ lat: p.lat(), lng: p.lng() });
        });

        this._instances[elementId] = { map, marker, report };
        return true;
    },

    // "Use my location" — needs a secure context (https / localhost).
    locate(elementId) {
        const instance = this._instances[elementId];
        if (!instance || !navigator.geolocation) return false;
        navigator.geolocation.getCurrentPosition(p => {
            const pos = { lat: p.coords.latitude, lng: p.coords.longitude };
            instance.map.setCenter(pos);
            instance.map.setZoom(17);
            instance.marker.setPosition(pos);
            instance.report(pos);
        });
        return true;
    },

    // Live-tracking map on the customer's order page: destination pin + moving rider.
    async live(elementId, key, destLat, destLng) {
        try { await this._load(key); } catch { return false; }
        const el = document.getElementById(elementId);
        if (!el) return false;

        const hasDest = typeof destLat === 'number' && typeof destLng === 'number';
        const center = hasDest ? { lat: destLat, lng: destLng } : { lat: 23.5880, lng: 58.3829 };
        const map = new google.maps.Map(el, {
            center,
            zoom: 14,
            streetViewControl: false,
            mapTypeControl: false,
            fullscreenControl: false,
            clickableIcons: false
        });
        const destMarker = hasDest
            ? new google.maps.Marker({ position: center, map, label: { text: '🏠', fontSize: '18px' } })
            : null;
        this._instances[elementId] = { map, destMarker, driverMarker: null, fitted: false };
        return true;
    },

    updateDriver(elementId, lat, lng) {
        const inst = this._instances[elementId];
        if (!inst || !window.google?.maps) return;
        const pos = { lat, lng };
        if (!inst.driverMarker) {
            inst.driverMarker = new google.maps.Marker({
                position: pos,
                map: inst.map,
                zIndex: 10,
                label: { text: '🛵', fontSize: '20px' },
                icon: {
                    path: google.maps.SymbolPath.CIRCLE,
                    scale: 16,
                    fillColor: '#FF5A00',
                    fillOpacity: .95,
                    strokeColor: '#FFFFFF',
                    strokeWeight: 3
                }
            });
        } else {
            inst.driverMarker.setPosition(pos);
        }
        if (!inst.fitted) {
            inst.fitted = true;
            if (inst.destMarker) {
                const bounds = new google.maps.LatLngBounds();
                bounds.extend(pos);
                bounds.extend(inst.destMarker.getPosition());
                inst.map.fitBounds(bounds, 70);
            } else {
                inst.map.setCenter(pos);
                inst.map.setZoom(15);
            }
        }
    },

    dispose(elementId) {
        delete this._instances[elementId];
    },

    // "Near me" on the home page — browser position, resolved quietly.
    position() {
        return new Promise(resolve => {
            if (!navigator.geolocation) { resolve(null); return; }
            navigator.geolocation.getCurrentPosition(
                p => resolve({ lat: p.coords.latitude, lng: p.coords.longitude }),
                () => resolve(null),
                { enableHighAccuracy: false, timeout: 8000, maximumAge: 300000 });
        });
    },

    // Google reverse geocode → human area name ("Al Khuwair") for the header pill.
    async areaName(key, lat, lng) {
        try {
            await this._load(key);
            const geocoder = new google.maps.Geocoder();
            return await new Promise(resolve => {
                geocoder.geocode({ location: { lat, lng } }, (results, status) => {
                    if (status === 'OK' && results?.[0]) {
                        const c = results[0].address_components.find(x =>
                            x.types.includes('sublocality') || x.types.includes('neighborhood') || x.types.includes('locality'));
                        resolve(c?.long_name ?? results[0].formatted_address ?? null);
                    } else resolve(null);
                });
            });
        } catch { return null; }
    }
};
