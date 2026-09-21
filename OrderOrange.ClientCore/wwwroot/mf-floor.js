// The 2D floor plan's hands: pointer-based drag for tables AND rooms, plus a
// draw mode that sketches new room rectangles — one code path for mouse and touch.
// Blazor owns the data; this file only moves pixels and reports results in canvas
// percentages: tables by their CENTER, rooms by their TOP-LEFT corner and size.
// The till's fullscreen switch — a real POS owns the whole display.
// A page load always drops fullscreen (the browser insists), and the app reloads
// itself after a dropped connection — so the choice is REMEMBERED, and the first
// tap after a reload silently takes the display back. Esc / an explicit exit
// clears the memory: leaving fullscreen on purpose must stay left.
window.mfPos = {
    toggleFullscreen() {
        if (document.fullscreenElement) {
            sessionStorage.removeItem('oo-pos-fs');
            document.exitFullscreen();
        } else {
            sessionStorage.setItem('oo-pos-fs', '1');
            document.documentElement.requestFullscreen?.();
        }
    },

    // Called when the POS page appears: if fullscreen was on before the reload,
    // the first pointer press anywhere restores it (a gesture is required).
    restoreFullscreen() {
        if (sessionStorage.getItem('oo-pos-fs') !== '1' || document.fullscreenElement) return;
        const once = () => {
            document.removeEventListener('pointerdown', once, true);
            if (sessionStorage.getItem('oo-pos-fs') === '1' && !document.fullscreenElement)
                document.documentElement.requestFullscreen?.().catch(() => { });
        };
        document.addEventListener('pointerdown', once, true);
    },
};

// Leaving fullscreen while the page is alive (Esc, system gesture) is a choice.
document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement) sessionStorage.removeItem('oo-pos-fs');
});

