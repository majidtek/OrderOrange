"""One-off, idempotent: a 'Developer API' screen in the guide's Setup chapter, an API FAQ entry, feature-list wording."""
import io, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
shot = Image.open('../ad/preview/api-desktop.png').convert('RGB')
shot.crop((0, 0, shot.width, min(shot.height, 980))).save('frame_0207_0207.0s.png')

def patch(p, pairs):
    s = io.open(p, encoding='utf-8').read()
    for a, b, marker in pairs:
        if marker in s: continue
        assert s.count(a) == 1, (p, a[:60], s.count(a)); s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

patch('gen.py', [("  (F('0109_'), 'Invoice designer',",
 "  (F('0207_'), 'Developer API', 'Everything the partner app does, your own code can do too. A REST API over HTTPS with the same login: read and write the menu, orders, tables, reservations, customers, bills and reports of your store, in JSON, with verified examples in curl, JavaScript, C# and Python. It is how a CRM, an accounting package, a kitchen screen or a chain’s head-office system connects to OrderOrange. Free with every partner account.'),\n  (F('0109_'), 'Invoice designer',", "'Developer API'")])
patch('gen2.py', [(" 'Invoice designer':", " 'Developer API': ('https://partner.orderorange.com/api.html', ['Same login, Bearer token', '100+ endpoints, JSON over HTTPS', 'CRM, accounting, ERP, kitchen screens']),\n 'Invoice designer':", "'Developer API'")])
patch('gen3.py', [
 (" 'Invoice designer': [", " 'Developer API': ['Create a team member for the integration and give it a custom role with only the permissions it needs.', 'Sign that member in with POST api/auth/login from your server and keep the token (12 hours, or 30 days with remember).', 'Read with GET and write with POST or PUT; the reference at partner.orderorange.com/api.html lists every endpoint with real examples.'],\n 'Invoice designer': [", "'Developer API': ["),
 (" 'Invoice designer': '", " 'Developer API': 'Accounting reads the daily invoices, a CRM reads and writes customers, an ERP polls the live board; there are no webhooks yet, so poll politely and use the invoice number as your key.',\n 'Invoice designer': '", "'Developer API': '"),
 (" ('Does it work offline?',", " ('Is there an API?', 'Yes, and it is free. The same login gives a Bearer token for a REST API that covers menu, orders, tables, reservations, customers, bills and reports, so a CRM, an accounting package or your own app can connect. The reference with live examples is at partner.orderorange.com/api.html.'),\n ('Does it work offline?',", "'Is there an API?'"),
])
q = io.open('seo.py', encoding='utf-8').read()
if 'REST API for CRM' not in q:
    q = q.replace('"featureList": ["Offline POS edition on request",', '"featureList": ["REST API for CRM, accounting and ERP integrations", "Offline POS edition on request",')
    q = q.replace(' 39 ', ' 40 ').replace('٣٩', '٤٠').replace('۳۹', '۴۰').replace('39画面', '40画面').replace('39个', '40个')
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('api screen patched')
