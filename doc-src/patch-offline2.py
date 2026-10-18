"""Second half of the offline patch (gen3 HOW/NOTES/FAQ + seo), with exact anchors. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
s = io.open('gen3.py', encoding='utf-8').read()
if 'Working offline' not in s:
    a = " 'Invoice designer': ["
    assert s.count(a) == 1, ('HOW anchor', s.count(a))
    s = s.replace(a, " 'Working offline': ['Open Support from the everything-menu and press New ticket.', 'Category Technical problem, subject Offline, and tell us which PC or tablet runs the till.', 'Open the ticket; the team switches the offline edition on for that shop and writes back in the same ticket.'],\n" + a)
    a2 = " 'Invoice designer': '"
    assert s.count(a2) == 1, ('NOTES anchor', s.count(a2))
    s = s.replace(a2, " 'Working offline': 'While offline the till keeps its own numbering and cash totals; nothing is lost and nothing is double-counted when it syncs.',\n" + a2)
    a3 = " ('Which browser and devices are supported?',"
    assert s.count(a3) == 1
    s = s.replace(a3, " ('Does it work offline?', 'Yes. An offline edition of the POS keeps selling and printing without internet and syncs when the connection is back. Ask for it through Support → New ticket with the subject Offline.'),\n" + a3)
    io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
q = io.open('seo.py', encoding='utf-8').read()
if 'Offline POS edition' not in q:
    q = q.replace('"featureList": ["AI ordering assistant', '"featureList": ["Offline POS edition on request", "AI ordering assistant')
    q = q.replace(' 38 ', ' 39 ').replace('٣٨', '٣٩').replace('۳۸', '۳۹').replace('38画面', '39画面').replace('38个', '39个')
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
g = io.open('gen.py', encoding='utf-8').read(); g2 = io.open('gen2.py', encoding='utf-8').read()
print('gen.py has screen:', 'Working offline' in g, '| gen2 has meta:', 'Working offline' in g2, '| gen3 ok, seo ok')