// The salon panels on the BOARD: free hands, no arranging. Drag the title bar
// to put a panel anywhere; stretch it by EITHER bottom corner (left corner
// pulls the left wall, right corner the right). Panels never cover each other
// — a blocked axis slides along the neighbour. Rectangles live on the server.
window.mfSal = {
    wire(dotnetRef) {
        const board = document.querySelector('.sal-board');
        if (!board) return;
        const G = 10;
        board.querySelectorAll('.sal[data-sal]').forEach(el => {
            if (el._mfWired) return;
            el._mfWired = true;
            const id = +el.dataset.sal;
            const others = () => [...board.querySelectorAll('.sal[data-sal]')]
                .filter(x => x !== el)
                .map(x => ({ l: x.offsetLeft, t: x.offsetTop, w: x.offsetWidth, h: x.offsetHeight }));
            const collides = (l, t, w, h, obs) => obs.some(o =>
                l < o.l + o.w + G && l + w > o.l - G &&
                t < o.t + o.h + G && t + h > o.t - G);
            const report = () => dotnetRef.invokeMethodAsync('OnSalonRect', id,
                el.offsetLeft / board.offsetWidth * 100,
                el.offsetTop / board.offsetHeight * 100,
                el.offsetWidth / board.offsetWidth * 100,
                el.offsetHeight / board.offsetHeight * 100);

            // ---- move: the whole title bar carries the panel. Near a window
            // edge the board scrolls along, so anywhere really means anywhere. ----
            const head = el.querySelector('.sal-head');
            if (head) {
                let d = null;
                const scroller = () => board.closest('.sal-board-scroll');

                // Mid-drag the panel travels FREELY, even across others (a red
                // dash warns); the DROP settles it on the nearest free ground.
                const apply = () => {
                    if (!d) return;
                    const sc = scroller();
                    const w = el.offsetWidth, h = el.offsetHeight;
                    // scrolling mid-drag shifts the board under the finger — compensate
                    const sx = sc ? sc.scrollLeft - d.sl0 : 0;
                    const sy = sc ? sc.scrollTop - d.st0 : 0;
                    const l = Math.min(Math.max(d.l + (d.px - d.x) + sx, 0), board.offsetWidth - w);
                    const t2 = Math.min(Math.max(d.t + (d.py - d.y) + sy, 0), board.offsetHeight - h);
                    d.lastL = l;
                    d.lastT = t2;
                    el.style.left = l + 'px';
                    el.style.top = t2 + 'px';
                    el.classList.toggle('sal-bad', collides(l, t2, w, h, d.obs));
                };

                // The landing: shortest push out of whoever is underneath.
                const settle = (l, t2, w, h, obs, fallbackL, fallbackT) => {
                    let cl = l, ct = t2;
                    for (var i = 0; i < 80 && collides(cl, ct, w, h, obs); i++) {
                        const o = obs.find(o =>
                            cl < o.l + o.w + G && cl + w > o.l - G &&
                            ct < o.t + o.h + G && ct + h > o.t - G);
                        if (!o) break;
                        const moves = [
                            [o.l - w - G - 2 - cl, 0],
                            [o.l + o.w + G + 2 - cl, 0],
                            [0, o.t - h - G - 2 - ct],
                            [0, o.t + o.h + G + 2 - ct],
                        ].filter(m =>
                            cl + m[0] >= 0 && cl + m[0] + w <= board.offsetWidth &&
                            ct + m[1] >= 0 && ct + m[1] + h <= board.offsetHeight);
                        if (!moves.length) return [fallbackL, fallbackT];
                        moves.sort((a, b) => (Math.abs(a[0]) + Math.abs(a[1])) - (Math.abs(b[0]) + Math.abs(b[1])));
                        cl += moves[0][0];
                        ct += moves[0][1];
                    }
                    return collides(cl, ct, w, h, obs) ? [fallbackL, fallbackT] : [cl, ct];
                };

                head.addEventListener('pointerdown', e => {
                    if (e.target.closest('button, input, a')) return;
                    e.preventDefault();
                    try { head.setPointerCapture(e.pointerId); } catch { }
                    const sc = scroller();
                    d = {
                        x: e.clientX, y: e.clientY, px: e.clientX, py: e.clientY,
                        l: el.offsetLeft, t: el.offsetTop,
                        lastL: el.offsetLeft, lastT: el.offsetTop, obs: others(),
                        sl0: sc ? sc.scrollLeft : 0, st0: sc ? sc.scrollTop : 0,
                        timer: setInterval(() => {
                            // the finger holds still at an edge: keep the board rolling
                            if (!d) return;
                            const s2 = scroller();
                            if (!s2) return;
                            const r = s2.getBoundingClientRect();
                            let rolled = false;
                            if (d.py < r.top + 70) { s2.scrollTop -= 16; rolled = true; }
                            else if (d.py > r.bottom - 70) { s2.scrollTop += 16; rolled = true; }
                            if (d.px < r.left + 70) { s2.scrollLeft -= 16; rolled = true; }
                            else if (d.px > r.right - 70) { s2.scrollLeft += 16; rolled = true; }
                            if (rolled) apply();
                        }, 30),
                    };
                    el.classList.add('sal-lift');
                });
                head.addEventListener('pointermove', e => {
                    if (!d) return;
                    d.px = e.clientX;
                    d.py = e.clientY;
                    apply();
                });
                const drop = () => {
                    if (!d) return;
                    clearInterval(d.timer);
                    const [l, t2] = settle(d.lastL, d.lastT, el.offsetWidth, el.offsetHeight, d.obs, d.l, d.t);
                    el.style.left = l + 'px';
                    el.style.top = t2 + 'px';
                    d = null;
                    el.classList.remove('sal-lift', 'sal-bad');
                    report();
                };
                head.addEventListener('pointerup', drop);
                head.addEventListener('pointercancel', drop);
            }

            // ---- resize: both bottom corners, width AND height ----
            const corner = (grip, side) => {
                if (!grip) return;
                let s = null;
                grip.addEventListener('pointerdown', e => {
                    e.preventDefault();
                    e.stopPropagation();
                    try { grip.setPointerCapture(e.pointerId); } catch { }
                    s = {
                        x: e.clientX, y: e.clientY,
                        l: el.offsetLeft, w: el.offsetWidth, h: el.offsetHeight,
                        lastL: el.offsetLeft, lastW: el.offsetWidth, lastH: el.offsetHeight,
                        obs: others(),
                    };
                });
                grip.addEventListener('pointermove', e => {
                    if (!s) return;
                    const dx = e.clientX - s.x;
                    let l = s.l, w = s.w;
                    if (side === 'r') {
                        w = Math.min(Math.max(s.w + dx, 280), board.offsetWidth - s.l);
                    } else {
                        l = Math.min(Math.max(s.l + dx, 0), s.l + s.w - 280);
                        w = s.w + (s.l - l);
                    }
                    let h = Math.min(Math.max(s.h + (e.clientY - s.y), 140), board.offsetHeight - el.offsetTop);
                    if (collides(l, el.offsetTop, w, h, s.obs)) { l = s.lastL; w = s.lastW; h = s.lastH; }
                    s.lastL = l;
                    s.lastW = w;
                    s.lastH = h;
                    el.style.left = l + 'px';
                    el.style.width = w + 'px';
                    el.style.height = h + 'px';
                });
                const done = () => {
                    if (!s) return;
                    s = null;
                    report();
                };
                grip.addEventListener('pointerup', done);
                grip.addEventListener('pointercancel', done);
            };
            corner(el.querySelector('.sal-c.l'), 'l');
            corner(el.querySelector('.sal-c.r'), 'r');
        });
    },
};
window.mfFloor = {
    setDrawMode(canvas, on) {
        if (canvas) canvas._mfDraw = !!on;
    },

    /// The whole floor, the whole screen — and the same button brings it back.
    fullscreen(el) {
        if (document.fullscreenElement) document.exitFullscreen();
        else el?.requestFullscreen?.();
    },

    /// The workspace itself can grow or shrink (percent positions rescale with
    /// it). The chosen size sticks per browser, and the Tables page re-applies
    /// it on every visit.
    resizeCanvas(canvas, factor) {
        if (!canvas) return;
        const w = Math.min(4200, Math.max(900, Math.round(canvas.offsetWidth * factor / 100) * 100));
        const h = Math.min(2600, Math.max(600, Math.round(canvas.offsetHeight * factor / 100) * 100));
        canvas.style.width = w + 'px';
        canvas.style.height = h + 'px';
        canvas.style.minHeight = h + 'px';
        try { localStorage.setItem('oo-floor-size', w + 'x' + h); } catch { }
    },

    applySavedSize(canvas) {
        if (!canvas) return;
        try {
            const saved = localStorage.getItem('oo-floor-size');
            if (!saved) return;
            const [w, h] = saved.split('x').map(Number);
            if (w >= 900 && h >= 600) {
                canvas.style.width = w + 'px';
                canvas.style.height = h + 'px';
                canvas.style.minHeight = h + 'px';
            }
        } catch { }
    },

    init(canvas, dotnetRef) {
        if (!canvas || canvas._mfFloorWired) return;
        canvas._mfFloorWired = true;
        this.applySavedSize(canvas);

        let drag = null;

        const pct = (v, whole) => Math.min(Math.max(v / whole * 100, 0), 100);

        // The salons, as boxes: a table dragged inside one may not cross its border.
        // M leaves room for the chairs that sit just outside the table's own box.
        const M = 16;
        const roomRects = () => [...canvas.querySelectorAll('.oo-fp-room')].map(el => ({
            l: el.offsetLeft, t: el.offsetTop, w: el.offsetWidth, h: el.offsetHeight,
        }));
        const roomAt = (rooms, cx, cy) =>
            rooms.find(rm => cx >= rm.l && cx <= rm.l + rm.w && cy >= rm.t && cy <= rm.t + rm.h);

        canvas.addEventListener('pointerdown', e => {
            if (e.button === 2) return;
            const r = canvas.getBoundingClientRect();

            // Sketching a new room: any press on OPEN floor starts the rectangle —
            // never inside an existing salon, and the stroke stops at their walls.
            if (canvas._mfDraw && !e.target.closest('.oo-fp-table') && !e.target.closest('.oo-fp-room')) {
                e.preventDefault();
                try { canvas.setPointerCapture(e.pointerId); } catch { }
                const draft = document.createElement('div');
                draft.className = 'oo-fp-roomdraft';
                canvas.appendChild(draft);
                drag = {
                    kind: 'draw', draft, x0: e.clientX - r.left, y0: e.clientY - r.top,
                    moved: false, others: roomRects(),
                };
                return;
            }

            const table = e.target.closest('.oo-fp-table');
            const tableGrip = e.target.closest('.oo-fp-table .ts');
            const grip = e.target.closest('.oo-fp-room .rs');
            const room = e.target.closest('.oo-fp-room');
            const el = table ?? room;
            if (!el) return;
            e.preventDefault();
            try { el.setPointerCapture(e.pointerId); } catch { }

            // The corner grip on a table: the owner sets its drawn size by hand.
            if (tableGrip && table) {
                drag = {
                    kind: 'tresize', el: table, id: +table.dataset.id,
                    startX: e.clientX, startY: e.clientY,
                    origW: table.offsetWidth, origH: table.offsetHeight, moved: false,
                    rtl: getComputedStyle(canvas).direction === 'rtl',
                    rooms: roomRects(),
                    // With the centering transform still on, offsetLeft/Top IS the center.
                    cx: table.offsetLeft, cy: table.offsetTop,
                };
                return;
            }

            // Rects of every OTHER salon: the one in hand may never cross them.
            const othersOf = own => [...canvas.querySelectorAll('.oo-fp-room')]
                .filter(x => x !== own)
                .map(x => ({ l: x.offsetLeft, t: x.offsetTop, w: x.offsetWidth, h: x.offsetHeight }));

            if (grip && room) {
                drag = {
                    kind: 'resize', el: room, id: +room.dataset.roomId,
                    startX: e.clientX, startY: e.clientY,
                    origW: room.offsetWidth, origH: room.offsetHeight, moved: false,
                    others: othersOf(room),
                };
                return;
            }

            drag = {
                kind: table ? 'table' : 'room',
                el,
                id: table ? +el.dataset.id : +el.dataset.roomId,
                startX: e.clientX, startY: e.clientY,
                origLeft: el.offsetLeft, origTop: el.offsetTop, moved: false,
                rooms: table ? roomRects() : null,
                others: table ? null : othersOf(el),
            };
            if (!table) {
                // The salon carries its tables: whoever stands inside travels along.
                const rr = { l: el.offsetLeft, t: el.offsetTop, w: el.offsetWidth, h: el.offsetHeight };
                drag.lastL = rr.l;
                drag.lastT = rr.t;
                drag.members = [...canvas.querySelectorAll('.oo-fp-table')]
                    .filter(tb => tb.offsetLeft >= rr.l && tb.offsetLeft <= rr.l + rr.w &&
                                  tb.offsetTop >= rr.t && tb.offsetTop <= rr.t + rr.h)
                    .map(tb => ({ el: tb, x: tb.offsetLeft, y: tb.offsetTop }));
            }
        });

        canvas.addEventListener('pointermove', e => {
            if (!drag) return;
            const r = canvas.getBoundingClientRect();

            if (drag.kind === 'draw') {
                let x1 = e.clientX - r.left, y1 = e.clientY - r.top;
                // The stroke stops at the first salon wall it reaches.
                for (const o of drag.others) {
                    const yLo = Math.min(drag.y0, y1), yHi = Math.max(drag.y0, y1);
                    const xLo = Math.min(drag.x0, x1), xHi = Math.max(drag.x0, x1);
                    if (yLo < o.t + o.h + M && yHi > o.t - M) {
                        if (o.l >= drag.x0 && x1 > o.l - M) x1 = o.l - M;
                        if (o.l + o.w <= drag.x0 && x1 < o.l + o.w + M) x1 = o.l + o.w + M;
                    }
                    if (xLo < o.l + o.w + M && xHi > o.l - M) {
                        if (o.t >= drag.y0 && y1 > o.t - M) y1 = o.t - M;
                        if (o.t + o.h <= drag.y0 && y1 < o.t + o.h + M) y1 = o.t + o.h + M;
                    }
                }
                if (Math.abs(x1 - drag.x0) + Math.abs(y1 - drag.y0) > 6) drag.moved = true;
                const left = Math.max(Math.min(drag.x0, x1), 0);
                const top = Math.max(Math.min(drag.y0, y1), 0);
                drag.draft.style.left = left + 'px';
                drag.draft.style.top = top + 'px';
                drag.draft.style.width = Math.min(Math.abs(x1 - drag.x0), r.width - left) + 'px';
                drag.draft.style.height = Math.min(Math.abs(y1 - drag.y0), r.height - top) + 'px';
                return;
            }

            const dx = e.clientX - drag.startX;
            const dy = e.clientY - drag.startY;
            if (!drag.moved && Math.abs(dx) + Math.abs(dy) < 7) return;
            drag.moved = true;
            drag.el.classList.add('dragging');

            if (drag.kind === 'resize') {
                const L = drag.el.offsetLeft, T = drag.el.offsetTop;
                let w = Math.min(Math.max(drag.origW + dx, 50), r.width - L);
                let h = Math.min(Math.max(drag.origH + dy, 44), r.height - T);
                // Growth stops where a neighbouring salon begins.
                for (const o of drag.others) {
                    if (o.l - M >= L && T < o.t + o.h + M && T + h > o.t - M)
                        w = Math.min(w, o.l - M - L);
                }
                for (const o of drag.others) {
                    if (o.t - M >= T && L < o.l + o.w + M && L + w > o.l - M)
                        h = Math.min(h, o.t - M - T);
                }
                drag.el.style.width = Math.max(w, 50) + 'px';
                drag.el.style.height = Math.max(h, 44) + 'px';
                return;
            }

            if (drag.kind === 'tresize') {
                // In RTL the grip lives on the visual left, so the drag reads mirrored.
                const wd = drag.rtl ? -dx : dx;
                // Inside a salon the table may grow only until its chairs touch the wall.
                let maxW = 480, maxH = 480;
                const home = roomAt(drag.rooms, drag.cx, drag.cy);
                if (home) {
                    maxW = Math.min(maxW, 2 * Math.min(drag.cx - home.l - M, home.l + home.w - drag.cx - M));
                    maxH = Math.min(maxH, 2 * Math.min(drag.cy - home.t - M, home.t + home.h - drag.cy - M));
                }
                drag.el.style.width = Math.min(Math.max(drag.origW + wd, 60), Math.max(maxW, 60)) + 'px';
                drag.el.style.height = Math.min(Math.max(drag.origH + dy, 52), Math.max(maxH, 52)) + 'px';
                drag.el.style.minHeight = '0';
                return;
            }

            if (drag.kind === 'table') {
                const w = drag.el.offsetWidth, h = drag.el.offsetHeight;
                let left = Math.min(Math.max(drag.origLeft + dx, w * -0.2), r.width - w * 0.6);
                let top = Math.min(Math.max(drag.origTop + dy, 0), r.height - h * 0.7);
                // A salon is a container: while the table's center is inside one,
                // the whole box — chairs included — stays inside its walls.
                const home = roomAt(drag.rooms, left + w / 2, top + h / 2);
                if (home) {
                    left = home.w >= w + 2 * M
                        ? Math.min(Math.max(left, home.l + M), home.l + home.w - w - M)
                        : home.l + (home.w - w) / 2;
                    top = home.h >= h + 2 * M
                        ? Math.min(Math.max(top, home.t + M), home.t + home.h - h - M)
                        : home.t + (home.h - h) / 2;
                }
                drag.el.style.left = left + 'px';
                drag.el.style.top = top + 'px';
                // While dragging, position is raw pixels; the centering transform
                // that normally anchors (X%, Y%) must not double-shift it.
                drag.el.style.transform = 'none';
                return;
            }

            // Room drag: top-left anchored, kept fully inside the canvas — and a
            // salon NEVER crosses another salon: a blocked axis simply stops,
            // so the room slides along walls instead of passing through them.
            let left = Math.min(Math.max(drag.origLeft + dx, 0), r.width - drag.el.offsetWidth);
            let top = Math.min(Math.max(drag.origTop + dy, 0), r.height - drag.el.offsetHeight);
            const rw = drag.el.offsetWidth, rh = drag.el.offsetHeight;
            const collides = (l, t) => drag.others.some(o =>
                l < o.l + o.w + M && l + rw > o.l - M &&
                t < o.t + o.h + M && t + rh > o.t - M);
            if (collides(left, top)) {
                if (!collides(left, drag.lastT)) top = drag.lastT;
                else if (!collides(drag.lastL, top)) left = drag.lastL;
                else { left = drag.lastL; top = drag.lastT; }
            }
            drag.lastL = left;
            drag.lastT = top;
            drag.el.style.left = left + 'px';
            drag.el.style.top = top + 'px';
            // The tables inside ride along, live.
            const shiftX = left - drag.origLeft, shiftY = top - drag.origTop;
            for (const m of drag.members) {
                m.el.style.left = (m.x + shiftX) + 'px';
                m.el.style.top = (m.y + shiftY) + 'px';
            }
        });

        const finish = e => {
            if (!drag) return;
            const d = drag;
            drag = null;
            const r = canvas.getBoundingClientRect();

            if (d.kind === 'draw') {
                const w = pct(d.draft.offsetWidth, r.width);
                const h = pct(d.draft.offsetHeight, r.height);
                const x = pct(d.draft.offsetLeft, r.width);
                const y = pct(d.draft.offsetTop, r.height);
                d.draft.remove();
                // A real sketch, not a stray tap — anything tinier than 6% is noise.
                if (d.moved && w > 6 && h > 6) dotnetRef.invokeMethodAsync('OnRoomDrawn', x, y, w, h);
                else dotnetRef.invokeMethodAsync('OnDrawCancelled');
                return;
            }

            d.el.classList.remove('dragging');

            if (d.kind === 'resize') {
                if (d.moved) dotnetRef.invokeMethodAsync('OnRoomResized', d.id,
                    pct(d.el.offsetWidth, r.width), pct(d.el.offsetHeight, r.height));
                return;
            }

            if (d.kind === 'tresize') {
                if (d.moved) dotnetRef.invokeMethodAsync('OnTableResized', d.id,
                    Math.round(d.el.offsetWidth), Math.round(d.el.offsetHeight));
                return;
            }

            if (d.kind === 'table') {
                if (d.moved) {
                    let x = pct(d.el.offsetLeft + d.el.offsetWidth / 2, r.width);
                    let y = pct(d.el.offsetTop + d.el.offsetHeight / 2, r.height);
                    // Hand the element back to percentage positioning at the very spot
                    // it was dropped, so the render that follows changes nothing visually.
                    d.el.style.left = x + '%';
                    d.el.style.top = y + '%';
                    d.el.style.transform = '';
                    dotnetRef.invokeMethodAsync('OnTableMoved', d.id, x, y);
                } else {
                    dotnetRef.invokeMethodAsync('OnTableTapped', d.id);
                }
                return;
            }

            if (d.moved) {
                dotnetRef.invokeMethodAsync('OnRoomMoved', d.id,
                    pct(d.el.offsetLeft, r.width), pct(d.el.offsetTop, r.height));
            } else {
                dotnetRef.invokeMethodAsync('OnRoomTapped', d.id);
            }
        };

        canvas.addEventListener('pointerup', finish);
        canvas.addEventListener('pointercancel', () => {
            if (!drag) return;
            if (drag.kind === 'draw') drag.draft.remove();
            else drag.el.classList.remove('dragging');
            drag = null;
            dotnetRef.invokeMethodAsync('OnDragCancelled');
        });
    },
};

