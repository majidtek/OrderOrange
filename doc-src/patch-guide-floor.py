# -*- coding: utf-8 -*-
"""The guide learns how the floor is arranged: salons and tables are dragged, dropped and
resized, and the plan saves itself. Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screen
s = io.open('gen.py', encoding='utf-8').read()
anchor = "  (F('0200_'), 'Table QR codes',"
screen = ("  (F('0210_'), 'Arranging the floor', 'Your dining room is drawn, not typed. Each salon is a card you "
          "drag by the grip in its corner and resize from its edge; the tables inside travel with it, and a salon "
          "that is made smaller pulls its own tables back in rather than leaving them stranded. Salons cannot be "
          "dropped on top of one another, so the plan always matches a room a waiter could walk through. "
          "<b>Add salon</b> and <b>Add table</b> build it out, the pencil renames, the bin removes, and every move "
          "is saved the moment you let go — there is no Save button to forget. The strip at the top counts free and "
          "occupied seats across the whole floor.'),\n")
if "'Arranging the floor'" not in s:
    assert s.count(anchor) == 1, ('gen.py anchor', s.count(anchor))
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, screen + anchor))
    print('gen.py: floor screen added to Selling')

# ---------------------------------------------------------------- how-to and note
s = io.open('gen3.py', encoding='utf-8').read()

anchor = " 'Table QR codes': ['Set the width"
how = (" 'Arranging the floor': ['Press <b>Add salon</b> for each room you serve in, then <b>Add table</b> inside it.',"
       " 'Drag a salon by the grip at its top-left to move it; drag the corner marker to resize it.',"
       " 'Drag a table to a new place inside its salon, or onto another salon to move it there.',"
       " 'Use the pencil to rename a salon and the bin to remove it; every change saves as you drop it.'],\n")
if "'Arranging the floor':" not in s.split('NOTES')[0]:
    assert s.count(anchor) == 1, ('HOW anchor', s.count(anchor))
    s = s.replace(anchor, how + anchor)

note = (" 'Arranging the floor': 'Two rules keep the plan honest: salons never overlap, and a table belongs to the "
        "salon it sits in. That is why a table follows its salon when you move it and is pulled back inside when "
        "you shrink one — the floor on screen always matches the floor your waiters walk.',\n")
i = s.index('NOTES = {')
anchor2 = " 'Table QR codes': "
if "'Arranging the floor': 'Two rules keep the plan honest" not in s:
    j = s.index(anchor2, i)
    s = s[:j] + note + s[j:]

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to and note')

# ---------------------------------------------------------------- the path and key actions
s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Table QR codes': ("
entry = (" 'Arranging the floor': ('/tables', ['Drag a salon by its grip, resize from the corner',"
         " 'Tables travel with their salon', 'Saved the moment you let go']),\n")
if "'Arranging the floor':" not in s:
    assert s.count(anchor) == 1, ('gen2 anchor', s.count(anchor))
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, entry + anchor))
    print('gen2.py: META entry')

# ---------------------------------------------------------------- what search engines are told
s = io.open('seo.py', encoding='utf-8').read()
old = '"featureList": ["Free restaurant POS system",'
new = '"featureList": ["Free restaurant POS system", "Drag-and-drop floor plan with salons and tables",'
if 'Drag-and-drop floor plan' not in s:
    assert s.count(old) == 1
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('seo.py: floor plan in the feature list')
