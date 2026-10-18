"""SEO layer for the hosted partner guide. Run AFTER host.py.

Takes wwwroot/guide.html (English, images already external) and
  * enriches its <head>: description, canonical, hreflang, Open Graph, Twitter, JSON-LD
    (SoftwareApplication + FAQPage + BreadcrumbList),
  * writes 13 language variants guide.<lang>.html — same English walkthrough, but the
    <title>, description, social tags, hero and a short notice are in that language, with
    the right <html lang dir>, so each language can rank for "restaurant POS" searches,
  * adds a language bar to every variant,
  * writes robots.txt and sitemap.xml for partner.orderorange.com.
"""
import io, os, re, html, json, shutil
os.chdir(os.path.dirname(os.path.abspath(__file__)))
import i18n   # the whole body in every language (2026-10-15); caches in i18n/<lang>.json
WWW = r'D:\OrderOrange\OrderOrange.RestaurantWeb\wwwroot'
BASE = 'https://partner.orderorange.com/'
src = io.open(os.path.join(WWW, 'guide.html'), encoding='utf-8').read()
shutil.copy('og-guide.jpg', os.path.join(WWW, 'guide', 'og-guide.jpg'))
shutil.copy('og-api.jpg', os.path.join(WWW, 'guide', 'og-api.jpg'))
shutil.copy('og-api.jpg', os.path.join(WWW, 'guide', 'og-api.jpg'))