// Category strip arrows: glide the pill rail left/right with a smooth animation.
// dir is visual (-1 = left, +1 = right) and works the same in RTL.
window.mfPos.slideCats = function (selector, dir) {
    const el = document.querySelector(selector);
    if (!el) return;
    el.scrollBy({ left: dir * Math.max(160, el.clientWidth * 0.6), behavior: "smooth" });
};

// Scroll hint for the category strip: while there are still categories hidden past
// an edge, mark that edge so CSS can show a gently bouncing chevron there. It stops
// nagging the moment the row is scrolled all the way, and reappears if it can't be.
window.mfPos.catsHint = function (scrollSel, rowSel) {
    var el = document.querySelector(scrollSel);
    var row = document.querySelector(rowSel);
    if (!el || !row) return;
    var update = function () {
        // Works in both directions: |scrollLeft| grows toward the hidden side.
        var max = el.scrollWidth - el.clientWidth - 2;
        var pos = Math.abs(el.scrollLeft);
        row.classList.toggle("cats-more", max > 4 && pos < max);   // more the natural scroll way
        row.classList.toggle("cats-back", max > 4 && pos > 4);     // and some already behind
    };
    if (el._catsHint) el.removeEventListener("scroll", el._catsHint);
    el._catsHint = update;
    el.addEventListener("scroll", update, { passive: true });
    window.addEventListener("resize", update);
    // Categories arrive after the first render — settle, then measure.
    setTimeout(update, 60);
    setTimeout(update, 400);
};

