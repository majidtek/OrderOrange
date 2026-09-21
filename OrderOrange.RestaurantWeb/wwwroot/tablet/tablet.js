/* OrderOrange — the tablet page for old browsers.
   Plain ES5 and jQuery 1.12: no arrow functions, no let/const, no template strings, no
   fetch, no Promise, no Array.find. Everything talks to the same API the partner app uses.
   The token lives in localStorage; a 401 anywhere sends the tablet back to the sign-in.
   Photos arrive one category at a time, after the menu, so the first screen is fast. */
(function ($) {
  'use strict';

  var API = (function () {
    var m = /[?&]api=([^&]+)/.exec(window.location.search);
    var base = m ? decodeURIComponent(m[1]) : 'https://api.orderorange.com/';
    return base.charAt(base.length - 1) === '/' ? base : base + '/';
  })();

  // ---------------------------------------------------------------- words
  var T = {
    en: { ingredients: 'Ingredients', popular: 'Popular', lang: 'Language', loginSub: 'Tablet for the floor', user: 'Username or email', pass: 'Password', signIn: 'Sign in',
      pickStore: 'Which business?', logout: 'Sign out', whereIs: 'Where is this order?', walkIn: 'Walk-in / Takeaway',
      tables: 'Tables', seats: 'seats', busy: 'occupied', order: 'Order', tapDish: 'Tap a dish to add it.',
      total: 'Total', sendKitchen: 'Send to kitchen', paidCash: 'Paid · cash', paidCard: 'Paid · card', clear: 'Clear',
      sent: 'Sent', newOrder: 'New order', wait: 'Please wait…', search: 'Search dishes…', orderNote: 'Note for the kitchen (optional)',
      all: 'All', badLogin: 'Wrong username or password.', offline: 'No connection. Try again.', noStore: 'This account has no business.',
      noMenu: 'The menu is empty.', noHits: 'Nothing like that on the menu.', lineNote: 'Note for this dish (e.g. no onion):', table: 'Table', takeaway: 'Takeaway',
      sentTo: 'sent to the kitchen', paid: 'paid', unpaid: 'to be paid', empty: 'Add something first.', viewOrder: 'View order',
      customer: 'Customer', noCustomer: 'Walk-in', noCustomerSub: 'No customer on this order', searchCust: 'Name or phone…',
      addCustomer: 'Add a new customer', name: 'Name', phone: 'Phone', save: 'Save and use', needName: 'Name and phone, please.',
      noMatch: 'Nobody matches. Add them below.', items: 'items', results: 'results', note: 'Note', addNote: 'Add a note', notePh: 'e.g. no onion, extra spicy…', done: 'Done', clearOrder: '🗑 Clear order', clearSure: 'Tap again to clear the order', open: 'Open', openInvoices: 'Open invoices', noOpen: 'No open invoices right now.', editing: 'Editing', stopEdit: 'Stop editing', saveChanges: 'Save changes', saved: 'Invoice updated', itemsN: 'items', unpaid: 'unpaid', addToOrder: 'Add to order', zoomHint: 'Tap the picture to zoom', pickSub: 'Choose where this tablet works today', noteChips: ['No onion', 'No ice', 'Extra spicy', 'Less spicy', 'Well done', 'Extra sauce', 'No sugar', 'Takeaway box'] },
    ar: { ingredients: 'المكونات', popular: 'الأكثر طلباً', lang: 'اللغة', loginSub: 'جهاز الصالة', user: 'اسم المستخدم أو البريد', pass: 'كلمة المرور', signIn: 'دخول',
      pickStore: 'أي نشاط؟', logout: 'خروج', whereIs: 'أين هذا الطلب؟', walkIn: 'زبون عابر / سفري',
      tables: 'الطاولات', seats: 'مقاعد', busy: 'مشغولة', order: 'الطلب', tapDish: 'اضغط على طبق لإضافته.',
      total: 'الإجمالي', sendKitchen: 'إرسال إلى المطبخ', paidCash: 'مدفوع · نقداً', paidCard: 'مدفوع · بطاقة', clear: 'مسح',
      sent: 'تم الإرسال', newOrder: 'طلب جديد', wait: 'انتظر من فضلك…', search: 'ابحث عن طبق…', orderNote: 'ملاحظة للمطبخ (اختياري)',
      all: 'الكل', badLogin: 'اسم المستخدم أو كلمة المرور غير صحيحة.', offline: 'لا يوجد اتصال. حاول مرة أخرى.', noStore: 'لا يوجد نشاط لهذا الحساب.',
      noMenu: 'القائمة فارغة.', noHits: 'لا شيء مشابه في القائمة.', lineNote: 'ملاحظة لهذا الطبق (مثلاً: بدون بصل):', table: 'طاولة', takeaway: 'سفري',
      sentTo: 'أُرسل إلى المطبخ', paid: 'مدفوع', unpaid: 'للدفع لاحقاً', empty: 'أضف شيئاً أولاً.', viewOrder: 'عرض الطلب',
      customer: 'الزبون', noCustomer: 'زبون عابر', noCustomerSub: 'بدون زبون على هذا الطلب', searchCust: 'الاسم أو الهاتف…',
      addCustomer: 'إضافة زبون جديد', name: 'الاسم', phone: 'الهاتف', save: 'حفظ واستخدام', needName: 'الاسم والهاتف من فضلك.',
      noMatch: 'لا أحد يطابق. أضفه بالأسفل.', items: 'أصناف', results: 'نتائج', note: 'ملاحظة', addNote: 'أضف ملاحظة', notePh: 'مثلاً: بدون بصل، حار زيادة…', done: 'تم', clearOrder: '🗑 مسح الطلب', clearSure: 'اضغط مرة أخرى لمسح الطلب', open: 'المفتوحة', openInvoices: 'الفواتير المفتوحة', noOpen: 'لا فواتير مفتوحة الآن.', editing: 'تعديل', stopEdit: 'إيقاف التعديل', saveChanges: 'حفظ التعديلات', saved: 'تم تحديث الفاتورة', itemsN: 'أصناف', unpaid: 'غير مدفوعة', addToOrder: 'أضف إلى الطلب', zoomHint: 'اضغط على الصورة للتكبير', pickSub: 'اختر أين يعمل هذا الجهاز اليوم', noteChips: ['بدون بصل', 'بدون ثلج', 'حار زيادة', 'حار أقل', 'ناضج جيداً', 'صوص زيادة', 'بدون سكر', 'علبة سفري'] }
  };
  if (window.TABLET_T) for (var lc in window.TABLET_T) T[lc] = window.TABLET_T[lc];
  var LANGS = [['en', 'English'], ['ar', 'العربية'], ['fa', 'فارسی'], ['ur', 'اردو'], ['hi', 'हिन्दी'], ['tr', 'Türkçe'], ['de', 'Deutsch'],
    ['fr', 'Français'], ['es', 'Español'], ['it', 'Italiano'], ['pt', 'Português'], ['ru', 'Русский'], ['ja', '日本語'], ['zh', '中文']];
  var RTL = { ar: 1, fa: 1, ur: 1 };
  var lang = localStorage.getItem('oo.tablet.lang') || 'en';
  if (!T[lang]) lang = 'en';
  function langName(code) { for (var i = 0; i < LANGS.length; i++) if (LANGS[i][0] === code) return LANGS[i][1]; return code; }
  function t(k) { return (T[lang] && T[lang][k]) || T.en[k] || k; }
  function applyLang() {
    $('html').attr('lang', lang).attr('dir', RTL[lang] ? 'rtl' : 'ltr');
    $('.lang-cur').text(langName(lang)); $('.lang-code').text(lang.toUpperCase());
    $('#lang-list .lang-row').removeClass('on').filter('[data-lang="' + lang + '"]').addClass('on');
    $('[data-t]').each(function () { $(this).text(t($(this).attr('data-t'))); });
    $('[data-tp]').each(function () { $(this).attr('placeholder', t($(this).attr('data-tp'))); });
    localStorage.setItem('oo.tablet.lang', lang);
  }
  function nameOf(x) { return (x && x.names && x.names[lang]) || (x && x.name) || ''; }
  function descOf(x) { return (x && x.descriptions && x.descriptions[lang]) || (x && x.description) || ''; }

  // ---------------------------------------------------------------- state
  var S = {
    token: localStorage.getItem('oo.tablet.token') || '',
    storeName: localStorage.getItem('oo.tablet.store') || '',
    currency: localStorage.getItem('oo.tablet.cur') || 'OMR',
    menu: [], catId: 0, tableId: 0, tableName: '',
    customers: null, customerId: 0, customerName: '',
    editId: 0, editNumber: '', editLines: null,   // the invoice being edited, and its lines as loaded
    photos: {}, photoCats: {},   // itemId -> data URI; catId -> 'loading' | 'done'
    fullPhotos: {},              // itemId -> [data URIs] at full size, once asked for
    cart: {},  // itemId -> { item, qty, note }
    order: []  // itemIds in the order they were added
  };
  function money(n) { return (Math.round(n * 1000) / 1000).toFixed(3) + ' ' + S.currency; }
  function priceOf(it) { return it.finalPrice != null ? it.finalPrice : it.price; }

  // ---------------------------------------------------------------- api
  function call(method, path, body, ok, fail) {
    $.ajax({
      url: API + path, type: method, dataType: 'json', cache: false, timeout: 60000,
      contentType: 'application/json', data: body === undefined ? undefined : JSON.stringify(body),
      headers: S.token ? { Authorization: 'Bearer ' + S.token } : {},
      success: function (data) { ok(data); },
      error: function (xhr) {
        if (xhr.status === 401 && path.indexOf('auth/login') < 0) { signOut(); return; }
        var msg = '';
        try { msg = JSON.parse(xhr.responseText).message || ''; } catch (e) { msg = ''; }
        if (!msg) msg = xhr.status === 0 ? t('offline') : ('Error ' + xhr.status);
        if (fail) fail(msg, xhr.status); else alert(msg);
      }
    });
  }
  function busy(on) { $('#busy').toggleClass('on', !!on); }
  function show(id) { $('.scr').removeClass('on'); $('#' + id).addClass('on'); window.scrollTo(0, 0); }

  // ---------------------------------------------------------------- sign in
  function signOut() {
    S.token = ''; localStorage.removeItem('oo.tablet.token');
    show('scr-login');
  }
  $('#login-form').on('submit', function (e) {
    e.preventDefault();
    var u = $.trim($('#login-user').val()), p = $('#login-pass').val();
    if (!u || !p) return;
    $('#login-err').text(''); $('#login-btn').prop('disabled', true); busy(true);
    S.token = '';
    call('POST', 'api/auth/login', { email: u, password: p }, function (r) {
      busy(false); $('#login-btn').prop('disabled', false);
      S.token = r.token; localStorage.setItem('oo.tablet.token', r.token);
      $('#login-pass').val('');
      afterLogin(r);
    }, function (msg, status) {
      busy(false); $('#login-btn').prop('disabled', false);
      $('#login-err').text(status === 401 || status === 400 ? t('badLogin') : msg);
    });
  });

  // An owner with several businesses picks one; everyone else lands on the menu.
  function afterLogin(r) {
    call('GET', 'api/restaurants/my-stores', undefined, function (stores) {
      if (!stores || !stores.length) {
        if (r && r.restaurantId) { enterStore(r.restaurantName || ''); return; }
        $('#login-err').text(t('noStore')); signOut(); return;
      }
      if (stores.length === 1) { switchStore(stores[0]); return; }
      var $l = $('#store-list').empty();
      for (var i = 0; i < stores.length; i++) {
        (function (st) {
          var logo = st.logoData && (st.logoData.indexOf('data:') === 0 || st.logoData.indexOf('http') === 0) ? st.logoData : null;
          var $p = $('<a href="#" class="pick"><span class="pick-logo">' + (logo ? '' : (st.logoEmoji || '🏪')) + '</span><span class="pick-nm"></span><span class="pick-area"></span><span class="pick-go">›</span></a>');
          if (logo) $p.find('.pick-logo').addClass('has').css('background-image', 'url(' + logo + ')');
          $p.find('.pick-nm').text(st.name); $p.find('.pick-area').text(st.area || '');
          $p.on('click', function (e) { e.preventDefault(); switchStore(st); });
          $l.append($p);
        })(stores[i]);
      }
      show('scr-stores');
    }, function () { if (r && r.restaurantId) enterStore(r.restaurantName || ''); else signOut(); });
  }
  function switchStore(st) {
    busy(true);
    call('POST', 'api/auth/switch-store/' + st.id, {}, function (r) {
      busy(false);
      S.token = r.token; localStorage.setItem('oo.tablet.token', r.token);
      enterStore(r.restaurantName || st.name);
    }, function (msg) { busy(false); alert(msg); });
  }
  function enterStore(name) {
    S.storeName = name; localStorage.setItem('oo.tablet.store', name);
    S.photos = {}; S.photoCats = {}; S.customers = null;
    $('#pos-store').text(name);
    $('#pos-avatar').text((name || '?').charAt(0).toUpperCase());
    // the shop's own currency symbol, quietly; OMR until it answers
    call('GET', 'api/restaurants/mine', undefined, function (me) {
      var c = me && (me.currency || me.currencySymbol);
      if (c) { S.currency = c; localStorage.setItem('oo.tablet.cur', c); renderCart(); }
      if (me && me.name) { S.storeName = me.name; $('#pos-store').text(me.name); $('#pos-avatar').text(me.name.charAt(0).toUpperCase()); }
    }, function () {});
    loadMenu();
    loadTables();
    openPos(0, '');
    loadOpenInvoices(false);
  }

  // ---------------------------------------------------------------- table (optional)
  function openTableDlg() { loadTables(); $('#table-dlg').addClass('on'); }
  function closeTableDlg() { $('#table-dlg').removeClass('on'); }
  function setTable(id, name) {
    S.tableId = id; S.tableName = name;
    $('#pos-where').text(id ? (t('table') + ' ' + name) : t('walkIn'));
    $('#pos-table').toggleClass('has', !!id);
    closeTableDlg(); updateSummary();
  }
  $('#pos-table').on('click', function (e) { e.preventDefault(); openTableDlg(); });
  $('#table-close').on('click', function (e) { e.preventDefault(); closeTableDlg(); });
  $('#table-dlg').on('click', function (e) { if (e.target === this) closeTableDlg(); });
  $('.tile-walkin').on('click', function (e) { e.preventDefault(); setTable(0, ''); });

  function loadTables() {
    var $l = $('#table-list').html('<div class="tile-sub">' + t('wait') + '</div>');
    call('GET', 'api/storetables', undefined, function (tables) {
      $l.empty();
      for (var i = 0; i < tables.length; i++) {
        (function (tb) {
          var seated = !!tb.storeCustomerId || !!tb.occupiedAt;
          var who = tb.customerName || tb.guestName || '';
          var $t = $('<a href="#" class="tile' + (seated ? ' busy-t' : '') + (tb.id === S.tableId ? ' on' : '') + '"><span class="tile-ic">🪑</span><span class="tile-nm"></span><span class="tile-sub"></span></a>');
          $t.find('.tile-nm').text(tb.name);
          $t.find('.tile-sub').text(seated ? (who || t('busy')) : (tb.seats + ' ' + t('seats')));
          $t.on('click', function (e) { e.preventDefault(); setTable(tb.id, tb.name); });
          $t.appendTo($l);
        })(tables[i]);
      }
    }, function (msg) { $l.html('<div class="err">' + msg + '</div>'); });
  }

  // ---------------------------------------------------------------- customer (optional)
  function openCustDlg() {
    $('#cust-search').val(''); $('#cust-err').text('');
    if (S.customers === null) {
      $('#cust-list').html('<div class="tile-sub">' + t('wait') + '</div>');
      call('GET', 'api/storecustomers', undefined, function (list) { S.customers = list || []; renderCustomers(); }, function (msg) { $('#cust-list').html('<div class="err">' + msg + '</div>'); });
    } else renderCustomers();
    $('#cust-dlg').addClass('on');
    setTimeout(function () { $('#cust-search').focus(); }, 50);
  }
  function closeCustDlg() { $('#cust-dlg').removeClass('on'); }
  function renderCustomers() {
    var q = fold($('#cust-search').val()), $l = $('#cust-list').empty(), shown = 0;
    var list = S.customers || [];
    for (var i = 0; i < list.length && shown < 60; i++) {
      var c = list[i];
      if (c.phone === '0000' || c.isActive === false) continue;   // the nameless walk-in row
      var hay = fold(String(c.name || '') + ' ' + String(c.phone || ''));
      if (q && hay.indexOf(q) < 0 && !fuzzyHit(q, hay)) continue;
      shown++;
      (function (cu) {
        var $r = $('<a href="#" class="cust-row' + (cu.id === S.customerId ? ' on' : '') + '"><span class="cr-av"></span><span class="cr-nm"></span><span class="cr-sub"></span></a>');
        $r.find('.cr-av').text(initials(cu.name));
        $r.find('.cr-nm').text(cu.name || '');
        $r.find('.cr-sub').text(cu.phone || '');
        $r.on('click', function (e) { e.preventDefault(); setCustomer(cu.id, cu.name); });
        $l.append($r);
      })(c);
    }
    if (!shown) $l.html('<div class="tile-sub" style="padding:8px 4px">' + t('noMatch') + '</div>');
  }
  function initials(name) {
    var parts = String(name || '').split(' '), s = '';
    for (var i = 0; i < parts.length && s.length < 2; i++) if (parts[i]) s += parts[i].charAt(0).toUpperCase();
    return s || '?';
  }
  function setCustomer(id, name) {
    S.customerId = id || 0; S.customerName = name || '';
    $('#pos-who').text(id ? name : t('noCustomer'));
    $('#pos-customer').toggleClass('has', !!id);
    closeCustDlg(); updateSummary();
  }
  $('#pos-customer').on('click', function (e) { e.preventDefault(); openCustDlg(); });
  $('#cust-close').on('click', function (e) { e.preventDefault(); closeCustDlg(); });
  $('#cust-dlg').on('click', function (e) { if (e.target === this) closeCustDlg(); });
  $('#cust-none').on('click', function (e) { e.preventDefault(); setCustomer(0, ''); });
  $('#cust-search').on('input keyup', function () { renderCustomers(); });
  $('#cust-save').on('click', function () {
    var name = $.trim($('#cust-new-name').val()), phone = $.trim($('#cust-new-phone').val());
    if (!name || !phone) { $('#cust-err').text(t('needName')); return; }
    busy(true);
    call('POST', 'api/storecustomers', { name: name, phone: phone, address: '' }, function (c) {
      busy(false);
      if (S.customers) S.customers.unshift(c);
      $('#cust-new-name').val(''); $('#cust-new-phone').val('');
      setCustomer(c.id, c.name);
    }, function (msg) { busy(false); $('#cust-err').text(msg); });
  });

  // ---------------------------------------------------------------- open invoices (edit)
  function openInvDlg() {
    $('#inv-list').html('<div class="tile-sub">' + t('wait') + '</div>');
    $('#inv-dlg').addClass('on');
    loadOpenInvoices(true);
  }
  function closeInvDlg() { $('#inv-dlg').removeClass('on'); }
  function ago(iso) {
    var ms = new Date() - new Date(iso); if (isNaN(ms) || ms < 0) return '';
    var m = Math.round(ms / 60000); if (m < 60) return m + ' min'; var hh = Math.floor(m / 60); return hh + ' h ' + (m % 60) + ' min';
  }
  function loadOpenInvoices(render) {
    call('GET', 'api/invoices?days=2&take=100', undefined, function (rows) {
      var open = [];
      for (var i = 0; i < (rows || []).length; i++) {
        var r = rows[i];
        if (r.isPaid || r.status === 'Cancelled' || r.status === 'Rejected' || r.replacedByNumber) continue;
        open.push(r);
      }
      $('#open-count').text(open.length ? open.length : '').toggle(open.length > 0);
      if (!render) return;
      var $l = $('#inv-list').empty();
      if (!open.length) { $l.html('<div class="tile-sub" style="padding:8px 4px">' + t('noOpen') + '</div>'); return; }
      for (var k = 0; k < open.length; k++) {
        (function (r) {
          var $r = $('<a href="#" class="inv-row"><span class="ir-no"></span><span class="ir-who"></span><span class="ir-sub"></span><span class="ir-amt"></span></a>');
          $r.find('.ir-no').text(r.number);
          $r.find('.ir-who').text((r.tableName ? t('table') + ' ' + r.tableName : t('takeaway')) + (r.customerName && r.customerName !== 'Walk-in' ? ' · ' + r.customerName : ''));
          $r.find('.ir-sub').text(r.itemCount + ' ' + t('itemsN') + ' · ' + ago(r.placedAt) + ' · ' + t('unpaid'));
          $r.find('.ir-amt').text(money(r.total));
          $r.on('click', function (e) { e.preventDefault(); startEdit(r.id); });
          $l.append($r);
        })(open[k]);
      }
    }, function (msg) { if (render) $('#inv-list').html('<div class="err">' + msg + '</div>'); });
  }
  function itemById(id) {
    for (var i = 0; i < S.menu.length; i++) for (var j = 0; j < S.menu[i].items.length; j++) if (S.menu[i].items[j].id === id) return S.menu[i].items[j];
    return null;
  }
  function startEdit(id) {
    busy(true);
    call('GET', 'api/invoices/' + id, undefined, function (inv) {
      busy(false);
      S.cart = {}; S.order = [];
      S.editId = inv.id; S.editNumber = inv.number; S.editLines = inv.lines || [];
      for (var i = 0; i < S.editLines.length; i++) {
        var ln = S.editLines[i], it = itemById(ln.menuItemId) || { id: ln.menuItemId, name: ln.name, price: ln.unitPrice, _cat: null };
        var key = it.id;
        if (!S.cart[key]) { S.cart[key] = { item: it, qty: 0, note: ln.notes || '', unitPrice: ln.unitPrice }; S.order.push(key); }
        S.cart[key].qty += ln.quantity;
      }
      S.tableId = 0; S.tableName = inv.tableName || '';
      $('#pos-where').text(inv.tableName ? (t('table') + ' ' + inv.tableName) : t('walkIn'));
      closeInvDlg(); renderItems(); renderCart(); openCart();
    }, function (msg) { busy(false); alert(msg); });
  }
  function stopEdit() {
    S.editId = 0; S.editNumber = ''; S.editLines = null;
    S.cart = {}; S.order = []; $('#order-note').val('');
    renderItems(); renderCart(); closeCart();
  }
  $('#pos-open').on('click', function (e) { e.preventDefault(); openInvDlg(); });
  $('#inv-close').on('click', function (e) { e.preventDefault(); closeInvDlg(); });
  $('#inv-dlg').on('click', function (e) { if (e.target === this) closeInvDlg(); });
  $('#edit-stop').on('click', function (e) { e.preventDefault(); stopEdit(); });

  // ---------------------------------------------------------------- menu
  function skeleton(n) {
    var s = '';
    for (var i = 0; i < n; i++) s += '<div class="item skel"><span class="item-ph"></span><span class="item-body"><span class="sk-line w1"></span><span class="sk-line w2"></span><span class="sk-line w3"></span></span></div>';
    return s;
  }
  function loadMenu() {
    S.menuLoading = true; S.menu = [];
    $('#item-grid').html(skeleton(12));
    call('GET', 'api/menu?light=true', undefined, function (cats) {
      S.menuLoading = false; S.menu = [];
      for (var i = 0; i < cats.length; i++) {
        var items = [];
        for (var j = 0; j < (cats[i].items || []).length; j++) {
          var it = cats[i].items[j];
          if (it.status === 2 || it.isRejected) continue;  // never sell what the platform refused
          it._cat = cats[i];
          it._key = fold(allNames(it) + ' ' + allNames(cats[i]));
          items.push(it);
        }
        if (items.length) S.menu.push({ id: cats[i].id, name: cats[i].name, names: cats[i].names, items: items });
      }
      renderCats(); renderItems();
      loadPhotos(0);
    }, function (msg) { S.menuLoading = false; $('#item-grid').html('<div class="err">' + msg + '</div>'); });
  }
  function allNames(x) {
    var s = String(x.name || '');
    if (x.names) for (var k in x.names) if (x.names.hasOwnProperty(k) && x.names[k]) s += ' ' + x.names[k];
    return s;
  }

  // Photos come per category from the same endpoint the POS uses, after the menu is
  // already on screen; "All" walks the categories one by one so nothing blocks.
  function loadPhotos(catId) {
    var cats = [];
    if (catId) { for (var i = 0; i < S.menu.length; i++) if (S.menu[i].id === catId) cats.push(S.menu[i]); }
    else cats = S.menu.slice(0);
    (function next(k) {
      if (k >= cats.length) { $('#item-grid').removeClass('ph-loading'); return; }
      var c = cats[k];
      if (S.photoCats[c.id]) { next(k + 1); return; }
      S.photoCats[c.id] = 'loading';
      $('#item-grid').addClass('ph-loading');
      call('GET', 'api/menu/categories/' + c.id + '/items', undefined, function (items) {
        S.photoCats[c.id] = 'done';
        for (var j = 0; j < (items || []).length; j++) {
          var p = items[j].photo || items[j].photoUrl;
          if (p) { S.photos[items[j].id] = p; paintPhoto(items[j].id); }
        }
        next(k + 1);
      }, function () { S.photoCats[c.id] = 'done'; next(k + 1); });
    })(0);
  }
  function paintPhoto(id) {
    var p = S.photos[id]; if (!p) return;
    var $ph = $('#item-grid .item[data-id="' + id + '"] .item-ph');
    if ($ph.length) { $ph.addClass('has fresh').css('background-image', 'url(' + p + ')').find('.item-ic').remove(); if (!$ph.find('.item-zoom').length) $ph.append('<span class="item-zoom">⤢</span>'); setTimeout(function () { $ph.removeClass('fresh'); }, 600); }
    $('#cart-lines .line[data-id="' + id + '"] .line-ph').addClass('has').css('background-image', 'url(' + p + ')').text('');
  }

  function renderCats() {
    var $s = $('#cat-strip').empty();
    $('<a href="#" class="cat' + (S.catId === 0 ? ' on' : '') + '" data-cat="0"></a>').text(t('all')).appendTo($s);
    for (var i = 0; i < S.menu.length; i++) {
      $('<a href="#" class="cat' + (S.catId === S.menu[i].id ? ' on' : '') + '" data-cat="' + S.menu[i].id + '"><span class="cn"></span><span class="cc">' + S.menu[i].items.length + '</span></a>')
        .find('.cn').text(nameOf(S.menu[i])).end().appendTo($s);
    }
  }
  $('#cat-strip').on('click', '.cat', function (e) {
    e.preventDefault(); S.catId = parseInt($(this).attr('data-cat'), 10) || 0;
    $('#cat-strip .cat').removeClass('on'); $(this).addClass('on');
    $('#pos-search').val(''); $('#search-clear').hide();
    renderItems(); loadPhotos(S.catId);
  });

  // ---------------------------------------------------------------- smart search
  // Typos, any language, any word order: every dish is scored against the query and
  // the best come first. "chkn alfredo" finds Chicken Alfredo, «برجر» finds burgers.
  function fold(s) {
    s = String(s || '').toLowerCase();
    s = s.replace(/[ً-ْٰ]/g, '')          // Arabic diacritics
         .replace(/[آأإ]/g, 'ا')      // آ أ إ -> ا
         .replace(/[ىی]/g, 'ي')            // ى ی -> ي
         .replace(/ک/g, 'ك').replace(/ة/g, 'ه')   // ک -> ك, ة -> ه
         .replace(/[‌-‏]/g, ' ')
         .replace(/[^\w؀-ۿऀ-ॿ぀-ヿ一-鿿\s]/g, ' ')
         .replace(/\s+/g, ' ');
    return $.trim(s);
  }
  function bigrams(s) { var out = {}, w = ' ' + s + ' '; for (var i = 0; i < w.length - 1; i++) out[w.substr(i, 2)] = (out[w.substr(i, 2)] || 0) + 1; return out; }
  function dice(a, b) {
    if (a === b) return 1;
    if (a.length < 2 || b.length < 2) return 0;
    var A = bigrams(a), B = bigrams(b), inter = 0, na = 0, nb = 0, k;
    for (k in A) { na += A[k]; if (B[k]) inter += Math.min(A[k], B[k]); }
    for (k in B) nb += B[k];
    return (2 * inter) / (na + nb);
  }
  // How well one query word fits a name: exact word 3, word prefix 2.2, substring 1.6, fuzzy 0-1.4.
  function wordScore(qw, words, hay) {
    if (hay.indexOf(' ' + qw + ' ') >= 0) return 3;
    var best = 0;
    for (var i = 0; i < words.length; i++) {
      var w = words[i];
      if (w.indexOf(qw) === 0) { best = Math.max(best, 2.2); continue; }
      if (w.indexOf(qw) > 0) { best = Math.max(best, 1.6); continue; }
      if (qw.length >= 3) { var d = dice(qw, w); if (d >= 0.5) best = Math.max(best, d * 1.4); }
    }
    return best;
  }
  function scoreItem(it, qwords) {
    var hay = ' ' + it._key + ' ', words = it._key.split(' '), total = 0;
    for (var i = 0; i < qwords.length; i++) {
      var s = wordScore(qwords[i], words, hay);
      if (s === 0) return 0;         // every query word must land somewhere
      total += s;
    }
    if (it.isPopular) total += 0.2;
    return total;
  }
  function fuzzyHit(q, hay) {
    var qw = q.split(' '), words = hay.split(' ');
    for (var i = 0; i < qw.length; i++) if (qw[i] && wordScore(qw[i], words, ' ' + hay + ' ') === 0) return false;
    return true;
  }
  var searchTimer = null;
  $('#pos-search').on('input keyup', function (e) {
    if (e.type === 'keyup' && e.keyCode === 13) {
      var $first = $('#item-grid .item').first();
      if ($('#item-grid .item').length === 1 && $first.data('item')) { add($first.data('item'), 1); $(this).val('').trigger('input'); return; }
    }
    $('#search-clear').toggle(!!$(this).val());
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(renderItems, 120);
  });
  $('#search-clear').on('click', function (e) { e.preventDefault(); $('#pos-search').val('').focus(); $(this).hide(); renderItems(); });

  function renderItems() {
    var q = fold($('#pos-search').val()), qwords = q ? q.split(' ') : [];
    var $g = $('#item-grid').empty().toggleClass('search', !!q), rows = [], i, j;
    if (q) {
      for (i = 0; i < S.menu.length; i++) for (j = 0; j < S.menu[i].items.length; j++) {
        var sc = scoreItem(S.menu[i].items[j], qwords);
        if (sc > 0) rows.push({ it: S.menu[i].items[j], sc: sc });
      }
      rows.sort(function (a, b) { return b.sc - a.sc; });
      if (rows.length > 40) rows = rows.slice(0, 40);
      $g.append('<div class="grid-note">' + rows.length + ' ' + t('results') + '</div>');
      if (rows.length && S.photoCats[rows[0].it._cat.id] !== 'done') loadPhotos(0);
    } else {
      for (i = 0; i < S.menu.length; i++) {
        if (S.catId && S.menu[i].id !== S.catId) continue;
        for (j = 0; j < S.menu[i].items.length; j++) rows.push({ it: S.menu[i].items[j], sc: 0 });
      }
    }
    if (!rows.length) { if (S.menuLoading && !q) $g.html(skeleton(12)); else $g.append('<div class="grid-note">' + (q ? t('noHits') : t('noMenu')) + '</div>'); return; }
    for (i = 0; i < rows.length; i++) {
      var it = rows[i].it, line = S.cart[it.id], p = S.photos[it.id];
      var $t = $('<a href="#" class="item' + (line ? ' in' : '') + (it.isAvailable === false ? ' off' : '') + '" data-id="' + it.id + '">' +
        '<span class="item-ph' + (p ? ' has' : '') + '">' + (p ? '<span class="item-zoom">⤢</span>' : '<span class="item-ic">' + (it.imageEmoji || '🍽️') + '</span>') + '</span>' +
        '<span class="item-body"><span class="item-nm"></span>' + (q ? '<span class="item-cat"></span>' : '') + '<span class="item-pr"></span></span>' +
        '<span class="item-add">+</span>' + (line ? '<span class="item-q">' + line.qty + '</span>' : '') + '</a>');
      if (p) $t.find('.item-ph').css('background-image', 'url(' + p + ')');
      $t.find('.item-nm').text(nameOf(it));
      if (q) $t.find('.item-cat').text(nameOf(it._cat));
      $t.find('.item-pr').text(money(priceOf(it)));
      $t.data('item', it);
      $g.append($t);
    }
  }
  $('#item-grid').on('click', '.item', function (e) {
    e.preventDefault();
    var it = $(this).data('item');
    if (!it) return;
    if ($(e.target).closest('.item-zoom').length) { openLightbox(it); return; }
    if (it.isAvailable === false) return;
    add(it, 1);
  });

  // ---------------------------------------------------------------- the photo, full screen
  var LB = { item: null, zoom: false, drag: null, list: [], at: 0 };
  function openLightbox(it) {
    var p = S.photos[it.id]; if (!p) return;
    LB.item = it; LB.zoom = false; LB.list = [p]; LB.at = 0;
    showLb();
    $('#lb-nm').text(nameOf(it)); $('#lb-pr').text(money(priceOf(it)));
    $('#lb-was').text(priceOf(it) < it.price ? money(it.price) : '').toggle(priceOf(it) < it.price);
    // what the card had no room for
    var d = descOf(it), ing = it.ingredients || '', $m = $('#lb-meta').empty();
    if (it._cat) $m.append($('<span class="lb-chip"></span>').text(nameOf(it._cat)));
    if (it.isPopular) $m.append($('<span class="lb-chip hot"></span>').text('⭐ ' + t('popular')));
    if (it.discountPercent > 0) $m.append($('<span class="lb-chip off"></span>').text('-' + it.discountPercent + '%'));
    if (it.unit) $m.append($('<span class="lb-chip"></span>').text(it.unit));
    $m.toggle($m.children().length > 0);
    $('#lb-desc').text(d).toggle(!!d);
    $('#lb-ing-tx').text(ing); $('#lb-ing').toggle(!!ing);
    $('#lightbox').toggleClass('det', !!(d || ing));
    $('#lb-add').toggle(it.isAvailable !== false);
    $('#lightbox').addClass('on');
    // the card carried a thumbnail; the real pictures follow
    if (S.fullPhotos[it.id]) { LB.list = S.fullPhotos[it.id]; showLb(); return; }
    call('GET', 'api/menu/items/' + it.id + '/photos', undefined, function (list) {
      var out = [], main = -1;
      for (var i = 0; i < (list || []).length; i++) { if (list[i].data) { if (list[i].isMain && main < 0) main = out.length; out.push(list[i].data); } }
      if (main > 0) { var m = out.splice(main, 1)[0]; out.unshift(m); }
      if (!out.length) out = [p];
      S.fullPhotos[it.id] = out;
      if (LB.item && LB.item.id === it.id) { LB.list = out; LB.at = 0; showLb(); }
    }, function () {});
  }
  function showLb() {
    $('#lb-img').attr('src', LB.list[LB.at]).removeClass('zoomed').css({ left: 0, top: 0 }); LB.zoom = false;
    var many = LB.list.length > 1;
    $('#lb-prev, #lb-next').toggle(many);
    var $d = $('#lb-dots').empty().toggle(many);
    for (var i = 0; i < LB.list.length; i++) $d.append('<i' + (i === LB.at ? ' class="on"' : '') + '></i>');
  }
  function lbStep(d) { if (LB.list.length < 2) return; LB.at = (LB.at + d + LB.list.length) % LB.list.length; showLb(); }
  $('#lb-prev').on('click', function (e) { e.preventDefault(); lbStep(-1); });
  $('#lb-next').on('click', function (e) { e.preventDefault(); lbStep(1); });
  function closeLightbox() { $('#lightbox').removeClass('on'); LB.item = null; }
  $('#lb-close').on('click', function (e) { e.preventDefault(); closeLightbox(); });
  $('#lightbox').on('click', function (e) { if (e.target === this || $(e.target).hasClass('lb-stage')) closeLightbox(); });
  $('#lb-add').on('click', function (e) { e.preventDefault(); if (LB.item) add(LB.item, 1); closeLightbox(); });
  // tap toggles 2x; while zoomed the picture follows a finger or the mouse
  $('#lb-img').on('click', function (e) {
    e.preventDefault();
    if (LB.drag && LB.drag.moved) { LB.drag = null; return; }
    LB.zoom = !LB.zoom;
    $(this).toggleClass('zoomed', LB.zoom).css({ left: 0, top: 0 });
  });
  function dragStart(x, y) { LB.drag = { x: x, y: y, l: parseInt($('#lb-img').css('left'), 10) || 0, t: parseInt($('#lb-img').css('top'), 10) || 0, moved: false }; }
  function dragMove(x, y) {
    if (!LB.drag) return;
    if (!LB.zoom) {
      var sx = x - LB.drag.x;
      if (Math.abs(sx) > 60 && !LB.drag.moved) { LB.drag.moved = true; lbStep(sx < 0 ? 1 : -1); }
      return;
    }
    var dx = x - LB.drag.x, dy = y - LB.drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 6) LB.drag.moved = true;
    $('#lb-img').css({ left: LB.drag.l + dx, top: LB.drag.t + dy });
  }
  $('#lb-img').on('touchstart', function (e) { var t = e.originalEvent.touches[0]; dragStart(t.pageX, t.pageY); })
    .on('touchmove', function (e) { if (LB.zoom) e.preventDefault(); var t = e.originalEvent.touches[0]; dragMove(t.pageX, t.pageY); })
    .on('mousedown', function (e) { dragStart(e.pageX, e.pageY); e.preventDefault(); })
    .on('mousemove', function (e) { if (LB.drag) dragMove(e.pageX, e.pageY); })
    .on('mouseup mouseleave', function () { if (LB.drag && !LB.drag.moved) LB.drag = null; });

  // ---------------------------------------------------------------- cart
  function add(it, delta) {
    var line = S.cart[it.id];
    if (!line) { line = S.cart[it.id] = { item: it, qty: 0, note: '' }; S.order.push(it.id); }
    line.qty += delta;
    if (line.qty <= 0) { delete S.cart[it.id]; S.order.splice(S.order.indexOf(it.id), 1); }
    renderCart();
    var $tile = $('#item-grid .item[data-id="' + it.id + '"]');
    $tile.find('.item-q').remove();
    if (S.cart[it.id]) { $tile.addClass('in bump').append('<span class="item-q">' + S.cart[it.id].qty + '</span>'); setTimeout(function () { $tile.removeClass('bump'); }, 250); } else { $tile.removeClass('in'); }
    if (delta > 0) { var $b = $('#cart-bar').removeClass('bump'); setTimeout(function () { $b.addClass('bump'); }, 10); setTimeout(function () { $b.removeClass('bump'); }, 350); }
  }
  function renderCart() {
    var $l = $('#cart-lines').empty(), total = 0, count = 0;
    for (var i = 0; i < S.order.length; i++) {
      var line = S.cart[S.order[i]]; if (!line) continue;
      var it = line.item, price = line.unitPrice != null ? line.unitPrice : priceOf(it), p = S.photos[it.id];
      total += price * line.qty; count += line.qty;
      var $r = $('<div class="line' + (line.note ? ' noted' : '') + (line.editing ? ' editing' : '') + '" data-id="' + it.id + '">' +
        '<div class="line-row"><span class="line-ph' + (p ? ' has' : '') + '">' + (p ? '' : (it.imageEmoji || '🍽️')) + '</span>' +
        '<div class="line-nm"><span class="nm"></span><a href="#" class="line-note-btn"><span class="ln-ic">✎</span><span class="ln-tx"></span></a></div>' +
        '<div class="line-qty"><a href="#" class="qbtn q-minus">−</a><span class="qn">' + line.qty + '</span><a href="#" class="qbtn q-plus">+</a></div>' +
        '<div class="line-amt"></div></div>' +
        '<div class="line-edit"><div class="chips"></div><input type="text" class="note-in" maxlength="120"><a href="#" class="note-ok"></a></div></div>');
      if (p) $r.find('.line-ph').css('background-image', 'url(' + p + ')');
      $r.find('.nm').text(nameOf(it));
      $r.find('.ln-tx').text(line.note ? line.note : t('addNote'));
      $r.find('.line-amt').text(money(price * line.qty));
      $r.find('.note-in').val(line.note || '').attr('placeholder', t('notePh'));
      $r.find('.note-ok').text(t('done'));
      var chips = t('noteChips'), $c = $r.find('.chips');
      for (var k = 0; k < chips.length; k++) $('<a href="#" class="chip"></a>').text(chips[k]).appendTo($c);
      $l.append($r);
    }
    $('#cart-total').text(money(total));
    $('#cart-count').text(count);
    $('#cb-count').text(count); $('#cb-total').text(money(total));
    $('#cart-bar').toggle(count > 0);
    if (count === 0) closeCart();
    $('#cart-empty').toggle(count === 0);
    $('#btn-send, #btn-cash, #btn-card').prop('disabled', count === 0);
    var editing = !!S.editId;
    $('#edit-strip').toggle(editing); $('#cart-dlg .dlg-box').toggleClass('editing-on', editing);
    $('#edit-tx').text(editing ? t('editing') + ' ' + S.editNumber : '');
    $('#cart-title').text(editing ? S.editNumber : t('order'));
    $('#btn-send').text(editing ? t('saveChanges') : t('sendKitchen'));
    $('#cb-label').text(editing ? t('editing') + ' ' + S.editNumber : t('viewOrder'));
    if (editing && count === 0) { $('#cart-bar').show(); }
    updateSummary();
  }
  function updateSummary() {
    $('#cart-sum').text((S.tableId ? t('table') + ' ' + S.tableName : t('walkIn')) + (S.customerName ? ' · ' + S.customerName : ''));
  }
  function openCart() { $('#cart-dlg').addClass('on'); }
  function closeCart() { $('#cart-dlg').removeClass('on'); }
  $('#cart-bar').on('click', function (e) { e.preventDefault(); openCart(); });
  $('#cart-close').on('click', function (e) { e.preventDefault(); closeCart(); });
  $('#cart-dlg').on('click', function (e) { if (e.target === this) closeCart(); });
  $('#cart-lines').on('click', '.q-plus', function (e) { e.preventDefault(); var l = S.cart[$(this).closest('.line').attr('data-id')]; if (l) add(l.item, 1); });
  $('#cart-lines').on('click', '.q-minus', function (e) { e.preventDefault(); var l = S.cart[$(this).closest('.line').attr('data-id')]; if (l) add(l.item, -1); });
  // ---- notes on a line: the ✎ opens an editor under the line; chips add words, Done keeps it
  function lineOf(el) { return S.cart[$(el).closest('.line').attr('data-id')]; }
  // one handler on the name cell only: the ✎ sits inside it, and binding both would
  // run twice per tap — open, then close, so the editor never appeared
  $('#cart-lines').on('click', '.line-nm', function (e) {
    e.preventDefault();
    var l = lineOf(this); if (!l) return;
    var $line = $(this).closest('.line'), open = $line.hasClass('editing');
    $('#cart-lines .line').removeClass('editing');
    for (var id in S.cart) if (S.cart.hasOwnProperty(id)) S.cart[id].editing = false;
    if (!open) { l.editing = true; $line.addClass('editing'); setTimeout(function () { $line.find('.note-in').focus(); }, 30); }
  });
  $('#cart-lines').on('click', '.line-edit', function (e) { e.stopPropagation(); });
  $('#cart-lines').on('click', '.chip', function (e) {
    e.preventDefault();
    var $in = $(this).closest('.line-edit').find('.note-in'), cur = $.trim($in.val()), w = $(this).text();
    if (cur.indexOf(w) >= 0) return;
    $in.val(cur ? cur + ', ' + w : w);
  });
  function keepNote(el) {
    var l = lineOf(el); if (!l) return;
    l.note = $.trim($(el).closest('.line-edit').find('.note-in').val()); l.editing = false;
    renderCart();
  }
  $('#cart-lines').on('click', '.note-ok', function (e) { e.preventDefault(); keepNote(this); });
  $('#cart-lines').on('keydown', '.note-in', function (e) { if (e.keyCode === 13) { e.preventDefault(); keepNote(this); } });
  $('#cart-lines').on('change', '.note-in', function () { var l = lineOf(this); if (l) l.note = $.trim($(this).val()); });

  // ---------------------------------------------------------------- the order
  function openPos(tableId, tableName) {
    S.editId = 0; S.editNumber = ''; S.editLines = null;
    S.tableId = tableId; S.tableName = tableName;
    S.customerId = 0; S.customerName = ''; $('#pos-who').text(t('noCustomer')); $('#pos-customer').removeClass('has');
    $('#pos-table').toggleClass('has', !!tableId);
    S.cart = {}; S.order = []; S.catId = 0; $('#order-note').val(''); $('#pos-search').val(''); $('#search-clear').hide();
    $('#pos-where').text(tableId ? (t('table') + ' ' + tableName) : t('walkIn'));
    renderCats(); renderItems(); renderCart();
    show('scr-pos');
  }

  function sameAsLoaded() {
    if (!S.editLines) return false;
    var want = {}, i, l;
    for (i = 0; i < S.editLines.length; i++) { l = S.editLines[i]; want[l.menuItemId] = (want[l.menuItemId] || 0) + l.quantity; }
    var got = {};
    for (i = 0; i < S.order.length; i++) { l = S.cart[S.order[i]]; if (l) got[l.item.id] = (got[l.item.id] || 0) + l.qty; }
    for (var k in want) if (want.hasOwnProperty(k) && want[k] !== got[k]) return false;
    for (var g in got) if (got.hasOwnProperty(g) && got[g] !== want[g]) return false;
    for (i = 0; i < S.order.length; i++) { l = S.cart[S.order[i]]; if (l && l.note) { var orig = null; for (var q = 0; q < S.editLines.length; q++) if (S.editLines[q].menuItemId === l.item.id) orig = S.editLines[q].notes || ''; if (orig !== l.note) return false; } }
    return true;
  }
  function sendEdit(markPaid) {
    if (!S.order.length) { $('#send-err').text(t('empty')); return; }
    var id = S.editId, number = S.editNumber;
    $('#send-err').text(''); busy(true);
    var finish = function (o) {
      busy(false);
      $('#done-no').text((o && o.number) || number);
      $('#done-sub').text(t('saved') + ' · ' + money((o && o.total != null) ? o.total : 0) + ' · ' + (markPaid ? t('paid') : t('unpaid')));
      S.editId = 0; S.editNumber = ''; S.editLines = null; S.cart = {}; S.order = [];
      closeCart(); loadOpenInvoices(false); show('scr-done');
    };
    if (markPaid && sameAsLoaded()) {
      // nothing changed: just settle the bill, no replacement needed
      call('POST', 'api/orders/' + id + '/paid', {}, function () { finish(null); }, function (msg) { busy(false); $('#send-err').text(msg); });
      return;
    }
    var lines = [];
    for (var i = 0; i < S.order.length; i++) {
      var l = S.cart[S.order[i]]; if (!l) continue;
      lines.push({ menuItemId: l.item.id, name: l.item.name || nameOf(l.item), unitPrice: l.unitPrice != null ? l.unitPrice : priceOf(l.item), quantity: l.qty, notes: l.note || null });
    }
    call('POST', 'api/invoices/' + id + '/replace', { lines: lines, reason: $.trim($('#order-note').val()) || 'Edited on the tablet', markPaid: !!markPaid },
      finish, function (msg) { busy(false); $('#send-err').text(msg); });
  }

  function send(markPaid, method) {
    if (S.editId) { sendEdit(markPaid); return; }
    if (!S.order.length) { $('#send-err').text(t('empty')); return; }
    var items = [];
    for (var i = 0; i < S.order.length; i++) {
      var l = S.cart[S.order[i]]; if (!l) continue;
      items.push({ menuItemId: l.item.id, quantity: l.qty, notes: l.note || null });
    }
    var body = {
      storeCustomerId: S.customerId || 0, paymentMethod: method, items: items,
      notes: $.trim($('#order-note').val()) || null,
      markPaid: !!markPaid, tableId: S.tableId || null, counterSale: true
    };
    $('#send-err').text(''); busy(true);
    call('POST', 'api/orders/counter', body, function (o) {
      busy(false);
      $('#done-no').text(o.number || '');
      $('#done-sub').text((S.tableId ? t('table') + ' ' + S.tableName : t('takeaway')) + (S.customerName ? ' · ' + S.customerName : '') + ' · ' + money(o.total || 0) + ' · ' + (markPaid ? t('paid') : t('unpaid')));
      S.cart = {}; S.order = [];
      closeCart(); loadOpenInvoices(false);
      show('scr-done');
    }, function (msg) { busy(false); $('#send-err').text(msg); });
  }
  $('#btn-send').on('click', function () { send(false, 0); });
  $('#btn-cash').on('click', function () { send(true, 0); });
  $('#btn-card').on('click', function () { send(true, 1); });
  $('#btn-again').on('click', function () { openPos(0, ''); loadTables(); });

  // ---------------------------------------------------------------- language, sign out, start
  (function () {
    var $l = $('#lang-list');
    for (var i = 0; i < LANGS.length; i++) $l.append($('<a href="#" class="lang-row"><span class="lr-code"></span><span class="lr-nm"></span><span class="lr-ok">✓</span></a>').attr('data-lang', LANGS[i][0]).find('.lr-code').text(LANGS[i][0].toUpperCase()).end().find('.lr-nm').text(LANGS[i][1]).end());
  })();
  $(document).on('click', '.lang-open', function (e) { e.preventDefault(); $('#lang-dlg').addClass('on'); });
  $('#lang-close').on('click', function (e) { e.preventDefault(); $('#lang-dlg').removeClass('on'); });
  $('#lang-dlg').on('click', function (e) { if (e.target === this) $(this).removeClass('on'); });
  $('#lang-list').on('click', '.lang-row', function (e) { e.preventDefault(); $('#lang-dlg').removeClass('on'); $(this).trigger('pick'); });
  $('#lang-list').on('pick', '.lang-row', function () { lang = $(this).attr('data-lang'); applyLang(); renderCats(); renderItems(); renderCart(); $('#pos-where').text(S.tableId ? (t('table') + ' ' + S.tableName) : t('walkIn')); $('#pos-who').text(S.customerId ? S.customerName : t('noCustomer')); });
  $('#logout, #pick-logout').on('click', function (e) { e.preventDefault(); signOut(); });

  applyLang();
  if (S.token) {
    busy(true);
    call('GET', 'api/restaurants/mine', undefined, function (me) { busy(false); enterStore(me.name || S.storeName); },
      function () { busy(false); signOut(); });
  } else {
    show('scr-login');
  }
})(jQuery);
