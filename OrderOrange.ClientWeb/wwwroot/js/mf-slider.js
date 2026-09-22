// A small slideshow for the /pos page: sliding track, dots, thumbnails, arrows, swipe,
// keyboard, autoplay with a progress bar that pauses while the pointer is on it.
// No library — the page is server-rendered and this only decorates what is already there.
window.mfSlider = (function () {
  function init(id) {
    var root = document.getElementById(id);
    if (!root || root.dataset.ready) return;
    root.dataset.ready = '1';
    var track = root.querySelector('.mf-slider-track');
    var slides = Array.prototype.slice.call(track.children);
    var dots = root.querySelector('.mf-slider-dots');
    var thumbs = Array.prototype.slice.call(root.querySelectorAll('.mf-thumb'));
    var bar = root.querySelector('.mf-slider-bar i');
    var n = slides.length, at = 0, timer = null, paused = false, EVERY = 5000;

    slides.forEach(function (_, i) {
      var d = document.createElement('button');
      d.type = 'button'; d.className = 'mf-dot'; d.setAttribute('aria-label', 'Slide ' + (i + 1));
      d.addEventListener('click', function () { go(i, true); });
      dots.appendChild(d);
    });
    thumbs.forEach(function (t, i) { t.addEventListener('click', function () { go(i, true); }); });

    function paint() {
      if (!root.classList.contains('fade')) track.style.transform = 'translateX(' + (-at * 100) + '%)';
      slides.forEach(function (s, i) { s.classList.toggle('on', i === at); });
      Array.prototype.forEach.call(dots.children, function (d, i) { d.classList.toggle('on', i === at); });
      thumbs.forEach(function (t, i) { t.classList.toggle('on', i === at); });
      var onThumb = thumbs[at];
      if (onThumb && onThumb.scrollIntoView) onThumb.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' });
      restartBar();
    }
    // never show a black frame: switch only once the picture has arrived
    function whenReady(i, fn) {
      var img = slides[(i + n) % n].querySelector('img');
      if (!img || img.complete) { fn(); return; }
      var done = false, fin = function () { if (!done) { done = true; fn(); } };
      img.addEventListener('load', fin); img.addEventListener('error', fin);
      setTimeout(fin, 4000);
    }
    function go(i, byHand) { whenReady(i, function () { at = (i + n) % n; paint(); if (byHand) arm(); }); }
    function next() { go(at + 1); }
    function restartBar() {
      if (!bar) return;
      bar.style.transition = 'none'; bar.style.width = '0';
      void bar.offsetWidth;
      if (!paused) { bar.style.transition = 'width ' + EVERY + 'ms linear'; bar.style.width = '100%'; }
    }
    function arm() { clearInterval(timer); timer = setInterval(function () { if (!paused) next(); }, EVERY); }

    // the whole show, full screen — tapping a slide, the ⛶ button; ✕ or Esc to leave
    function setFull(on) {
      root.classList.toggle('full', on);
      document.documentElement.classList.toggle('mf-noscroll', on);
      if (on) root.focus();
      paint();
    }
    var fullBtn = root.querySelector('.mf-slider-full'), closeBtn = root.querySelector('.mf-slider-close');
    if (fullBtn) fullBtn.addEventListener('click', function () { setFull(!root.classList.contains('full')); });
    if (closeBtn) closeBtn.addEventListener('click', function () { setFull(false); });
    // a tap on a phone only turns/holds the slide — full screen is the ⛶ button there;
    // a mouse click on a slide still opens it
    var touchAt = 0;
    root.addEventListener('touchstart', function () { touchAt = Date.now(); }, { passive: true });
    slides.forEach(function (s) {
      var a = s.querySelector('a');
      if (a) a.addEventListener('click', function (e) { e.preventDefault(); if (Date.now() - touchAt < 1200) return; setFull(true); });
    });
    root.addEventListener('keydown', function (e) { if (e.key === 'Escape') setFull(false); });

    root.querySelector('.mf-slider-btn.prev').addEventListener('click', function () { go(at - 1, true); });
    root.querySelector('.mf-slider-btn.next').addEventListener('click', function () { go(at + 1, true); });
    root.addEventListener('mouseenter', function () { paused = true; restartBar(); });
    root.addEventListener('mouseleave', function () { paused = false; restartBar(); });
    root.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowRight') { go(at + 1, true); e.preventDefault(); }
      if (e.key === 'ArrowLeft') { go(at - 1, true); e.preventDefault(); }
    });

    // swipe: touch events first (a finger that scrolls the page never turns a slide), mouse via pointer events
    var x0 = null, y0 = null, swiped = false;
    function swipeStart(x, y) { x0 = x; y0 = y; swiped = false; paused = true; }
    function swipeEnd(x, y) {
      if (x0 === null) return;
      var dx = x - x0, dy = y - y0; x0 = y0 = null; paused = false;
      if (Math.abs(dx) > 40 && Math.abs(dx) > Math.abs(dy) * 1.2) { swiped = true; go(at + (dx < 0 ? 1 : -1), true); }
      else restartBar();
    }
    track.addEventListener('touchstart', function (e) { var t = e.touches[0]; swipeStart(t.clientX, t.clientY); }, { passive: true });
    track.addEventListener('touchend', function (e) { var t = e.changedTouches[0]; swipeEnd(t.clientX, t.clientY); }, { passive: true });
    track.addEventListener('touchcancel', function () { x0 = y0 = null; paused = false; restartBar(); }, { passive: true });
    track.addEventListener('mousedown', function (e) { swipeStart(e.clientX, e.clientY); e.preventDefault(); });
    track.addEventListener('mouseup', function (e) { swipeEnd(e.clientX, e.clientY); });
    track.addEventListener('mouseleave', function () { if (x0 !== null) { x0 = y0 = null; paused = false; restartBar(); } });
    // a swipe must not also open the full-screen show
    track.addEventListener('click', function (e) { if (swiped) { e.preventDefault(); e.stopPropagation(); swiped = false; } }, true);

    // only run the clock while the slider is on screen
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (es) {
        es.forEach(function (en) { if (en.isIntersecting) { arm(); restartBar(); } else { clearInterval(timer); } });
      }, { threshold: 0.3 }).observe(root);
    } else arm();
    paint();
  }
  // sections drift in as they scroll into view (CSS only hides them once JS is present)
  function reveal() {
    document.documentElement.classList.add('js');
    // in-page anchors: the Blazor router would re-render the page and drop the hash, so
    // scroll ourselves (the href stays a real link for crawlers and no-JS browsers)
    if (!document.documentElement.dataset.anchors) {
      document.documentElement.dataset.anchors = '1';
      // on window, capture phase: that runs before Blazor's own document listener
      window.addEventListener('click', function (e) {
        var a = e.target.closest && e.target.closest('a[href*="#"]');
        if (!a) return;
        var href = a.getAttribute('href') || '', i = href.indexOf('#');
        if (i < 0) return;
        var path = href.slice(0, i);
        if (path && path !== location.pathname) return;
        var el = document.getElementById(href.slice(i + 1));
        if (!el) return;
        e.preventDefault(); e.stopPropagation();
        window.__anch = (window.__anch || 0) + 1;
        var pn = document.querySelector('.mf-posnav.open'); if (pn) { pn.classList.remove('open'); var bb = pn.querySelector('.mf-posnav-burger'); if (bb) bb.setAttribute('aria-expanded', 'false'); }
        var top = el.getBoundingClientRect().top + (window.pageYOffset || document.documentElement.scrollTop) - 70;
        window.scrollTo({ top: top, behavior: 'smooth' });
        try { history.replaceState(null, '', location.pathname + '#' + el.id); } catch (x) {}
      }, true);
    }
    // stagger: siblings that reveal together get a growing delay
    var groups = new Map();
    document.querySelectorAll('.mf-reveal').forEach(function (el) {
      var k = el.parentNode, n = groups.get(k) || 0; groups.set(k, n + 1); el.style.setProperty('--i', n);
    });
    // headline words rise one after another
    var h1 = document.querySelector('.mf-hero-text h1');
    if (h1 && !h1.dataset.split) {
      h1.dataset.split = '1';
      var words = h1.textContent.trim().split(/\s+/);
      h1.innerHTML = words.map(function (w, i) { return '<span class="w" style="--i:' + i + '">' + w + '</span>'; }).join(' ');
    }
    // nav condenses on scroll + reading progress + active section (scrollspy)
    var nav = document.querySelector('.mf-posnav');
    if (nav && !nav.dataset.live) {
      nav.dataset.live = '1';
      var bar = document.createElement('i'); bar.className = 'mf-posnav-progress'; nav.appendChild(bar);
      var links = Array.prototype.slice.call(nav.querySelectorAll('.links a[href*="#"]'));
      var targets = links.map(function (a) { var h = a.getAttribute('href'); return document.getElementById(h.slice(h.indexOf('#') + 1)); });
      function onScroll() {
        var y = window.pageYOffset || document.documentElement.scrollTop;
        nav.classList.toggle('scrolled', y > 24);
        var doc = document.documentElement, max = doc.scrollHeight - doc.clientHeight;
        bar.style.width = (max > 0 ? Math.min(100, y / max * 100) : 0) + '%';
        var current = -1;
        targets.forEach(function (t, i) { if (t && t.getBoundingClientRect().top - 120 <= 0) current = i; });
        links.forEach(function (a, i) { a.classList.toggle('on', i === current); });
      }
      window.addEventListener('scroll', onScroll, { passive: true }); onScroll();
      // phone menu
      var burger = nav.querySelector('.mf-posnav-burger');
      if (burger) {
        burger.addEventListener('click', function () { var open = nav.classList.toggle('open'); burger.setAttribute('aria-expanded', open ? 'true' : 'false'); });
        nav.querySelectorAll('.links a').forEach(function (a) { a.addEventListener('click', function () { nav.classList.remove('open'); burger.setAttribute('aria-expanded', 'false'); }); });
        document.addEventListener('click', function (e) { if (!nav.contains(e.target)) { nav.classList.remove('open'); burger.setAttribute('aria-expanded', 'false'); } });
      }
    }
    var els = document.querySelectorAll('.mf-reveal');
    if (!('IntersectionObserver' in window)) { els.forEach(function (e) { e.classList.add('in'); }); return; }
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (en) { if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); } });
    }, { threshold: 0.12 });
    els.forEach(function (e) { io.observe(e); });
  }
  return { init: init, reveal: reveal };
})();
