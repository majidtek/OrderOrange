import base64, io, glob, html, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))

# Reuse the screen list and copy from gen.py without re-running its output.
src = io.open('gen.py', encoding='utf-8').read()
ns = {'__file__': os.path.abspath('gen.py')}
exec(src[:src.index("def sec(")], ns)          # defines img, F, parts
img, parts = ns['img'], ns['parts']

META = {
 'Dashboard': ('/', ['Read today at a glance', 'Open orders that need a decision', 'Jump anywhere from Quick actions']),
 'The menu': ('/  (Ctrl+K)', ['Search pages, products and customers', 'Operations · Basic information · Reports · Settings · Users']),
 'POS': ('/pos', ['In person, take-away or delivery', 'Save as open or Close & pay', 'Receipt and kitchen ticket print automatically']),
 'POS on a tablet': ('/pos-tablet', ['Categories on top, dishes in a grid', 'Floating total opens the ticket', 'Waiters land here after login']),
 'Tables': ('/tables', ['Rooms and tables as on the floor', 'Free · seated · QR order · bill requested', 'Tap a table to open its order']),
 'Arranging the floor': ('/tables', ['Drag a salon by its grip, resize from the corner', 'Tables travel with their salon', 'Saved the moment you let go']),
 'Table QR codes': ('/table-qr', ['One code per table', 'A scan opens the menu for that table', 'Print all or download one']),
 'What your customers see': ('https://www.orderorange.com', ['Restaurants, markets and pharmacies', 'Search by dish or craving', 'Delivery, pickup or table QR']),
 'Smart search': ('https://www.orderorange.com', ['Restaurants and dishes in one search', 'Typos, wrong keyboard, Arabizi, 14 languages', 'Did-you-mean, recent and trending']),
 'OrderOrange AI': ('https://www.orderorange.com', ['Orders by text or voice, start to finish', 'Order tracking and menu questions', 'Same assistant inside the POS']),
 'Loyalty': ('/loyalty', ['Points per OMR, reward, welcome gift', 'Bronze, Silver, Gold by lifetime points', 'Points arrive with every paid order', 'Nudge guests on WhatsApp']),
 'A guest\u2019s card': ('/loyalty', ['Ring up a visit by hand', 'Hand over a reward in one press', 'Adjust with a reason that is kept', 'Every movement on the card']),
 'Instagram': ('/instagram', ['Connect once, post from the menu', 'Ready hashtags', 'Token renews itself', '100 posts a day, counted']),
 'Scheduled posts': ('/instagram/schedule', ['Pick a photo or a dish', 'Day and time, or a shortcut', 'The plan: waiting and sent', 'Menu poster: draw, post or download']),
 'Today\u2019s dishes on WhatsApp': ('https://www.orderorange.com', ['Carousel of dishes with photos and prices', 'Order now on every card', 'Straight from your menu', 'Sent at the hour you choose']),
 'Surveys': ('/surveys', ['Multiple-choice questions', 'Show or hide to guests', 'Results and a share link']),
 'More than one business': ('/add-business', ['Several businesses, one login', 'Type, name, cuisine, map pin', 'Switch from the top bar']),
 'Bill report': ('/bills/report', ['Category · month · company', 'Paid, unpaid and overdue', 'Excel and A4 print']),
 'Order assistant and product printers': ('/products/printers', ['Type "2 lamb ribs table 3"', 'Route each dish to kitchen, bar or none', 'Cashier receipt always at the till']),
 'Live orders': ('/orders', ['Accept or reject new orders', 'New → Cooking → Ready', 'Refreshes on its own']),
 'Take-out and dine-in board': ('/deliveries', ['Mark delivered', 'Assign a courier', 'Share live location']),
 'Delivery hand-over': ('/delivery', ['Print the delivery note', 'Mark ready', 'Courier status per order']),
 'Kitchen display': ('/kds', ['Two lanes: new and cooking', 'Timers turn amber, then red', 'One screen per station', 'Recall a bumped ticket for 30 minutes']),
 'Reservations': ('/reservations', ['Web, table-QR and phone bookings', 'Confirm, cancel or Seat now', 'Guest notes shown inline']),
 'Table chat': ('/table-chats', ['One thread per table', 'Unread counters', 'Latest message preview']),
 'Answering a table': ('/table-chats', ['Text, photo or voice reply', 'Guest sees it on their phone', 'Answer from the till']),
 'Products': ('/menu', ['Categories and dishes', 'Available · sold out · popular counters', 'New category / New product']),
 'Editing a product': ('/menu', ['Up to four photos', 'Names in every language', 'Available · Popular · In-store only', 'Days of the week, hours and lead time']),
 'Warehouses map': ('/warehouses', ['Drag places into position', 'Draw a line: this feeds that', 'Move stock between places', 'Every move is logged']),
 'Adding a place': ('/warehouses', ['Ten pictures: factory to customers', 'Kind: main, dry, cold, freezer, kitchen, bar, branch', 'One default place']),
 'Central warehouse for branches': ('/warehouses', ['One central place per owner', 'Branches pull, head office pushes', 'Materials matched by name and unit', 'Logged in both shops']),
 'Waste': ('/waste', ['Material or finished dish', 'Eight reasons', '7 / 30 / 90 day view', 'Undo with one press']),
 'Recipes': ('/materials/recipes', ['Ingredients per portion', 'Portion cost and margin', 'Missing recipes flagged']),
 'Discount codes': ('/discounts', ['First order · min items · min spend · loyalty · happy hour', 'Cap, end date, usage limit', 'On/off with one tap']),
 'Bills and expenses': ('/bills', ['Total, unpaid, overdue', 'Days late per bill', 'Repeat monthly']),
 'Purchases': ('/materials/purchases', ['Receive stock with cost per unit', 'Cash, card, later or installments', 'Templates for regular orders']),
 'Receiving stock': ('/materials/purchases', ['Code or barcode bar: 7*20 = twenty of no. 7', 'Tap a tile to add a line', 'Templates for regular orders', 'Cash, card, later or instalments']),
 'Paying later or in instalments': ('/materials/purchases', ['Pay now, later, or in steps', '2, 3, 4 or 6 steps, weekly or monthly', 'Method fixed now or decided when paying', 'Open steps wait in Bills and purchases']),
 'Payments to suppliers': ('/materials/payments', ['Owed · overdue · this week · paid', 'Installment steps', 'Pay per line']),
 'Invoices': ('/invoices', ['Search by number, filter by date', 'Pencil replaces, cross cancels', 'A reason is required and kept']),
 'Cancelled invoices': ('/invoices', ['Who added it, who removed it', 'The reason they typed', 'Which invoice replaced it']),
 'What changed on an invoice': ('/invoices', ['Old number and new, side by side', 'Every line before and after', 'Added lines picked out']),
 'Payroll': ('/staff/payroll', ['Salary per person with hours', 'Pay one or Pay everyone', 'Expected · paid · remaining']),
 'Contracts': ('/contracts', ['Products and deliveries per month', 'Email, print or signing link', 'Monthly amount']),
 'Users and roles': ('/team-users', ['One login per person', 'Preset or custom role', 'Switch a login off']),
 'Roles and permissions': ('/team-roles', ['Page-by-page permissions', 'Adjust presets', 'Create your own role']),
 'Team activity': ('/team-activity', ['Online now / offline', 'Last login and current page', 'Page history']),
 'Team chat': ('/chat', ['One-to-one messages', 'Photos, voice, location', 'Unread counts']),
 'Attendance': ('/attendance', ["I'm here / I'm leaving", 'Position recorded', 'Leave requests to the owner']),
 'Calendar': ('/calendar', ['Month, week, day', 'Shared with the team', 'Reminders']),
 'Daily report': ('/daily-report', ['Every invoice of the day', 'Invoices · revenue · items · cancelled', 'Excel and A4 print']),
 'Consumption': ('/materials/report', ['Worked out from the recipes, not guessed', 'Costliest dishes by ingredient spend', 'Days of stock left per material']),
 'Product report': ('/reports/products', ['Today, 7 days, 30 days, year, range', 'Top five dishes', 'Share per product']),
 'Store settings': ('/settings', ['Profile, languages, hours', 'Map location and contacts', 'Kitchen language for tickets']),
 'Working offline': ('/support', ['Sells and prints without internet', 'Syncs when the connection returns', 'Request it with a support ticket']),
 'Developer API': ('https://partner.orderorange.com/api.html', ['Same login, Bearer token', '100+ endpoints, JSON over HTTPS', 'CRM, accounting, ERP, kitchen screens']),
 'Your currency': ('/settings', ['Pick a symbol, or type your own', 'A setting, not a conversion — figures never change', 'Follows onto the POS, invoices, reports and receipts']),
 'Invoice designer': ('/invoice-designer', ['Templates and logo', 'Fonts and messages', 'Live preview']),
 'Receipt preview': ('/invoice-designer', ['80 / 100 / 125 % preview', 'Verification QR on the bill']),
}

