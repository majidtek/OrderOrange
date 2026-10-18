# -*- coding: utf-8 -*-
"""The reports Majed pointed at, written properly: the product report screen gets the
detail it was missing, and the catalogue entries for production capacity, consumption and
the invoice trail stop being one-liners. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the product report screen
s = io.open('gen.py', encoding='utf-8').read()
old = ("  (F('0104_'), 'Product report', 'Which dishes sell and what each brings in, for today, seven days, thirty "
       "days, a year or any date range: dishes sold, revenue, distinct dishes, average price, the top five and a "
       "full table with the share of every product.'),")
new = ("  (F('0104_'), 'Product report', 'Which dishes sell, and what each one brings in. Four figures across the top "
       "— dishes sold, revenue, how many different dishes moved at all, and the average price they went out at — "
       "over today, seven days, thirty days, a year, or any two dates you pick. Below them the <b>top five</b> as "
       "bars you can read at a glance, each showing both what it earned and how many went, because the dish that "
       "sells most and the dish that earns most are rarely the same one. Then <b>every dish</b> in a table: quantity, "
       "average price, revenue and the share of your takings it accounts for, filterable by name. Excel and A4 print "
       "sit in the corner of every view.'),")
if 'the dish that earns most' not in s:
    assert s.count(old) == 1, ('product report', s.count(old))
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('gen.py: product report deepened')

# ---------------------------------------------------------------- the catalogue entries
s = io.open('gen3.py', encoding='utf-8').read()

pairs = [
 # Production capacity — what the shelf can still make, and what is holding it back
 (("  ('Production capacity', '/materials/capacity', 'How many of each dish the stock on hand can still make, "
   "so you know what to buy today.'),"),
  ("  ('Production capacity', '/materials/capacity', 'How many portions of each dish today\\u2019s shelf can still "
   "make \\u2014 and what stops it. Every dish shows the number it can produce and the single material holding it "
   "back (\\u201cLimited by Lamb\\u201d), plus what one more unit would buy you (\\u201c+1 kg \\u2192 +2.2 portions\\u201d), "
   "so a purchase order writes itself. Counters at the top split the menu into what can be made, what is blocked "
   "outright, and what is down to its last ten portions.'),")),
 # Consumption — what the sales burned off the shelf
 (("  ('Consumption', '/materials/report', 'Raw materials used in a period, computed from recipes and sales, "
   "against purchases and stock.'),"),
  ("  ('Consumption', '/materials/report', 'What your sales burned off the shelf, dish by dish, worked out from the "
   "recipes rather than guessed: material cost for the period, how many orders it counted and how many materials "
   "moved. The <b>costliest dishes</b> are ranked by the money their ingredients consumed, not by their price \\u2014 "
   "which is how a popular dish turns out to be the expensive one. Then <b>material by material</b>, each with how "
   "much went and how many days of it are left at the current rate, so \\u201cLamb: 1 day left\\u201d reaches you "
   "before the kitchen does.'),")),
]
done = 0
for old, new in pairs:
    if new[:60] in s:
        continue
    assert s.count(old) == 1, (old[:44], s.count(old))
    s = s.replace(old, new)
    done += 1

# The invoice trail belongs in Money, next to the bill report.
anchor = ("  ('Salary report', '/staff/salary-report', 'Payroll per month and per employee: base, hours, leave, "
          "deductions and what was paid.'),")
invoices = (anchor + "\n  ('Invoices', '/invoices', 'The paper trail. <b>Manage</b> lists every invoice with its "
            "customer, items, total and state, and lets you cancel one or replace it with a corrected copy. "
            "<b>Removed invoices</b> keeps what was cancelled \\u2014 who added it, who removed it, the reason they "
            "typed and which invoice replaced it. <b>Edit history</b> shows the correction itself side by side, "
            "before and after, with the added lines picked out, so a change can be explained months later.'),")
if "'Invoices', '/invoices'" not in s:
    assert s.count(anchor) == 1, ('money group', s.count(anchor))
    s = s.replace(anchor, invoices)
    done += 1

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: %d catalogue entries written' % done)

# ---------------------------------------------------------------- what search engines are told
s = io.open('seo.py', encoding='utf-8').read()
old = '"Daily and product reports",'
new = ('"Product report: what sells and what each dish earns", '
       '"Production capacity: portions the shelf can still make, and the material blocking each", '
       '"Consumption report: materials burned by sales, with days of stock left", '
       '"Invoice trail: cancel, replace and a before-and-after edit history",')
if 'Production capacity: portions' not in s:
    assert s.count(old) == 1, ('featureList', s.count(old))
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('seo.py: the reports are named in the feature list')
