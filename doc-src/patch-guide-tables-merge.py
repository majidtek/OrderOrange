# -*- coding: utf-8 -*-
"""Two screens were showing the same picture: the tables board, once as "Tables" and again
as "Arranging the floor". They are one screen in the app and they are now one screen in the
guide, written to cover both reading the room and rearranging it.

Only the screen description takes markup; HOW and NOTES are escaped. Idempotent."""
import io, os, re
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()

# ---------------------------------------------------------------- drop the duplicate screen
start = s.find("  (F('0210_'), 'Arranging the floor'")
if start >= 0:
    end = s.index("'),\n", start) + len("'),\n")
    s = s[:start] + s[end:]
    print('gen.py: the duplicate floor screen removed')

# ---------------------------------------------------------------- one screen, both jobs
old_start = s.find("  (F('0021_'), 'Tables',")
assert old_start >= 0, 'tables screen missing'
old_end = s.index("'),\n", old_start) + len("'),\n")
new = ("  (F('0021_'), 'Tables', 'The dining room as it stands, and the tool that arranges it. Across the top, how "
       "much of the house is free and how much is sitting \\u2014 <b>44 free, 27 occupied</b> tells a host where to "
       "put the next party before they have finished asking. Each salon is a card carrying its name, how many tables "
       "it holds and how many people those seat; inside it every table shows its number, its seats and what is "
       "happening at it: green for free, warm for occupied, a dot for an order still open. Under a seated table is "
       "who is there and for how long \\u2014 <b>Walk-in</b> when the waiter seated them, <b>QR</b> when the guests "
       "scanned the code and ordered from their own phones \\u2014 and a clock running since they sat, which is how a "
       "forgotten table becomes obvious from across the room. Tap any table to open its running bill and add a round."
       "<br><br>The floor is drawn, not typed. Drag a salon by the grip in its corner to move it and pull its edge to "
       "resize it; the tables inside travel with it, and a salon made smaller pulls its own tables back in rather than "
       "stranding them. Salons cannot be dropped on top of one another, so the plan always matches a room a waiter "
       "could walk through. <b>Add salon</b> and <b>Add table</b> build it out, the pencil renames, the bin removes, "
       "and every move is saved the moment you let go \\u2014 there is no Save button to forget.'),\n")
if '44 free, 27 occupied' not in s or 'The floor is drawn, not typed' not in s:
    s = s[:old_start] + new + s[old_end:]
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen.py: one tables screen covering both')

# ---------------------------------------------------------------- merged how-to and note
s = io.open('gen3.py', encoding='utf-8').read()
old = re.search(r" 'Tables': \[.*?\],\n", s, re.S)
assert old, 'tables HOW missing'
how = (" 'Tables': ['Read the two counters at the top for free and occupied seats across the whole floor.',"
       " 'Green is free, warm is seated, and a dot means an order is still open on that table.',"
       " 'Walk-in means the waiter seated them; QR means the guests scanned the code and ordered themselves.',"
       " 'Watch the clock under a seated table \\u2014 it counts from when they sat, so a forgotten table stands out.',"
       " 'Tap a table to open its running bill and add a round.',"
       " 'To rearrange: drag a salon by the grip at its corner, pull its edge to resize, and use Add salon, Add table, "
       "the pencil and the bin. Nothing needs saving.'],\n")
if 'To rearrange: drag a salon by the grip' not in s:
    s = s[:old.start()] + how + s[old.end():]

note = (" 'Tables': 'Two rules keep the plan honest: salons never overlap, and a table belongs to the salon it sits "
        "in. That is why a table follows its salon when you move it and is pulled back inside when you shrink one "
        "\\u2014 the floor on screen always matches the floor your waiters walk.',\n")
i = s.index('NOTES = {')
m = re.search(r" 'Tables': '.*?',\n", s[i:], re.S)
if m and 'Two rules keep the plan honest' not in s:
    s = s[:i + m.start()] + note + s[i + m.end():]
elif 'Two rules keep the plan honest' not in s:
    j = s.index(" 'Table QR codes': ", i)
    s = s[:j] + note + s[j:]

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to and note merged')