# ---------------------------------------------------------------- copy per language
# title, description, eyebrow, h1 (before span), h1 span, hero paragraph, open button, tour button, notice, language name
L = {
 'en': dict(dir='ltr', name='English',
   title='Free Open-Source Restaurant POS & QR Ordering — OrderOrange Guide',
   desc='Step-by-step guide to the free OrderOrange restaurant POS: point of sale, QR code menu, kitchen display, online ordering, stock, staff and reports — with real screens.',
   eyebrow='Partner app · Edition 1', h1='Run the whole restaurant ', span='from one screen.',
   p='A page-by-page tour of the OrderOrange partner app: selling, orders and guests, your menu, money, your team, reports and setup. Every screen comes from the Saffron Grill House demo store. From a single café to a large multi-branch restaurant: every branch is connected under one account.',
   open='Open partner app ↗', tour='Start the tour', notice=''),
 'ar': dict(dir='rtl', name='العربية',
   title='كاشير مطاعم مجاني مفتوح المصدر وطلب بـ QR — دليل شريك OrderOrange',
   desc='دليل خطوة بخطوة لكاشير OrderOrange المجاني للمطاعم: نقاط بيع، قائمة QR، شاشة مطبخ، طلب أونلاين، مخزون، موظفون وتقارير — بشاشات حقيقية.',
   eyebrow='تطبيق الشريك · الإصدار ١', h1='أدر مطعمك كاملاً ', span='من شاشة واحدة.',
   p='جولة صفحة بصفحة في تطبيق شريك أوردر أورنج: البيع، الطلبات والضيوف، المنيو، المال، الفريق، التقارير والإعدادات. كل شاشة مأخوذة من متجر Saffron Grill House التجريبي. من مقهى واحد إلى مطعم كبير متعدد الفروع: كل فرع مرتبط بحساب واحد.',
   open='افتح تطبيق الشريك ↗', tour='ابدأ الجولة', notice='الشاشات والشرح التفصيلي بالإنجليزية؛ التطبيق نفسه يعمل بالعربية بالكامل.'),
 'fa': dict(dir='rtl', name='فارسی',
   title='صندوق رایگان متن‌باز رستوران و سفارش با QR — راهنمای همکار OrderOrange',
   desc='راهنمای گام‌به‌گام صندوق رایگان رستوران OrderOrange: نقطه فروش، منوی QR، نمایشگر آشپزخانه، سفارش آنلاین، انبار، کارکنان و گزارش‌ها — با صفحه‌های واقعی.',
   eyebrow='اپ همکار · نسخهٔ ۱', h1='کل رستوران را ', span='از یک صفحه اداره کنید.',
   p='گشتی صفحه‌به‌صفحه در اپ همکار اردر اورنج: فروش، سفارش‌ها و مهمان‌ها، منو، مالی، تیم، گزارش‌ها و تنظیمات. همهٔ تصاویر از فروشگاه دموی Saffron Grill House است. از یک کافهٔ کوچک تا یک رستوران بزرگ چندشعبه‌ای: همهٔ شعبه‌ها زیر یک حساب به هم متصل‌اند.',
   open='باز کردن اپ همکار ↗', tour='شروع تور', notice='تصاویر و توضیحات به انگلیسی است؛ خود اپ کاملاً فارسی دارد.'),
 'ur': dict(dir='rtl', name='اردو',
   title='ریستورانوں کے لیے مفت POS اور QR آرڈرنگ ایپ — OrderOrange پارٹنر گائیڈ',
   desc='OrderOrange ریستورانوں اور کیفوں کے لیے مفت POS ہے: میز پر QR آرڈر، کچن بورڈ، ڈیلیوری، بکنگ، تنخواہیں اور رپورٹس۔ ۴۱ اصل اسکرینوں کے ساتھ مرحلہ وار گائیڈ۔ ایک لاگ ان، کئی کاروبار۔ AI آرڈر اسسٹنٹ۔ کسی بھی ملک میں کام کرتا ہے: اپنی کرنسی، ٹیکس اور زبان منتخب کریں۔',
   eyebrow='پارٹنر ایپ · ایڈیشن ۱', h1='پورا ریستوران ', span='ایک اسکرین سے چلائیں۔',
   p='OrderOrange پارٹنر ایپ کا صفحہ بہ صفحہ دورہ: فروخت، آرڈر اور مہمان، مینیو، پیسہ، ٹیم، رپورٹس اور سیٹ اپ۔ ہر اسکرین Saffron Grill House ڈیمو اسٹور سے ہے۔ ایک کیفے سے لے کر بڑے کثیر شاخ ریستوران تک: ہر شاخ ایک ہی اکاؤنٹ کے تحت جڑی ہوئی ہے۔',
   open='پارٹنر ایپ کھولیں ↗', tour='ٹور شروع کریں', notice='اسکرینیں اور تفصیل انگریزی میں ہیں؛ ایپ خود مکمل اردو میں چلتی ہے۔'),
 'hi': dict(dir='ltr', name='हिन्दी',
   title='रेस्टोरेंट के लिए मुफ़्त POS और QR ऑर्डरिंग ऐप — OrderOrange पार्टनर गाइड',
   desc='OrderOrange रेस्टोरेंट और कैफ़े के लिए मुफ़्त POS है: टेबल पर QR ऑर्डर, किचन बोर्ड, डिलीवरी, बुकिंग, वेतन और रिपोर्ट। 41 असली स्क्रीन के साथ चरण-दर-चरण गाइड। एक लॉगिन, कई बिज़नेस। AI ऑर्डर असिस्टेंट। किसी भी देश में काम करता है: अपनी मुद्रा, कर दर और भाषा चुनें।',
   eyebrow='पार्टनर ऐप · संस्करण 1', h1='पूरा रेस्टोरेंट ', span='एक स्क्रीन से चलाएँ।',
   p='OrderOrange पार्टनर ऐप का पेज-दर-पेज टूर: बिक्री, ऑर्डर और मेहमान, मेन्यू, पैसा, टीम, रिपोर्ट और सेटअप। हर स्क्रीन Saffron Grill House डेमो स्टोर से है। एक कैफ़े से लेकर बड़े मल्टी-ब्रांच रेस्टोरेंट तक: हर ब्रांच एक ही खाते के तहत जुड़ी है।',
   open='पार्टनर ऐप खोलें ↗', tour='टूर शुरू करें', notice='स्क्रीन और विवरण अंग्रेज़ी में हैं; ऐप स्वयं पूरी तरह हिंदी में चलता है।'),
 'de': dict(dir='ltr', name='Deutsch',
   title='Kostenloses Restaurant-Kassensystem & QR-Bestellung — OrderOrange Partner-Guide',
   desc='OrderOrange ist ein kostenloses Kassensystem für Restaurants und Cafés: QR-Bestellung am Tisch, Küchenmonitor, Lieferung, Reservierungen, Lohn und Berichte. Schritt-für-Schritt-Guide mit 41 echten Screens. Mehrere Betriebe, ein Login. KI-Bestellassistent. Funktioniert in jedem Land: eigenes Währungssymbol, eigener Steuersatz, eigene Sprache.',
   eyebrow='Partner-App · Ausgabe 1', h1='Das ganze Restaurant ', span='von einem Bildschirm aus.',
   p='Ein Rundgang Seite für Seite durch die OrderOrange-Partner-App: Verkauf, Bestellungen und Gäste, Speisekarte, Finanzen, Team, Berichte und Einrichtung. Jeder Screen stammt aus dem Demo-Restaurant Saffron Grill House. Vom einzelnen Café bis zum großen Restaurant mit vielen Filialen: jede Filiale ist unter einem Konto verbunden.',
   open='Partner-App öffnen ↗', tour='Rundgang starten', notice='Screens und Schritte sind auf Englisch; die App selbst läuft vollständig auf Deutsch.'),
 'es': dict(dir='ltr', name='Español',
   title='Sistema POS gratuito y pedidos por QR para restaurantes — Guía para socios de OrderOrange',
   desc='OrderOrange es un POS gratuito para restaurantes y cafeterías: pedidos por QR en la mesa, pantalla de cocina, reparto, reservas, nóminas e informes. Guía paso a paso con 41 pantallas reales. Varios negocios, un solo acceso. Asistente de pedidos con IA. Funciona en cualquier país: elige tu símbolo de moneda, tu impuesto y tu idioma.',
   eyebrow='App para socios · Edición 1', h1='Dirige todo el restaurante ', span='desde una sola pantalla.',
   p='Un recorrido página a página por la app para socios de OrderOrange: ventas, pedidos y clientes, carta, dinero, equipo, informes y configuración. Cada pantalla proviene del restaurante de demostración Saffron Grill House. De una sola cafetería a un gran restaurante con varias sucursales: cada sucursal está conectada bajo una misma cuenta.',
   open='Abrir la app para socios ↗', tour='Empezar el recorrido', notice='Las pantallas y los pasos están en inglés; la app funciona completamente en español.'),
 'fr': dict(dir='ltr', name='Français',
   title='Caisse (POS) gratuite et commande par QR pour restaurants — Guide partenaire OrderOrange',
   desc='OrderOrange est une caisse gratuite pour restaurants et cafés : commande par QR à table, écran cuisine, livraison, réservations, paie et rapports. Guide pas à pas avec 41 écrans réels. Plusieurs établissements, un seul compte. Assistant de commande IA. Fonctionne dans tout pays : votre symbole monétaire, votre taux de TVA, votre langue.',
   eyebrow='App partenaire · Édition 1', h1='Gérez tout le restaurant ', span='depuis un seul écran.',
   p="Une visite page par page de l'app partenaire OrderOrange : ventes, commandes et clients, carte, finances, équipe, rapports et réglages. Chaque écran vient du restaurant de démonstration Saffron Grill House. D'un simple café à un grand restaurant multi-sites : chaque établissement est relié sous un seul compte.",
   open="Ouvrir l'app partenaire ↗", tour='Commencer la visite', notice="Les écrans et les étapes sont en anglais ; l'app elle-même est entièrement en français."),
 'it': dict(dir='ltr', name='Italiano',
   title='POS gratuito e ordini via QR per ristoranti — Guida partner OrderOrange',
   desc='OrderOrange è un POS gratuito per ristoranti e caffè: ordini via QR al tavolo, schermo cucina, consegne, prenotazioni, stipendi e report. Guida passo passo con 41 schermate reali. Più attività, un solo accesso. Assistente ordini con IA. Funziona in qualsiasi paese: simbolo di valuta, aliquota e lingua a scelta.',
   eyebrow='App partner · Edizione 1', h1='Gestisci tutto il ristorante ', span='da un solo schermo.',
   p="Un tour pagina per pagina dell'app partner OrderOrange: vendite, ordini e ospiti, menu, denaro, team, report e impostazioni. Ogni schermata viene dal ristorante demo Saffron Grill House. Da un singolo caffè a un grande ristorante con più sedi: ogni sede è collegata sotto un unico account.",
   open="Apri l'app partner ↗", tour='Inizia il tour', notice="Schermate e passaggi sono in inglese; l'app è interamente in italiano."),
 'ja': dict(dir='ltr', name='日本語',
   title='レストラン向け無料POS・QR注文アプリ — OrderOrange パートナーガイド',
   desc='OrderOrangeはレストラン・カフェ向け無料POSです。テーブルQR注文、キッチン画面、配達、予約、給与、レポートまで。実際の41画面で手順を解説。 複数店舗を1つのアカウントで。 AI注文アシスタント。 どの国でも使えます。通貨記号、税率、言語を自分で選べます。',
   eyebrow='パートナーアプリ · 第1版', h1='レストランのすべてを ', span='ひとつの画面から。',
   p='OrderOrangeパートナーアプリをページごとに案内します：販売、注文とお客様、メニュー、お金、チーム、レポート、設定。すべての画面はデモ店 Saffron Grill House のものです。 1軒のカフェから大規模な多店舗レストランまで：全店舗が1つのアカウントでつながります。',
   open='パートナーアプリを開く ↗', tour='ツアーを始める', notice='画面と手順は英語です。アプリ本体は日本語で利用できます。'),
 'pt': dict(dir='ltr', name='Português',
   title='POS gratuito e pedidos por QR para restaurantes — Guia do parceiro OrderOrange',
   desc='O OrderOrange é um POS gratuito para restaurantes e cafés: pedidos por QR na mesa, ecrã de cozinha, entregas, reservas, salários e relatórios. Guia passo a passo com 41 ecrãs reais. Vários negócios, um só login. Assistente de pedidos com IA. Funciona em qualquer país: escolha o símbolo da moeda, a taxa e o idioma.',
   eyebrow='App do parceiro · Edição 1', h1='Gira todo o restaurante ', span='a partir de um só ecrã.',
   p='Uma visita página a página pela app do parceiro OrderOrange: vendas, pedidos e clientes, menu, dinheiro, equipa, relatórios e configuração. Cada ecrã vem do restaurante de demonstração Saffron Grill House. De um único café a um grande restaurante com várias filiais: cada filial está ligada numa só conta.',
   open='Abrir a app do parceiro ↗', tour='Começar a visita', notice='Os ecrãs e os passos estão em inglês; a app funciona totalmente em português.'),
 'ru': dict(dir='ltr', name='Русский',
   title='Бесплатная POS-система и QR-заказы для ресторанов — руководство партнёра OrderOrange',
   desc='OrderOrange — бесплатная касса для ресторанов и кафе: QR-заказ за столом, кухонный экран, доставка, бронирование, зарплаты и отчёты. Пошаговое руководство с 41 реальными экранами. Несколько заведений — один аккаунт. ИИ-помощник для заказов. Работает в любой стране: свой символ валюты, своя ставка налога, свой язык.',
   eyebrow='Приложение партнёра · выпуск 1', h1='Управляйте всем рестораном ', span='с одного экрана.',
   p='Страница за страницей по приложению партнёра OrderOrange: продажи, заказы и гости, меню, деньги, команда, отчёты и настройки. Все экраны — из демо-ресторана Saffron Grill House. От одного кафе до крупного ресторана с несколькими филиалами: все филиалы связаны в одном аккаунте.',
   open='Открыть приложение партнёра ↗', tour='Начать тур', notice='Экраны и шаги на английском; само приложение полностью на русском.'),
 'tr': dict(dir='ltr', name='Türkçe',
   title="restoranlar için ücretsiz POS ve QR sipariş uygulaması — OrderOrange Partner Rehberi",
   desc="OrderOrange,ki restoran ve kafeler için ücretsiz POS: masada QR sipariş, mutfak ekranı, teslimat, rezervasyon, maaş ve raporlar. 41 gerçek ekranla adım adım rehber. Tek girişle birden fazla işletme. Yapay zekâ sipariş asistanı. Her ülkede çalışır: kendi para birimi simgenizi, vergi oranınızı ve dilinizi seçin.",
   eyebrow='Partner uygulaması · 1. baskı', h1='Tüm restoranı ', span='tek ekrandan yönetin.',
   p='OrderOrange partner uygulamasında sayfa sayfa tur: satış, siparişler ve misafirler, menü, para, ekip, raporlar ve kurulum. Her ekran Saffron Grill House demo restoranından. Tek bir kafeden çok şubeli büyük restorana: her şube tek hesap altında bağlıdır.',
   open='Partner uygulamasını aç ↗', tour='Tura başla', notice='Ekranlar ve adımlar İngilizce; uygulamanın kendisi tamamen Türkçe çalışır.'),
 'zh': dict(dir='ltr', name='中文',
   title='餐厅免费POS与扫码点餐应用 — OrderOrange 合作伙伴指南',
   desc='OrderOrange 是面向餐厅和咖啡馆的免费POS：桌面扫码点餐、厨房看板、外送、预订、工资和报表。41个真实界面，逐步讲解。 一个账号管理多家店。 AI点餐助手。 适用于任何国家：自选货币符号、税率和语言。',
   eyebrow='合作伙伴应用 · 第1版', h1='在一个屏幕上 ', span='管理整家餐厅。',
   p='逐页介绍 OrderOrange 合作伙伴应用：销售、订单与顾客、菜单、财务、团队、报表和设置。所有界面来自演示店 Saffron Grill House。 从一家咖啡馆到大型多门店餐厅：所有门店在一个账号下互联。',
   open='打开合作伙伴应用 ↗', tour='开始导览', notice='界面和步骤为英文；应用本身完全支持中文。'),
}
ORDER = ['en', 'ar', 'fa', 'ur', 'hi', 'de', 'es', 'fr', 'it', 'ja', 'pt', 'ru', 'tr', 'zh']
def url(l): return BASE + ('guide.html' if l == 'en' else f'guide.{l}.html')

