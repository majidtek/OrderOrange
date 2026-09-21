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

    root.querySelector('.mf-slider-btn.prev').addEventListener('click', function () { go(at - 1, true); });
    root.querySelector('.mf-slider-btn.next').addEventListener('click', function () { go(at + 1, true); });
    root.addEventListener('mouseenter', function () { paused = true; restartBar(); });
    root.addEventListener('mouseleave', function () { paused = false; restartBar(); });
    root.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowRight') { go(at + 1, true); e.preventDefault(); }
      if (e.key === 'ArrowLeft') { go(at - 1, true); e.preventDefault(); }
    });

    // swipe (pointer events cover touch and mouse)
    var x0 = null;
    track.addEventListener('pointerdown', function (e) { x0 = e.clientX; paused = true; });
    track.addEventListener('pointerup', function (e) {
      if (x0 === null) return;
      var dx = e.clientX - x0; x0 = null; paused = false;
      if (Math.abs(dx) > 40) go(at + (dx < 0 ? 1 : -1), true); else restartBar();
    });
    track.addEventListener('pointercancel', function () { x0 = null; paused = false; restartBar(); });

    // only run the clock while the slider is on screen
    if ('IntersectionObserver' in window) {
      new IntersectionObserver(function (es) {
        es.forEach(function (en) { if (en.isIntersecting) { arm(); restartBar(); } else { clearInterval(timer); } });
      }, { threshold: 0.3 }).observe(root);
    } else arm();
    paint();
  }
  return { init: init };
})();
