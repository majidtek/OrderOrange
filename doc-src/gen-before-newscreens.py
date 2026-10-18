import base64, io, glob, html, os
from PIL import Image
os.chdir(os.path.dirname(os.path.abspath(__file__)))
img = {}
for f in sorted(glob.glob('frame_*.png')):
    im = Image.open(f).convert('RGB')
    if im.width > 1000:
        im = im.resize((1000, int(im.height * 1000 / im.width)), Image.LANCZOS)
    b = io.BytesIO(); im.save(b, 'JPEG', quality=80, optimize=True)
    img[f[:-4]] = (base64.b64encode(b.getvalue()).decode(), im.width, im.height)
F = lambda k: [x for x in img if x.startswith('frame_' + k)][0]

parts = [
 ('around', 'Getting around', [
  (F('0000_'), 'Dashboard', 'The home screen. The strip at the top shows today at a glance: new orders waiting, orders being prepared, orders today and revenue today with the change against yesterday. Below it, <b>Needs your attention</b> lists orders that need a decision, <b>Quick actions</b> jumps to any page, and the week chart shows revenue per day.'),
  (F('0005_'), 'The menu', 'Press the ☰ button or Ctrl+K to open the full menu. Pages are grouped into Operations, Basic information, Reports, Settings and User management. Type in the search box to jump straight to a page, a product or a customer.'),
 ]),
 ('sell', 'Selling', [
  (F('0010_'), 'POS', 'Ring up a sale in seconds. Pick the order type (in person, take-away, delivery), choose a table or a walk-in customer, tap dishes on the left and they land in the ticket on the right with quantities, tax and total. <b>Save as open</b> keeps the table running; <b>Close &amp; pay</b> settles it in cash or card and prints the receipt and the kitchen ticket.'),
  (F('0018_'), 'POS on a tablet', 'The same till laid out for a tablet held by a waiter: categories across the top, dishes in a grid, and a floating total that opens the ticket. Everything a waiter needs is one tap away.'),
  (F('0021_'), 'Tables', 'Your floor plan, room by room. Each table shows whether it is free, seated, ordered through the table QR code or waiting for the bill, with the time it has been occupied. Tap a table to open its running order.'),
  (F('0171_0171'), 'Order assistant and product printers', 'Type an order in plain words, for example "2 lamb ribs table 3", and the assistant adds it to that table. Behind it, <b>Product printers</b> decides which kitchen printer each dish goes to: kitchen, bar or none, while the cashier receipt always prints at the till.'),
 ]),
 ('orders', 'Orders and guests', [
  (F('0028_'), 'Live orders', 'The board refreshes on its own. New orders arrive on the left with the customer, the lines and any note; press <b>Accept</b> to send them to the kitchen or <b>Reject</b> to turn them down. Orders move from New to Cooking to Ready as the kitchen works.'),
  (F('0032_'), 'Take-out and dine-in board', 'Every accepted order with its table or pick-up details. Mark an order <b>Delivered</b> once it reaches the guest, assign a courier for delivery orders, or share a live location link with the customer.'),
  (F('0056_'), 'Delivery hand-over', 'Orders that leave the building. Each card shows the customer, the address and the courier status. <b>Print invoice</b> gives the rider a delivery note; <b>Mark ready</b> tells the customer the order is on its way.'),
  (F('0035_'), 'Reservations', 'Bookings from the website, the table QR code and phone calls in one list, by day and time, with guests, table and the note the guest left. Confirm, cancel, or press <b>Seat now</b> when the party arrives; the table on the floor plan turns occupied.'),
  (F('0143_'), 'Table chat', 'Guests who scan the table QR code can message the house. Every table with a conversation appears here with the latest message and an unread counter.'),
  (F('0146_'), 'Answering a table', 'Open a table to see the conversation. Reply with text, a photo or a voice note; the guest sees it on their phone. Typical use: "Can we have the bill please?" answered from the till without walking over.'),
 ]),
 ('menu', 'Your menu', [
  (F('0089_'), 'Products', 'Categories on top, dishes below, each with photo, price and availability. The counters show how many products are available, sold out or marked popular. Add a category or a product from the buttons at the top right.'),
  (F('0171_0172'), 'Editing a product', 'Up to four photos, a name in Arabic plus any other languages, a description, the category, price, discount, sort order and the ingredients that link it to your stock. Three options decide where it shows: <b>Available</b> (can be ordered now), <b>Popular</b> (starred on the menu) and <b>In-store only</b> (kept off the public web menu but still on the POS and the table QR menu). Days and hours limit when it can be ordered.'),
  (F('0082_'), 'Recipes', 'Link each dish to the materials one portion uses. The card shows the ingredient count, the cost of a portion and the margin against the selling price, so you know which dishes make money. Dishes without a recipe are marked so you can add them.'),
  (F('0038_'), 'Discount codes', 'Your own codes, valid only at your store. Each code has a rule: first order, a minimum number of items, a minimum spend, a number of orders this month, or happy-hour days and times. Set a cap per order, an end date and a usage limit, and switch a code on or off with one tap.'),
 ]),
 ('money', 'Money', [
  (F('0048_'), 'Bills and expenses', 'Rent, electricity, gas, internet, salaries and every other bill your store pays. The tiles show the total, what is unpaid and what is overdue; the list shows due dates and days late. Mark a bill paid, edit it, or set it to repeat monthly.'),
  (F('0065_'), 'Purchases', 'Receive stock from a supplier: pick the materials, enter quantity and cost per unit, and press <b>Receive stock</b>. Pay in cash or card now, or later in one or several installments. Templates save a regular order so a weekly delivery is two taps.'),
  (F('0070_'), 'Payments to suppliers', 'Everything still owed to suppliers, oldest first, with the installment step and how many days late. Press <b>Pay</b> on a line to settle it; the tiles above track unpaid, overdue and paid this month.'),
  (F('0074_'), 'Payroll', 'Run salaries for a month. Each team member shows their role, hours clocked and salary; pay one person or <b>Pay everyone</b>. The tiles show what is expected, paid and remaining.'),
  (F('0045_'), 'Contracts', 'Standing supply agreements for regular customers such as canteens and clubs: products per month, deliveries per month and the monthly amount. Email the contract, print it, or send a signing link.'),
 ]),
 ('team', 'Your team', [
  (F('0139_'), 'Users and roles', 'Give each team member their own login. Every person has a role: owner, manager, cashier, kitchen, waiter or one of your own roles, and only sees the pages that role opens. Switch a login off without deleting it.'),
  (F('0140_'), 'Roles and permissions', 'Exactly what each role can do, page by page: point of sale, live orders, cancelling orders, invoices, reports and so on. Adjust a preset or create your own role such as Accountant.'),
  (F('0142_'), 'Team activity', 'Who is online now, who is offline, their last login and the page each person is on. The history column opens the pages a person visited.'),
  (F('0167_'), 'Team chat', 'Private one-to-one messages between team members: text, photos, voice notes and location. Unread counts show on the contact list.'),
  (F('0077_'), 'Attendance', 'Staff press <b>I\'m here</b> when they arrive and again when they leave; the position is recorded with each press so the owner sees they clocked in at the shop. Leave requests (hourly or daily) go to the owner for approval and feed the payroll.'),
  (F('0042_'), 'Calendar', 'Meetings, deliveries, deadlines and notes the whole team sees, by month, week or day, with a reminder when it matters.'),
 ]),
 ('reports', 'Reports', [
  (F('0100_'), 'Daily report', 'One day, every invoice in full: pick the day, read the totals for invoices, revenue, items sold and cancellations, and open any invoice to see exactly what was on it. Export to Excel or print on A4.'),
  (F('0104_'), 'Product report', 'Which dishes sell and what each brings in, for today, seven days, thirty days, a year or any date range: dishes sold, revenue, distinct dishes, average price, the top five and a full table with the share of every product.'),
 ]),
 ('setup', 'Setup', [
  (F('0170_'), 'Store settings', 'Your profile and languages, opening hours, delivery terms, location on the map, contact details, the short web address of your store page, kitchen language for tickets and the photos shown to customers.'),
  (F('0109_'), 'Invoice designer', 'Craft the printed receipt: choose a template, upload the logo, set fonts and sizes, write the tagline, thank-you and promotion lines. The live preview on the right updates as you type.'),
  (F('0126_'), 'Receipt preview', 'The full designer with the receipt rendered at 80, 100 or 125 percent, including the QR code guests scan to verify the bill.'),
 ]),
]

