# -*- coding: utf-8 -*-
"""Consumption becomes a screen of its own in the Reports chapter, beside the product
report it answers. HOW, NOTES and the catalogue are escaped — only the screen description
takes markup. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screen
s = io.open('gen.py', encoding='utf-8').read()
anchor = "  (F('0104_'), 'Product report',"
screen = (
 "  (F('0215_'), 'Consumption', 'The product report says what you sold; this says what it cost you to sell it. "
 "Every order in the period is run back through its recipe, so the figures are worked out rather than guessed: the "
 "material cost of the period, how many orders were counted into it, and how many different materials moved. "
 "<b>Costliest dishes</b> ranks by the money the ingredients consumed, not by menu price \\u2014 which is how a "
 "popular, cheap-looking dish turns out to be the expensive one, and why a price list is sometimes worth rewriting. "
 "<b>Material by material</b> then shows how much of each went, what it was worth, and how many days of it are left "
 "at the current rate: \\u201cLamb \\u00b7 1 day left\\u201d reaches you before the kitchen does. Seven, thirty or "
 "ninety days, Excel and A4 print.'),\n")
if "'Consumption'," not in s:
    assert s.count(anchor) == 1, ('gen.py anchor', s.count(anchor))
    # after the product report, because it answers the question that report raises
    at = s.index(anchor)
    end = s.index("'),\n", at) + len("'),\n")
    s = s[:end] + screen + s[end:]
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen.py: consumption screen added to Reports')

# ---------------------------------------------------------------- how-to and note
s = io.open('gen3.py', encoding='utf-8').read()

anchor = " 'Product report': ['Pick Today, 7 days, 30 days, Year or a date range.',"
how = (" 'Consumption': ['Choose 7, 30 or 90 days.',"
       " 'Read the costliest dishes by what their materials cost you, not by their price.',"
       " 'Open a material to see which dishes consumed it, and watch the days-left badge.',"
       " 'Export to Excel to work out a new price, or print A4 for a purchase meeting.'],\n")
if "'Consumption':" not in s.split('NOTES')[0]:
    assert s.count(anchor) == 1, ('HOW anchor', s.count(anchor))
    s = s.replace(anchor, how + anchor)

note = (" 'Consumption': 'The figures come from the recipes, so they are only as good as the recipes are. A dish with "
        "no recipe contributes nothing here \\u2014 if a material seems to last forever, check that the dishes using "
        "it have their materials entered.',\n")
i = s.index('NOTES = {')
anchor2 = " 'Product report': "
if "'Consumption': 'The figures come from the recipes" not in s:
    j = s.index(anchor2, i)
    s = s[:j] + note + s[j:]

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to and note')

# ---------------------------------------------------------------- path and key actions
s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Product report': ("
entry = (" 'Consumption': ('/materials/report', ['Worked out from the recipes, not guessed',"
         " 'Costliest dishes by ingredient spend', 'Days of stock left per material']),\n")
if "'Consumption': (" not in s:
    assert s.count(anchor) == 1, ('gen2 anchor', s.count(anchor))
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, entry + anchor))
    print('gen2.py: META entry')
