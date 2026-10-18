import base64, io, glob, html, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))

src = io.open('gen.py', encoding='utf-8').read()
ns = {'__file__': os.path.abspath('gen.py')}
exec(src[:src.index("def sec(")], ns)          # img, F, parts (copy + screens)
img, parts = ns['img'], ns['parts']
src2 = io.open('gen2.py', encoding='utf-8').read()
ns2 = {}
exec(src2[src2.index('META = {'):src2.rindex('def sec(')], ns2)   # META: paths + key actions
META = ns2['META']

# one tint per chapter, warm to cool, all sitting on the same light ground
TINT = {'around': '#FF5A00', 'sell': '#E8420E', 'orders': '#D9480F', 'menu': '#C2410C',
        'money': '#0F766E', 'team': '#1D4ED8', 'reports': '#7C3AED', 'setup': '#475569'}
SUB = {'around': 'Dashboard and the everything-menu', 'sell': 'POS, tablet, tables, assistant',
       'orders': 'Live board, delivery, bookings, chat', 'menu': 'Products, recipes, discounts',
       'money': 'Bills, purchases, payroll, contracts', 'team': 'Logins, roles, presence, attendance',
       'reports': 'Daily and product reports', 'setup': 'Settings, receipts, printers'}

HOW = {
 'Dashboard': ['Check the four tiles: waiting, being prepared, orders today, revenue today.', 'Open "Needs your attention" and accept or reject anything new.', 'Use Quick actions instead of the menu for the pages you use most.'],
 'POS': ['Choose In person, Take-away or Delivery, then a table or a walk-in customer.', 'Tap dishes; change quantity or add a note on the ticket line.', 'Save as open to keep the table running, or Close & pay to settle in cash or card.', 'The kitchen ticket and the receipt print on their own after you save.'],
 'POS on a tablet': ['Pick a category at the top and tap dishes.', 'Tap the floating total to review the ticket.', 'Save or pay exactly as on the desktop POS.'],
 'Tables': ['Green is free, orange is seated, a QR badge means the guest ordered from their phone.', 'Tap a table to open its running order and add rounds.', 'Bill requested shows when a guest asked to pay from the table chat.'],
 'Live orders': ['New orders arrive on the left with a sound.', 'Accept sends the ticket to the kitchen; Reject asks for a reason the customer sees.', 'Move the order to Cooking and Ready as it progresses; the customer is notified each time.'],
 'Reservations': ['Bookings arrive as Pending; Confirm to promise the table.', 'Press Seat now when the party arrives — the table turns occupied on the floor plan.', 'Switch on History to see past and cancelled bookings.'],
 'Answering a table': ['Open the table thread; the guest\'s messages are on the left.', 'Reply by text, photo or voice; press the microphone to record.', 'Mark the bill as brought or the order as noted so the guest knows you saw it.'],
 'Products': ['Add a category first, then products inside it.', 'Drag the sort order or set the number in the product to change the menu order.', 'Sold out and Available counters update as you toggle products.'],
 'Editing a product': ['Add up to four photos and tick the main one.', 'Arabic name is required; add English, Persian or any other language with "Add another language".', 'Tick Available, Popular or In-store only.', 'Limit days and hours if the dish is only served at certain times.'],
 'Recipes': ['Open a dish and add each material with the amount one portion uses.', 'The card shows portion cost and margin against the selling price.', 'Sales then reduce stock automatically and feed the Consumption report.'],
 'Discount codes': ['Press New code, give it a name and a discount (percent or amount).', 'Pick the rule: first order, min items, min spend, orders this month, or days and hours.', 'Set a cap, an end date and a usage limit; switch it on.'],
 'Bills and expenses': ['Press New bill, choose the category, vendor, amount and due date.', 'Tick Recurring for rent, gas and other monthly bills.', 'Mark paid from the list; overdue bills are flagged with days late.'],
 'Purchases': ['Enter the supplier and invoice number, then pick materials with quantity and cost.', 'Choose Cash, Card, or Later / installments.', 'Receive stock updates the shelf quantities and the Payments page.'],
 'Payroll': ['Choose the month with the arrows.', 'Check hours (from Attendance) and press Pay for each person, or Pay everyone.', 'Bonus and deduction can be added per person before paying.'],
 'Users and roles': ['Press Add user, enter name, login, phone and a password.', 'Choose a role; the person only sees the pages that role opens.', 'Switch a login off when someone leaves; their history stays.'],
 'Attendance': ['Staff press I\'m here on arrival; the position is recorded.', 'Press again when leaving; hours go to payroll.', 'Request leave (hours or days); the owner approves or rejects from the report.'],
 'Daily report': ['Pick the day with the arrows or the calendar.', 'Open any invoice row to see its lines.', 'Press Excel or Print A4 to export.'],
 'Product report': ['Pick Today, 7 days, 30 days, Year or a date range.', 'Read the top five and the full table with share per product.', 'Export the same view to Excel.'],
 'Store settings': ['Fill profile, phone and address; pin the location on the map.', 'Set opening hours per day and delivery terms.', 'Choose the kitchen language for tickets and the short web address of your page.'],
 'Invoice designer': ['Pick a template, upload your logo and choose its size.', 'Write tagline, thank-you and promotion lines.', 'Watch the live preview; press Save when it looks right.'],
}

NOTES = {
 'Dashboard': 'The revenue chart counts paid sales by the day they were placed, so a sale you type in later for yesterday lands on yesterday.',
 'The menu': 'A team member only sees the tiles their role opens. If a page is missing for someone, check Roles and permissions.',
 'POS': 'Nothing is sent to the kitchen until you press Save as open or Close & pay. Edits to an open table print one "invoice edited" ticket showing what changed (1 → 3, 5 → 0, new lines).',
 'POS on a tablet': 'Logins with the Waiter role open this page automatically after sign-in.',
 'Tables': 'Table QR codes are printed from Table QR codes; each code carries the store and table, so a scan opens the right menu and chat.',
 'Order assistant and product printers': 'Printing itself is done by the OrderOrange till app on the shop PC (download at api.orderorange.com/api/till/download). It updates itself and can start with Windows.',
 'Live orders': 'Order sounds and browser notifications need the bell at the top right to be allowed once in the browser.',
 'Take-out and dine-in board': 'Share live location sends the customer a link that follows the rider on a map until hand-over.',
 'Delivery hand-over': 'Orders placed at the counter with a customer address become delivery orders; orders with a table or marked counter sale never appear here.',
 'Reservations': 'A booking cannot be made for a time that has already passed. Online booking can be switched off in Store settings.',
 'Table chat': 'Guests do not need an account; the table code they scanned identifies them.',
 'Answering a table': 'A guest can also ask for the bill or order more from the same chat; both show up on the Tables page.',
 'Products': 'New products created by team members wait for OrderOrange approval before customers see them; the owner\'s edits to approved products go live at once.',
 'Editing a product': 'Give every product a name in all 14 languages, otherwise customers reading another language see the Arabic name.',
 'Recipes': 'Production capacity (in Reports) uses these recipes and your stock to say how many portions of each dish you can still make.',
 'Discount codes': 'Codes are checked at checkout on the website, the table QR menu and the POS; a code that fails tells the customer why.',
 'Bills and expenses': 'Bill report (in Reports) groups the same bills by category, month and vendor.',
 'Purchases': 'Save as template keeps the supplier and lines; the next delivery is one tap plus quantities.',
 'Payments to suppliers': 'An installment plan splits one invoice into dated steps; each step is paid on its own here.',
 'Payroll': 'Hours come from Attendance for staff linked to a login; unlinked staff show a dash.',
 'Contracts': 'A signed contract gets a public page with a verification code the customer can open.',
 'Users and roles': 'Passwords are set here; a member can also sign in with a one-time email code if their login is an email address.',
 'Roles and permissions': 'Permissions are enforced on the server, so a page hidden by a role cannot be opened by typing its address.',
 'Team activity': '"Online" means active in the last three minutes; the list refreshes every ten seconds.',
 'Team chat': 'Messages stay per store; switching to another business shows that business\'s team.',
 'Attendance': 'The distance between the press and the store is shown to the owner, so a clock-in from home stands out.',
 'Calendar': 'Reminders go to the bell of every team member with access to the calendar.',
 'Daily report': 'Cancelled invoices stay in the list, struck through, with who cancelled them and why.',
 'Product report': 'Share is each product\'s part of revenue in the chosen period, not of quantity.',
 'Store settings': 'Kitchen language changes only the kitchen ticket. The customer receipt keeps the store\'s own language.',
 'Invoice designer': 'The same design is used by the till app on the shop PC, so the printed receipt matches the preview.',
 'Receipt preview': 'Guests scan the code at the bottom to open a verified copy of their bill.',
}

FAQ = [
 ('Can two businesses share one login?', 'Yes. An account can own or work in several stores; the chooser appears at sign-in and the top bar switches at any time.'),
 ('Which browser and devices are supported?', 'Any current Chrome, Safari, Edge or Firefox on phone, tablet or PC. The app installs as a web app from the browser menu.'),
 ('Why did nothing print?', 'Printing is done by the till app on the shop PC. Check that it is running in the tray, signed in to the right store, and that the product printers map points at a printer that exists.'),
 ('How do customers order from the table?', 'Print the table QR codes. A scan opens the menu for that table; the order lands on Live orders and the table turns occupied.'),
 ('Can I hide a product from the website but keep it at the counter?', 'Yes, tick In-store only on the product. It stays on the POS and the table QR menu.'),
 ('Where do reviews come from?', 'Customers can review an order after it is delivered; reviews appear under Reports and on your public page.'),
 ('What happens to a rejected order?', 'The customer is told immediately with the reason you typed; nothing is charged for online card payments.'),
 ('How do I change the language?', 'The globe icon in the top bar switches the whole app; each team member picks their own.'),
 ('Can a waiter see revenue?', 'Only if their role allows Reports. Presets keep money pages for owner and manager.'),
 ('Where is my data?', 'On OrderOrange servers in the cloud, backed up daily. Export any report to Excel whenever you like.'),
]