# ---------------------------------------------------------------- pieces shared by every variant
faq = [(html.unescape(q), html.unescape(a)) for q, a in re.findall(r'<details[^>]*><summary>(.*?)</summary><p>(.*?)</p></details>', src)]
def head_for(l):
    t = L[l]
    alts = ''.join(f'<link rel="alternate" hreflang="{k}" href="{url(k)}">' for k in ORDER) + f'<link rel="alternate" hreflang="x-default" href="{url("en")}">'
    ld = [
      {"@context": "https://schema.org", "@type": "SoftwareApplication", "name": "OrderOrange Partner", "applicationCategory": "BusinessApplication",
       "applicationSubCategory": "Restaurant POS", "operatingSystem": "Web, Android, iOS, Windows", "url": "https://partner.orderorange.com/",
       "image": BASE + "guide/og-guide.jpg", "inLanguage": l, "description": t['desc'],
       "offers": {"@type": "Offer", "price": "0", "priceCurrency": "OMR", "availability": "https://schema.org/InStock"},
       "featureList": ["Free restaurant POS system", "Drag-and-drop floor plan with salons and tables", "Works in any country with your own currency symbol", "Your own tax rate and opening hours", "Stock and purchases by barcode with supplier invoices and installments", "QR code menu, free, with table ordering", "Kitchen display screen (KDS) for the pass, with bump, per-station filtering and late-order timers", "Warehouse map with drag-and-drop places, stock transfers and a central warehouse for branches", "Waste log with reasons, cost and undo", "Instagram posting and scheduling from the menu, with a menu poster", "WhatsApp dish carousel with Order now buttons", "Loyalty programme with points, rewards, tiers and WhatsApp nudges", "Online ordering system for restaurants", "REST API for CRM, accounting and ERP integrations", "Offline POS edition on request", "Dish availability by weekday, hours and lead time", "AI ordering assistant for guests and staff, by text or voice", "Assistant runs on the platform's own vocabulary \u2014 no outside AI service, no per-message cost", "Report of every question guests asked the assistant", "Large multi-branch restaurants connected under one account", "Several businesses under one login", "Customer ordering website and app at orderorange.com", "Smart search across restaurants and dishes in 14 languages with typo and keyboard correction", "Point of sale", "Tablet POS", "Table QR ordering", "Live kitchen board", "Delivery hand-over", "Reservations", "Table chat", "Recipes and raw materials",
                       "Bills and purchases", "Payroll", "Supply contracts", "Roles and permissions", "Attendance", "Product report: what sells and what each dish earns", "Production capacity: portions the shelf can still make, and the material blocking each", "Consumption report: materials burned by sales, with days of stock left", "Invoice trail: cancel, replace and a before-and-after edit history", "Receipt designer"],
       "publisher": {"@type": "Organization", "name": "OrderOrange", "url": "https://www.orderorange.com/", "logo": "https://www.orderorange.com/logo.svg"},
       "areaServed": [{"@type": "Place", "name": "Worldwide"}], "isAccessibleForFree": True, "sameAs": ["https://github.com/majidtek/OrderOrange"]},
      {"@context": "https://schema.org", "@type": "WebPage", "@id": url(l), "url": url(l), "name": t['title'], "description": t['desc'], "inLanguage": l,
       "isPartOf": {"@type": "WebSite", "name": "OrderOrange Partner", "url": "https://partner.orderorange.com/"},
       "primaryImageOfPage": BASE + "guide/og-guide.jpg", "dateModified": "2026-09-17"},
      {"@context": "https://schema.org", "@type": "BreadcrumbList", "itemListElement": [
        {"@type": "ListItem", "position": 1, "name": "OrderOrange", "item": "https://www.orderorange.com/"},
        {"@type": "ListItem", "position": 2, "name": "Partner Guide", "item": url(l)}]},
    ]
    if faq:
        ld.append({"@context": "https://schema.org", "@type": "FAQPage", "mainEntity": [
            {"@type": "Question", "name": q, "acceptedAnswer": {"@type": "Answer", "text": a}} for q, a in faq]})
    return (f'<meta name="description" content="{html.escape(t["desc"], quote=True)}">'
            f'<meta name="robots" content="index,follow,max-image-preview:large">'
            f'<link rel="canonical" href="{url(l)}">' + alts +
            f'<meta property="og:type" content="website"><meta property="og:site_name" content="OrderOrange"><meta property="og:locale" content="{l}">'
            f'<meta property="og:title" content="{html.escape(t["title"], quote=True)}"><meta property="og:description" content="{html.escape(t["desc"], quote=True)}">'
            f'<meta property="og:url" content="{url(l)}"><meta property="og:image" content="{BASE}guide/og-guide.jpg"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630">'
            f'<meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{html.escape(t["title"], quote=True)}">'
            f'<meta name="twitter:description" content="{html.escape(t["desc"], quote=True)}"><meta name="twitter:image" content="{BASE}guide/og-guide.jpg">'
            + ''.join('<script type="application/ld+json">' + json.dumps(x, ensure_ascii=False) + '</script>' for x in ld))

