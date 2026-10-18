# -*- coding: utf-8 -*-
"""The kitchen display joins the guide, and the FAQ stops claiming the live orders list
is the kitchen board — there is a real one now. Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# ---------------------------------------------------------------- the screen
s = io.open('gen.py', encoding='utf-8').read()
anchor = "  (F('0032_'), 'Take-out and dine-in board',"
screen = ("  (F('0211_'), 'Kitchen display', 'A screen for the pass instead of a printer. Tickets arrive in two "
          "lanes — <b>New</b> and <b>Cooking</b>, oldest first, because the oldest ticket is always the next job — "
          "and each one counts up from the moment the food was ordered, its edge turning amber and then red as it "
          "runs late against your own preparation time. A cook ticks a line off as it is plated and presses the one "
          "big button: <b>Start cooking</b>, then <b>Ready</b>. What makes it a kitchen screen rather than a list of "
          "web orders is where the tickets come from: online orders AND every round a waiter rings up at a table, "
          "which a printer alone never shows you. Each station can be filtered to its own items using your product "
          "printer map, so the bar screen shows drinks and the grill screen shows grills. It rings when something new "
          "arrives, keeps the display awake, and goes full screen on a tap.'),\n")
if "'Kitchen display'" not in s:
    assert s.count(anchor) == 1, ('gen.py anchor', s.count(anchor))
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, screen + anchor))
    print('gen.py: kitchen display added to Orders and guests')

# ---------------------------------------------------------------- how-to, note, FAQ
s = io.open('gen3.py', encoding='utf-8').read()

anchor = " 'Reservations': ['Bookings arrive as Pending;"
how = (" 'Kitchen display': ['Open <b>Kitchen display</b> from Operations on the screen above the pass, then press the "
       "full-screen button; it keeps itself awake.',"
       " 'Press <b>Start cooking</b> when the food goes on and <b>Ready</b> when it is plated — the ticket leaves the board.',"
       " 'Tap a line to strike it through as you finish it; every screen in the kitchen sees the same ticks.',"
       " 'Use the station buttons along the top so a screen shows only its own items, from your product printer map.'],\n")
if "'Kitchen display':" not in s.split('NOTES')[0]:
    assert s.count(anchor) == 1, ('HOW anchor', s.count(anchor))
    s = s.replace(anchor, how + anchor)

note = (" 'Kitchen display': 'Bumped the wrong ticket? It sits in the strip along the bottom for half an hour — press "
        "it to pull it back. The clock on every ticket is the shop server\\u2019s, not the tablet\\u2019s, so two screens "
        "in one kitchen never disagree about how late an order is.',\n")
i = s.index('NOTES = {')
anchor2 = " 'Reservations': "
if "'Kitchen display': 'Bumped the wrong ticket" not in s:
    j = s.index(anchor2, i)
    s = s[:j] + note + s[j:]

# the old answer predates the real screen
old_q = (" ('Can it replace a kitchen display system?', 'Yes. Live orders is the kitchen board: new orders arrive with "
         "a sound, move through Accepted, Cooking and Ready, and the guest is told at every step. Kitchen printers "
         "work alongside it through the till app.'),")
new_q = (" ('Can it replace a kitchen display system?', 'It IS one. Kitchen display is a full-screen board built for "
         "the pass: two lanes, a timer on every ticket that turns amber then red as it runs late, tap a line to strike "
         "it off, one button to start and one to bump. It shows online orders and rounds sent from the tables in the "
         "same place, and each station can be filtered to its own items. Kitchen printers still work alongside it "
         "through the till app.'),")
if 'It IS one.' not in s:
    assert s.count(old_q) == 1, ('FAQ', s.count(old_q))
    s = s.replace(old_q, new_q)

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to, note, and the FAQ answer corrected')

# ---------------------------------------------------------------- path and key actions
s = io.open('gen2.py', encoding='utf-8').read()
anchor = " 'Reservations': ("
entry = (" 'Kitchen display': ('/kds', ['Two lanes: new and cooking', 'Timers turn amber, then red',"
         " 'One screen per station', 'Recall a bumped ticket for 30 minutes']),\n")
if "'Kitchen display':" not in s:
    assert s.count(anchor) == 1, ('gen2 anchor', s.count(anchor))
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s.replace(anchor, entry + anchor))
    print('gen2.py: META entry')

# ---------------------------------------------------------------- what search engines are told
s = io.open('seo.py', encoding='utf-8').read()
old = '"Kitchen display board",'
new = '"Kitchen display screen (KDS) for the pass, with bump, per-station filtering and late-order timers",'
if 'KDS) for the pass' not in s:
    assert s.count(old) == 1, ('featureList', s.count(old))
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, new))
    print('seo.py: the feature list names the real KDS')