def sec(n, id, title, items):
    h = f'<section id="{id}"><div class="sec-head"><span class="eyebrow">Chapter {n:02d}</span><h2>{html.escape(title)}</h2></div>'
    for k, t, d in items:
        b, w, hh = img[k]
        path, acts = META[t]
        li = ''.join(f'<li>{html.escape(a)}</li>' for a in acts)
        h += (f'<article><div class="txt"><h3>{html.escape(t)}</h3><p>{d}</p><ul class="acts">{li}</ul></div>'
              f'<div class="shot"><div class="chrome"><i></i><i></i><i></i><span>partner.orderorange.com{html.escape(path)}</span></div>'
              f'<img src="data:image/jpeg;base64,{b}" width="{w}" height="{hh}" alt="{html.escape(t)}" loading="lazy"></div></article>')
    return h + '</section>'

toc = ''.join(f'<a href="#{i}"><em>{n+1:02d}</em>{html.escape(t)}</a>' for n, (i, t, _) in enumerate(parts))
body = ''.join(sec(n + 1, i, t, items) for n, (i, t, items) in enumerate(parts))
count = sum(len(items) for _, _, items in parts)

css = """
:root{--bg:#FBF8F3;--surface:#FFFFFF;--ink:#1C1712;--ink-2:#4E4438;--muted:#8A7D6E;--line:#EAE1D5;--panel:#FFF1E6;--accent:#FF5A00;--accent-deep:#B93F00;--chrome:#F1EAE0;--shadow:0 18px 44px rgba(90,50,10,.12)}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#120E0A;--surface:#1B1610;--ink:#F6EFE6;--ink-2:#D6CBBD;--muted:#9F917F;--line:#2E261C;--panel:#2A1D12;--accent:#FF7A2A;--accent-deep:#FFA36B;--chrome:#241D15;--shadow:0 18px 44px rgba(0,0,0,.45)}}
:root[data-theme="dark"]{--bg:#120E0A;--surface:#1B1610;--ink:#F6EFE6;--ink-2:#D6CBBD;--muted:#9F917F;--line:#2E261C;--panel:#2A1D12;--accent:#FF7A2A;--accent-deep:#FFA36B;--chrome:#241D15;--shadow:0 18px 44px rgba(0,0,0,.45)}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.6 "Source Sans 3",system-ui,"Segoe UI",Arial,sans-serif;-webkit-font-smoothing:antialiased}
a{color:inherit}
.top{position:sticky;top:0;z-index:6;background:color-mix(in srgb,var(--bg) 86%,transparent);backdrop-filter:blur(10px);border-bottom:1px solid var(--line)}
.top .in{max-width:1180px;margin:0 auto;padding:12px 20px;display:flex;align-items:center;gap:12px}
.mark{width:32px;height:32px;border-radius:10px;background:linear-gradient(135deg,#FF8A3D,#FF5A00);display:grid;place-items:center;color:#fff;font:700 16px Sora,sans-serif;flex:none;box-shadow:0 6px 14px rgba(255,90,0,.3)}
.top b{font:600 15px Sora,sans-serif;letter-spacing:-.1px}
.top .ver{margin-inline-start:auto;font-size:12px;color:var(--muted);letter-spacing:.4px;text-transform:uppercase;white-space:nowrap}
.hero{max-width:1180px;margin:0 auto;padding:56px 20px 28px;display:grid;gap:22px}
.eyebrow{display:inline-block;font:600 11.5px/1 "Source Sans 3",sans-serif;letter-spacing:1.6px;text-transform:uppercase;color:var(--accent-deep)}
.hero h1{font:700 clamp(34px,6vw,60px)/1.02 Sora,sans-serif;letter-spacing:-1.2px;margin:0;max-width:16ch;text-wrap:balance}
.hero p{margin:0;color:var(--ink-2);font-size:18px;max-width:60ch}
.stats{display:flex;gap:28px;flex-wrap:wrap;padding-top:6px}
.stats div{display:grid;gap:2px}.stats b{font:600 26px/1 Sora,sans-serif;font-variant-numeric:tabular-nums}.stats span{font-size:12.5px;color:var(--muted);letter-spacing:.3px;text-transform:uppercase}
.wrap{max-width:1180px;margin:0 auto;padding:10px 20px 80px;display:grid;grid-template-columns:1fr;gap:34px}
.toc{display:flex;gap:8px;overflow-x:auto;scrollbar-width:none;padding-bottom:6px;padding-top:6px;position:sticky;top:57px;z-index:5;background:var(--bg);margin:0 -20px;padding-inline:20px}
.toc::-webkit-scrollbar{display:none}
.toc a{flex:none;display:inline-flex;align-items:center;gap:8px;text-decoration:none;font:600 13px/1 "Source Sans 3",sans-serif;color:var(--ink-2);border:1px solid var(--line);background:var(--surface);border-radius:999px;padding:9px 14px 9px 10px;transition:border-color .15s,color .15s}
.toc a em{font-style:normal;font:600 11px/1 Sora,sans-serif;color:var(--accent);background:var(--panel);border-radius:999px;padding:5px 7px}
.toc a:hover,.toc a.on,.toc a:focus-visible{border-color:var(--accent);color:var(--accent-deep);outline:none}
section{scroll-margin-top:118px}
.sec-head{display:grid;gap:6px;margin:26px 0 10px;padding-top:26px;border-top:1px solid var(--line)}
section:first-child .sec-head{border-top:0;padding-top:0;margin-top:0}
h2{font:700 clamp(24px,3.4vw,32px)/1.1 Sora,sans-serif;letter-spacing:-.6px;margin:0}
article{display:grid;gap:16px;background:var(--surface);border:1px solid var(--line);border-radius:22px;padding:20px;margin-top:18px}
h3{font:600 20px/1.2 Sora,sans-serif;letter-spacing:-.3px;margin:0 0 8px;text-wrap:balance}
article p{margin:0;color:var(--ink-2);max-width:62ch}
article p b{font-weight:600;color:var(--ink)}
.acts{list-style:none;margin:14px 0 0;padding:0;display:flex;flex-wrap:wrap;gap:6px}
.acts li{font-size:12.5px;font-weight:600;color:var(--accent-deep);background:var(--panel);border-radius:999px;padding:5px 11px}
.shot{border-radius:14px;overflow:hidden;border:1px solid var(--line);box-shadow:var(--shadow);background:var(--chrome);min-width:0}
.chrome{display:flex;align-items:center;gap:6px;padding:9px 12px;background:var(--chrome)}
.chrome i{width:9px;height:9px;border-radius:50%;background:var(--line)}.chrome i:first-child{background:#FF7A2A}
.chrome span{margin-inline-start:8px;flex:1;min-width:0;font:500 11.5px/1 "Source Sans 3",sans-serif;color:var(--muted);background:var(--surface);border-radius:6px;padding:6px 10px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.shot img{display:block;width:100%;height:auto}
footer{max-width:1180px;margin:0 auto;padding:18px 20px 48px;color:var(--muted);font-size:13px;display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;border-top:1px solid var(--line)}
@media (min-width:900px){
  .wrap{grid-template-columns:230px minmax(0,1fr);gap:44px}
  .toc{position:sticky;top:90px;align-self:start;flex-direction:column;overflow:visible;margin:0;padding:0;background:none;gap:4px}
  .toc a{border:0;background:none;padding:8px 10px;border-radius:10px;color:var(--muted)}
  .toc a.on,.toc a:hover{background:var(--panel);color:var(--accent-deep)}
}
@media (min-width:1100px){ article{grid-template-columns:300px minmax(0,1fr);gap:26px;align-items:start} }
@media (prefers-reduced-motion:no-preference){html{scroll-behavior:smooth}}
"""

