"""One-off: AI wording in the guide SEO copy (seo.py) and the customer-site blurbs (14 json files). Idempotent."""
import io, json, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
q = io.open('seo.py', encoding='utf-8').read()
if 'AI ordering assistant for guests' not in q:
    q = q.replace('"featureList": ["Large multi-branch restaurants', '"featureList": ["AI ordering assistant for guests and staff, by text or voice", "Large multi-branch restaurants')
if ' 37 ' in q:
    q = q.replace(' 37 ', ' 38 ').replace('٣٧', '٣٨').replace('۳۷', '۳۸').replace('37画面', '38画面').replace('37个', '38个')
ai = {'en': ' AI ordering assistant.', 'ar': ' مساعد ذكي للطلبات.', 'fa': ' دستیار هوشمند سفارش.', 'ur': ' AI آرڈر اسسٹنٹ۔', 'hi': ' AI ऑर्डर असिस्टेंट।',
      'de': ' KI-Bestellassistent.', 'es': ' Asistente de pedidos con IA.', 'fr': ' Assistant de commande IA.', 'it': ' Assistente ordini con IA.',
      'ja': ' AI注文アシスタント。', 'pt': ' Assistente de pedidos com IA.', 'ru': ' ИИ-помощник для заказов.', 'tr': ' Yapay zekâ sipariş asistanı.', 'zh': ' AI点餐助手。'}
n = 0
for code, t in ai.items():
    i = q.index(" '%s': dict(" % code)
    j = q.index("\n   desc=", i) + len("\n   desc=")
    quote = q[j]
    k = q.index(quote + ",\n", j + 1)          # closing quote of the desc literal
    if t.strip() in q[j:k]: continue
    q = q[:k] + t + q[k:]; n += 1
io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
blurb = {'en': ' Order by chat or voice with the OrderOrange AI assistant.', 'ar': ' اطلب بالدردشة أو بالصوت مع مساعد أوردر أورنج الذكي.',
         'fa': ' با دستیار هوشمند اردر اورنج، با چت یا صدا سفارش دهید.', 'ur': ' OrderOrange AI اسسٹنٹ سے چیٹ یا آواز سے آرڈر کریں۔',
         'hi': ' OrderOrange AI असिस्टेंट से चैट या आवाज़ से ऑर्डर करें।', 'de': ' Bestellen Sie per Chat oder Sprache mit dem OrderOrange-KI-Assistenten.',
         'es': ' Pide por chat o por voz con el asistente de IA de OrderOrange.', 'fr': " Commandez par chat ou à la voix avec l'assistant IA d'OrderOrange.",
         'it': " Ordina via chat o a voce con l'assistente IA di OrderOrange.", 'ja': ' OrderOrangeのAIアシスタントでチャットや音声から注文。',
         'pt': ' Peça por chat ou por voz com o assistente de IA da OrderOrange.', 'ru': ' Заказывайте в чате или голосом с ИИ-помощником OrderOrange.',
         'tr': ' OrderOrange yapay zekâ asistanıyla sohbet veya sesle sipariş verin.', 'zh': ' 通过 OrderOrange AI 助手用聊天或语音下单。'}
m = 0
for code, t in blurb.items():
    p = 'D:/OrderOrange/OrderOrange.ClientCore/Localization/%s.json' % code
    s = io.open(p, encoding='utf-8-sig').read()
    d = json.loads(s)
    if t.strip() in d['seo.siteBlurb']: continue
    old = json.dumps(d['seo.siteBlurb'], ensure_ascii=False)
    new = json.dumps(d['seo.siteBlurb'] + t, ensure_ascii=False)
    assert s.count(old) >= 1, code
    s = s.replace(old, new, 1); json.loads(s)
    io.open(p, 'w', encoding='utf-8-sig', newline='').write(s); m += 1
print('seo descs +%d, blurbs +%d' % (n, m))