def shot(k, t, path):
    b, w, hh = img[k]
    return (f'<div class="shot"><div class="chrome"><i></i><i></i><i></i><span>partner.orderorange.com{html.escape(path)}</span></div>'
            f'<img src="data:image/jpeg;base64,{b}" width="{w}" height="{hh}" alt="{html.escape(t)}" loading="lazy"></div>')

import re as _re
def _words(t): return len(_re.sub(r'<[^>]+>', ' ', t).split())
def _sec_words(items):
    return sum(_words(d) + sum(_words(a) for a in META[t][1]) + sum(_words(x) for x in HOW.get(t, [])) + _words(NOTES.get(t, '')) for _, t, d in items)
read_min = {i: max(1, round(_sec_words(items) / 200)) for i, _, items in parts}

def crop_top(key, keep=0.56):
    src = [f for f in glob.glob('frame_*.png') if f[:-4] == key][0]
    im = Image.open(src).convert('RGB'); im = im.crop((0, 0, im.width, int(im.height * keep)))
    if im.width > 1000: im = im.resize((1000, int(im.height * 1000 / im.width)), Image.LANCZOS)
    b = io.BytesIO(); im.save(b, 'JPEG', quality=82, optimize=True)
    img[key + '_crop'] = (base64.b64encode(b.getvalue()).decode(), im.width, im.height)
    return key + '_crop'
pos_key = crop_top(ns['F']('0010_'))

def sec(n, id, title, items):
    h = (f'<section id="{id}" style="--tint:{TINT[id]}" data-title="{html.escape(title)}"><div class="band"><span class="wm">{n:02d}</span><div>'
         f'<span class="eyebrow">Chapter {n:02d} · {len(items)} screens · {read_min[id]} min read</span><h2>{html.escape(title)}</h2><p class="sub">{html.escape(SUB[id])}</p></div>'
         f'<ul class="band-list">' + ''.join(f'<li><a href="#{id}-{j+1}">{html.escape(tt)}</a></li>' for j, (_, tt, _) in enumerate(items)) + '</ul></div>')
    for j, (k, t, d) in enumerate(items):
        if t == 'POS': k = pos_key
        path, acts = META[t]
        li = ''.join(f'<li>{html.escape(a)}</li>' for a in acts)
        h += (f'<article id="{id}-{j+1}" class="{"flip" if j % 2 else ""}"><div class="txt"><span class="step">{n:02d}.{j+1}</span><h3>{html.escape(t)}</h3><p>{d}</p>'
              f'<ul class="acts">{li}</ul>'
              + (('<div class="how"><span class="how-h">How to</span><ol>' + ''.join(f'<li>{html.escape(x)}</li>' for x in HOW[t]) + '</ol></div>') if t in HOW else '')
              + (f'<div class="note"><b>Good to know</b>{html.escape(NOTES[t])}</div>' if t in NOTES else '')
              + f'<a class="go" href="https://partner.orderorange.com{html.escape(path.split()[0])}" target="_blank" rel="noopener">Open this page ↗</a></div>'
              f'{shot(k, t, path)}</article>')
    return h + '</section>'

toc = ''.join(f'<a href="#{i}" style="--tint:{TINT[i]}"><em>{n+1:02d}</em>{html.escape(t)}</a>' for n, (i, t, _) in enumerate(parts))
cards = ''.join(f'<a href="#{i}" style="--tint:{TINT[i]}"><em>{n+1:02d}</em><b>{html.escape(t)}</b><span>{html.escape(SUB[i])}</span><small>{len(items)} screens · {read_min[i]} min</small></a>'
                for n, (i, t, items) in enumerate(parts))
INTRO = [
 ('Sign in', 'Use your username and password, Google, or a one-time code sent to your email. Tick <b>Keep me signed in</b> on a shop device so the app opens without asking again for a month.'),
 ('Your role decides your home', 'Owners and managers land on the dashboard. Waiters land on the tablet POS. Every role sees only the pages it is allowed to open.'),
 ('More than one business', 'If your account has several stores, a chooser appears at sign-in and the store name in the top bar switches between them at any time.'),
 ('Fourteen languages', 'The globe icon in the top bar switches the whole app: English, Arabic, Persian, Urdu, Hindi, Turkish, German, Spanish, French, Italian, Portuguese, Russian, Japanese and Chinese. Numbers follow the language too.'),
 ('The bell', 'New orders, bookings, messages and reminders arrive in the bell at the top right, and as push notifications on your phone if you allow them once.'),
 ('Search everything', 'Ctrl+K or the search box finds pages, products, customers and orders from anywhere.'),
 ('The till app', 'Printing to kitchen and receipt printers is done by a small Windows app on the shop PC. It starts with Windows, updates itself, and shows a bell when a table orders by QR code.'),
 ('Your public page', 'Customers find you at orderorange.com/your-name. The store page, table QR menus and online booking all come from what you set up here.'),
]
intro_html = ('<section id="start" style="--tint:#FF5A00" data-title="The basics"><div class="sec-head"><span class="wm">00</span><span class="eyebrow">Before you start</span><h2>The basics</h2><p class="sub">Eight things that make every page below make sense</p></div><div class="intro">'
              + ''.join(f'<div class="ib"><em>{n+1:02d}</em><b>{html.escape(t)}</b><p>{d}</p></div>' for n, (t, d) in enumerate(INTRO)) + '</div></section>')
faq_html = ('<section id="faq" style="--tint:#475569" data-title="Questions"><div class="sec-head"><span class="wm">?</span><span class="eyebrow">Questions</span><h2>Frequently asked</h2><p class="sub">Short answers to what partners ask first</p></div><div class="faq">'
            + ''.join(f'<details{" open" if n == 0 else ""}><summary>{html.escape(q)}</summary><p>{html.escape(a)}</p></details>' for n, (q, a) in enumerate(FAQ)) + '</div></section>')
total_min = max(1, round((sum(_sec_words(items) for _, _, items in parts) + sum(_words(d) for _, d in INTRO) + sum(_words(q + a) for q, a in FAQ)) / 200))
ftmap = ''.join(f'<li><a href="#{i}">{html.escape(t)}</a></li>' for i, t, _ in parts)
body = intro_html + ''.join(sec(n + 1, i, t, items) for n, (i, t, items) in enumerate(parts)) + faq_html
def tree_item(i, n, t, items, tint):
    kids = ''.join(f'<li><a href="#{i}-{j+1}" data-q="{html.escape((t + " " + tt + " " + " ".join(META[tt][1])).lower())}">{html.escape(tt)}</a></li>' for j, (_, tt, _) in enumerate(items))
    return (f'<li class="tn" style="--tint:{tint}"><a href="#{i}" class="tn-h" data-q="{html.escape(t.lower())}"><em>{n}</em><span>{html.escape(t)}</span>'
            f'<b>{len(items)}</b><i class="tw"></i></a><ul>{kids}</ul></li>')
tree = ('<div class="tsearch"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>'
        '<input id="tq" type="search" placeholder="Search the guide…" autocomplete="off"><kbd>/</kbd></div><ul class="tree">'
        + '<li class="tn" style="--tint:#FF5A00"><a href="#start" class="tn-h" data-q="the basics sign in role languages bell search till"><em>00</em><span>The basics</span></a></li>'
        + ''.join(tree_item(i, f'{n+1:02d}', t, items, TINT[i]) for n, (i, t, items) in enumerate(parts))
        + '<li class="tn" style="--tint:#475569"><a href="#faq" class="tn-h" data-q="questions faq"><em>?</em><span>Questions</span></a></li></ul><div class="tnone">No page matches. Try another word.</div>')
toc = '<a href="#start" style="--tint:#FF5A00"><em>00</em>The basics</a>' + toc + '<a href="#faq" style="--tint:#475569"><em>?</em>Questions</a>'
count = sum(len(items) for _, _, items in parts)
LOGO = 'data:image/svg+xml;base64,' + base64.b64encode(open(r'D:\OrderOrange\OrderOrange.ClientCore\wwwroot\logo.svg', 'rb').read()).decode()
hero_main = shot(ns['F']('0000_'), 'Dashboard', '/')
hero_side = shot(pos_key, 'POS', '/pos')

