# -*- coding: utf-8 -*-
"""Guide SEO retuned to what people search (Google autocomplete, Oman, 2026-09-18). Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
q = io.open('seo.py', encoding='utf-8').read()
T = {
 'en': ("Restaurant POS system in Oman, free — QR code menu, kitchen display, online ordering | OrderOrange Partner Guide",
        "Free restaurant POS software in Oman: point of sale, QR code menu and table ordering, kitchen display board, online ordering, delivery, reservations, payroll and reports. Step-by-step guide with 40 real screens. Several businesses, one login. AI ordering assistant."),
 'ar': ("نظام نقاط بيع للمطاعم في عمان مجاني — منيو إلكتروني QR، شاشة مطبخ، طلب أونلاين | دليل شريك أوردر أورنج",
        "برنامج كاشير مطعم مجاني في عمان: نقاط بيع، منيو إلكتروني بباركود QR وطلب من الطاولة، شاشة مطبخ، طلب أونلاين، توصيل، حجوزات، رواتب وتقارير. دليل خطوة بخطوة مع ٤٠ شاشة حقيقية. عدة أعمال بحساب واحد. مساعد ذكي للطلبات."),
 'fa': ("نرم‌افزار رستوران رایگان در عمان — صندوق (POS)، منوی QR، نمایشگر آشپزخانه، سفارش آنلاین | راهنمای همکار اردر اورنج",
        "برنامه صندوق رستوران رایگان در عمان: نقطه فروش، منوی QR و سفارش از میز، نمایشگر آشپزخانه، سفارش آنلاین، ارسال، رزرو، حقوق و گزارش‌ها. راهنمای گام‌به‌گام با ۴۰ صفحهٔ واقعی. چند کسب‌وکار با یک حساب. دستیار هوشمند سفارش."),
}
for code, (title, desc) in T.items():
    i = q.index(" '%s': dict(" % code)
    j = q.index("\n   title=", i) + len("\n   title="); qt = q[j]; k = q.index(qt + ",\n", j + 1)
    q = q[:j] + repr(title) + q[k + 1:]
    j = q.index("\n   desc=", i) + len("\n   desc="); qt = q[j]; k = q.index(qt + ",\n", j + 1)
    q = q[:j] + repr(desc) + q[k + 1:]
if 'QR code menu, free' not in q:
    q = q.replace('"featureList": ["REST API', '"featureList": ["Free restaurant POS system", "QR code menu, free, with table ordering", "Kitchen display board", "Online ordering system for restaurants", "REST API')
io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)

g = io.open('gen3.py', encoding='utf-8').read()
a = " ('Is there an API?',"
faq = (" ('Is the POS free? What does a POS system cost in Oman?', 'OrderOrange is free for restaurants and cafés in Oman: the POS, the tablet POS, the QR code menu, the kitchen board, reservations, payroll and every report cost nothing and there is no commission. You only pay for your own hardware, and any Windows PC, tablet or phone will do.'),\n"
       " ('Does it have a QR code menu?', 'Yes, built in and free. Print one code per table from Table QR codes; a scan opens the menu for that table, and the guest orders, calls a waiter or reserves. No separate QR menu generator is needed.'),\n"
       " ('Can it replace a kitchen display system?', 'Yes. Live orders is the kitchen board: new orders arrive with a sound, move through Accepted, Cooking and Ready, and the guest is told at every step. Kitchen printers work alongside it through the till app.'),\n"
       " ('Is there online ordering for delivery and pickup?', 'Yes. Every store gets its own page at orderorange.com with online ordering for delivery or pickup, in 14 languages, and your own couriers can be assigned from the Deliveries page.'),\n")
if 'What does a POS system cost in Oman?' not in g:
    assert g.count(a) == 1; g = g.replace(a, faq + a)
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(g)
print('guide seo retuned')