page = ('<title>OrderOrange Partner Guide</title>\n'
 '<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Sora:wght@600;700&family=Source+Sans+3:wght@400;500;600&display=swap">\n'
 f'<style>{css}</style>\n'
 '<div class="top"><div class="in"><span class="mark">O</span><b>OrderOrange · Partner Guide</b><span class="ver">Edition 1 · Sep 2026</span></div></div>\n'
 '<div class="hero"><span class="eyebrow">For restaurant partners</span><h1>Run the whole restaurant from one screen.</h1>\n'
 '<p>A page-by-page tour of the OrderOrange partner app: selling, orders and guests, your menu, money, your team, reports and setup. Every screen below comes from the Saffron Grill House demo store.</p>\n'
 f'<div class="stats"><div><b>{count}</b><span>Screens</span></div><div><b>{len(parts)}</b><span>Chapters</span></div><div><b>14</b><span>Languages</span></div><div><b>3</b><span>Phone · tablet · desktop</span></div></div></div>\n'
 f'<div class="wrap"><nav class="toc">{toc}</nav><main>{body}</main></div>\n'
 '<footer><span>OrderOrange · Partner app guide</span><span>partner.orderorange.com · numbers shown are demo data</span></footer>\n'
 '<script>\n'
 "(function(){var links=[].slice.call(document.querySelectorAll('.toc a'));var secs=links.map(function(a){return document.querySelector(a.getAttribute('href'))});\n"
 "function on(){var y=window.scrollY+140,i=0;secs.forEach(function(s,k){if(s&&s.offsetTop<=y)i=k});links.forEach(function(a,k){a.classList.toggle('on',k===i)})}\n"
 "window.addEventListener('scroll',on,{passive:true});on();})();\n"
 '</script>')
out = os.path.join('..', 'orderorange-partner-guide.html')
open(out, 'w', encoding='utf-8').write(page)
print('written', round(len(page) / 1e6, 2), 'MB,', count, 'screens')
