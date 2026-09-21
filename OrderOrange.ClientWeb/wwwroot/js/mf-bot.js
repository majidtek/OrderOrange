// Assistant chat helpers: keep the newest message in view.
window.mfBot = {
    scroll(id) {
        var el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    }
};
