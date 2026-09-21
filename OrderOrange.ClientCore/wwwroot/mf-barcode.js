// Code 39 barcodes as inline SVG — no library, no network, prints crisp at any size.
//
// Code 39 is the label-printer workhorse: digits, capitals and a dash are exactly
// what SKUs look like, every scanner ever made reads it, and its table is public.
// Each character is 9 elements (5 bars, 4 spaces), 3 of them wide.
window.mfBarcode = (function () {
    // n = narrow, w = wide; elements alternate bar/space starting with a bar.
    var CODE39 = {
        "0": "nnnwwnwnn", "1": "wnnwnnnnw", "2": "nnwwnnnnw", "3": "wnwwnnnnn",
        "4": "nnnwwnnnw", "5": "wnnwwnnnn", "6": "nnwwwnnnn", "7": "nnnwnnwnw",
        "8": "wnnwnnwnn", "9": "nnwwnnwnn",
        "A": "wnnnnwnnw", "B": "nnwnnwnnw", "C": "wnwnnwnnn", "D": "nnnnwwnnw",
        "E": "wnnnwwnnn", "F": "nnwnwwnnn", "G": "nnnnnwwnw", "H": "wnnnnwwnn",
        "I": "nnwnnwwnn", "J": "nnnnwwwnn", "K": "wnnnnnnww", "L": "nnwnnnnww",
        "M": "wnwnnnnwn", "N": "nnnnwnnww", "O": "wnnnwnnwn", "P": "nnwnwnnwn",
        "Q": "nnnnnnwww", "R": "wnnnnnwwn", "S": "nnwnnnwwn", "T": "nnnnwnwwn",
        "U": "wwnnnnnnw", "V": "nwwnnnnnw", "W": "wwwnnnnnn", "X": "nwnnwnnnw",
        "Y": "wwnnwnnnn", "Z": "nwwnwnnnn",
        "-": "nwnnnnwnw", ".": "wwnnnnwnn", " ": "nwwnnnwnn", "*": "nwnnwnwnn",
        "$": "nwnwnwnnn", "/": "nwnwnnnwn", "+": "nwnnnwnwn", "%": "nnnwnwnwn"
    };

    // Anything the alphabet cannot carry becomes a dash — a label must never be blank.
    function clean(value) {
        var s = String(value || "").toUpperCase();
        var out = "";
        for (var i = 0; i < s.length; i++) out += CODE39[s[i]] && s[i] !== "*" ? s[i] : "-";
        return out || "0";
    }

    // Returns an SVG string. height in px; narrow module width derived from target width.
    function svg(value, opts) {
        opts = opts || {};
        var text = clean(value);
        var full = "*" + text + "*";
        var narrow = opts.narrow || 2;          // px per narrow module
        var wide = narrow * 2.6;
        var height = opts.height || 44;

        var x = 0, bars = [];
        for (var c = 0; c < full.length; c++) {
            var pattern = CODE39[full[c]];
            for (var e = 0; e < 9; e++) {
                var w = pattern[e] === "w" ? wide : narrow;
                if (e % 2 === 0) bars.push('<rect x="' + x.toFixed(2) + '" y="0" width="' + w.toFixed(2) + '" height="' + height + '"/>');
                x += w;
            }
            x += narrow;                        // inter-character gap
        }
        var width = x.toFixed(2);
        return '<svg class="bc-svg" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + width + " " + height + '" ' +
               'width="' + width + '" height="' + height + '" preserveAspectRatio="xMidYMid meet" fill="#111">' +
               bars.join("") + "</svg>";
    }

    // Fill every [data-barcode] under root with its barcode.
    function renderAll(rootSelector) {
        var root = rootSelector ? document.querySelector(rootSelector) : document;
        if (!root) return;
        root.querySelectorAll("[data-barcode]").forEach(function (el) {
            var value = el.getAttribute("data-barcode");
            if (el.dataset.rendered === value) return;
            el.dataset.rendered = value;
            el.innerHTML = svg(value, {
                height: parseInt(el.getAttribute("data-height") || "44", 10),
                narrow: parseFloat(el.getAttribute("data-narrow") || "2")
            });
        });
    }

    return { svg: svg, renderAll: renderAll, print: function () { window.print(); } };
})();
