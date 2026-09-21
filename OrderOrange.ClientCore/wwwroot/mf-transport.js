// Some networks quietly strangle WebSockets: the handshake leaves, nothing
// returns, and the page stands there dressed but lifeless. Before Blazor
// starts, we knock on the socket door ourselves — any answer at all (even a
// slammed 400) proves the road is open and Blazor may take it. Silence means
// the road is cut, and the circuit rides plain long-polling instead, which
// travels as ordinary HTTP and gets through anything that lets the page load.
(function () {
    var KEY = 'oo-lp';
    var started = false;

    function start(longPollingOnly) {
        if (started) return;
        started = true;
        var options = {};
        if (longPollingOnly) {
            try { sessionStorage.setItem(KEY, '1'); } catch (e) { }
            options.circuit = {
                configureSignalR: function (builder) {
                    // HttpTransportType.LongPolling = 4
                    builder.withUrl(document.baseURI.replace(/\/+$/, '') + '/_blazor', { transport: 4 });
                }
            };
        }
        Blazor.start(options);
    }

    var stuck = false;
    try { stuck = sessionStorage.getItem(KEY) === '1'; } catch (e) { }
    if (stuck) { start(true); return; }

    var probe;
    try {
        probe = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/_blazor');
    } catch (e) { start(false); return; }

    var answered = function () { try { probe.close(); } catch (e) { } start(false); };
    probe.onopen = answered;
    probe.onerror = answered;
    probe.onclose = answered;
    setTimeout(function () {
        if (!started) { try { probe.close(); } catch (e) { } start(true); }
    }, 3500);
})();
