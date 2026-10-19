# -*- coding: utf-8 -*-
"""Loyalty joins the guide after the WhatsApp carousel; the tree menu becomes truly sticky
on desktop (the drawer rule was overriding it). Idempotent. Run, then gen3 -> host -> seo."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
lines = s.split('\n')
if not any(l.startswith("  (F('0227_'), 'Loyalty'") for l in lines):
    at = [i for i, l in enumerate(lines) if l.startswith("  (F('0224_'), 'Today")]
    assert len(at) == 1, at
    lines.insert(at[0] + 1,
     "  (F('0227_'), 'Loyalty', 'Reward the guests who keep coming back. Set the rate — say <b>10 points per 1 OMR</b> — what a reward costs and what it is worth, a welcome gift for the first visit, and the lifetime points that make a guest <b>Silver</b> or <b>Gold</b>. From then on points arrive on their own: every order paid at the till, closed at a table or delivered credits the guest, once, as long as the guest is on the order. Find a guest by name or phone to see their card — the ring fills towards the next reward — ring up a visit by hand, hand out a reward with one press, or correct a balance with a reason. The tiles say how many members you have, what the points on their cards are worth to you, and what has been redeemed; <b>Almost there</b> lists guests one visit from a reward and <b>Gone quiet</b> the regulars who have not been in for a month, each with a WhatsApp message ready to send.'),")
    io.open('gen.py', 'w', encoding='utf-8', newline='').write('\n'.join(lines)); print('gen.py: loyalty screen')

s = io.open('gen2.py', encoding='utf-8').read()
if "'Loyalty':" not in s:
    a = " 'Instagram': ("
    assert s.count(a) == 1
    s = s.replace(a, " 'Loyalty': ('/loyalty', ['Points per OMR, reward, welcome gift', 'Bronze, Silver, Gold by lifetime points', 'Points arrive with every paid order', 'Nudge guests on WhatsApp']),\n" + a)
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s); print('gen2.py: META')

s = io.open('gen3.py', encoding='utf-8').read()
if "'Loyalty': [" not in s:
    how_anchor = " 'Instagram': ['Open Instagram under Operations"
    assert s.count(how_anchor) == 1
    s = s.replace(how_anchor,
        " 'Loyalty': ['Open Loyalty under Operations, switch the programme On, set the rate and the reward, and press Save.',"
        " 'On the POS, pick the guest from the customer book instead of the walk-in - that is how the points know whose they are.',"
        " 'Type a name or phone under Find a guest, then Ring up a visit, Use the reward, or Adjust with a reason.',"
        " 'Use the WhatsApp button on a guest, or on the Almost there and Gone quiet strips, to send a ready-made message.'],\n" + how_anchor)
    note_anchor = " 'Instagram': 'Square pictures look best."
    assert s.count(note_anchor) == 1
    s = s.replace(note_anchor,
        " 'Loyalty': 'An order can never earn twice, and a reward that fails can never fail the sale - the points ride on the order, they never hold it up. Points on cards are shown with what they would cost you, so the liability is never a surprise.',\n" + note_anchor)
    faq_anchor = " ('Can I run several branches from one warehouse?',"
    assert s.count(faq_anchor) == 1
    s = s.replace(faq_anchor,
        " ('Is there a loyalty programme?', 'Yes. Set points per OMR, what a reward costs and what it is worth, a welcome gift and Silver and Gold tiers. Points are added automatically on every paid order; staff can ring up a visit, hand out a reward or correct a balance, and the page shows who is one visit from a reward and which regulars have gone quiet, with a WhatsApp message ready for each.'),\n" + faq_anchor)
# the tree menu: the drawer rule beat the sticky one on desktop
old = '  .side.drawer{position:static;width:auto;transform:none;'
if old in s:
    s = s.replace(old, '  .side.drawer{position:sticky;top:84px;width:auto;transform:none;')
    print('gen3.py: tree menu sticky on desktop')
io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: how-to, note, FAQ')

s = io.open('seo.py', encoding='utf-8').read()
old = '"WhatsApp dish carousel with Order now buttons",'
if 'Loyalty programme' not in s:
    assert s.count(old) == 1
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(s.replace(old, old + ' "Loyalty programme with points, rewards, tiers and WhatsApp nudges",'))
    print('seo.py: feature list')