// Global hotkeys for a page. F-keys fire everywhere; letters and symbols only when
// the user is not typing in a field — a cashier writing a note must never trigger
// "pay" with the letter p.
window.mfKeys = (function () {
    var bound = null;
    return {
        bind: function (ref, keys) {
            this.unbind();
            var wanted = {};
            (keys || []).forEach(function (k) { wanted[k] = true; });
            bound = function (e) {
                var key = e.key.length === 1 ? e.key.toLowerCase() : e.key;
                if (!wanted[key] && !wanted[e.key]) return;

                var tag = (e.target.tagName || "").toLowerCase();
                var typing = tag === "input" || tag === "textarea" || tag === "select" || e.target.isContentEditable;
                var isFnKey = /^F\d{1,2}$/.test(e.key);
                // Modifier combos belong to the browser; typed letters belong to the field.
                if (e.ctrlKey || e.altKey || e.metaKey) return;
                if (typing && !isFnKey) return;

                e.preventDefault();
                ref.invokeMethodAsync("OnHotkey", key);
            };
            window.addEventListener("keydown", bound, true);
        },
        unbind: function () {
            if (bound) { window.removeEventListener("keydown", bound, true); bound = null; }
        }
    };
})();

// Put the cursor at the END of a field — the scan-and-type box. Selecting the text
// instead would make the next keystroke erase what a hotkey just typed in.
window.mfPos.focus = function (selector) {
    var el = document.querySelector(selector);
    if (!el) return;
    el.focus();
    var n = (el.value || "").length;
    try { el.setSelectionRange(n, n); } catch (e) { }
};