LANGBAR_CSS = ('.lsel{position:relative;flex:0 0 36px;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);display:inline-flex;align-items:center;justify-content:center;gap:6px;margin-inline-end:8px;color:var(--ink-2);font:800 12.5px Manrope,sans-serif;cursor:pointer}'
'.lsel:hover{border-color:var(--accent);color:var(--accent-deep)}.lsel svg{width:18px;height:18px;flex:0 0 18px}.lsel b{display:none;white-space:nowrap}.lsel i{display:none;font-style:normal;opacity:.6}'
'.lsel-wrap{display:inline-flex}html.ldlg-open{overflow:hidden}'
'.lmenu{position:fixed;inset:0;z-index:200;display:flex;align-items:center;justify-content:center;padding:16px;background:rgba(36,31,27,.42);backdrop-filter:blur(6px);-webkit-backdrop-filter:blur(6px);opacity:0;pointer-events:none;transition:opacity .18s}'
'.lmenu.open{opacity:1;pointer-events:auto}'
'.lcard{direction:ltr;width:min(560px,100%);max-height:calc(100vh - 32px);overflow:auto;background:#fff;border-radius:24px;box-shadow:0 30px 80px rgba(36,31,27,.28);padding:16px 16px 18px;transform:translateY(14px) scale(.97);transition:transform .2s}'
'.lmenu.open .lcard{transform:none}'
'.lhead{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:2px 6px 12px}.lhead b{font:800 17px Manrope,sans-serif;color:var(--ink)}'
'.lx{width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);color:var(--ink-2);font-size:18px;line-height:1;display:flex;align-items:center;justify-content:center;cursor:pointer}.lx:hover{border-color:var(--accent);color:var(--accent-deep)}'
'.lgrid{display:grid;grid-template-columns:1fr 1fr;gap:6px}@media (min-width:640px){.lgrid{grid-template-columns:1fr 1fr 1fr}}'
'.lgrid a{direction:ltr;display:flex;align-items:center;gap:10px;padding:10px 12px;border-radius:14px;border:1px solid transparent;color:var(--ink);text-decoration:none;font:600 14.5px Manrope,sans-serif;white-space:nowrap;outline:none;text-align:left}'
'.lgrid a:hover,.lgrid a:focus-visible{background:var(--bg-2);border-color:var(--line)}.lgrid a.on{background:#FFF1E6;border-color:#FFD2B8;color:#B33F00}'
'.lgrid .fl{flex:0 0 34px;height:24px;border-radius:7px;background:var(--bg-2);color:var(--ink-2);font:800 11px Manrope,sans-serif;letter-spacing:.6px;display:flex;align-items:center;justify-content:center}.lgrid a.on .fl{background:#FF5A00;color:#fff}'
'.lgrid .nm{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;unicode-bidi:plaintext}.lgrid .ck{flex:0 0 auto;opacity:0;font-weight:800;color:#FF5A00}.lgrid a.on .ck{opacity:1}'
'@media (min-width:901px){.lsel{width:auto;padding:0 12px 0 10px;border-radius:999px}.lsel b,.lsel i{display:inline}}'
               '.hero .notice{margin:14px 0 0;padding:10px 14px;border-radius:12px;background:var(--panel);color:var(--ink-2);font-size:14px;line-height:1.5}'
               '[dir="rtl"] .hero{direction:rtl}[dir="rtl"] .hero h1{letter-spacing:0}'
               '[dir="rtl"] main,[dir="rtl"] article,[dir="rtl"] section,[dir="rtl"] .txt,[dir="rtl"] .note,[dir="rtl"] .how,[dir="rtl"] .faq,[dir="rtl"] .band,[dir="rtl"] .end,[dir="rtl"] .intro,[dir="rtl"] .catalog,[dir="rtl"] .chapters,[dir="rtl"] .tree,[dir="rtl"] .toc{direction:rtl;text-align:start}'
               '[dir="rtl"] h1,[dir="rtl"] h2,[dir="rtl"] h3{letter-spacing:0}')