css = """
:root{--bg:#FFFFFF;--bg-2:#FBF8F3;--surface:#FFFFFF;--ink:#15110D;--ink-2:#544A3F;--muted:#8E8274;--line:#EEE6DB;--panel:#FFF2E8;--accent:#FF5A00;--accent-deep:#C44300;--tint:#FF5A00;--shadow:0 30px 70px rgba(70,35,5,.12),0 2px 8px rgba(70,35,5,.06)}
*{box-sizing:border-box}
html{color-scheme:light}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.6 Manrope,system-ui,"Segoe UI",Arial,sans-serif;-webkit-font-smoothing:antialiased;overflow-x:hidden}
a{color:inherit}
.progress{position:fixed;inset:0 0 auto 0;height:3px;background:linear-gradient(90deg,#FF8A3D,#FF5A00);transform-origin:0 50%;transform:scaleX(0);z-index:9}
.top{position:sticky;top:0;z-index:6;background:rgba(255,255,255,.72);backdrop-filter:blur(14px);-webkit-backdrop-filter:blur(14px);border-bottom:1px solid var(--line)}
.top .in{max-width:1220px;margin:0 auto;padding:12px 22px;display:flex;align-items:center;gap:12px}
.logo{width:36px;height:36px;border-radius:10px;flex:none;object-fit:contain}
.ft-brand .logo{width:44px;height:44px}
.mark{width:34px;height:34px;border-radius:11px;background:linear-gradient(135deg,#FF9A4D,#FF5A00);display:grid;place-items:center;color:#fff;font:800 16px Manrope,sans-serif;flex:none;box-shadow:0 8px 18px rgba(255,90,0,.28)}
.top b{font:700 15px Manrope,sans-serif;letter-spacing:-.2px}
.top .where{display:none;align-items:center;gap:8px;margin-inline-start:14px;font:700 12px Manrope,sans-serif;color:var(--muted);padding-inline-start:14px;border-inline-start:1px solid var(--line)}
.top .where i{width:8px;height:8px;border-radius:50%;background:var(--tint,#FF5A00);box-shadow:0 0 0 4px color-mix(in srgb,var(--tint,#FF5A00) 18%,#fff)}
.top .where em{font-style:normal;font-variant-numeric:tabular-nums;color:var(--ink-2);background:var(--bg-2);border-radius:999px;padding:3px 8px}
.top .links{margin-inline-start:auto;display:none;gap:4px}
.top .links a{text-decoration:none;font:700 12.5px Manrope,sans-serif;color:var(--ink-2);padding:8px 11px;border-radius:10px}
.top .links a:hover{background:var(--panel);color:var(--accent-deep)}
.top .cta{text-decoration:none;font:800 12.5px Manrope,sans-serif;color:#fff;background:var(--ink);padding:9px 14px;border-radius:999px;white-space:nowrap}
.top .cta i{font-style:normal}.top .cta span{display:none}
.top .tsb{flex:0 0 36px;margin-inline-start:0;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);color:var(--ink-2);display:grid;place-items:center;cursor:pointer;margin-inline-end:8px}
.top .tsb:hover{border-color:var(--accent);color:var(--accent-deep)}
.top .in{padding:8px 12px;gap:8px}
.top b{font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0;flex:1 1 auto}
.top b .lg{display:none}.top b .sm{display:inline}
.top .logo{flex:0 0 32px;width:32px;height:32px}
.top .cta{flex:0 0 auto;padding:9px 13px;font-size:12.5px}
.top .links + .cta{margin-inline-start:8px}
.hero{position:relative;overflow:hidden;background:radial-gradient(1200px 520px at 85% -10%,#FFE3CF 0%,rgba(255,227,207,0) 60%),radial-gradient(700px 420px at -10% 110%,#FFF0E0 0%,rgba(255,240,224,0) 60%),var(--bg-2)}
.hero .spot{position:absolute;width:520px;height:520px;border-radius:50%;background:radial-gradient(circle,rgba(255,140,60,.28),rgba(255,140,60,0) 60%);pointer-events:none;transform:translate(-50%,-50%);left:70%;top:40%;transition:left .25s ease-out,top .25s ease-out;filter:blur(10px)}
.hero .in{position:relative;max-width:1220px;margin:0 auto;padding:64px 22px 30px;display:grid;gap:30px;align-items:center}
.eyebrow{display:inline-flex;align-items:center;gap:8px;font:800 11px/1 Manrope,sans-serif;letter-spacing:1.9px;text-transform:uppercase;color:var(--tint)}
.eyebrow::before{content:"";width:18px;height:2px;background:var(--tint);border-radius:2px}
.hero h1{font:800 clamp(38px,6.2vw,68px)/.98 Manrope,sans-serif;letter-spacing:-2.2px;margin:14px 0 0;max-width:14ch;text-wrap:balance}
.hero h1 span{background:linear-gradient(90deg,#FF5A00,#FF9A4D);-webkit-background-clip:text;background-clip:text;color:transparent}
.hero p{margin:18px 0 0;color:var(--ink-2);font-size:18px;max-width:52ch;line-height:1.65}
.hero .row{display:flex;gap:10px;flex-wrap:wrap;margin-top:24px;align-items:center}
.btn{display:inline-flex;align-items:center;gap:8px;text-decoration:none;font:800 14px Manrope,sans-serif;padding:13px 20px;border-radius:999px;transition:transform .15s,box-shadow .15s}
.btn.p{background:var(--accent);color:#fff;box-shadow:0 12px 26px rgba(255,90,0,.28)}.btn.p:hover{transform:translateY(-1px);box-shadow:0 16px 30px rgba(255,90,0,.34)}
.btn.s{background:#fff;border:1px solid var(--line);color:var(--ink)}.btn.s:hover{border-color:#FFB48A}
.stats{display:flex;gap:26px;flex-wrap:wrap;margin-top:30px}
.stats div{display:grid;gap:2px}.stats b{font:800 26px/1 Manrope,sans-serif;font-variant-numeric:tabular-nums;letter-spacing:-.8px}
.stats span{font-size:11.5px;color:var(--muted);letter-spacing:.6px;text-transform:uppercase;font-weight:700}
@keyframes rise{from{opacity:0;transform:translateY(22px)}to{opacity:1;transform:none}}
@keyframes stageIn{from{opacity:0;transform:translateX(50px) rotateY(-16deg) rotateX(4deg)}to{opacity:1;transform:rotateY(-9deg) rotateX(3deg)}}
@keyframes floaty{0%,100%{transform:rotateY(-9deg) rotateX(3deg) translateZ(60px) translateY(0)}50%{transform:rotateY(-9deg) rotateX(3deg) translateZ(60px) translateY(-10px)}}
@keyframes drift{0%,100%{background-position:85% -10%,-10% 110%}50%{background-position:80% 0%,-4% 100%}}
.hero .in > div:first-child > *{animation:rise .8s cubic-bezier(.16,1,.3,1) both}
.hero .in > div:first-child > :nth-child(2){animation-delay:.08s}.hero .in > div:first-child > :nth-child(3){animation-delay:.16s}.hero .in > div:first-child > :nth-child(4){animation-delay:.24s}.hero .in > div:first-child > :nth-child(5){animation-delay:.32s}
.hero{background-size:auto,auto,auto;animation:drift 14s ease-in-out infinite}
.stage{position:relative;perspective:1600px}
.stage .main{animation:stageIn 1s cubic-bezier(.16,1,.3,1) .2s both}
.stage .side{animation:rise .8s cubic-bezier(.16,1,.3,1) .55s both,floaty 6s ease-in-out 1.4s infinite}
.wm{transition:transform .6s ease;will-change:transform}
@keyframes shimmer{0%{background-position:0% 50%}100%{background-position:200% 50%}}
.hero h1 span{background:linear-gradient(90deg,#FF5A00,#FF9A4D,#FF5A00,#FFB070);background-size:200% 100%;-webkit-background-clip:text;background-clip:text;color:transparent;animation:shimmer 6s linear infinite}
.stage{transition:transform .15s ease-out}
.shot img{transition:transform .6s cubic-bezier(.16,1,.3,1)}
article:hover .shot img{filter:saturate(1.05)}
.tree .tn{animation:rise .6s cubic-bezier(.16,1,.3,1) both}
.tree .tn:nth-child(2){animation-delay:.05s}.tree .tn:nth-child(3){animation-delay:.1s}.tree .tn:nth-child(4){animation-delay:.15s}.tree .tn:nth-child(5){animation-delay:.2s}.tree .tn:nth-child(6){animation-delay:.25s}.tree .tn:nth-child(7){animation-delay:.3s}.tree .tn:nth-child(8){animation-delay:.35s}.tree .tn:nth-child(9){animation-delay:.4s}.tree .tn:nth-child(10){animation-delay:.45s}
.step{animation:rise .5s both}
.acts li{transition:transform .15s,background .15s}.acts li:hover{transform:translateY(-1px);background:#fff}
@keyframes pulse{0%,100%{box-shadow:0 12px 26px rgba(255,90,0,.28)}50%{box-shadow:0 12px 34px rgba(255,90,0,.48)}}
.btn.p{animation:pulse 3.2s ease-in-out infinite}
.chapters a::before{transition:width .18s}.chapters a:hover::before{width:7px}
.mark{transition:transform .3s}.top .in:hover .mark{transform:rotate(-8deg) scale(1.06)}
section:hover .wm{transform:translateY(-6px)}
.chapters a em,.toc a em{transition:transform .2s}
.chapters a:hover em{transform:translateX(3px)}
.btn.p{position:relative;overflow:hidden}
.btn.p::after{content:"";position:absolute;inset:0;background:linear-gradient(110deg,transparent 30%,rgba(255,255,255,.35) 50%,transparent 70%);transform:translateX(-120%);transition:transform .6s}
.btn.p:hover::after{transform:translateX(120%)}
.stage .main{transform:rotateY(-9deg) rotateX(3deg);transform-origin:70% 50%}
.stage .side{position:absolute;right:-6px;bottom:-24px;width:46%;transform:rotateY(-9deg) rotateX(3deg) translateZ(60px);border-radius:18px;transform-style:preserve-3d}
.stage .side .shot{border:3px solid #fff;outline:1px solid var(--line);border-radius:18px;box-shadow:0 34px 70px rgba(70,35,5,.30),0 10px 22px rgba(70,35,5,.14),0 0 0 6px rgba(255,90,0,.10)}
.stage .shot{box-shadow:0 40px 90px rgba(70,35,5,.18),0 4px 12px rgba(70,35,5,.08)}
.chapters{max-width:1220px;margin:26px auto 0;padding:0 22px;display:grid;grid-template-columns:repeat(2,1fr);gap:12px;position:relative;z-index:1}
.chapters a{--tint:var(--accent);text-decoration:none;background:var(--surface);border:1px solid var(--line);border-radius:18px;padding:16px 16px 14px;display:grid;gap:5px;position:relative;overflow:hidden;transition:transform .18s,box-shadow .18s,border-color .18s}
.chapters a::before{content:"";position:absolute;inset:0 auto 0 0;width:4px;background:var(--tint);border-radius:0 4px 4px 0}
.chapters a:hover,.chapters a:focus-visible{transform:translateY(-3px);box-shadow:0 18px 38px rgba(70,35,5,.10);border-color:color-mix(in srgb,var(--tint) 45%,#fff);outline:none}
.chapters em{font:800 11px/1 Manrope,sans-serif;color:var(--tint);font-style:normal;letter-spacing:1px}
.chapters b{font:800 15px/1.2 Manrope,sans-serif;letter-spacing:-.3px}
.chapters span{font-size:12.5px;color:var(--ink-2);line-height:1.35}
.chapters small{font-size:11px;color:var(--muted);font-weight:700;letter-spacing:.4px;text-transform:uppercase;margin-top:4px}
.wrap{max-width:1220px;margin:0 auto;padding:28px 22px 60px;display:grid;grid-template-columns:1fr;gap:0}
.side{display:none}
.tmb{flex:0 0 36px;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);display:flex;flex-direction:column;align-items:center;justify-content:center;gap:3px;cursor:pointer;margin-inline-start:auto;margin-inline-end:0;padding:0}
.tmb i{display:block;width:15px;height:2px;background:var(--ink);border-radius:2px;margin:0}
.tmb i:nth-child(2){width:11px}
.dback{position:fixed;inset:0;background:rgba(20,14,9,.45);backdrop-filter:blur(3px);z-index:18;display:none}
.dback.on{display:block}
.side.drawer{display:block;position:fixed;top:0;bottom:0;right:0;width:min(320px,86vw);background:var(--surface);z-index:19;padding:16px 14px 24px;overflow:auto;box-shadow:-24px 0 60px rgba(20,14,9,.25);transform:translateX(105%);transition:transform .28s cubic-bezier(.16,1,.3,1)}
.side.drawer.on{transform:none}
.side.drawer .dhead{display:flex;align-items:center;gap:10px;margin-bottom:10px}
.side.drawer .dhead b{font:800 14px Manrope,sans-serif;flex:1}
.side.drawer .dhead button{width:32px;height:32px;border-radius:50%;border:1px solid var(--line);background:#fff;font:800 16px/30px Manrope,sans-serif;cursor:pointer}
.tsearch{display:flex;align-items:center;gap:8px;background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:9px 12px;color:var(--muted);box-shadow:0 8px 20px rgba(70,35,5,.05);transition:border-color .15s,box-shadow .15s}
.tsearch:focus-within{border-color:#FFB48A;box-shadow:0 0 0 4px rgba(255,90,0,.10)}
.tsearch input{flex:1;min-width:0;border:0;outline:0;background:none;font:600 13.5px Manrope,sans-serif;color:var(--ink)}
.tsearch input::placeholder{color:var(--muted);font-weight:500}
.tsearch kbd{font:700 10.5px Manrope,sans-serif;color:var(--muted);border:1px solid var(--line);border-radius:6px;padding:2px 6px;background:var(--bg-2)}
.tree{list-style:none;margin:12px 0 0;padding:0;display:grid;gap:1px}
.tree ul{list-style:none;margin:2px 0 4px 17px;padding:0 0 0 14px;border-left:1.5px dashed color-mix(in srgb,var(--tint) 35%,var(--line));display:grid;gap:1px}
.tn.closed > ul{display:none}
.tn-h{display:flex;align-items:center;gap:8px;text-decoration:none;padding:7px 8px;border-radius:11px;color:var(--ink);font:700 13px Manrope,sans-serif;transition:background .15s,color .15s}
.tn-h em{font-style:normal;width:10px;height:10px;border-radius:3px;background:var(--tint);flex:none;box-shadow:0 0 0 3px color-mix(in srgb,var(--tint) 18%,#fff);font-size:0}
.tn-h span{flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.tn-h b{font:800 10.5px Manrope,sans-serif;color:var(--muted);background:var(--bg-2);border-radius:999px;padding:3px 7px}
.tn-h .tw{width:22px;height:22px;border-radius:7px;display:grid;place-items:center;flex:none;color:var(--muted);transition:background .15s}
.tn-h .tw::before{content:"";width:6px;height:6px;border-right:2px solid currentColor;border-bottom:2px solid currentColor;transform:rotate(45deg) translate(-1px,-1px);transition:transform .2s}
.tn.closed > .tn-h .tw::before{transform:rotate(-45deg) translate(-1px,1px)}
.tn-h .tw:hover{background:var(--bg-2);color:var(--ink)}
.tn-h:hover,.tn.on > .tn-h{background:color-mix(in srgb,var(--tint) 9%,#fff);color:var(--tint)}
.tree ul a{display:flex;align-items:center;gap:8px;text-decoration:none;font:600 12.5px/1.3 Manrope,sans-serif;color:var(--ink-2);padding:5px 8px;border-radius:9px;position:relative;transition:background .15s,color .15s}
.tree ul a::before{content:"";position:absolute;left:-14px;top:50%;width:10px;height:1.5px;background:color-mix(in srgb,var(--tint) 35%,var(--line))}
.tree ul a::after{content:"";width:5px;height:5px;border-radius:50%;background:var(--line);flex:none;order:-1;transition:background .15s,transform .15s}
.tree ul a:hover,.tree ul a.on{color:var(--tint);background:color-mix(in srgb,var(--tint) 8%,#fff)}
.tree ul a.on::after{background:var(--tint);transform:scale(1.4)}
.tree ul a.on{font-weight:800}
.tree .hide{display:none!important}
.tree mark{background:color-mix(in srgb,var(--tint) 22%,#fff);color:inherit;border-radius:3px;padding:0 1px}
.tnone{display:none;font:600 12.5px Manrope,sans-serif;color:var(--muted);padding:10px 12px}
.pal{position:fixed;inset:0;background:rgba(20,14,9,.5);backdrop-filter:blur(8px);z-index:30;display:none;align-items:flex-start;justify-content:center;padding:10vh 16px 16px}
.pal.on{display:flex}
.pal-box{width:min(640px,100%);background:#fff;border-radius:20px;box-shadow:0 40px 90px rgba(0,0,0,.35);overflow:hidden;animation:rise .25s cubic-bezier(.16,1,.3,1) both}
.pal-in{display:flex;align-items:center;gap:10px;padding:14px 16px;border-bottom:1px solid var(--line);color:var(--muted)}
.pal-in input{flex:1;border:0;outline:0;font:600 16px Manrope,sans-serif;color:var(--ink);background:none;min-width:0}
.pal-in kbd{font:700 10.5px Manrope,sans-serif;color:var(--muted);border:1px solid var(--line);border-radius:6px;padding:3px 7px;background:var(--bg-2)}
.pal-list{list-style:none;margin:0;padding:8px;max-height:52vh;overflow:auto}
.pal-list li a{display:flex;align-items:center;gap:12px;text-decoration:none;padding:10px 12px;border-radius:12px;color:var(--ink)}
.pal-list li a em{font-style:normal;font:800 10.5px/1 Manrope,sans-serif;color:#fff;background:var(--tint);border-radius:999px;padding:5px 7px;min-width:28px;text-align:center}
.pal-list li a b{font:700 14px Manrope,sans-serif}.pal-list li a small{color:var(--muted);font-size:12px;margin-inline-start:auto;white-space:nowrap}
.pal-list li.sel a,.pal-list li a:hover{background:color-mix(in srgb,var(--tint) 9%,#fff)}
.pal-foot{display:flex;gap:16px;padding:10px 16px;border-top:1px solid var(--line);font:600 11.5px Manrope,sans-serif;color:var(--muted)}
.tsearch{cursor:text}
.cbtn{position:fixed;right:18px;bottom:18px;z-index:15;display:inline-flex;align-items:center;gap:8px;border:0;cursor:pointer;background:var(--ink);color:#fff;font:800 13px Manrope,sans-serif;padding:12px 16px;border-radius:999px;box-shadow:0 14px 34px rgba(20,14,9,.28);transition:transform .15s,background .15s}
.cbtn:hover{transform:translateY(-2px);background:var(--accent)}
.csheet{position:fixed;inset:0;background:rgba(20,14,9,.5);backdrop-filter:blur(6px);z-index:25;display:none;align-items:flex-end;justify-content:center;padding:0}
.csheet.on{display:flex}
.cform{width:min(560px,100%);background:#fff;border-radius:24px 24px 0 0;padding:20px 20px 24px;display:grid;gap:12px;box-shadow:0 -20px 60px rgba(0,0,0,.25);animation:sheetUp .3s cubic-bezier(.16,1,.3,1) both;max-height:92vh;overflow:auto}
@keyframes sheetUp{from{transform:translateY(40px);opacity:0}to{transform:none;opacity:1}}
.chead{display:flex;align-items:flex-start;gap:12px}.chead h3{margin:6px 0 4px;font:800 20px Manrope,sans-serif;letter-spacing:-.4px}.chead p{margin:0;color:var(--muted);font-size:13.5px}
.cx{margin-inline-start:auto;width:34px;height:34px;border-radius:50%;border:1px solid var(--line);background:#fff;font:800 18px/32px Manrope,sans-serif;cursor:pointer;flex:none}
.cform label{display:grid;gap:5px;font:800 11px Manrope,sans-serif;letter-spacing:1.2px;text-transform:uppercase;color:var(--muted)}
.cform input,.cform textarea{font:600 15px Manrope,sans-serif;color:var(--ink);border:1.5px solid var(--line);border-radius:12px;padding:11px 12px;outline:0;background:var(--bg-2);transition:border-color .15s,box-shadow .15s;width:100%;box-sizing:border-box;resize:vertical}
.cform input:focus,.cform textarea:focus{border-color:var(--accent);box-shadow:0 0 0 4px rgba(255,90,0,.12);background:#fff}
.cform input.bad,.cform textarea.bad{border-color:#D93025}
.cfoot{display:flex;align-items:center;gap:12px;flex-wrap:wrap}.cfoot .btn{margin-inline-start:auto}
.cmsg{font:700 13px Manrope,sans-serif;color:var(--muted);min-height:1em}.cmsg.ok{color:#0F766E}.cmsg.err{color:#D93025}
.cnote{margin:0;font-size:12px;color:var(--muted)}.cnote a{color:var(--accent-deep);font-weight:700}
.cform.sent label,.cform.sent .cfoot .btn{display:none}
.cform .thanks{display:none;text-align:center;padding:26px 10px 10px;gap:8px}.cform.sent .thanks{display:grid}
.thanks b{font:800 22px Manrope,sans-serif}.thanks p{margin:0;color:var(--muted)}.thanks .btn{margin:10px auto 0}
@media (min-width:700px){.csheet{align-items:center;padding:20px}.cform{border-radius:24px}}
.tnone.show{display:block}
.toc{display:none}
.toc::-webkit-scrollbar{display:none}
.toc a{flex:none;display:inline-flex;align-items:center;gap:8px;text-decoration:none;font:700 12.5px/1 Manrope,sans-serif;color:var(--ink-2);border:1px solid var(--line);background:var(--surface);border-radius:999px;padding:9px 14px 9px 9px;transition:border-color .15s,color .15s,background .15s}
.toc a em{font-style:normal;font:800 10.5px/1 Manrope,sans-serif;color:#fff;background:var(--tint);border-radius:999px;padding:5px 7px}
.toc a:hover,.toc a.on,.toc a:focus-visible{border-color:var(--tint);color:var(--tint);outline:none}
section{scroll-margin-top:120px}
.sec-head{position:relative;display:grid;gap:6px;margin:40px 0 10px;padding-top:18px}
.band{position:relative;overflow:hidden;margin:44px 0 6px;padding:28px 26px 22px;border-radius:26px;color:#fff;background:linear-gradient(135deg,color-mix(in srgb,var(--tint) 88%,#000) 0%,var(--tint) 55%,color-mix(in srgb,var(--tint) 70%,#fff) 100%);display:grid;gap:16px}
.band::after{content:"";position:absolute;width:380px;height:380px;right:-120px;top:-160px;border-radius:50%;background:rgba(255,255,255,.18);filter:blur(40px)}
.band .eyebrow{color:rgba(255,255,255,.85)}.band .eyebrow::before{background:rgba(255,255,255,.85)}
.band h2{color:#fff;margin-top:6px}.band .sub{color:rgba(255,255,255,.82)}
.band .wm{position:absolute;right:18px;top:-8px;font:800 150px/1 Manrope,sans-serif;letter-spacing:-8px;color:#fff;opacity:.12;pointer-events:none}
.band-list{list-style:none;margin:0;padding:0;display:flex;flex-wrap:wrap;gap:6px;position:relative}
.band-list a{display:inline-block;text-decoration:none;color:#fff;font:700 12px Manrope,sans-serif;background:rgba(255,255,255,.16);border:1px solid rgba(255,255,255,.28);border-radius:999px;padding:6px 11px;backdrop-filter:blur(4px);transition:background .15s,transform .15s}
.band-list a:hover{background:rgba(255,255,255,.3);transform:translateY(-1px)}
.sec-head .wm{position:absolute;right:0;top:-18px;font:800 120px/1 Manrope,sans-serif;letter-spacing:-6px;color:var(--tint);opacity:.07;pointer-events:none;user-select:none}
h2{font:800 clamp(28px,3.8vw,40px)/1.02 Manrope,sans-serif;letter-spacing:-1.3px;margin:2px 0 0}
.sub{margin:0;color:var(--muted);font-size:15px}
article{display:grid;gap:18px;background:var(--surface);border:1px solid var(--line);border-radius:26px;padding:22px;margin-top:20px;position:relative;transition:box-shadow .2s,border-color .2s}
article:hover{box-shadow:0 22px 50px rgba(70,35,5,.08);border-color:color-mix(in srgb,var(--tint) 35%,#fff)}
.step{display:inline-block;font:800 11px/1 Manrope,sans-serif;letter-spacing:1px;color:var(--tint);background:color-mix(in srgb,var(--tint) 10%,#fff);border-radius:999px;padding:6px 9px;margin-bottom:10px}
h3{font:800 21px/1.15 Manrope,sans-serif;letter-spacing:-.5px;margin:0 0 8px;text-wrap:balance}
article p{margin:0;color:var(--ink-2);max-width:60ch;font-size:15.5px}
article p b{font-weight:800;color:var(--ink)}
.acts{list-style:none;margin:14px 0 0;padding:0;display:flex;flex-wrap:wrap;gap:6px}
.acts li{font-size:12px;font-weight:700;color:var(--ink-2);background:var(--bg-2);border:1px solid var(--line);border-radius:999px;padding:6px 11px}
.how{margin-top:16px;border-top:1px dashed var(--line);padding-top:12px}
.how-h{display:block;font:800 10.5px/1 Manrope,sans-serif;letter-spacing:1.6px;text-transform:uppercase;color:var(--tint);margin-bottom:8px}
.how ol{margin:0;padding:0;list-style:none;counter-reset:st;display:grid;gap:7px}
.how li{position:relative;padding-left:28px;font-size:14px;color:var(--ink-2);line-height:1.45}
.how li::before{counter-increment:st;content:counter(st);position:absolute;left:0;top:1px;width:20px;height:20px;border-radius:50%;background:color-mix(in srgb,var(--tint) 12%,#fff);color:var(--tint);font:800 11px/20px Manrope,sans-serif;text-align:center}
.note{margin-top:14px;background:color-mix(in srgb,var(--tint) 7%,#fff);border:1px solid color-mix(in srgb,var(--tint) 22%,#fff);border-radius:14px;padding:11px 13px;font-size:13.5px;color:var(--ink-2);line-height:1.5}
.note b{display:block;font:800 10.5px/1 Manrope,sans-serif;letter-spacing:1.4px;text-transform:uppercase;color:var(--tint);margin-bottom:5px}
.intro{display:grid;grid-template-columns:1fr;gap:12px;margin-top:18px}
.ib{background:var(--surface);border:1px solid var(--line);border-radius:20px;padding:18px;display:grid;gap:6px;position:relative}
.ib em{font:800 11px/1 Manrope,sans-serif;color:var(--tint);font-style:normal;letter-spacing:1px}
.ib b{font:800 16px/1.2 Manrope,sans-serif;letter-spacing:-.3px}
.ib p{margin:0;font-size:14.5px;color:var(--ink-2)}
.ib p b{display:inline;font-size:inherit;color:var(--ink)}
.faq{display:grid;gap:8px;margin-top:18px}
.faq details{background:var(--surface);border:1px solid var(--line);border-radius:16px;padding:0 18px}
.faq summary{cursor:pointer;list-style:none;font:800 15px/1.3 Manrope,sans-serif;padding:15px 30px 15px 0;position:relative}
.faq summary::-webkit-details-marker{display:none}
.faq summary::after{content:"+";position:absolute;right:0;top:12px;width:24px;height:24px;border-radius:50%;background:var(--bg-2);border:1px solid var(--line);text-align:center;font:800 15px/22px Manrope,sans-serif;color:var(--tint)}
.faq details[open] summary::after{content:"–"}
.faq p{margin:0;padding:0 0 16px;color:var(--ink-2);font-size:14.5px;max-width:70ch}
.shot img{cursor:zoom-in}
.lb .cap small{display:block;font-weight:600;opacity:.7;margin-top:2px}
.lb{position:fixed;inset:0;background:rgba(20,14,9,.92);backdrop-filter:blur(6px);z-index:20;display:none}
.lb.on{display:block}
.lb .stage2{position:absolute;inset:0;overflow:auto;touch-action:pan-x pan-y;cursor:grab;-webkit-overflow-scrolling:touch;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.35) transparent}
.lb .stage2.drag{cursor:grabbing}
.lb .box{min-width:100%;min-height:100%;width:max-content;height:max-content;display:flex;padding:70px 16px 80px;box-sizing:border-box}
.lb img{display:block;margin:auto;border-radius:10px;box-shadow:0 30px 80px rgba(0,0,0,.5);background:#fff;max-width:none;flex:none;-webkit-user-drag:none;user-select:none}
.lb .x{position:fixed;top:14px;right:14px;width:40px;height:40px;border-radius:50%;background:#fff;color:var(--ink);border:0;font:800 18px/40px Manrope,sans-serif;cursor:pointer;z-index:2}
.lb .ctl{position:fixed;left:50%;bottom:16px;transform:translateX(-50%);display:flex;gap:6px;background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.2);border-radius:999px;padding:6px;z-index:2;backdrop-filter:blur(8px)}
.lb .ctl button{width:38px;height:38px;border-radius:50%;border:0;background:#fff;color:var(--ink);font:800 16px Manrope,sans-serif;cursor:pointer}
.lb .ctl button.w{width:auto;padding:0 14px;font-size:12.5px}
.lb .cap{position:fixed;left:0;right:0;top:18px;text-align:center;color:#fff;font:700 13px Manrope,sans-serif;opacity:.85;pointer-events:none;padding:0 70px;z-index:2}
.go{display:inline-block;margin-top:14px;text-decoration:none;font:800 13px Manrope,sans-serif;color:var(--tint)}
.go:hover{text-decoration:underline}
.shot{border-radius:16px;overflow:hidden;border:1px solid var(--line);box-shadow:var(--shadow);background:#F4EEE6;min-width:0}
.chrome{display:flex;align-items:center;gap:6px;padding:9px 12px;background:#F4EEE6}
.chrome i{width:9px;height:9px;border-radius:50%;background:#E3D9CC}.chrome i:first-child{background:#FF7A2A}
.chrome span{margin-inline-start:8px;flex:1;min-width:0;font:700 11.5px/1 Manrope,sans-serif;color:var(--muted);background:#fff;border-radius:7px;padding:6px 10px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.shot img{display:block;width:100%;height:auto}
.end{max-width:1220px;margin:10px auto 0;padding:0 22px 54px}
.end .card{background:linear-gradient(135deg,#1A140F,#2A1D12);color:#fff;border-radius:26px;padding:34px 28px;display:grid;gap:14px;position:relative;overflow:hidden}
.end .card::after{content:"";position:absolute;width:420px;height:420px;right:-160px;top:-200px;border-radius:50%;background:rgba(255,122,42,.35);filter:blur(60px)}
.end h2{color:#fff}.end p{margin:0;color:rgba(255,255,255,.78);max-width:56ch;position:relative}
.end .row{display:flex;gap:10px;flex-wrap:wrap;position:relative}
.end .btn.s{background:rgba(255,255,255,.08);border-color:rgba(255,255,255,.2);color:#fff}
.ft{margin-top:20px;background:linear-gradient(180deg,#1A130D,#120D09);color:#fff;position:relative;overflow:hidden}
.ft::before{content:"";position:absolute;width:640px;height:640px;left:-200px;top:-360px;border-radius:50%;background:rgba(255,122,42,.22);filter:blur(80px)}
.ft::after{content:"";position:absolute;inset:0;background-image:radial-gradient(rgba(255,255,255,.06) 1px,transparent 1px);background-size:22px 22px;pointer-events:none}
.ft-in{position:relative;max-width:1220px;margin:0 auto;padding:54px 22px 30px;display:grid;grid-template-columns:1fr;gap:30px;z-index:1}
.ft-brand{display:grid;gap:12px;max-width:34ch;justify-items:start}
.ft-brand .mark{width:40px;height:40px;border-radius:12px;font-size:18px}
.ft-brand b{font:800 20px Manrope,sans-serif;letter-spacing:-.4px}
.ft-brand p{margin:0;color:rgba(255,255,255,.72);font-size:14.5px;line-height:1.6}
.ft-badges{display:flex;flex-wrap:wrap;gap:6px}
.ft-badges span{font:700 11px Manrope,sans-serif;color:rgba(255,255,255,.85);border:1px solid rgba(255,255,255,.18);background:rgba(255,255,255,.06);border-radius:999px;padding:5px 10px}
.ft-col h4{margin:0 0 12px;font:800 11px/1 Manrope,sans-serif;letter-spacing:1.6px;text-transform:uppercase;color:#FF9A4D}
.ft-col ul{list-style:none;margin:0;padding:0;display:grid;gap:8px}
.ft-col a{text-decoration:none;color:rgba(255,255,255,.78);font:600 14px Manrope,sans-serif;transition:color .15s,transform .15s;display:inline-block}
.ft-col a:hover{color:#fff;transform:translateX(3px)}
.ft-bar{position:relative;z-index:1;max-width:1220px;margin:0 auto;padding:18px 22px 34px;border-top:1px solid rgba(255,255,255,.12);display:flex;flex-wrap:wrap;gap:10px 22px;align-items:center;font:600 12.5px Manrope,sans-serif;color:rgba(255,255,255,.55)}
.ft-mail{text-decoration:none;color:rgba(255,255,255,.85);font:700 12.5px Manrope,sans-serif}.ft-mail:hover{color:#FF9A4D}
.ft-top{margin-inline-start:auto;text-decoration:none;color:#fff;background:rgba(255,255,255,.1);border:1px solid rgba(255,255,255,.18);border-radius:999px;padding:8px 14px;font:800 12.5px Manrope,sans-serif;transition:background .15s}
.ft-top:hover{background:var(--accent)}
@media (min-width:760px){.ft-in{grid-template-columns:1.4fr 1fr 1fr 1fr;gap:40px}}
@media (min-width:700px){.chapters{grid-template-columns:repeat(4,1fr)}.intro{grid-template-columns:repeat(2,1fr)}}
@media (min-width:1100px){.intro{grid-template-columns:repeat(2,1fr)}}
@media (min-width:900px){
  .top .links{display:flex}
  .top .where{display:inline-flex}
  .top .in{padding:12px 22px}
  .top b{font-size:15px;flex:0 1 auto}.top b .lg{display:inline}.top b .sm{display:none}
  .top .logo{flex:0 0 36px;width:36px;height:36px}.top .in{gap:12px}.top .cta{padding:9px 14px}.top .tsb{margin-inline-end:8px}
  .top .tsb{margin-inline-start:0}
  .tmb{display:none}
  .side.drawer{position:static;width:auto;transform:none;box-shadow:none;padding:0;background:none;display:block}
  .side.drawer .dhead,.dback{display:none!important}
  .top .cta span{display:inline}.top .cta i{display:none}
  .hero .in{grid-template-columns:1.05fr 1fr;padding:76px 22px 54px}
  .wrap{grid-template-columns:264px minmax(0,1fr);gap:44px}
  .toc{display:none}
  .side{display:block;position:sticky;top:84px;align-self:start;max-height:calc(100vh - 100px);overflow:auto;scrollbar-width:thin;padding-right:4px}
}
@media (min-width:1100px){
  article{grid-template-columns:320px minmax(0,1fr);gap:30px;align-items:start}
  article.flip{grid-template-columns:minmax(0,1fr) 320px}
  article.flip .txt{order:2}
}
@media (max-width:899px){.stage .main{transform:none}.stage .side{display:none}}
/* scroll reveal: only elements below the fold get .rv (added by script), so the page is fully visible at rest */
.rv{opacity:0;transform:translateY(26px) scale(.985);transition:opacity .7s ease,transform .8s cubic-bezier(.16,1,.3,1)}
.rv.in{opacity:1;transform:none}
.rv .shot{transition:transform .9s cubic-bezier(.16,1,.3,1) .1s}
.rv:not(.in) .shot{transform:translateY(18px)}
.chapters a.rv{transition-delay:calc(var(--i,0)*60ms)}
.stage .main{transition:transform .6s ease}
@media (prefers-reduced-motion:no-preference){html{scroll-behavior:smooth}}
@media (prefers-reduced-motion:reduce){.rv{opacity:1;transform:none;transition:none}.rv .shot{transform:none;transition:none}.hero,.hero .in > div:first-child > *,.stage .main,.stage .side,.hero h1 span,.tree .tn,.btn.p,.step{animation:none}}
@media (prefers-reduced-motion:reduce){.chapters a,article,.btn{transition:none}}
"""