// Close the numpad when the tap lands anywhere else — the pad and its code box are
// the only safe ground. Capture phase, so it sees the tap before Blazor does.
window.mfPos.padAutoClose = function (ref) {
    window.mfPos.padAutoCloseOff();
    window.__padClose = function (e) {
        if (e.target.closest(".pos-pad") || e.target.closest(".pos-code")) return;
        window.mfPos.padAutoCloseOff();
        ref.invokeMethodAsync("ClosePad");
    };
    document.addEventListener("pointerdown", window.__padClose, true);
};
window.mfPos.padAutoCloseOff = function () {
    if (window.__padClose) {
        document.removeEventListener("pointerdown", window.__padClose, true);
        window.__padClose = null;
    }
};

// Pin a floating pad just under its anchor box, in viewport coordinates. Fixed
// positioning dodges every z-index war the header's stacking contexts can start.
window.mfPos.placePad = function (anchorSel, padSel) {
    var anchor = document.querySelector(anchorSel);
    var pad = document.querySelector(padSel);
    if (!anchor || !pad) return;
    var r = anchor.getBoundingClientRect();
    var w = pad.offsetWidth || 230;
    var left = Math.max(8, Math.min(r.right - w, window.innerWidth - w - 8));
    pad.style.top = (r.bottom + 8) + "px";
    pad.style.left = left + "px";
};