MOBILE_TYPE = ('@media (max-width:900px){body{font-size:14.5px}.hero h1{font-size:clamp(30px,8.4vw,40px);letter-spacing:-1.4px}.hero p{font-size:15px;line-height:1.55}.hero .notice{font-size:13px}'
               '.stats b{font-size:26px}.stats span{font-size:10.5px}.txt p,article p{font-size:14.5px;line-height:1.55}.txt h3,article h3{font-size:19px}section h2,.band h2{font-size:24px}.sub,.band .sub{font-size:13.5px}'
               '.end h2{font-size:26px}.end p{font-size:15px}.note{font-size:13px}.how li{font-size:13px}.acts li{font-size:11.5px}.faq p,.faq summary{font-size:14px}.chapters a b{font-size:16px}.catalog .cat-card span{font-size:12.5px}}')
               
# Two-letter badges rather than flag emoji: Windows draws no flag emoji at all (it shows the letters anyway).
# Country badges (Majed: 'for Arabic use Oman, not SA').
FLAGS = {'en': 'GB', 'ar': 'OM', 'fa': 'IR', 'ur': 'PK', 'hi': 'IN', 'de': 'DE', 'es': 'ES', 'fr': 'FR', 'it': 'IT', 'ja': 'JP', 'pt': 'PT', 'ru': 'RU', 'tr': 'TR', 'zh': 'CN'}
def langbar(cur):
    """A globe button that opens a proper menu (Majed 2026-10-15: "make this modern" about the
    browser's plain dropdown): a rounded card with flags, native names and a tick on the current
    language; a bottom sheet on phones. Keyboard: arrows move, Esc closes."""
    items = ''.join(f'<a href="{url(k)}" role="option" lang="{k}" class="{"on" if k == cur else ""}" aria-selected="{"true" if k == cur else "false"}">'
                    f'<span class="fl">{FLAGS[k]}</span><span class="nm">{L[k]["name"]}</span><span class="ck">✓</span></a>' for k in ORDER)
    return ('<div class="lsel-wrap"><button type="button" class="lsel" id="lsel" title="Language" aria-haspopup="listbox" aria-expanded="false"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/></svg>'
            f'<b>{L[cur]["name"]}</b><i>▾</i></button>'
            '<div class="lmenu" id="lmenu" role="dialog" aria-modal="true" aria-label="Language"><div class="lcard"><div class="lhead"><b>Language · اللغة · زبان</b><button type="button" class="lx" id="lx" aria-label="Close">✕</button></div>'
            f'<div class="lgrid" role="listbox">{items}</div></div></div></div>')