page = ('<title>OrderOrange Partner Guide</title>\n'
 '<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Manrope:wght@500;600;700;800&display=swap">\n'
 f'<style>{css}</style>\n'
 '<div class="progress" id="prog"></div><a id="top"></a>\n'
 '<button class="cbtn" id="cbtn" aria-label="Contact us"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><path d="M4 4h16v12H7l-3 3z"/></svg><span>Contact us</span></button>'
 '<div class="csheet" id="csheet"><form class="cform" id="cform" novalidate><div class="chead"><div><span class="eyebrow">Contact us</span><h3>Tell us what you need</h3><p>We answer by email or phone, usually the same day.</p></div><button type="button" class="cx" id="cx" aria-label="Close">×</button></div>'
 '<label>Email<input name="email" type="email" required placeholder="you@example.com" autocomplete="email"></label>'
 '<label>Phone<input name="phone" type="tel" required placeholder="+968 9xxx xxxx" autocomplete="tel"></label>'
 '<label>Title<input name="title" type="text" required maxlength="120" placeholder="What is it about?"></label>'
 '<label>Message<textarea name="text" required minlength="5" maxlength="3000" rows="5" placeholder="Write your message…"></textarea></label>'
 '<div class="cfoot"><span class="cmsg" id="cmsg"></span><button type="submit" class="btn p" id="csend">Send message</button></div><div class="thanks"><b>Thank you!</b><p>Your message is in. We reply by email or phone.</p><button type="button" class="btn s" id="cdone">Close</button></div><p class="cnote">We take up to 100 messages a day while the form is new. If it is full, email <a href="mailto:info@orderorange.com">info@orderorange.com</a>.</p></form></div>'
 '<div class="pal" id="pal"><div class="pal-box"><div class="pal-in"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg><input id="palq" placeholder="Jump to a page, a chapter or a question…" autocomplete="off"><kbd>Esc</kbd></div><ul class="pal-list" id="pallist"></ul><div class="pal-foot"><span>↑↓ move</span><span>↵ open</span><span>Ctrl+K anywhere</span></div></div></div>\n'
 f'<div class="top"><div class="in"><img class="logo" src="{LOGO}" alt="OrderOrange" width="36" height="36"><b><span class="lg">OrderOrange · Partner Guide</span><span class="sm">Partner Guide</span></b><span class="where" id="where"><i></i><span>Introduction</span><em>0%</em></span>'
 '<nav class="links"><a href="#sell">Selling</a><a href="#orders">Orders</a><a href="#menu">Menu</a><a href="#money">Money</a><a href="#team">Team</a><a href="#reports">Reports</a></nav>'
 '<button class="tmb" id="tmb" aria-label="Menu"><i></i><i></i><i></i></button>'
 '<button class="tsb" id="tsb" aria-label="Search the guide"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg></button>'
 '<a class="cta" href="https://partner.orderorange.com/register" target="_blank" rel="noopener"><span>Open partner app</span><i>Open app</i></a></div></div>\n'
 '<div class="hero"><div class="spot" id="spot"></div><div class="in"><div><span class="eyebrow">Partner app · Edition 1</span><h1>Run the whole restaurant <span>from one screen.</span></h1>\n'
 '<p>A page-by-page tour of the OrderOrange partner app: selling, orders and guests, your menu, money, your team, reports and setup. Every screen comes from the Saffron Grill House demo store.</p>\n'
 '<div class="row"><a class="btn p" href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Open partner app ↗</a><a class="btn s" href="#around">Start the tour</a></div>\n'
 f'<div class="stats"><div><b>{count}</b><span>Screens</span></div><div><b>{len(parts)}</b><span>Chapters</span></div><div><b>14</b><span>Languages</span></div><div><b>3</b><span>Devices</span></div><div><b>{total_min}</b><span>Min read</span></div></div></div>\n'
 f'<div class="stage"><div class="main">{hero_main}</div><div class="side">{hero_side}</div></div></div></div>\n'
 f'<div class="chapters">{cards}</div>\n'
 f'<div class="dback" id="dback"></div><div class="wrap"><nav class="toc">{toc}</nav><aside class="side drawer" id="side"><div class="dhead"><img class="logo" src="{LOGO}" alt="" width="30" height="30"><b>Guide menu</b><button type="button" id="dclose" aria-label="Close">×</button></div>{tree}</aside><main>{body}</main></div>\n'
 '<div class="end"><div class="card"><span class="eyebrow" style="--tint:#FF9A4D">Ready when you are</span><h2>Sign in and start selling.</h2>'
 '<p>Your login opens exactly the pages your role allows. Waiters land on the tablet POS, owners on the dashboard. Everything in this guide is live at partner.orderorange.com.</p>'
 '<div class="row"><a class="btn p" href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Open the partner app</a><a class="btn s" href="#around">Back to the top</a></div></div></div>\n'
 '<footer class="ft"><div class="ft-in">'
 f'<div class="ft-brand"><img class="logo" src="{LOGO}" alt="OrderOrange" width="44" height="44"><b>OrderOrange</b><p>One app for the counter, the kitchen, the floor and the books. Built for restaurants and shops in Oman.</p>'
 '<div class="ft-badges"><span>14 languages</span><span>Phone · tablet · desktop</span><span>Arabic &amp; Persian tickets</span></div></div>'
 f'<div class="ft-col"><h4>Chapters</h4><ul>{ftmap}</ul></div>'
 '<div class="ft-col"><h4>Product</h4><ul><li><a href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Partner app</a></li><li><a href="https://www.orderorange.com" target="_blank" rel="noopener">Customer website</a></li><li><a href="https://api.orderorange.com/api/till/download">Till app for Windows</a></li><li><a href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Join as a partner</a></li></ul></div>'
 '<div class="ft-col"><h4>Help</h4><ul><li><a href="#faq">Frequently asked</a></li><li><a href="#start">Before you start</a></li><li><a href="https://partner.orderorange.com/support" target="_blank" rel="noopener">Support desk</a></li><li><a href="mailto:info@orderorange.com">info@orderorange.com</a></li><li><a href="https://www.orderorange.com" target="_blank" rel="noopener">orderorange.com</a></li></ul></div>'
 '</div><div class="ft-bar"><span>© 2026 OrderOrange · Partner app guide, edition 1</span><span>Screens and numbers are demo data from Saffron Grill House</span><a class="ft-mail" href="mailto:info@orderorange.com">info@orderorange.com</a><a class="ft-top" href="#top" id="totop">Back to top ↑</a></div></footer>\n'
 '<script>\n'
 "(function(){var links=[].slice.call(document.querySelectorAll('.toc a'));var secs=links.map(function(a){return document.querySelector(a.getAttribute('href'))});\n"
 "function on(){var y=window.scrollY+150,i=0;secs.forEach(function(s,k){if(s&&s.offsetTop<=y)i=k});links.forEach(function(a,k){a.classList.toggle('on',k===i)})}\n"
 "var p=document.getElementById('prog');function pr(){var d=document.documentElement;var m=d.scrollHeight-d.clientHeight;p.style.transform='scaleX('+(m>0?window.scrollY/m:0)+')'}\n"
 "window.addEventListener('scroll',function(){on();pr()},{passive:true});on();pr();\n"
 "var rm=window.matchMedia('(prefers-reduced-motion: reduce)').matches;\n"
 "if(!rm&&'IntersectionObserver' in window){var els=[].slice.call(document.querySelectorAll('article,.chapters a,.sec-head,.ib,.faq details,.end .card'));var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){e.target.classList.add('in');io.unobserve(e.target)}})},{rootMargin:'0px 0px -8% 0px',threshold:.08});\n"
 "els.forEach(function(el,i){if(el.getBoundingClientRect().top>window.innerHeight*.9){el.classList.add('rv');el.style.setProperty('--i',i%4);io.observe(el)}});}\n"
 "if(!rm){[].slice.call(document.querySelectorAll('.stats b')).forEach(function(b){var v=parseInt(b.textContent,10);if(!v)return;var t0=null;function step(ts){if(!t0)t0=ts;var p=Math.min(1,(ts-t0)/900);b.textContent=Math.round(v*(1-Math.pow(1-p,3)));if(p<1)requestAnimationFrame(step)}requestAnimationFrame(step)});}\n"
 "var tq=document.getElementById('tq'),tree=document.querySelector('.tree'),none=document.querySelector('.tnone');\n"
 "if(tree){[].slice.call(tree.querySelectorAll('.tn-h .tw')).forEach(function(tw){tw.addEventListener('click',function(e){e.preventDefault();e.stopPropagation();tw.closest('.tn').classList.toggle('closed')})});\n"
 "function filt(){var q=(tq.value||'').trim().toLowerCase();var any=false;[].slice.call(tree.querySelectorAll('.tn')).forEach(function(li){var head=li.querySelector('.tn-h');var kids=[].slice.call(li.querySelectorAll('ul a'));var hit=false;kids.forEach(function(a){var ok=!q||(a.getAttribute('data-q')||'').indexOf(q)>=0;a.classList.toggle('hide',!ok);if(ok)hit=true;var txt=a.textContent;a.innerHTML=q&&ok&&txt.toLowerCase().indexOf(q)>=0?txt.replace(new RegExp('('+q.replace(/[.*+?^${}()|[\\]\\\\]/g,'\\\\$&')+')','ig'),'<mark>$1</mark>'):txt});var hh=!q||(head.getAttribute('data-q')||'').indexOf(q)>=0;var show=hh||hit;li.classList.toggle('hide',!show);if(show)any=true;if(q&&hit)li.classList.remove('closed')});none.classList.toggle('show',!any)}\n"
 "tq.addEventListener('input',filt);document.addEventListener('keydown',function(e){if(e.key==='/'&&document.activeElement!==tq&&!/input|textarea/i.test(document.activeElement.tagName)){e.preventDefault();tq.focus()}if(e.key==='Escape'&&document.activeElement===tq){tq.value='';filt();tq.blur()}});\n"
 "var sub=[].slice.call(tree.querySelectorAll('ul a'));var subT=sub.map(function(a){return document.querySelector(a.getAttribute('href'))});\n"
 "function onTree(){var y=window.scrollY+160,i=-1;subT.forEach(function(s,k){if(s&&s.offsetTop<=y)i=k});sub.forEach(function(a,k){a.classList.toggle('on',k===i)});var tops=[].slice.call(tree.querySelectorAll('.tn'));var cur=-1;tops.forEach(function(li,k){var s=document.querySelector(li.querySelector('.tn-h').getAttribute('href'));if(s&&s.offsetTop<=y)cur=k});tops.forEach(function(li,k){li.classList.toggle('on',k===cur)})}\n"
 "window.addEventListener('scroll',onTree,{passive:true});onTree();}\n"

 "var stage=document.querySelector('.stage');if(stage&&!rm&&matchMedia('(hover:hover)').matches){var hero=document.querySelector('.hero');hero.addEventListener('mousemove',function(e){var r=hero.getBoundingClientRect();var x=(e.clientX-r.left)/r.width-.5,y=(e.clientY-r.top)/r.height-.5;stage.style.transform='translate('+(x*14)+'px,'+(y*10)+'px)'});hero.addEventListener('mouseleave',function(){stage.style.transform=''})} var wms=[].slice.call(document.querySelectorAll('.wm'));function wmScroll(){wms.forEach(function(w){var r=w.getBoundingClientRect();if(r.top<innerHeight&&r.bottom>0){w.style.transform='translateY('+((r.top/innerHeight-.5)*40)+'px)'}})} if(!rm){window.addEventListener('scroll',wmScroll,{passive:true});wmScroll()} var where=document.getElementById('where'),secsAll=[].slice.call(document.querySelectorAll('section[data-title]'));function whereFn(){var y=window.scrollY+120,cur=null;secsAll.forEach(function(s){if(s.offsetTop<=y)cur=s});var d=document.documentElement,m=d.scrollHeight-d.clientHeight,pc=Math.round(m>0?window.scrollY/m*100:0);if(where){where.querySelector('span').textContent=cur?cur.getAttribute('data-title'):'Introduction';where.querySelector('em').textContent=pc+'%';where.style.setProperty('--tint',cur?getComputedStyle(cur).getPropertyValue('--tint'):'#FF5A00')}}window.addEventListener('scroll',whereFn,{passive:true});whereFn(); var pal=document.getElementById('pal'),palq=document.getElementById('palq'),pallist=document.getElementById('pallist');var entries=[];[].slice.call(document.querySelectorAll('.tree .tn')).forEach(function(li){var h=li.querySelector('.tn-h');var tint=li.style.getPropertyValue('--tint');entries.push({href:h.getAttribute('href'),num:h.querySelector('em').textContent,title:h.querySelector('span').textContent,sub:'Chapter',q:h.getAttribute('data-q')||'',tint:tint});[].slice.call(li.querySelectorAll('ul a')).forEach(function(a){entries.push({href:a.getAttribute('href'),num:h.querySelector('em').textContent,title:a.textContent,sub:h.querySelector('span').textContent,q:a.getAttribute('data-q')||'',tint:tint})})});var sel=0;function render(){var q=(palq.value||'').trim().toLowerCase();var hits=entries.filter(function(e){return !q||e.q.indexOf(q)>=0||e.title.toLowerCase().indexOf(q)>=0}).slice(0,12);sel=Math.min(sel,Math.max(0,hits.length-1));pallist.innerHTML=hits.map(function(e,i){return '<li class=\"'+(i===sel?'sel':'')+'\" style=\"--tint:'+e.tint+'\"><a href=\"'+e.href+'\"><em>'+e.num+'</em><b>'+e.title+'</b><small>'+e.sub+'</small></a></li>'}).join('')||'<li><a><b>No match</b></a></li>'}function openPal(){pal.classList.add('on');palq.value='';sel=0;render();setTimeout(function(){palq.focus()},30)}function closePal(){pal.classList.remove('on')}palq.addEventListener('input',function(){sel=0;render()});palq.addEventListener('keydown',function(e){var n=pallist.querySelectorAll('li').length;if(e.key==='ArrowDown'){sel=(sel+1)%n;render();e.preventDefault()}else if(e.key==='ArrowUp'){sel=(sel-1+n)%n;render();e.preventDefault()}else if(e.key==='Enter'){var a=pallist.querySelector('li.sel a');if(a&&a.getAttribute('href')){closePal();location.hash=a.getAttribute('href')}}else if(e.key==='Escape'){closePal()}});pal.addEventListener('click',function(e){if(e.target===pal)closePal();var a=e.target.closest('a');if(a&&a.getAttribute('href')){closePal()}});document.addEventListener('keydown',function(e){if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();pal.classList.contains('on')?closePal():openPal()}});var tsb=document.getElementById('tsb');if(tsb){tsb.addEventListener('click',openPal)} var spot=document.getElementById('spot');if(spot&&!rm&&matchMedia('(hover:hover)').matches){var heroEl=document.querySelector('.hero');heroEl.addEventListener('mousemove',function(e){var r=heroEl.getBoundingClientRect();spot.style.left=(e.clientX-r.left)+'px';spot.style.top=(e.clientY-r.top)+'px'})} var shotsAll=[].slice.call(document.querySelectorAll('main .shot img'));function drift(){if(rm)return;shotsAll.forEach(function(im){var r=im.getBoundingClientRect();if(r.top<innerHeight&&r.bottom>0){im.style.transform='translateY('+((r.top+r.height/2-innerHeight/2)/innerHeight*-10)+'px)'}})}window.addEventListener('scroll',drift,{passive:true});drift(); var API=(location.hostname==='38.247.147.129'?'https://38.247.147.129:8600':'https://api.orderorange.com');try{fetch(API+'/api/track',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({app:'Partner',page:'/guide.html'}),keepalive:true}).catch(function(){})}catch(e){} var side=document.getElementById('side'),dback=document.getElementById('dback'),tmb=document.getElementById('tmb');function dOpen(){side.classList.add('on');dback.classList.add('on');document.body.style.overflow='hidden'}function dClose(){side.classList.remove('on');dback.classList.remove('on');document.body.style.overflow=''}if(tmb){tmb.addEventListener('click',dOpen)}dback.addEventListener('click',dClose);document.getElementById('dclose').addEventListener('click',dClose);side.addEventListener('click',function(e){var a=e.target.closest('a');if(a&&a.getAttribute('href')&&a.getAttribute('href').charAt(0)==='#'&&!e.target.closest('.tw'))dClose()});document.addEventListener('keydown',function(e){if(e.key==='Escape'&&side.classList.contains('on'))dClose()}); var cb=document.getElementById('cbtn'),cs=document.getElementById('csheet'),cf=document.getElementById('cform'),cm=document.getElementById('cmsg'),csend=document.getElementById('csend');function copen(){cs.classList.add('on');document.body.style.overflow='hidden';setTimeout(function(){var f=cf.querySelector('input');if(f)f.focus()},50)}function cclose(){cs.classList.remove('on');document.body.style.overflow=''}cb.addEventListener('click',copen);document.getElementById('cx').addEventListener('click',cclose);document.getElementById('cdone').addEventListener('click',function(){cclose();cf.classList.remove('sent');cf.reset();cm.textContent=''});cs.addEventListener('click',function(e){if(e.target===cs)cclose()});cf.addEventListener('submit',function(e){e.preventDefault();var ok=true;[].slice.call(cf.querySelectorAll('input,textarea')).forEach(function(i){var v=i.checkValidity();i.classList.toggle('bad',!v);if(!v)ok=false});if(!ok){cm.className='cmsg err';cm.textContent='Please fill every field.';return}var d={email:cf.email.value.trim(),phone:cf.phone.value.trim(),title:cf.title.value.trim(),text:cf.text.value.trim(),source:'guide '+location.pathname};csend.disabled=true;cm.className='cmsg';cm.textContent='Sending…';fetch(API+'/api/contact',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(d)}).then(function(r){return r.json().catch(function(){return {}}).then(function(j){return {s:r.status,j:j}})}).then(function(x){csend.disabled=false;if(x.s===200){cf.classList.add('sent');cm.textContent=''}else{cm.className='cmsg err';cm.textContent=(x.j&&x.j.message)||'Something went wrong. Please try again.'}}).catch(function(){csend.disabled=false;cm.className='cmsg err';cm.textContent='No connection. Please try again.'})});document.addEventListener('keydown',function(e){if(e.key==='Escape'&&cs.classList.contains('on'))cclose()}); var lb=document.createElement('div');lb.className='lb';lb.innerHTML='<div class=\"stage2\"><div class=\"box\"><img alt=\"\"></div></div><button class=\"x\" aria-label=\"Close\">×</button><div class=\"cap\"></div><div class=\"ctl\"><button data-z=\"-\" aria-label=\"Zoom out\">−</button><button data-z=\"0\" class=\"w\">Fit</button><button data-z=\"+\" aria-label=\"Zoom in\">+</button></div>';document.body.appendChild(lb);var st=lb.querySelector('.stage2'),li=lb.querySelector('img'),cap=lb.querySelector('.cap');var Z={s:1,base:1,w:1000,h:800};function setW(){li.style.width=Math.round(Z.w*Z.s)+'px';li.style.height='auto'}function fit(){Z.w=li.naturalWidth||1000;Z.h=li.naturalHeight||800;Z.base=Math.min((st.clientWidth-32)/Z.w,(st.clientHeight-150)/Z.h,1);Z.s=Z.base;setW();st.scrollLeft=0;st.scrollTop=0}function zoomAt(f,cx,cy){var ns=Math.min(Math.max(Z.base,Z.s*f),Z.base*6);if(ns===Z.s)return;var r=li.getBoundingClientRect();if(cx==null){cx=innerWidth/2;cy=innerHeight/2}var px=(cx-r.left)/r.width,py=(cy-r.top)/r.height;Z.s=ns;setW();var r2=li.getBoundingClientRect();st.scrollLeft+=(r2.left+px*r2.width)-cx;st.scrollTop+=(r2.top+py*r2.height)-cy}function close(){lb.classList.remove('on');document.body.style.overflow=''}function openLb(im){li.src=im.src;cap.textContent=im.alt+' · pinch or scroll to zoom, drag to move';lb.classList.add('on');document.body.style.overflow='hidden';if(li.complete&&li.naturalWidth)fit();else li.onload=fit}[].slice.call(document.querySelectorAll('.shot img')).forEach(function(im){im.addEventListener('click',function(){openLb(im)})});lb.querySelector('.x').addEventListener('click',close);lb.querySelector('.ctl').addEventListener('click',function(e){var b=e.target.closest('button');if(!b)return;var z=b.getAttribute('data-z');if(z==='+')zoomAt(1.5);else if(z==='-')zoomAt(1/1.5);else fit()});st.addEventListener('wheel',function(e){if(e.ctrlKey||Z.s<=Z.base||Math.abs(e.deltaY)>=Math.abs(e.deltaX)){e.preventDefault();zoomAt(e.deltaY<0?1.15:1/1.15,e.clientX,e.clientY)}},{passive:false});var ptrs={},drag=null,pinch=null,lastTap=0;st.addEventListener('pointerdown',function(e){ptrs[e.pointerId]={x:e.clientX,y:e.clientY};var ks=Object.keys(ptrs);if(ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];pinch={d:Math.hypot(a.x-b.x,a.y-b.y),s:Z.s};drag=null}else if(e.pointerType==='mouse'){drag={x:e.clientX,y:e.clientY,sl:st.scrollLeft,stp:st.scrollTop,moved:false};st.classList.add('drag');st.setPointerCapture(e.pointerId)}});st.addEventListener('pointermove',function(e){if(!ptrs[e.pointerId])return;ptrs[e.pointerId]={x:e.clientX,y:e.clientY};var ks=Object.keys(ptrs);if(pinch&&ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];var d=Math.hypot(a.x-b.x,a.y-b.y);var target=pinch.s*(d/pinch.d);zoomAt(target/Z.s,(a.x+b.x)/2,(a.y+b.y)/2)}else if(drag){var dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>4)drag.moved=true;st.scrollLeft=drag.sl-dx;st.scrollTop=drag.stp-dy}});function up(e){delete ptrs[e.pointerId];st.classList.remove('drag');if(Object.keys(ptrs).length<2)pinch=null;if(drag&&!drag.moved){var now=Date.now();if(now-lastTap<320){if(Z.s>Z.base*1.05)fit();else zoomAt(2.5,e.clientX,e.clientY)}lastTap=now}drag=null}st.addEventListener('pointerup',up);st.addEventListener('pointercancel',up);var tapT=0;st.addEventListener('touchend',function(e){if(e.touches.length)return;var now=Date.now();if(now-tapT<320&&e.changedTouches.length===1){var t=e.changedTouches[0];if(Z.s>Z.base*1.05)fit();else zoomAt(2.5,t.clientX,t.clientY);e.preventDefault()}tapT=now},{passive:false});window.addEventListener('resize',function(){if(lb.classList.contains('on'))fit()});document.addEventListener('keydown',function(e){if(!lb.classList.contains('on'))return;if(e.key==='Escape')close();if(e.key==='+'||e.key==='=')zoomAt(1.5);if(e.key==='-')zoomAt(1/1.5);if(e.key==='0')fit()});})();\n"
 '</script>')
out = os.path.join('..', 'orderorange-partner-guide.html')
open(out, 'w', encoding='utf-8').write(page)
print('written', round(len(page) / 1e6, 2), 'MB,', count, 'screens')