// Keep a chat column pinned to its newest message.
window.mfPos.scrollBottom = function (selector) {
    const el = document.querySelector(selector);
    if (el) el.scrollTop = el.scrollHeight;
};

// On a phone the on-screen keyboard covers the bottom of the screen. The visual
// viewport shrinks but `position:fixed` does not, so the chat's input row ends up
// underneath the keyboard. Lift the panel by exactly how much is hidden.
window.mfPos.keyboardAware = function (selector) {
    const panel = document.querySelector(selector);
    if (!panel || !window.visualViewport || panel.dataset.kbBound) return;
    panel.dataset.kbBound = "1";

    const vv = window.visualViewport;
    const apply = function () {
        if (!panel.isConnected) return;
        // A panel the user has dragged owns its own position — never fight it.
        if (panel.classList.contains("dragged")) { panel.style.transform = ""; return; }

        const body = panel.querySelector(".pb-body");
        // A phone serving the desktop layout reports innerWidth 1280 but is still a
        // phone — the panel is full-screen there too, so it needs the same treatment.
        const fullScreen = window.innerWidth <= 720
            || document.documentElement.classList.contains("oo-desktop-mode");
        if (fullScreen) {
            // Full-screen on a phone: match the VISIBLE viewport exactly, so the
            // keyboard and the address bar both simply shrink the panel instead of
            // hiding its bottom. Fixed positioning alone cannot see either of them.
            panel.style.transform = "";
            panel.style.height = vv.height + "px";
            panel.style.top = vv.offsetTop + "px";
            panel.style.bottom = "auto";
        } else {
            panel.style.height = "";
            panel.style.top = "";
            panel.style.bottom = "";
            const hidden = Math.max(0, window.innerHeight - vv.height - vv.offsetTop);
            panel.style.transform = hidden > 40 ? "translateY(-" + hidden + "px)" : "";
        }
        if (body) body.scrollTop = body.scrollHeight;
    };
    vv.addEventListener("resize", apply);
    vv.addEventListener("scroll", apply);
    // Rotating the phone changes which layout applies — re-measure for the new one.
    window.addEventListener("resize", apply);
    window.addEventListener("orientationchange", function () { setTimeout(apply, 120); });
    apply();
};

