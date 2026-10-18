# -*- coding: utf-8 -*-
"""The barcode screen gets the purchase picture Majed sent (frame_0208 re-taken from production)
and a matching text; a new screen after it shows paying later or in instalments (frame_0209)."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
lines = s.split('\n')
at = [i for i, l in enumerate(lines) if l.startswith("  (F('0208_'), 'Receiving stock by barcode'")]
assert len(at) == 1
lines[at[0]] = ("  (F('0208_'), 'Receiving stock', 'A purchase in three taps. The bar at the top takes a <b>code or barcode</b> — from a scanner, a phone camera or the keyboard — and a shorthand like <b>7*20</b> means twenty of material number 7. Below it the shelf is laid out as tiles, each with its number, cost and unit; tap a tile to add a line, tap again to add one more. The basket on the right fills in quantity, cost per unit and line total, with the supplier, the invoice number and its date above; <b>Templates</b> replays a regular order and <b>Save as template</b> makes one. Choose cash or card, or <b>Later / installments</b>, and press <b>Receive stock</b>: the shelf, the supplier ledger and the bills are all updated at once.'),")
if "'Paying later or in instalments'" not in s:
    lines.insert(at[0] + 1,
     "  (F('0209_'), 'Paying later or in instalments', 'Not every supplier is paid on the spot. <b>Pay now</b> settles the invoice in full; <b>Pay later</b> parks it for one payment on a date you pick; <b>Installments</b> splits it into 2, 3, 4 or 6 steps, weekly or monthly, and shows the exact dates and amounts before you commit. The method — cash, card, transfer, cheque — can be fixed now or left as <b>Decide when paying</b>, so each step records how it was actually settled. Everything lands in Bills and purchases, where the open steps wait with their due dates and a single press marks one paid.'),")
io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('gen.py: receiving stock retold, instalments screen added')

s = io.open('gen2.py', encoding='utf-8').read()
if "'Receiving stock':" not in s:
    old = " 'Receiving stock by barcode': ("
    assert s.count(old) == 1
    i = s.index(old); j = s.index('\n', i) + 1
    s = s[:i] + (" 'Receiving stock': ('/materials/purchases', ['Code or barcode bar: 7*20 = twenty of no. 7', 'Tap a tile to add a line', 'Templates for regular orders', 'Cash, card, later or instalments']),\n"
                 " 'Paying later or in instalments': ('/materials/purchases', ['Pay now, later, or in steps', '2, 3, 4 or 6 steps, weekly or monthly', 'Method fixed now or decided when paying', 'Open steps wait in Bills and purchases']),\n") + s[j:]
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s); print('gen2.py: META')

s = io.open('gen3.py', encoding='utf-8').read()
changed = False
for key in ("'Receiving stock by barcode': [", "'Receiving stock by barcode': '"):
    if key in s:
        s = s.replace(key, key.replace('Receiving stock by barcode', 'Receiving stock')); changed = True
if "'Paying later or in instalments': [" not in s:
    how_anchor = " 'Waste': ['Open Waste under Operations."
    assert s.count(how_anchor) == 1
    s = s.replace(how_anchor,
        " 'Paying later or in instalments': ['Fill the basket, then press Later / installments next to Cash and Card.',"
        " 'Pick Pay later and a date, or Installments and the number of steps and the rhythm - the dates and amounts appear at once.',"
        " 'Leave the method as Decide when paying unless you already know; press Save, then Receive stock.',"
        " 'Open Bills and purchases when a step falls due and mark it paid with its real method.'],\n" + how_anchor)
    note_anchor = " 'Waste': 'A dish written off"
    assert s.count(note_anchor) == 1
    s = s.replace(note_anchor,
        " 'Paying later or in instalments': 'Amounts are split to the last baisa - the final step absorbs the rounding, so the steps always add up to the invoice exactly.',\n" + note_anchor)
    changed = True
if changed:
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s); print('gen3.py: how-to and note')
