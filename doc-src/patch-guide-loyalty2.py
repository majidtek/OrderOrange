# -*- coding: utf-8 -*-
"""The loyalty chapter in full: the board with its live strip (who is here, what is unpaid)
and a second screen for a guest's card. Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
lines = s.split('\n')
at = [i for i, l in enumerate(lines) if l.startswith("  (F('0227_'), 'Loyalty'")]
assert len(at) == 1, at

# ---- the board screen, retold now that it carries the live strip
lines[at[0]] = ("  (F('0227_'), 'Loyalty', 'Reward the guests who keep coming back. Set the rate — say <b>10 points per 1 OMR</b> — "
    "what a reward costs and what it is worth, a welcome gift for the first visit, and the lifetime points that make a guest "
    "<b>Silver</b> or <b>Gold</b>. From then on the points arrive by themselves: every order paid at the till, closed at a table "
    "or delivered credits the guest, once, as long as the guest is on the order rather than the walk-in. "
    "The four tiles count the members and the tier mix, what the points on their cards would cost you, what has already been "
    "given back, and thirty days of earning as a bar chart. <b>Almost there</b> lists the guests one visit from a reward and "
    "<b>Gone quiet</b> the regulars who have not been in for a month — each with a WhatsApp message ready to send. "
    "Underneath, two panels refresh themselves every ten seconds: <b>Online now</b> — who is on your store page or sitting at a "
    "table with an open tab this minute — and <b>Open invoices</b>, every bill not yet paid with the guest\\u2019s points beside "
    "it, so the till can offer a reward before the money is taken.'),")

if not any(l.startswith("  (F('0228_'), 'A guest") for l in lines):
    lines.insert(at[0] + 1,
     "  (F('0228_'), 'A guest\\u2019s card', 'Everything about one regular on a single card. The ring fills towards the next "
     "reward and turns the colour of their tier; beside it sit the visits, the lifetime points, what they have already redeemed, "
     "the last time they came and — for a guest still climbing — how many points to Silver or Gold. When the balance covers a "
     "reward the card glows and says what it is worth. Three actions follow: <b>Ring up a visit</b> turns an amount into points "
     "for a sale that was not rung up on the till, <b>Use 100 pts</b> hands the reward over (the page throws confetti, the balance "
     "drops), and <b>Adjust</b> corrects a balance with a reason that is kept. <b>Message on WhatsApp</b> opens a chat with the "
     "text already written. Below is every movement on that card — earned, reward, welcome gift, adjustment — with the amount, "
     "the note, who did it and when; on the right, the podium of your top three regulars and the rest of the leaderboard.'),")
io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('gen.py: board retold, guest card added')

s = io.open('gen2.py', encoding='utf-8').read()
if "'A guest\\u2019s card':" not in s and "'A guest" not in s:
    a = " 'Loyalty': ("
    assert s.count(a) == 1
    i = s.index(a); j = s.index('\n', i) + 1
    s = s[:i] + s[i:j] + (" 'A guest\\u2019s card': ('/loyalty', ['Ring up a visit by hand', 'Hand over a reward in one press',"
                          " 'Adjust with a reason that is kept', 'Every movement on the card']),\n") + s[j:]
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s); print('gen2.py: META')

s = io.open('gen3.py', encoding='utf-8').read()
if "'A guest\\u2019s card': [" not in s:
    how_anchor = " 'Instagram': ['Open Instagram under Operations"
    assert s.count(how_anchor) == 1
    s = s.replace(how_anchor,
        " 'A guest\\u2019s card': ['Find the guest by name or phone, or tap them in Almost there, Online now, Open invoices or the leaderboard.',"
        " 'Ring up a visit: type what they spent and press the points button - the balance rolls up.',"
        " 'Hand over a reward: press Use ... pts. The points leave the card and the movement is logged.',"
        " 'Correct a mistake with Adjust - a reason is required and stays on the card for good.'],\n" + how_anchor)
    note_anchor = " 'Instagram': 'Square pictures look best."
    assert s.count(note_anchor) == 1
    s = s.replace(note_anchor,
        " 'A guest\\u2019s card': 'Nothing here is silent: every point added, given back or corrected is written down with the person who did it, so a balance can always be explained to the guest in front of you.',\n" + note_anchor)
    # the board note gains the live strip's rules
    old = " 'Loyalty': 'An order can never earn twice, and a reward that fails can never fail the sale"
    assert s.count(old) == 1
    s = s.replace(old, " 'Loyalty': 'Online now counts your store page on orderorange.com and the tables with an open tab; a tab left open for more than twelve hours is treated as forgotten, not as a guest in the room. An order can never earn twice, and a reward that fails can never fail the sale")
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s); print('gen3.py: how-to and notes')
