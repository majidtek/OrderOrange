// Replaces flat system emoji with Microsoft Fluent 3D artwork (img/emoji3d/*)
// inside the icon containers of the customer app. Unmapped emoji keep the
// system glyph. Survives Blazor re-renders via MutationObserver.
(function () {
    'use strict';

    var MAP = {
        '🍕': 'pizza', '🍔': 'hamburger', '🌯': 'burrito', '🌮': 'taco', '🍣': 'sushi',
        '🍟': 'french-fries', '🍗': 'poultry-leg', '🍖': 'meat-on-bone', '🥗': 'green-salad',
        '🍰': 'shortcake', '🎂': 'birthday-cake', '🍩': 'doughnut', '🍪': 'cookie',
        '☕': 'hot-beverage', '🥤': 'cup-with-straw', '🧋': 'bubble-tea', '🍚': 'cooked-rice',
        '🍛': 'curry-rice', '🍜': 'steaming-bowl', '🍝': 'spaghetti', '🥘': 'shallow-pan-of-food',
        '🍲': 'pot-of-food', '🥙': 'stuffed-flatbread', '🧆': 'falafel', '🥪': 'sandwich',
        '🌭': 'hot-dog', '🍦': 'soft-ice-cream', '🍨': 'ice-cream', '🥞': 'pancakes', '🧇': 'waffle',
        '🍎': 'red-apple', '🍇': 'grapes', '🥭': 'mango', '🍉': 'watermelon', '🍌': 'banana',
        '🍹': 'tropical-drink', '🧃': 'beverage-box', '💐': 'bouquet', '🌹': 'rose',
        '🌸': 'cherry-blossom', '🌷': 'tulip', '🌻': 'sunflower', '💊': 'pill',
        '🛒': 'shopping-cart', '🛍': 'shopping-bags', '🏪': 'convenience-store',
        '🍽': 'fork-and-knife-with-plate', '❤': 'red-heart', '🍳': 'cooking', '🥩': 'cut-of-meat',
        '🍤': 'fried-shrimp', '🥐': 'croissant', '🍞': 'bread', '🧁': 'cupcake',
        '🍫': 'chocolate-bar', '🍿': 'popcorn', '🥟': 'dumpling', '🍱': 'bento-box',
        '🍧': 'shaved-ice', '🥣': 'bowl-with-spoon', '🫖': 'teapot', '🍵': 'teacup-without-handle',
        '🥑': 'avocado', '🍊': 'tangerine', '🍓': 'strawberry', '🥦': 'broccoli',
        '🧀': 'cheese-wedge', '🥚': 'egg', '🍯': 'honey-pot', '🥜': 'peanuts',
        '🦐': 'shrimp', '🦞': 'lobster', '🐟': 'fish'
    };

    var TARGETS = '.mf-cuisine-circle, .mf-menu-emoji, .mf-hit-emoji, .mf-rest-banner, ' +
                  '.mf-vert-hero-emoji, .mf-again-card > div:first-child, .mf-loader-core';

    function slugFor(text) {
        // strip variation selectors / zero-width joiners before matching
        var key = text.replace(/[︀-️‍]/g, '');
        return MAP[key] || null;
    }

    function swap(el) {
        if (el.dataset.mf3d) return;
        if (el.childElementCount > 0) { el.dataset.mf3d = 'n'; return; } // only pure-text nodes
        var text = (el.textContent || '').trim();
        var slug = slugFor(text);
        el.dataset.mf3d = slug ? 'y' : 'n';
        if (!slug) return;
        var img = document.createElement('img');
        img.src = 'img/emoji3d/' + slug + '.png';
        img.alt = text;
        img.className = 'mf-emoji3d';
        img.loading = 'lazy';
        img.onerror = function () { el.textContent = text; }; // fall back to the glyph
        el.textContent = '';
        el.appendChild(img);
    }

    function scan(root) {
        var scope = root && root.querySelectorAll ? root : document;
        scope.querySelectorAll(TARGETS).forEach(swap);
    }

    function boot() {
        scan(document);
        new MutationObserver(function () {
            clearTimeout(boot._t);
            boot._t = setTimeout(function () { scan(document); }, 80);
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
