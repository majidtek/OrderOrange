"""One-off: the 'Working offline' screen (offline edition on request through a support ticket)."""
import io, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
Image.open(r'C:/Users/Administrator/.claude/uploads/3f109875-61f7-48dc-95ed-ef557c0e8fdc/89b694bc-image.jpg').convert('RGB').crop((0, 222, 923, 1900)).save('frame_0206_0206.0s.png')

def patch(p, pairs):
    s = io.open(p, encoding='utf-8').read()
    for a, b in pairs:
        assert s.count(a) == 1, (p, a[:60], s.count(a)); s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

patch('gen.py', [("  (F('0109_'), 'Invoice designer',",
 "  (F('0206_'), 'Working offline', 'OrderOrange can work offline. For shops where the internet drops or is not there at all, an offline edition of the POS keeps selling, printing kitchen tickets and receipts, and holding the day\u2019s orders on the shop PC; everything is sent to the cloud the moment the connection returns, so reports, the customer app and the other branches catch up automatically. It is switched on per shop: open Support, press New ticket, choose Technical problem and write \u201cOffline\u201d as the subject.'),\n  (F('0109_'), 'Invoice designer',")])
patch('gen2.py', [(" 'Invoice designer':", " 'Working offline': ('/support', ['Sells and prints without internet', 'Syncs when the connection returns', 'Request it with a support ticket']),\n 'Invoice designer':")])
patch('gen3.py', [
 (" 'Invoice designer':", " 'Working offline': ['Open Support from the everything-menu and press New ticket.', 'Category Technical problem, subject Offline, and tell us which PC or tablet runs the till.', 'Open the ticket; the team switches the offline edition on for that shop and writes back in the same ticket.'],\n 'Invoice designer':"),
])
# FAQ + notes live in gen3 too
s = io.open('gen3.py', encoding='utf-8').read()
a = " ('Which browser and devices are supported?',"
assert s.count(a) == 1
s = s.replace(a, " ('Does it work offline?', 'Yes. An offline edition of the POS keeps selling and printing without internet and syncs when the connection is back. Ask for it through Support \u2192 New ticket with the subject Offline.'),\n" + a)
a2 = " 'Invoice designer': '"
if s.count(a2) == 1:
    s = s.replace(a2, " 'Working offline': 'While offline the till keeps its own numbering and cash totals; nothing is lost and nothing is double-counted when it syncs.',\n" + a2)
io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
q = io.open('seo.py', encoding='utf-8').read()
if 'Offline POS edition' not in q:
    q = q.replace('"featureList": ["AI ordering assistant', '"featureList": ["Offline POS edition on request", "AI ordering assistant')
q = q.replace(' 38 ', ' 39 ').replace('٣٨', '٣٩').replace('۳۸', '۳۹').replace('38画面', '39画面').replace('38个', '39个')
io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('offline screen patched')
