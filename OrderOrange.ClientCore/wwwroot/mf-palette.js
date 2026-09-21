// The global shortcut that opens the palette. The C# side has always called
// mfHotkey.bind, but nothing ever defined it — the call threw into an empty catch, so
// Ctrl+K did nothing while the palette's own footer advertised it.
window.mfHotkey = {
    bind(dotnet) {
        // A Blazor Server circuit can reconnect and hand over a fresh reference. Keep
        // the listener single and swap the reference underneath it, or every reconnect
        // adds another handler and one keypress opens the palette N times.
        window.__mfHotkeyRef = dotnet;
        if (window.__mfHotkeyBound) return;
        window.__mfHotkeyBound = true;

        const open = (e) => {
            e.preventDefault();
            const ref = window.__mfHotkeyRef;
            // The reference dies with the circuit; a dead one must not throw into the
            // page's global error handler.
            if (ref) { try { ref.invokeMethodAsync('OpenFromHotkey'); } catch (_) { } }
        };

        const typingIn = (el) =>
            !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable);

        document.addEventListener('keydown', (e) => {
            const key = (e.key || '').toLowerCase();

            // Ctrl+K, and Cmd+K for anyone on a Mac.
            if ((e.ctrlKey || e.metaKey) && key === 'k') { open(e); return; }

            // "/" is the other habit people bring with them — but only when they are
            // not already typing into something.
            if (key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !typingIn(e.target)) open(e);
        });

        // Arrow-key selection versus a parked mouse pointer. The list re-renders under a
        // stationary cursor, that cursor lands on some row, mouseenter fires, and the
        // keyboard's choice is silently overwritten. So rows only accept the pointer once
        // the pointer has actually moved; any arrow key hands control back to the keyboard.
        // Done in CSS classes rather than C# because a mousemove handler on a Blazor
        // Server circuit would be a network round trip per pixel.
        const card = () => document.querySelector('.cpx');
        document.addEventListener('mousemove', () => {
            const el = card();
            if (el && !el.classList.contains('mouse')) el.classList.add('mouse');
        }, { passive: true });
        document.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Home' || e.key === 'End') {
                card()?.classList.remove('mouse');
            }
        });
    },
};

// Two jobs the command palette cannot do from C#: put the caret in the box, and keep
// the highlighted row on screen while the arrow keys walk past the fold.
window.mfPalette = {
    focus() {
        // The input is created in the same render that asks for focus, so one frame of
        // patience is required before it exists to be focused.
        requestAnimationFrame(() => {
            const el = document.getElementById('cp-in');
            if (el) { el.focus(); el.setSelectionRange(el.value.length, el.value.length); }
        });
    },

    scrollToSelected() {
        const row = document.querySelector('.cpx-row.on');
        if (!row) return;
        const list = row.closest('.cpx-body');
        if (!list) return;

        const rowTop = row.offsetTop;
        const rowBottom = rowTop + row.offsetHeight;
        const viewTop = list.scrollTop;
        const viewBottom = viewTop + list.clientHeight;

        // A section heading sits directly above the first row of its group; scrolling to
        // the row alone would hide it, so give the top edge a little headroom.
        if (rowTop < viewTop + 28) list.scrollTop = Math.max(0, rowTop - 34);
        else if (rowBottom > viewBottom) list.scrollTop = rowBottom - list.clientHeight + 8;
    },
};
