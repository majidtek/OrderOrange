# -*- coding: utf-8 -*-
"""The guide's search copy stops sounding Oman-only: one line per language saying it runs
in any country and any currency, plus two feature entries and a wider areaServed.
Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('seo.py', encoding='utf-8').read()

# ---------------------------------------------------------------- the description tail
# Each language's desc gains one sentence. Keyed by a snippet unique to that entry.
TAILS = {
 "Step-by-step guide with 41 real screens. Several businesses, one login. AI ordering assistant.":
   "Step-by-step guide with 42 real screens. Several businesses, one login. AI ordering assistant. Works in any country: pick your own currency symbol, tax rate and language.",
}
for old, new in TAILS.items():
    if old in s and new not in s:
        s = s.replace(old, new)

# Every other language: append its own sentence after the existing description.
ADD = {
 'ar': ("desc='", " يعمل في أي دولة: اختر رمز عملتك ونسبة الضريبة ولغتك."),
 'fa': ("desc='", " در هر کشوری کار می‌کند: نماد پول، نرخ مالیات و زبان خودتان را انتخاب کنید."),
 'ur': ("desc='", " کسی بھی ملک میں کام کرتا ہے: اپنی کرنسی، ٹیکس اور زبان منتخب کریں۔"),
 'hi': ("desc='", " किसी भी देश में काम करता है: अपनी मुद्रा, कर दर और भाषा चुनें।"),
 'de': ("desc='", " Funktioniert in jedem Land: eigenes Währungssymbol, eigener Steuersatz, eigene Sprache."),
 'es': ("desc='", " Funciona en cualquier país: elige tu símbolo de moneda, tu impuesto y tu idioma."),
 'fr': ("desc='", " Fonctionne dans tout pays : votre symbole monétaire, votre taux de TVA, votre langue."),
 'it': ("desc='", " Funziona in qualsiasi paese: simbolo di valuta, aliquota e lingua a scelta."),
 'ja': ("desc='", " どの国でも使えます。通貨記号、税率、言語を自分で選べます。"),
 'pt': ("desc='", " Funciona em qualquer país: escolha o símbolo da moeda, a taxa e o idioma."),
 'ru': ("desc='", " Работает в любой стране: свой символ валюты, своя ставка налога, свой язык."),
 'tr': ("desc='", " Her ülkede çalışır: kendi para birimi simgenizi, vergi oranınızı ve dilinizi seçin."),
 'zh': ("desc='", " 适用于任何国家：自选货币符号、税率和语言。"),
}
lines = s.split('\n')
out = []
current = None
for line in lines:
    stripped = line.strip()
    if stripped.startswith("'") and "': dict(dir=" in stripped:
        current = stripped.split("'")[1]
    if current in ADD and stripped.startswith('desc='):
        tail = ADD[current][1]
        if tail.strip() not in line:
            # the description is a single-quoted python string ending in ',
            # some descriptions are single-quoted, some double — keep whichever it used
            end = line.rstrip()
            assert end.endswith("',") or end.endswith('",'), (current, end[-20:])
            quote = end[-2]
            line = end[:-2] + tail + quote + ","
    out.append(line)
s = '\n'.join(out)

# ---------------------------------------------------------------- structured data
old = '"featureList": ["Free restaurant POS system",'
new = '"featureList": ["Free restaurant POS system", "Works in any country with your own currency symbol", "Your own tax rate and opening hours",'
if 'Works in any country with your own currency symbol' not in s:
    assert s.count(old) == 1
    s = s.replace(old, new)

# a product used in one country today, built for more: say both
old = '"areaServed": {"@type": "Country", "name": "Oman"}},'
new = '"areaServed": [{"@type": "Country", "name": "Oman"}, {"@type": "Place", "name": "Worldwide"}]},'
if 'Worldwide' not in s:
    assert s.count(old) == 1
    s = s.replace(old, new)

io.open('seo.py', 'w', encoding='utf-8', newline='').write(s)
print('seo.py: any-country line in 14 languages, features and areaServed')