def sec(id, title, items):
    h = f'<section id="{id}"><h2>{html.escape(title)}</h2>'
    for k, t, d in items:
        b, w, hh = img[k]
        h += (f'<article><h3>{html.escape(t)}</h3><p>{d}</p>'
              f'<figure><img src="data:image/jpeg;base64,{b}" width="{w}" height="{hh}" alt="{html.escape(t)}" loading="lazy"></figure></article>')
    return h + '</section>'

toc = ''.join(f'<a href="#{i}">{html.escape(t)}</a>' for i, t, _ in parts)
body = ''.join(sec(i, t, items) for i, t, items in parts)
count = sum(len(items) for _, _, items in parts)
css = """
:root{--bg:#FFFCF7;--ink:#221B14;--muted:#7A6E61;--line:#EADFD0;--panel:#FFF6EC;--accent:#FF5A00;--accent-deep:#C24500;--frame:#F3EBE1}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#16110C;--ink:#F4ECE2;--muted:#B3A595;--line:#332A20;--panel:#23190F;--accent:#FF7A2A;--accent-deep:#FFA36B;--frame:#2A2118}}
:root[data-theme="dark"]{--bg:#16110C;--ink:#F4ECE2;--muted:#B3A595;--line:#332A20;--panel:#23190F;--accent:#FF7A2A;--accent-deep:#FFA36B;--frame:#2A2118}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.55 "Source Sans 3",system-ui,"Segoe UI",Arial,sans-serif}
header{position:sticky;top:0;z-index:5;background:var(--bg);border-bottom:1px solid var(--line)}
.bar{max-width:960px;margin:0 auto;padding:10px 16px;display:flex;align-items:center;gap:12px}
.mark{width:30px;height:30px;border-radius:9px;background:var(--accent);display:grid;place-items:center;color:#fff;font:700 15px Sora,sans-serif;flex:none}
.bar b{font:600 15px Sora,sans-serif}
.bar span.site{color:var(--muted);font-size:13px;margin-inline-start:auto;white-space:nowrap}
nav{max-width:960px;margin:0 auto;padding:0 16px 10px;display:flex;gap:6px;overflow-x:auto;scrollbar-width:none}
nav::-webkit-scrollbar{display:none}
nav a{flex:none;font:600 12.5px/1 "Source Sans 3",sans-serif;letter-spacing:.2px;color:var(--ink);text-decoration:none;border:1px solid var(--line);border-radius:999px;padding:7px 12px;background:var(--panel)}
nav a:hover,nav a:focus-visible{border-color:var(--accent);color:var(--accent-deep);outline:none}
main{max-width:960px;margin:0 auto;padding:22px 16px 60px}
.lead{max-width:62ch;margin:0 0 6px}
.lead h1{font:700 clamp(26px,4.5vw,36px)/1.1 Sora,sans-serif;margin:0 0 10px;text-wrap:balance;letter-spacing:-.3px}
.lead p{color:var(--muted);margin:0;font-size:17px}
.lead .meta{display:flex;gap:14px;flex-wrap:wrap;margin-top:14px;font-size:13px;color:var(--muted)}
.lead .meta i{font-style:normal;color:var(--accent-deep);font-weight:600}
section{margin-top:38px;scroll-margin-top:96px}
h2{font:700 clamp(20px,3vw,24px)/1.2 Sora,sans-serif;margin:0 0 6px;padding-bottom:8px;border-bottom:2px solid var(--accent);display:inline-block}
article{margin-top:22px}
h3{font:600 18px/1.25 Sora,sans-serif;margin:0 0 6px;text-wrap:balance}
article p{margin:0 0 12px;max-width:68ch}
article p b{font-weight:600;color:var(--accent-deep)}
figure{margin:0;background:var(--frame);border:1px solid var(--line);border-radius:14px;padding:8px}
figure img{display:block;width:100%;height:auto;border-radius:8px}
footer{max-width:960px;margin:0 auto;padding:0 16px 40px;color:var(--muted);font-size:13px}
@media (prefers-reduced-motion:no-preference){html{scroll-behavior:smooth}}
"""
page = f'''<title>OrderOrange Partner Guide</title>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Sora:wght@600;700&family=Source+Sans+3:ital,wght@0,400;0,600;1,400&display=swap">
<style>{css}</style>
<header><div class="bar"><span class="mark">O</span><b>OrderOrange Partner Guide</b><span class="site">partner.orderorange.com</span></div>
<nav>{toc}</nav></header>
<main>
<div class="lead"><h1>Run your restaurant from one screen</h1>
<p>What every page of the OrderOrange partner app does, in the order you will meet them: selling, orders and guests, your menu, money, your team, reports and setup. Screens are from the Saffron Grill House demo store.</p>
<div class="meta"><span><i>{count}</i> screens</span><span><i>{len(parts)}</i> sections</span><span>Works on phone, tablet and desktop</span></div></div>
{body}
</main>
<footer>OrderOrange · Partner app guide · September 2026. Numbers in the screens are demo data.</footer>'''
out = os.path.join('..', 'orderorange-partner-guide.html')
open(out, 'w', encoding='utf-8').write(page)
print('written', out, round(len(page) / 1e6, 2), 'MB,', count, 'screens')