LANG_JS = ('<script>(function(){var b=document.getElementById("lsel"),m=document.getElementById("lmenu");if(!b||!m)return;'
           'function set(o){m.classList.toggle("open",o);document.documentElement.classList.toggle("ldlg-open",o);b.setAttribute("aria-expanded",o?"true":"false");if(o){var c=m.querySelector("a.on")||m.querySelector("a");c&&c.focus()}else{b.focus()}}'
           'b.addEventListener("click",function(e){e.stopPropagation();set(!m.classList.contains("open"))});'
           'm.addEventListener("click",function(e){if(e.target===m)set(false)});document.getElementById("lx").addEventListener("click",function(){set(false)});'
           'document.addEventListener("keydown",function(e){if(e.key==="Escape")set(false);if(!m.classList.contains("open"))return;var a=[].slice.call(m.querySelectorAll("a")),i=a.indexOf(document.activeElement);'
           'if(e.key==="ArrowDown"||e.key==="ArrowRight"){e.preventDefault();(a[i+1]||a[0]).focus()}if(e.key==="ArrowUp"||e.key==="ArrowLeft"){e.preventDefault();(a[i-1]||a[a.length-1]).focus()}});})();</script>')

EN = L['en']
def variant(l):
    t = L[l]; page = src
    page = page.replace('<html lang="en">', f'<html lang="{l}" dir="{t["dir"]}">', 1)
    page = page.replace('<title>OrderOrange Partner Guide</title>', f'<title>{html.escape(t["title"])}</title>', 1)
    page = page.replace('</head>', head_for(l) + '</head>', 1)
    page = page.replace('</style>', LANGBAR_CSS + MOBILE_TYPE + '\n</style>', 1)
    page = page.replace('<button class="tmb" id="tmb"', langbar(l) + '<button class="tmb" id="tmb"', 1)
    page = page.replace('</body>', LANG_JS + '</body>', 1)
    if l != 'en':
        for a, b in [(f'<span class="eyebrow">{EN["eyebrow"]}</span>', f'<span class="eyebrow">{t["eyebrow"]}</span>'),
                     (f'<h1>{EN["h1"]}<span>{EN["span"]}</span></h1>', f'<h1>{t["h1"]}<span>{t["span"]}</span></h1>'),
                     (f'<p>{EN["p"]}</p>', f'<p>{t["p"]}</p>'),
                     (f'>{EN["open"]}</a>', f'>{t["open"]}</a>'), (f'>{EN["tour"]}</a>', f'>{t["tour"]}</a>')]:
            assert a in page, (l, a[:60]); page = page.replace(a, b, 1)
        # Everything else on the page — screens, steps, notes, FAQ, menus — in that language too.
        page = i18n.translate_page(page, l)
    return page

