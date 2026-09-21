// Guided-tour spotlight. JS owns the highlight box completely: it re-places it on
// every scroll (any inner scroller, via capture) and resize, so the glow stays glued
// to its target no matter how the page moves. Blazor only drives WHICH selector.
//
// The shield is FOUR panels framing the target instead of one sheet: on an
// interactive step the framed hole lets the cashier really tap the spotlighted
// control (add a dish, pick a table) while everything else stays inert. On a
// passive step the panels close ranks and cover the target too.
window.mfTour = (function () {
    let sel = null, interactive = false, spot = null, panels = null, bound = false;

    function stop(e) { e.stopPropagation(); e.preventDefault(); }

    function panel() {
        const d = document.createElement("div");
        d.style.cssText = "position:fixed;z-index:1997;cursor:default;";
        ["click", "pointerdown", "touchstart"].forEach(ev =>
            d.addEventListener(ev, stop, { passive: false }));
        document.body.appendChild(d);
        return d;
    }

    function set(p, x, y, w, h) {
        p.style.left = x + "px"; p.style.top = y + "px";
        p.style.width = Math.max(0, w) + "px"; p.style.height = Math.max(0, h) + "px";
        p.style.display = (w > 0 && h > 0) ? "block" : "none";
    }

    function place() {
        if (!sel || !spot || !panels) return;
        const t = document.querySelector(sel);
        const vw = window.innerWidth, vh = window.innerHeight;
        if (!t) {
            spot.style.display = "none";
            set(panels[0], 0, 0, vw, vh); set(panels[1], 0, 0, 0, 0);
            set(panels[2], 0, 0, 0, 0); set(panels[3], 0, 0, 0, 0);
            return;
        }
        const r = t.getBoundingClientRect();
        spot.style.display = "block";
        spot.style.left = (r.left - 8) + "px";
        spot.style.top = (r.top - 8) + "px";
        spot.style.width = (r.width + 16) + "px";
        spot.style.height = (r.height + 16) + "px";

        // Interactive: frame the hole so the target itself stays tappable.
        // Passive: the first panel swallows the whole screen.
        if (interactive) {
            const x1 = Math.max(0, r.left - 8), y1 = Math.max(0, r.top - 8);
            const x2 = Math.min(vw, r.right + 8), y2 = Math.min(vh, r.bottom + 8);
            set(panels[0], 0, 0, vw, y1);              // top
            set(panels[1], 0, y2, vw, vh - y2);        // bottom
            set(panels[2], 0, y1, x1, y2 - y1);        // start side
            set(panels[3], x2, y1, vw - x2, y2 - y1);  // end side
        } else {
            set(panels[0], 0, 0, vw, vh);
            set(panels[1], 0, 0, 0, 0); set(panels[2], 0, 0, 0, 0); set(panels[3], 0, 0, 0, 0);
        }
    }

    return {
        show: function (selector, canTouch) {
            sel = selector;
            interactive = !!canTouch;
            if (!panels) panels = [panel(), panel(), panel(), panel()];
            if (!spot) {
                spot = document.createElement("div");
                spot.id = "mf-tour-spot";
                spot.className = "tour-spot";
                document.body.appendChild(spot);
            }
            const el = document.querySelector(sel);
            if (el) el.scrollIntoView({ block: "center", inline: "nearest" });
            if (!bound) {
                window.addEventListener("scroll", place, true);
                window.addEventListener("resize", place);
                bound = true;
            }
            requestAnimationFrame(place);
            setTimeout(place, 300);   // once more after the scroll settles
            setTimeout(place, 700);   // and again after any dialog finishes opening
            return true;
        },
        hide: function () {
            sel = null;
            if (spot) { spot.remove(); spot = null; }
            if (panels) { panels.forEach(p => p.remove()); panels = null; }
            if (bound) {
                window.removeEventListener("scroll", place, true);
                window.removeEventListener("resize", place);
                bound = false;
            }
        },
        seen: function () { try { return localStorage.getItem("mfPosTour") === "1"; } catch { return true; } },
        markSeen: function () { try { localStorage.setItem("mfPosTour", "1"); } catch { } }
    };
})();