// Drag a floating panel by its title bar. Position is remembered for the session,
// so the assistant stays where the owner parked it while they work.
window.mfPos.draggable = function (panelSel, handleSel, memoryKey) {
    const panel = document.querySelector(panelSel);
    if (!panel) return;
    const handle = panel.querySelector(handleSel);
    // A panel with no handle can never be dragged, but it still has to be seen.
    if (!handle || handle.dataset.dragBound) { panel.classList.add("pb-ready"); return; }
    handle.dataset.dragBound = "1";

    const place = function (left, top) {
        const w = panel.offsetWidth, h = panel.offsetHeight;
        // Keep it on screen: a panel dragged off the edge can never be grabbed back.
        left = Math.max(6, Math.min(left, window.innerWidth - w - 6));
        top = Math.max(6, Math.min(top, window.innerHeight - h - 6));
        panel.classList.add("dragged");
        panel.style.transform = "";
        panel.style.insetInlineStart = "auto";
        panel.style.insetInlineEnd = "auto";
        panel.style.bottom = "auto";
        panel.style.left = left + "px";
        panel.style.top = top + "px";
        return { left: left, top: top };
    };

    // Restore where they left it — but only on a real desktop, where the panel floats.
    const floats = window.innerWidth > 720
        && !document.documentElement.classList.contains("oo-desktop-mode");
    if (memoryKey && floats) {
        try {
            const saved = JSON.parse(sessionStorage.getItem(memoryKey) || "null");
            if (saved) place(saved.left, saved.top);
        } catch (e) { /* a corrupt memory is not worth a broken panel */ }
    }

    // Only now is the panel where it belongs. Until this class lands the stylesheet
    // keeps it invisible, because Blazor paints the element at its CSS default one
    // round trip before this code runs — without the gate the user watches the panel
    // appear in the corner and then jump to wherever they last dragged it.
    panel.classList.add("pb-ready");

    let startX = 0, startY = 0, baseLeft = 0, baseTop = 0, dragging = false;

    handle.addEventListener("pointerdown", function (e) {
        // The close button lives inside the handle — never start a drag on it.
        if (e.target.closest(".pb-x")) return;
        // Phone panels span the screen — dragging one could only hide it.
        if (window.innerWidth <= 720) return;
        if (document.documentElement.classList.contains("oo-desktop-mode")) return;
        if (e.button !== 0 && e.pointerType === "mouse") return;

        const box = panel.getBoundingClientRect();
        baseLeft = box.left; baseTop = box.top;
        startX = e.clientX; startY = e.clientY;
        dragging = true;
        handle.setPointerCapture(e.pointerId);
        handle.classList.add("dragging");
        panel.classList.add("dragging");
        e.preventDefault();
    });

    handle.addEventListener("pointermove", function (e) {
        if (!dragging) return;
        place(baseLeft + (e.clientX - startX), baseTop + (e.clientY - startY));
    });

    const end = function (e) {
        if (!dragging) return;
        dragging = false;
        handle.classList.remove("dragging");
        panel.classList.remove("dragging");
        try { handle.releasePointerCapture(e.pointerId); } catch (err) { }
        if (memoryKey) {
            const box = panel.getBoundingClientRect();
            try { sessionStorage.setItem(memoryKey, JSON.stringify({ left: box.left, top: box.top })); } catch (err) { }
        }
    };
    handle.addEventListener("pointerup", end);
    handle.addEventListener("pointercancel", end);

    // A window that shrinks must not strand the panel outside the viewport.
    window.addEventListener("resize", function () {
        if (!panel.isConnected || !panel.classList.contains("dragged")) return;
        const box = panel.getBoundingClientRect();
        place(box.left, box.top);
    });
};