for l in ORDER:
    io.open(os.path.join(WWW, 'guide.html' if l == 'en' else f'guide.{l}.html'), 'w', encoding='utf-8', newline='').write(variant(l))

# ---------------------------------------------------------------- robots + sitemap for the partner host
io.open(os.path.join(WWW, 'robots.txt'), 'w', encoding='utf-8', newline='').write(
    'User-agent: *\nAllow: /guide.html\nAllow: /guide.*.html\nAllow: /guide/\nAllow: /api.html\nAllow: /sitemap.xml\nDisallow: /\n\nSitemap: https://partner.orderorange.com/sitemap.xml\n')
sm = ['<?xml version="1.0" encoding="UTF-8"?>', '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml" xmlns:image="http://www.google.com/schemas/sitemap-image/1.1">']
# the guide pages live at www.orderorange.com/guide now and sit in that site's sitemap
sm.append(' <url><loc>https://partner.orderorange.com/api.html</loc><lastmod>2026-09-18</lastmod><changefreq>monthly</changefreq><priority>0.9</priority><image:image><image:loc>https://partner.orderorange.com/guide/og-api.jpg</image:loc><image:title>OrderOrange API</image:title></image:image></url>')
sm.append('</urlset>')
io.open(os.path.join(WWW, 'sitemap.xml'), 'w', encoding='utf-8', newline='').write('\n'.join(sm) + '\n')
print('seo: guide + %d variants, %d FAQ entries, robots.txt, sitemap.xml' % (len(ORDER) - 1, len(faq)))
