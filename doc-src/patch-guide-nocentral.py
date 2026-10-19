# -*- coding: utf-8 -*-
"""The central-warehouse screen leaves the guide (Majed: the branch frame is an empty map).
Its substance moves into one sentence on the Warehouses map screen; the FAQ answer stays."""
import io, os, shutil
os.chdir(os.path.dirname(os.path.abspath(__file__)))

s = io.open('gen.py', encoding='utf-8').read()
lines = s.split('\n')
keep = [l for l in lines if not l.startswith("  (F('0222_'), 'Central warehouse for branches'")]
if len(keep) != len(lines):
    s = '\n'.join(keep)
    old = "every move is logged with who did it and why.'),"
    new = ("every move is logged with who did it and why. Run more than one business? Mark one place as the <b>central warehouse</b> "
           "and every branch you own can draw from it — the stock changes hands into the branch\\u2019s own materials, with a line in both logs.'),")
    assert s.count(old) == 1
    s = s.replace(old, new)
    io.open('gen.py', 'w', encoding='utf-8', newline='').write(s)
    print('gen.py: screen removed, central folded into the map screen')

s = io.open('gen2.py', encoding='utf-8').read()
s2 = '\n'.join(l for l in s.split('\n') if not l.startswith(" 'Central warehouse for branches': ("))
if s2 != s:
    io.open('gen2.py', 'w', encoding='utf-8', newline='').write(s2); print('gen2.py: META removed')

s = io.open('gen3.py', encoding='utf-8').read()
s2 = '\n'.join(l for l in s.split('\n') if not l.startswith(" 'Central warehouse for branches': "))
if s2 != s:
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s2); print('gen3.py: how-to and note removed')

if os.path.exists('frame_0222_0222.0s.png'):
    shutil.move('frame_0222_0222.0s.png', 'frame-backup/frame_0222_0222.0s.png'); print('frame moved to backup')
