# -*- coding: utf-8 -*-
"""The tables board, written from what is actually on it. Only the screen description
takes markup — HOW and NOTES are escaped. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
old = ("  (F('0021_'), 'Tables', 'Your floor plan, room by room. Each table shows whether it is free, seated, "
       "ordered through the table QR code or waiting for the bill, with the time it has been occupied. Tap a table "
       "to open its running order.'),")
new = ("  (F('0021_'), 'Tables', 'The dining room as it stands right now. Across the top, how much of the house is "
       "free and how much is sitting: <b>44 free, 27 occupied</b> tells a host where to put the next party before "
       "they have finished asking. Each salon is a card carrying its own name, how many tables it holds and how many "
       "people those seat, and inside it every table shows its number, its seats, and what is happening at it \\u2014 "
       "green for free, warm for occupied, a dot for an order still open. Under each seated table is who is there and "
       "for how long: <b>Walk-in</b> for a party the waiter seated, <b>QR</b> when the guests scanned the code and "
       "ordered from their own phones, and a clock counting since they sat down, which is how a table that has been "
       "forgotten becomes obvious. Tap any table to open its running bill and add a round.'),")
if '44 free, 27 occupied' not in s:
    assert s.count(old) == 1, ('tables screen', s.count(old))
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('gen.py: tables screen deepened')

s = io.open('gen3.py', encoding='utf-8').read()
old = (" 'Tables': ['Green is free, orange is seated, a QR badge means the guest ordered from their phone.',"
       " 'Tap a table to open its running order and add rounds.',"
       " 'Bill requested shows when a guest asked to pay from the table chat.'],")
new = (" 'Tables': ['Read the two counters at the top for free and occupied seats across the whole floor.',"
       " 'Green is free, warm is seated, and a dot means an order is still open on that table.',"
       " 'Walk-in means the waiter seated them; QR means the guests scanned the code and ordered themselves.',"
       " 'Watch the clock under a seated table \\u2014 it counts from when they sat, so a forgotten table stands out.',"
       " 'Tap a table to open its running bill and add a round.'],")
if 'counters at the top for free and occupied' not in s:
    assert s.count(old) == 1, ('tables how-to', s.count(old))
    s = s.replace(old, new)
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen3.py: tables how-to rewritten')
