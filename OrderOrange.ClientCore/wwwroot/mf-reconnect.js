// Friendly reconnect handling for Blazor Server: when the circuit drops (deploy,
// sleep, network blip) show a small branded overlay — and if the old session can't
// be resumed, reload the page automatically instead of leaving a dead white modal.
// A circuit that keeps dying is a WebSocket the network only pretends to carry:
// after the second forced reload we mark the tab for long-polling (mf-transport.js
// reads the mark) so the third try rides plain HTTP and stays alive.
//
// The overlay speaks the page's language and, once the rejoin drags on, offers two
// real buttons: "Reconnect now" (Blazor.reconnect — same circuit, nothing typed is
// lost) and "Reload page" (a fresh start). Coming back online retries by itself.
(function () {
    var WORDS = {
        en: { title: 'Connection lost', text: 'Reconnecting…', slow: 'Taking longer than usual. You can try again yourself.', offline: 'You are offline — check your internet connection.', failed: 'We could not reconnect.', updating: 'Updating…', retry: 'Reconnect now', reload: 'Reload page', attempt: 'Attempt {n}', attemptOf: 'Attempt {n} of {max}', nextIn: 'next try in {s} s' },
        ar: { title: 'انقطع الاتصال', text: 'جارٍ إعادة الاتصال…', slow: 'يستغرق الأمر وقتًا أطول من المعتاد. يمكنك المحاولة بنفسك.', offline: 'أنت غير متصل — تحقق من اتصال الإنترنت.', failed: 'لم نتمكن من إعادة الاتصال.', updating: 'جارٍ التحديث…', retry: 'أعد الاتصال الآن', reload: 'إعادة تحميل الصفحة', attempt: 'المحاولة {n}', attemptOf: 'المحاولة {n} من {max}', nextIn: 'المحاولة التالية بعد {s} ث' },
        fa: { title: 'ارتباط قطع شد', text: 'در حال اتصال دوباره…', slow: 'بیشتر از معمول طول کشید. می‌توانید خودتان دوباره تلاش کنید.', offline: 'آفلاین هستید — اتصال اینترنت را بررسی کنید.', failed: 'اتصال دوباره برقرار نشد.', updating: 'در حال به‌روزرسانی…', retry: 'اتصال دوباره', reload: 'بارگذاری دوباره صفحه', attempt: 'تلاش {n}', attemptOf: 'تلاش {n} از {max}', nextIn: 'تلاش بعدی در {s} ثانیه' },
        ur: { title: 'رابطہ منقطع ہو گیا', text: 'دوبارہ منسلک ہو رہا ہے…', slow: 'معمول سے زیادہ وقت لگ رہا ہے۔ آپ خود دوبارہ کوشش کر سکتے ہیں۔', offline: 'آپ آف لائن ہیں — انٹرنیٹ کنکشن چیک کریں۔', failed: 'دوبارہ منسلک نہیں ہو سکے۔', updating: 'اپ ڈیٹ ہو رہا ہے…', retry: 'ابھی دوبارہ منسلک کریں', reload: 'صفحہ دوبارہ لوڈ کریں', attempt: 'کوشش {n}', attemptOf: 'کوشش {n} از {max}', nextIn: 'اگلی کوشش {s} سیکنڈ میں' },
        hi: { title: 'कनेक्शन टूट गया', text: 'फिर से जोड़ रहे हैं…', slow: 'सामान्य से ज़्यादा समय लग रहा है। आप स्वयं फिर से कोशिश कर सकते हैं।', offline: 'आप ऑफ़लाइन हैं — इंटरनेट कनेक्शन जाँचें।', failed: 'फिर से कनेक्ट नहीं हो सका।', updating: 'अपडेट हो रहा है…', retry: 'अभी फिर से कनेक्ट करें', reload: 'पेज फिर से लोड करें', attempt: 'प्रयास {n}', attemptOf: 'प्रयास {n} / {max}', nextIn: 'अगला प्रयास {s} से. में' },
        tr: { title: 'Bağlantı kesildi', text: 'Yeniden bağlanıyor…', slow: 'Normalden uzun sürüyor. Kendiniz tekrar deneyebilirsiniz.', offline: 'Çevrimdışısınız — internet bağlantınızı kontrol edin.', failed: 'Yeniden bağlanamadık.', updating: 'Güncelleniyor…', retry: 'Şimdi yeniden bağlan', reload: 'Sayfayı yenile', attempt: 'Deneme {n}', attemptOf: 'Deneme {n} / {max}', nextIn: 'sonraki deneme {s} sn içinde' },
        fr: { title: 'Connexion perdue', text: 'Reconnexion…', slow: 'Cela prend plus de temps que d’habitude. Vous pouvez réessayer vous-même.', offline: 'Vous êtes hors ligne — vérifiez votre connexion internet.', failed: 'Impossible de se reconnecter.', updating: 'Mise à jour…', retry: 'Reconnecter maintenant', reload: 'Recharger la page', attempt: 'Tentative {n}', attemptOf: 'Tentative {n} sur {max}', nextIn: 'prochain essai dans {s} s' },
        es: { title: 'Conexión perdida', text: 'Reconectando…', slow: 'Está tardando más de lo habitual. Puedes intentarlo tú mismo.', offline: 'Estás sin conexión — revisa tu internet.', failed: 'No pudimos reconectar.', updating: 'Actualizando…', retry: 'Reconectar ahora', reload: 'Recargar página', attempt: 'Intento {n}', attemptOf: 'Intento {n} de {max}', nextIn: 'próximo intento en {s} s' },
        de: { title: 'Verbindung unterbrochen', text: 'Verbindung wird wiederhergestellt…', slow: 'Das dauert länger als üblich. Sie können es selbst erneut versuchen.', offline: 'Sie sind offline — prüfen Sie Ihre Internetverbindung.', failed: 'Verbindung konnte nicht wiederhergestellt werden.', updating: 'Aktualisierung…', retry: 'Jetzt neu verbinden', reload: 'Seite neu laden', attempt: 'Versuch {n}', attemptOf: 'Versuch {n} von {max}', nextIn: 'nächster Versuch in {s} s' },
        ru: { title: 'Соединение потеряно', text: 'Переподключение…', slow: 'Это занимает больше времени, чем обычно. Попробуйте сами.', offline: 'Вы не в сети — проверьте подключение к интернету.', failed: 'Не удалось переподключиться.', updating: 'Обновление…', retry: 'Переподключить', reload: 'Перезагрузить страницу', attempt: 'Попытка {n}', attemptOf: 'Попытка {n} из {max}', nextIn: 'следующая через {s} с' },
        it: { title: 'Connessione persa', text: 'Riconnessione…', slow: 'Sta richiedendo più tempo del solito. Puoi riprovare tu stesso.', offline: 'Sei offline — controlla la connessione internet.', failed: 'Impossibile riconnettersi.', updating: 'Aggiornamento…', retry: 'Riconnetti ora', reload: 'Ricarica pagina', attempt: 'Tentativo {n}', attemptOf: 'Tentativo {n} di {max}', nextIn: 'prossimo tentativo tra {s} s' },
        pt: { title: 'Conexão perdida', text: 'Reconectando…', slow: 'Está demorando mais que o normal. Você pode tentar novamente.', offline: 'Você está offline — verifique sua conexão.', failed: 'Não foi possível reconectar.', updating: 'Atualizando…', retry: 'Reconectar agora', reload: 'Recarregar página', attempt: 'Tentativa {n}', attemptOf: 'Tentativa {n} de {max}', nextIn: 'próxima tentativa em {s} s' },
        zh: { title: '连接已断开', text: '正在重新连接…', slow: '比平时花的时间更长，您可以自行重试。', offline: '您已离线，请检查网络连接。', failed: '无法重新连接。', updating: '正在更新…', retry: '立即重新连接', reload: '重新加载页面', attempt: '第 {n} 次尝试', attemptOf: '第 {n} 次尝试（共 {max} 次）', nextIn: '{s} 秒后再试' },
        ja: { title: '接続が切れました', text: '再接続しています…', slow: '通常より時間がかかっています。ご自身で再試行できます。', offline: 'オフラインです。インターネット接続を確認してください。', failed: '再接続できませんでした。', updating: '更新しています…', retry: '今すぐ再接続', reload: 'ページを再読み込み', attempt: '試行 {n} 回目', attemptOf: '試行 {n} / {max} 回', nextIn: '次は {s} 秒後' },
    };
    function words() { return WORDS[(document.documentElement.lang || 'en').slice(0, 2)] || WORDS.en; }

    function reloadCounted() {
        try {
            var n = parseInt(sessionStorage.getItem('oo-drops') || '0', 10) + 1;
            sessionStorage.setItem('oo-drops', String(n));
            if (n >= 2) sessionStorage.setItem('oo-lp', '1');
        } catch (e) { }
        location.reload();
    }

    // How long the tab has already had to reload itself this session: after two
    // forced reloads we stop reloading on our own and leave the choice to the person.
    function drops() { try { return parseInt(sessionStorage.getItem('oo-drops') || '0', 10); } catch (e) { return 0; } }

    var SLOW_AFTER = 2000;    // reveal the button (CSS does the same at 2 s even if this never runs)
    var RELOAD_AFTER = 8000;  // give up on the old circuit by ourselves — a hard load is the cure anyway

    // A tab that was in the background while the circuit died: the moment it is looked at
    // again with the card still up, reload — no waiting on timers that may have been throttled.
    document.addEventListener('visibilitychange', function () {
        var m = document.getElementById('components-reconnect-modal');
        if (document.visibilityState === 'visible' && m && /components-reconnect-(show|failed|rejected)/.test(m.className)) reloadCounted();
    });

    function arm() {
        var modal = document.getElementById('components-reconnect-modal');
        if (!modal) { setTimeout(arm, 500); return; }
        var title = modal.querySelector('.mf-reconnect-title');
        var text = modal.querySelector('.mf-reconnect-text');
        var status = modal.querySelector('.mf-reconnect-status');
        var retry = modal.querySelector('[data-act="retry"]');
        var reload = modal.querySelector('[data-act="reload"]');
        var shownAt = 0;
        var retrying = false;
        var timers = [];

        function say(key) {
            var w = words();
            if (title) title.textContent = w.title;
            if (text) text.textContent = w[key] || w.text;
            if (retry) retry.textContent = w.retry;
            if (reload) reload.textContent = w.reload;
        }
        function showing() { return (modal.className || '').includes('components-reconnect-show'); }
        function clearTimers() { timers.forEach(clearTimeout); timers = []; }
        function slow() {
            if (!showing()) return;
            modal.classList.add('mf-slow');
            say(navigator.onLine === false ? 'offline' : 'slow');
        }

        // "Reconnect now" = a HARD load: a fresh document from the server, new circuit,
        // new assets — not a polite retry of the old socket (Majed 2026-09-07: "make the
        // reconnection do hard load"). The same goes for the network coming back.
        function tryReconnect() {
            if (retrying) return;
            retrying = true;
            modal.classList.add('mf-retrying');
            modal.classList.remove('mf-slow');
            say('updating');
            // A person pressing this has a network that carries pages but not the socket
            // (attempt 1 of 30 hanging is that exact picture). The page that comes back
            // rides plain long-polling, so it can actually connect.
            try { sessionStorage.setItem('oo-lp', '1'); } catch (e) { }
            setTimeout(reloadCounted, 150);
        }

        // ---- "Attempt 12 of 30 · next try in 4 s" ---------------------------
        // Blazor announces every scheduled attempt through a DOM event; we keep
        // the line honest with a one-second countdown between announcements.
        var attempt = 0, maxRetries = 0, nextAt = 0, ticker = null;
        function fmt(s, args) { return s.replace(/\{(\w+)\}/g, function (_, k) { return args[k]; }); }
        function paintStatus() {
            if (!status) return;
            if (!attempt || !showing()) { status.textContent = ''; return; }
            var w = words();
            var line = fmt(maxRetries ? w.attemptOf : w.attempt, { n: attempt, max: maxRetries });
            var left = Math.max(0, Math.ceil((nextAt - Date.now()) / 1000));
            if (nextAt && left > 0) line += ' · ' + fmt(w.nextIn, { s: left });
            status.textContent = line;
        }
        function onState(e) {
            var d = (e && e.detail) || {};
            if (d.state === 'retrying') {
                attempt = d.currentAttempt || 0;
                nextAt = d.secondsToNextAttempt > 0 ? Date.now() + d.secondsToNextAttempt * 1000 : 0;
                var maxEl = document.getElementById('components-reconnect-max-retries');
                maxRetries = maxEl ? parseInt(maxEl.textContent, 10) || 0 : 0;
                paintStatus();
                if (!ticker) ticker = setInterval(paintStatus, 1000);
            } else if (d.state === 'hide' || d.state === 'failed' || d.state === 'rejected') {
                attempt = 0; nextAt = 0;
                if (ticker) { clearInterval(ticker); ticker = null; }
                paintStatus();
            }
        }
        document.addEventListener('components-reconnect-state-changed', onState);
        modal.addEventListener('components-reconnect-state-changed', onState);

        if (retry) retry.addEventListener('click', tryReconnect);
        if (reload) reload.addEventListener('click', function () { say('updating'); reloadCounted(); });
        // The network came back on its own — no need to make anyone press anything.
        window.addEventListener('online', function () { if (showing()) tryReconnect(); });
        window.addEventListener('offline', function () { if (showing()) { modal.classList.add('mf-slow'); say('offline'); } });

        var check = function () {
            var c = modal.className || '';
            if (c.includes('components-reconnect-show')) {
                if (shownAt) return; // class churn while already open — keep the timers
                shownAt = Date.now();
                modal.classList.remove('mf-slow', 'mf-failed');
                say(navigator.onLine === false ? 'offline' : 'text');
                if (navigator.onLine === false) modal.classList.add('mf-slow');
                timers.push(setTimeout(slow, SLOW_AFTER));
                // A rejoin that drags on usually means the server was updated — reload,
                // unless this tab has already been through that twice: then the buttons stay.
                timers.push(setTimeout(function () {
                    if (showing() && !retrying && drops() < 2) { say('updating'); reloadCounted(); }
                }, RELOAD_AFTER));
            } else if (c.includes('components-reconnect-failed') ||
                       c.includes('components-reconnect-rejected')) {
                clearTimers(); shownAt = 0;
                modal.classList.add('mf-slow', 'mf-failed');
                modal.classList.remove('mf-retrying');
                if (drops() < 2) { say('updating'); setTimeout(reloadCounted, 400); }
                else say('failed');
            } else {
                clearTimers(); shownAt = 0;
                modal.classList.remove('mf-slow', 'mf-failed', 'mf-retrying', 'mf-retried');
            }
        };

        new MutationObserver(check).observe(modal, { attributes: true, attributeFilter: ['class'] });
        check();
    }
    arm();

    // ---- The stock error bar -------------------------------------------------
    // A server-side exception ends the circuit for good, and Blazor unhides
    // #blazor-error-ui: a red band of untranslated English across the bottom of a
    // fourteen-language app. Nearly every time it is a deploy restarting the site
    // under someone's feet, and a reload puts it right — so recover silently the
    // first time, and only speak up if it happens twice in the same tab.
    const SORRY = {
        en: ['Something went wrong.', 'Reload'],
        ar: ['حدث خطأ ما.', 'إعادة التحميل'],
        fa: ['مشکلی پیش آمد.', 'بارگذاری دوباره'],
        ur: ['کچھ غلط ہو گیا۔', 'دوبارہ لوڈ کریں'],
        hi: ['कुछ गड़बड़ हो गई।', 'फिर से लोड करें'],
        tr: ['Bir şeyler ters gitti.', 'Yeniden yükle'],
        fr: ['Une erreur est survenue.', 'Recharger'],
        es: ['Algo salió mal.', 'Recargar'],
        de: ['Etwas ist schiefgelaufen.', 'Neu laden'],
        ru: ['Что-то пошло не так.', 'Обновить'],
        it: ['Qualcosa è andato storto.', 'Ricarica'],
        pt: ['Algo deu errado.', 'Recarregar'],
        zh: ['出了点问题。', '重新加载'],
        ja: ['問題が発生しました。', '再読み込み'],
    };

    function dress(bar) {
        const words = SORRY[document.documentElement.lang] || SORRY.en;
        const msg = bar.querySelector('.msg');
        const reload = bar.querySelector('.reload');
        if (msg) msg.textContent = words[0];
        if (reload) reload.textContent = words[1];
        bar.classList.add('oo-dressed');
    }

    function armErrorBar() {
        const bar = document.getElementById('blazor-error-ui');
        if (!bar) { setTimeout(armErrorBar, 500); return; }

        const check = () => {
            if (getComputedStyle(bar).display === 'none') return;

            let tries = 0;
            try { tries = parseInt(sessionStorage.getItem('oo-err') || '0', 10); } catch (e) { }
            if (tries >= 1) { dress(bar); return; }   // twice over: stop reloading, explain
            try { sessionStorage.setItem('oo-err', String(tries + 1)); } catch (e) { }
            location.reload();
        };

        new MutationObserver(check).observe(bar, { attributes: true, attributeFilter: ['style', 'class'] });
        check();
    }
    armErrorBar();

    // A tab that has run healthily for a while has clearly recovered; forget the
    // strike, or one bad moment would leave it in "second try" mode all session.
    setTimeout(function () { try { sessionStorage.removeItem('oo-err'); sessionStorage.removeItem('oo-drops'); } catch (e) { } }, 20000);
})();
