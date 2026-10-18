# -*- coding: utf-8 -*-
"""The guide learns two things: a shop picks its own currency symbol, and this till runs
in any country. Idempotent — run it, then gen3 → host → seo → api-gen."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screen itself
s = io.open('gen.py', encoding='utf-8').read()
anchor = "  (F('0109_'), 'Invoice designer',"
screen = ("  (F('0209_'), 'Your currency', 'Pick the symbol your prices wear — OMR, the rial glyph, "
          "\\u062f.\\u0625, \\u20b9, \\u0024, \\u20ac, or type your own. It is a setting, not a conversion: "
          "the figures stay exactly as you typed them and only the symbol beside them changes, "
          "everywhere at once \\u2014 the POS, invoices, every report and export, the kitchen screen "
          "and the printed receipt. That is what lets a shop outside Oman run OrderOrange in its "
          "own money.'),\n")
if "'Your currency'" not in s:
    assert s.count(anchor) == 1, ('gen.py anchor', s.count(anchor))
    s = s.replace(anchor, screen + anchor)
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen.py: currency screen added to Setup')

# ---------------------------------------------------------------- how-to, note, and the global copy
s = io.open('gen3.py', encoding='utf-8').read()

anchor = " 'Invoice designer': ['Pick a template, upload your logo and choose its size.',"
how = (" 'Your currency': ['Open Settings and find Currency symbol, under the kitchen language.',"
       " 'Choose a symbol from the list, or type your own in the box beside it \\u2014 up to eight characters.',"
       " 'The example under the box shows how your prices will read; press Save and the whole portal follows.'],\n")
if "'Your currency':" not in s.split('NOTES')[0]:
    assert s.count(anchor) == 1, ('HOW anchor', s.count(anchor))
    s = s.replace(anchor, how + anchor)

anchor = " 'Invoice designer': "
i = s.index('NOTES = {')
note = (" 'Your currency': 'Amounts are never converted. Rials carry three decimals and that stays true "
        "whatever symbol you choose, so set your prices in your own money and the symbol simply reads "
        "correctly beside them.',\n")
if "'Your currency': 'Amounts are never converted" not in s:
    j = s.index(anchor, i)
    s = s[:j] + note + s[j:]

# the guide says out loud that this is not an Oman-only product
anchor = " ('Large restaurants and chains', 'OrderOrange scales from one counter"
global_intro = (" ('Any country, any currency', 'Nothing in OrderOrange is tied to one country. The portal and the "
                "customer app speak fourteen languages and read right to left where they should; each shop picks the "
                "symbol its prices wear, sets its own tax rate, its own opening hours and its own delivery areas, and "
                "prints receipts in the language its kitchen reads. It is used in Oman today, and a caf\\u00e9 in Dubai, "
                "Riyadh, Istanbul, Delhi or London runs the same till with its own money on the screen.'),\n")
if "'Any country, any currency'" not in s:
    assert s.count(anchor) == 1, ('INTRO anchor', s.count(anchor))
    s = s.replace(anchor, global_intro + anchor)

anchor = " ('Which browser and devices are supported?',"
faq = (" ('Can I use OrderOrange outside Oman?', 'Yes. The system is not built around one country: choose your "
       "currency symbol in Settings, set your own tax percentage, opening hours and delivery areas, and run the whole "
       "portal in any of fourteen languages. Prices are stored exactly as you type them \\u2014 nothing is converted \\u2014 "
       "so a shop in the Emirates, Saudi Arabia, India, Turkey or Europe works the same way as any other.'),\n"
       " ('Can I change the currency symbol?', 'Yes, in Settings \\u2192 Currency symbol. Pick one from the list or type "
       "your own, and every screen and printout follows at once: the POS, invoices, reports, exports, the kitchen screen "
       "and the receipt.'),\n")
if "'Can I use OrderOrange outside Oman?'" not in s:
    assert s.count(anchor) == 1, ('FAQ anchor', s.count(anchor))
    s = s.replace(anchor, faq + anchor)

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to, note, global intro and two questions')
