# -*- coding: utf-8 -*-
"""Warehouses (map, adding a place, central warehouse for branches) and Waste join the guide,
in the stock chapter after receiving by barcode. Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screens
s = io.open('gen.py', encoding='utf-8').read()
if "'Warehouses map'" not in s:
    lines = s.split('\n')
    at = [i for i, l in enumerate(lines) if l.startswith("  (F('0208_'), 'Receiving stock by barcode'")]
    assert len(at) == 1, ('gen.py anchor', at)
    new = [
     "  (F('0220_'), 'Warehouses map', 'Where your stock actually is, drawn as a map rather than a list. Every place is a card you drag into position — a supplier, a delivery van, the main store, a cold room, the kitchen, the bar, your guests — wearing the picture you chose for it, and a dashed line from one card to the next says <b>this feeds that</b>. The materials page stays the single truth about how much you own; the map only splits that number by place, and anything you have not put anywhere yet stays visible as <b>Not in a place yet</b> so the two always reconcile. Tap a place to see what it holds; below the map, <b>Move stock</b> takes any quantity from one place to another and every move is logged with who did it and why.'),",
     "  (F('0221_'), 'Adding a place', 'A place is a name, a picture and a kind. Ten pictures cover a real supply chain — factory, truck, warehouse, store, restaurant, kitchen, cold room, freezer, bar, customers — and the kind (main store, dry store, cold room, freezer, kitchen, bar, branch) is what the reports group by. One place is the <b>default</b>: where stock lands when nothing else is named. New places appear on the map in a loose grid so they never pile up; drag them where you like, and the layout is remembered for every screen size.'),",
     "  (F('0222_'), 'Central warehouse for branches', 'If you run more than one business, mark one place as the <b>central warehouse</b> and every branch you own sees it: a dark card in the bar with what it holds and what it is worth. A branch pulls from it by choosing it under <b>From</b>; head office pushes by choosing <b>Branch: …</b> under <b>To</b>. This is the one move that changes ownership: the quantity leaves the central store\\u2019s materials, is added to the branch\\u2019s own materials (matched by name and unit, created if the branch never stocked it), and lands in the branch\\u2019s default place — so each branch keeps its own true count, and both shops get a line in their log.'),",
     "  (F('0223_'), 'Waste', 'What was thrown away, what it cost, and why. Record a write-off in seconds: a material or a finished dish, how much, and one of eight reasons — spoiled, expired, broken, over-made, staff meal, complaint, training, other. Recording takes it off the shelf; a dish also takes its recipe off the shelf and is valued at what its materials cost. The tiles at the top say what the last 7, 30 or 90 days threw away, what share of your stock that is, and the biggest cause; <b>Where it goes</b> and <b>Most wasted</b> point at what to fix first, and any entry can be undone with one press.'),",
    ]
    lines[at[0] + 1:at[0] + 1] = new
    io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines))
    print('gen.py: four screens added after receiving by barcode')

# ---------------------------------------------------------------- paths and key actions
s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Recipes': ("
entry = (" 'Warehouses map': ('/warehouses', ['Drag places into position', 'Draw a line: this feeds that', 'Move stock between places', 'Every move is logged']),\n"
         " 'Adding a place': ('/warehouses', ['Ten pictures: factory to customers', 'Kind: main, dry, cold, freezer, kitchen, bar, branch', 'One default place']),\n"
         " 'Central warehouse for branches': ('/warehouses', ['One central place per owner', 'Branches pull, head office pushes', 'Materials matched by name and unit', 'Logged in both shops']),\n"
         " 'Waste': ('/waste', ['Material or finished dish', 'Eight reasons', '7 / 30 / 90 day view', 'Undo with one press']),\n")
if "'Warehouses map':" not in s:
    assert s.count(anchor) == 1, ('gen2 anchor', s.count(anchor))
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, entry + anchor))
    print('gen2.py: META entries')

# ---------------------------------------------------------------- how-to, notes, FAQ
s = io.open('gen3.py', encoding='utf-8').read()
how_anchor = " 'Recipes': ['Open a dish and add each material"
how = (" 'Warehouses map': ['Open Warehouses under Operations and press Add a place for each spot you keep stock in.',"
       " 'Drag a card to arrange the map; press the arrow on a card, then tap the place it feeds, to draw a line.',"
       " 'Tap a place to see what it holds. Under Move stock pick From and To, type quantities and press Move.',"
       " 'Not in a place yet is what the materials page owns that you have not put anywhere - move it in when you count.'],\n"
       " 'Adding a place': ['Give it a name, pick a picture and a kind, and switch on Make this the default place for the one stock should land in.',"
       " 'Edit a place from the pencil on its card; deleting one sends its stock back to Not in a place yet, never into thin air.'],\n"
       " 'Central warehouse for branches': ['In the central store, edit the place and switch on Central warehouse for all my branches.',"
       " 'In a branch, choose the central warehouse under From, type quantities and press Move - it lands in the default place.',"
       " 'From head office, choose the central place under From and Branch: name under To to push a delivery.'],\n"
       " 'Waste': ['Open Waste under Operations. Pick Material or Finished dish, choose the item and how much.',"
       " 'Choose the reason, add a note if it helps, and press Record it - the shelf is updated at once.',"
       " 'Switch the tiles between 7, 30 and 90 days; press the undo arrow on an entry recorded by mistake.'],\n")
if "'Warehouses map': [" not in s:
    assert s.count(how_anchor) == 1, ('HOW anchor', s.count(how_anchor))
    s = s.replace(how_anchor, how + how_anchor)

note_anchor = " 'Recipes': 'Production capacity"
note = (" 'Warehouses map': 'The map is the same on a phone as on a desk: it scales down to fit and scrolls sideways, so the layout you arranged is the one everyone sees.',\n"
        " 'Central warehouse for branches': 'Moves inside one shop never change its totals; only a move from the central warehouse to a branch does, because that stock changes hands.',\n"
        " 'Waste': 'A dish written off is valued at its recipe cost, not its menu price - waste is what it cost you, not what you would have charged.',\n")
if "'Warehouses map': 'The map" not in s:
    assert s.count(note_anchor) == 1, ('NOTES anchor', s.count(note_anchor))
    s = s.replace(note_anchor, note + note_anchor)

faq_anchor = " ('Can it replace a kitchen display system?',"
faq = (" ('Can I run several branches from one warehouse?', 'Yes. Mark one place as the central warehouse and every business you own sees it. "
       "Branches pull stock from it, head office pushes deliveries to them, and the quantities move between the branches\\u2019 own materials "
       "so each shop keeps a true count. Inside one shop you can also map every place stock is kept - store, cold room, kitchen, bar - "
       "and move stock between them with a full log.'),\n"
       " ('Does it track waste?', 'Yes. Every write-off is recorded with a reason - spoiled, expired, broken, over-made, staff meal, complaint, "
       "training - and taken off the shelf at cost. The Waste page shows what the last 7, 30 or 90 days threw away, what share of your stock "
       "that is, and which items and reasons cost the most.'),\n")
if 'Can I run several branches from one warehouse?' not in s:
    assert s.count(faq_anchor) == 1, ('FAQ anchor', s.count(faq_anchor))
    s = s.replace(faq_anchor, faq + faq_anchor)
io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to, notes, two FAQ answers')

# ---------------------------------------------------------------- what search engines are told
s = io.open('seo.py', encoding='utf-8').read()
old = '"Kitchen display screen (KDS) for the pass, with bump, per-station filtering and late-order timers",'
new = old + ' "Warehouse map with drag-and-drop places, stock transfers and a central warehouse for branches", "Waste log with reasons, cost and undo",'
if 'Warehouse map with drag-and-drop' not in s:
    assert s.count(old) == 1, ('featureList', s.count(old))
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('seo.py: warehouses and waste in the feature list')
