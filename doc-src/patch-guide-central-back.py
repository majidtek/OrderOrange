# -*- coding: utf-8 -*-
"""The central-warehouse screen returns, wearing the head-office map picture (frame_0220,
the one Majed pointed at) instead of the empty branch map. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
if "'Central warehouse for branches'" not in s:
    # the sentence folded into the map screen goes back out; the screen carries it
    old = (" Run more than one business? Mark one place as the <b>central warehouse</b> "
           "and every branch you own can draw from it — the stock changes hands into the branch\\u2019s own materials, with a line in both logs.'),")
    assert s.count(old) == 1
    s = s.replace(old, "'),")
    lines = s.split('\n')
    at = [i for i, l in enumerate(lines) if l.startswith("  (F('0221_'), 'Adding a place'")]
    assert len(at) == 1
    lines.insert(at[0] + 1,
     "  (F('0220_'), 'Central warehouse for branches', 'If you run more than one business, mark one place as the <b>central warehouse</b> — the dark card on the map — and every branch you own sees it: a dark card in its own bar with what it holds and what it is worth. A branch pulls from it by choosing it under <b>From</b>; head office pushes by choosing <b>Branch: …</b> under <b>To</b>. This is the one move that changes ownership: the quantity leaves the central store\\u2019s materials, is added to the branch\\u2019s own materials (matched by name and unit, created if the branch never stocked it), and lands in the branch\\u2019s default place — so each branch keeps its own true count, and both shops get a line in their log.'),")
    io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines))
    print('gen.py: central screen back, on the head-office picture')

s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Waste': ('/waste',"
if "'Central warehouse for branches':" not in s:
    assert s.count(anchor) == 1
    s = s.replace(anchor, " 'Central warehouse for branches': ('/warehouses', ['One central place per owner', 'Branches pull, head office pushes', 'Materials matched by name and unit', 'Logged in both shops']),\n" + anchor)
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s); print('gen2.py: META back')

s = io.open('gen3.py', encoding='utf-8').read()
how_anchor = " 'Waste': ['Open Waste under Operations."
if "'Central warehouse for branches': [" not in s:
    assert s.count(how_anchor) == 1
    s = s.replace(how_anchor,
        " 'Central warehouse for branches': ['In the central store, edit the place and switch on Central warehouse for all my branches.',"
        " 'In a branch, choose the central warehouse under From, type quantities and press Move - it lands in the default place.',"
        " 'From head office, choose the central place under From and Branch: name under To to push a delivery.'],\n" + how_anchor)
note_anchor = " 'Waste': 'A dish written off"
if "'Central warehouse for branches': 'Moves" not in s:
    assert s.count(note_anchor) == 1
    s = s.replace(note_anchor,
        " 'Central warehouse for branches': 'Moves inside one shop never change its totals; only a move from the central warehouse to a branch does, because that stock changes hands.',\n" + note_anchor)
io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to and note back')
