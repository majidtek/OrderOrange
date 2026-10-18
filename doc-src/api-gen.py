# -*- coding: utf-8 -*-
"""Generates the OrderOrange developer API reference → RestaurantWeb/wwwroot/api.html.
Every example was run against the staging API on 2026-09-18; the responses are real."""
import io, os, html, json
os.chdir(os.path.dirname(os.path.abspath(__file__)))
WWW = r'D:\OrderOrange\OrderOrange.RestaurantWeb\wwwroot'
URL = 'https://partner.orderorange.com/api.html'
TITLE = 'OrderOrange API — REST reference for restaurant, CRM and accounting integrations'
DESC = ('The OrderOrange REST API: sign in with a Bearer token, read and write menus, orders, tables, reservations, '
        'customers, bills and reports of your restaurant, and connect your CRM or accounting software. JSON over HTTPS, verified examples in curl, JavaScript and C#.')

def e(s): return html.escape(s, quote=True)
def code(lang, text, label=None):
    return (f'<div class="code"><div class="code-h"><span>{e(label or lang)}</span><button type="button" class="copy" data-copy>Copy</button></div>'
            f'<pre><code class="lang-{lang}">{e(text.strip())}</code></pre></div>')
def tabs(items):
    """items: [(label, html)] → a tab strip."""
    h = '<div class="tabs"><div class="tab-h">' + ''.join(f'<button type="button" class="tab{" on" if i == 0 else ""}" data-tab="{i}">{e(l)}</button>' for i, (l, _) in enumerate(items)) + '</div>'
    h += ''.join(f'<div class="tab-p{" on" if i == 0 else ""}" data-pane="{i}">{c}</div>' for i, (_, c) in enumerate(items))
    return h + '</div>'
def table(rows, head=('Method', 'Route', 'Needs', 'Sends', 'Returns')):
    h = '<div class="tbl"><table><thead><tr>' + ''.join(f'<th>{e(x)}</th>' for x in head) + '</tr></thead><tbody>'
    for r in rows:
        cells = []
        for i, c in enumerate(r):
            if i == 0: cells.append(f'<td><span class="m {c.lower()}">{e(c)}</span></td>')
            elif i == 1: cells.append(f'<td><code>{e(c)}</code></td>')
            else: cells.append(f'<td>{c}</td>')
        h += '<tr>' + ''.join(cells) + '</tr>'
    return h + '</tbody></table></div>'
def fields(rows):
    return '<div class="tbl"><table class="f"><thead><tr><th>Field</th><th>Type</th><th>Meaning</th></tr></thead><tbody>' + ''.join(
        f'<tr><td><code>{e(n)}</code></td><td>{e(t)}</td><td>{m}</td></tr>' for n, t, m in rows) + '</tbody></table></div>'

OWNER = '<span class="tag">token · owner</span>'
def perm(p): return f'<span class="tag">token · {e(p)}</span>'
PUB = '<span class="tag pub">no token</span>'
ANY = '<span class="tag">token</span>'

# ------------------------------------------------------------------ examples (verified on staging 2026-09-18)
LOGIN_CURL = '''curl -X POST https://api.orderorange.com/api/auth/login \\
  -H "Content-Type: application/json" \\
  -d '{"email":"owner-login","password":"your-password","remember":true}'
'''
LOGIN_RES = '''{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9…",
  "userId": 10001199,
  "fullName": "Saffron Grill House",
  "email": "owner-login",
  "role": 1,
  "restaurantId": 5000209,
  "restaurantName": "Saffron Grill House",
  "storeRole": "owner",
  "perms": null
}'''
GET_CURL = '''curl https://api.orderorange.com/api/restaurants/mine \\
  -H "Authorization: Bearer $TOKEN"
'''
GET_RES = '''{
  "id": 5000209, "name": "Saffron Grill House", "cuisine": "Arabic & Grill",
  "area": "Azaiba", "phone": "+968 …", "isOpen": true, "isApproved": true,
  "storeType": 0, "allowsPickup": true, "taxPercent": 5.000,
  "deliveryFee": 0.000, "minOrder": 0.000, "avgPrepMinutes": 20,
  "slug": "siraj", "names": { "en": "Saffron Grill House", "ar": "سافرون جريل هاوس" }
}'''
POST_CURL = '''curl -X POST https://api.orderorange.com/api/orders/counter \\
  -H "Authorization: Bearer $TOKEN" \\
  -H "Content-Type: application/json" \\
  -d '{
    "storeCustomerId": 36,
    "paymentMethod": 0,
    "items": [ { "menuItemId": 15722, "quantity": 2, "notes": "cold please" } ],
    "counterSale": true
  }'
'''
POST_RES = '''{
  "id": 754259, "number": "MF-755259", "status": 0,
  "placedAt": "2026-09-18T01:05:48", "customerName": "Jack Miller",
  "orderType": 1, "paymentMethod": 0, "isPaid": false,
  "subtotal": 0.200, "taxPercent": 5.0, "taxAmount": 0.010, "total": 0.210,
  "items": [ { "id": 1008752, "name": "Water", "unitPrice": 0.100, "quantity": 2, "notes": "cold please", "lineTotal": 0.200 } ],
  "history": [ { "status": 0, "at": "2026-09-18T01:05:48", "by": "Saffron Grill House" } ]
}'''
JS_QUICK = '''const API = "https://api.orderorange.com";

// 1. sign in once, keep the token (12 h, or 30 days with remember: true)
const login = await fetch(`${API}/api/auth/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ email: "owner-login", password: "your-password", remember: true })
}).then(r => r.json());
const auth = { Authorization: `Bearer ${login.token}` };

// 2. read
const me = await fetch(`${API}/api/restaurants/mine`, { headers: auth }).then(r => r.json());
console.log(me.name, me.isOpen);

// 3. write — a counter sale of two waters
const order = await fetch(`${API}/api/orders/counter`, {
  method: "POST",
  headers: { ...auth, "Content-Type": "application/json" },
  body: JSON.stringify({ storeCustomerId: 0, paymentMethod: 0, counterSale: true,
                         items: [{ menuItemId: 15722, quantity: 2 }] })
}).then(r => r.json());
console.log(order.number, order.total);
'''
CS_QUICK = '''using System.Net.Http.Headers;
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("https://api.orderorange.com/") };

// 1. sign in
var login = await http.PostAsJsonAsync("api/auth/login",
    new { email = "owner-login", password = "your-password", remember = true });
var session = await login.Content.ReadFromJsonAsync<LoginResponse>();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);

// 2. read
var me = await http.GetFromJsonAsync<MyRestaurant>("api/restaurants/mine");
Console.WriteLine($"{me!.Name} open={me.IsOpen}");

// 3. write — accept the first order on the live board
var board = await http.GetFromJsonAsync<List<Order>>("api/orders/restaurant/board");
if (board is { Count: > 0 })
    (await http.PostAsync($"api/orders/{board[0].Id}/accept", null)).EnsureSuccessStatusCode();

record LoginResponse(string Token, int UserId, string FullName, int? RestaurantId, string? StoreRole);
record MyRestaurant(int Id, string Name, bool IsOpen, decimal TaxPercent);
record Order(int Id, string Number, int Status, decimal Total);
'''
PY_QUICK = '''import requests
API = "https://api.orderorange.com"
s = requests.Session()
tok = s.post(f"{API}/api/auth/login", json={"email": "owner-login", "password": "your-password", "remember": True}).json()["token"]
s.headers["Authorization"] = f"Bearer {tok}"

me = s.get(f"{API}/api/restaurants/mine").json()
print(me["name"], me["isOpen"])

# every invoice of yesterday, ready for your accounting package
day = s.get(f"{API}/api/invoices/daily", params={"date": "2026-09-17"}).json()
for row in day["rows"]:
    print(row["number"], row["total"], row["isPaid"])
'''
ITEM_POST = '''curl -X POST https://api.orderorange.com/api/menu/items \\
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \\
  -d '{
    "categoryId": 5771,
    "name": "Mixed Grill Plate",
    "description": "Chargrilled to order",
    "price": 4.5,
    "imageEmoji": "🍖",
    "isAvailable": true,
    "isPopular": true,
    "names": { "en": "Mixed Grill Plate", "ar": "طبق مشاوي مشكلة", "fa": "بشقاب گریل مخلوط" },
    "availableDays": "fri,sat",
    "availableFromMinutes": 720,
    "availableToMinutes": 1380,
    "leadTimeDays": 0
  }'
'''
ITEM_RES = '''{
  "id": 15724, "categoryId": 5771, "name": "Mixed Grill Plate", "price": 4.5,
  "isAvailable": true, "isPopular": true, "status": 0,
  "availableDays": "fri,sat", "availableFromMinutes": 720, "availableToMinutes": 1380
}'''
ACCOUNTING_PY = '''# Nightly: push yesterday's invoices into the accounting system (Zoho Books, QuickBooks, Odoo, Xero…)
import requests, datetime
API = "https://api.orderorange.com"
s = requests.Session()
s.headers["Authorization"] = "Bearer " + s.post(f"{API}/api/auth/login",
    json={"email": "sync-user", "password": "…", "remember": True}).json()["token"]

day = (datetime.date.today() - datetime.timedelta(days=1)).isoformat()
report = s.get(f"{API}/api/invoices/daily", params={"date": day}).json()
for row in report["rows"]:                       # one row per invoice
    inv = s.get(f"{API}/api/invoices/{row['id']}").json()   # lines, tax, payment
    accounting.create_invoice(
        number   = inv["number"],                # MF-755259 — use it as the idempotency key
        date     = inv["placedAt"][:10],
        customer = inv["customerName"] or "Walk-in",
        lines    = [(l["name"], l["quantity"], l["unitPrice"]) for l in inv["items"]],
        tax_rate = inv["taxPercent"],            # Oman VAT, 5 %
        total    = inv["total"],
        paid     = inv["isPaid"],
    )
# Cancelled and edited invoices have their own feeds:
#   GET /api/invoices/cancelled   GET /api/invoices/edits
'''
CRM_JS = '''// Keep the CRM in step with the store's customer list, both ways.
const API = "https://api.orderorange.com";
const auth = { Authorization: `Bearer ${token}` };

// OrderOrange → CRM: every customer, with order count and last visit
const customers = await fetch(`${API}/api/storecustomers?take=1000`, { headers: auth }).then(r => r.json());
for (const c of customers)
  await crm.upsertContact({ externalId: `oo-${c.id}`, name: c.name, phone: c.phone,
                            address: c.address, orders: c.orderCount, lastOrderAt: c.lastOrderAt });

// CRM → OrderOrange: a new lead becomes a store customer
await fetch(`${API}/api/storecustomers`, {
  method: "POST", headers: { ...auth, "Content-Type": "application/json" },
  body: JSON.stringify({ name: lead.name, phone: lead.phone, address: lead.address, notes: "From CRM" })
});
'''
KDS_JS = '''// A kitchen screen or an ERP that wants every new order: poll the board.
let seen = new Set();
setInterval(async () => {
  const board = await fetch(`${API}/api/orders/restaurant/board`, { headers: auth }).then(r => r.json());
  for (const o of board.filter(o => o.status === 0 && !seen.has(o.id))) {   // 0 = Pending
    seen.add(o.id);
    await erp.createSalesOrder(o);                                            // your side
    await fetch(`${API}/api/orders/${o.id}/accept`, { method: "POST", headers: auth });
  }
}, 5000);
'''

# ------------------------------------------------------------------ endpoint reference
REF = [
 ('restaurant', 'Your restaurant', 'Profile, hours, photos and the numbers of the store the token belongs to.', [
   ('GET', 'api/restaurants/mine', OWNER, '—', 'MyRestaurant'),
   ('PUT', 'api/restaurants/mine', perm('settings'), 'UpdateRestaurantRequest', '200'),
   ('GET', 'api/restaurants/my-stores', OWNER, '—', 'StoreSummary[] — every business this login can work in'),
   ('POST', 'api/auth/switch-store/{restaurantId}', OWNER, '—', 'LoginResponse with a token for that store'),
   ('GET', 'api/restaurants/mine/hours', OWNER, '—', 'DayHours[]'),
   ('PUT', 'api/restaurants/mine/hours', OWNER, 'DayHours[]', 'DayHours[]'),
   ('POST', 'api/restaurants/mine/toggle-open', OWNER, '—', '200 — flips open/closed'),
   ('PUT', 'api/restaurants/mine/logo', OWNER, '{ "data": "data:image/png;base64,…" }', '200'),
   ('GET', 'api/restaurants/mine/photos', OWNER, '—', 'RestaurantPhoto[]'),
   ('POST', 'api/restaurants/mine/photos', OWNER, 'AddStorePhotoRequest', '200'),
   ('GET', 'api/restaurants/mine/report/sales?period=week', perm('reports'), 'period = day · week · month, or from & to', 'SalesSummary'),
   ('GET', 'api/restaurants/mine/report/products?period=month', perm('reports'), 'search, skip, take', 'ProductSalesPage'),
   ('GET', 'api/restaurants/mine/report/customers?period=month', perm('reports'), 'search, skip, take', 'CustomerSalesPage'),
   ('GET', 'api/restaurants/mine/visits?period=week', perm('reports'), '—', 'StoreVisitStats'),
 ]),
 ('menu', 'Menu', 'Categories and products. New products start as <em>pending</em> until OrderOrange approves them; edits to approved products apply at once.', [
   ('GET', 'api/menu', perm('menu.view'), '?light=true drops photos · ?basic=true ids, names, prices only · ?lang=ar pre-translated names', 'MenuCategory[] with items'),
   ('GET', 'api/menu/categories/{id}/items', perm('menu.view'), '—', 'MenuItem[]'),
   ('POST', 'api/menu/categories', perm('menu'), '{ "name", "sortOrder", "names": { "en", "ar", … } }', '204'),
   ('PUT', 'api/menu/categories/{id}', perm('menu'), 'same body', '204'),
   ('DELETE', 'api/menu/categories/{id}', perm('menu'), '—', '204'),
   ('POST', 'api/menu/items', perm('menu'), 'SaveMenuItemRequest', 'MenuItem (status 0 = pending approval)'),
   ('PUT', 'api/menu/items/{id}', perm('menu'), 'SaveMenuItemRequest — replaces every field', '204'),
   ('POST', 'api/menu/items/{id}/toggle-available', perm('menu'), '—', '204'),
   ('DELETE', 'api/menu/items/{id}', perm('menu'), '—', '204'),
   ('GET', 'api/menu/items/{id}/photos', perm('menu.view'), '—', 'DishPhoto[]'),
 ]),
 ('orders', 'Orders', 'The live board, the history and every status change. Status numbers are listed under <a href="#objects">Objects</a>.', [
   ('GET', 'api/orders/restaurant/board', OWNER, '—', 'Order[] — everything not yet finished (the live board)'),
   ('GET', 'api/orders/restaurant/history?skip=0&take=25', OWNER, '—', 'Order[] newest first'),
   ('GET', 'api/orders/{id}', ANY, '—', 'Order'),
   ('POST', 'api/orders/counter', OWNER, 'PlaceCounterOrderRequest', 'Order — a sale made at the counter, a table or from your own system'),
   ('POST', 'api/orders/{id}/accept', OWNER, '—', '204 · Pending → Accepted'),
   ('POST', 'api/orders/{id}/preparing', OWNER, '—', '204 · Accepted → Preparing'),
   ('POST', 'api/orders/{id}/ready', OWNER, '—', '204 · Preparing → Ready'),
   ('POST', 'api/orders/{id}/paid', OWNER, '—', '204 · marks the bill paid'),
   ('POST', 'api/orders/{id}/reject', OWNER, '{ "reason": "Out of stock" }', '204 · the guest sees the reason'),
   ('POST', 'api/orders/{id}/cancel', ANY, '{ "reason": "…" }', '204'),
   ('GET', 'api/orders/restaurant/deliveries', OWNER, '—', 'Order[] — deliveries your own people take out'),
   ('POST', 'api/orders/{id}/courier', OWNER, '{ "name", "phone" }', '204 · assigns your courier'),
   ('POST', 'api/orders/{id}/self-delivered', OWNER, '—', '204 · Delivered'),
   ('GET', 'api/orders/restaurant/week-revenue', OWNER, '—', 'DayRevenue[] — the dashboard chart'),
 ]),
 ('invoices', 'Invoices and daily report', 'What accounting needs: every invoice with its lines, plus the cancelled and edited ones.', [
   ('GET', 'api/invoices?days=7&search=&skip=0&take=50', OWNER, '—', 'InvoiceRow[] (number, placedAt, total, status, customerName, isPaid)'),
   ('GET', 'api/invoices/{id}', OWNER, '—', 'InvoiceDetail — lines, tax, payment, history'),
   ('GET', 'api/invoices/daily?date=2026-09-17', perm('reports'), '—', '{ summary, rows[] } — the Daily report'),
   ('GET', 'api/invoices/cancelled', perm('reports'), '—', 'CancelledInvoice[] with who and why'),
   ('GET', 'api/invoices/edits', perm('reports'), '—', 'InvoiceEdit[] — replaced invoices, old and new number'),
   ('POST', 'api/invoices/{id}/cancel', perm('invoice.cancel'), '{ "reason" }', '204'),
   ('POST', 'api/export/xlsx', ANY, 'ExportSheetRequest (title, columns, rows ≤ 20 000)', '.xlsx file — the same Excel button every report has'),
 ]),
 ('tables', 'Tables, rooms and tabs', 'The floor plan and the running order of each table.', [
   ('GET', 'api/storetables', perm('tables.view'), '—', 'StoreTable[] with state (free, seated, QR order, bill requested)'),
   ('POST', 'api/storetables', perm('tables'), 'SaveStoreTableRequest', 'StoreTable'),
   ('PUT', 'api/storetables/{id}', perm('tables'), 'SaveStoreTableRequest', 'StoreTable'),
   ('POST', 'api/storetables/{id}/seat', perm('tables'), '{ "guests", "name" }', 'StoreTable'),
   ('POST', 'api/storetables/{id}/clear', perm('tables'), '—', 'StoreTable'),
   ('GET', 'api/storetables/qrcodes', perm('tables.view'), '—', 'TableQr[] — the printable codes'),
   ('GET', 'api/storerooms', perm('tables.view'), '—', 'StoreRoom[]'),
   ('GET', 'api/storetabs', OWNER, '—', 'StoreTab[] — open tabs'),
   ('POST', 'api/storetabs/open', OWNER, 'OpenTabRequest', 'StoreTab'),
   ('POST', 'api/storetabs/{id}/lines', OWNER, 'AddTabLinesRequest', 'StoreTab'),
   ('POST', 'api/storetabs/{id}/close', OWNER, 'CloseTabRequest (payment)', 'Order — the settled bill'),
 ]),
 ('reservations', 'Reservations', 'Bookings from the store page, the table QR or your own desk.', [
   ('GET', 'api/reservations?history=false', perm('reservations'), 'history=true for past and cancelled', 'Reservation[]'),
   ('POST', 'api/reservations', perm('reservations'), '{ "tableId", "name", "guests", "at", "note" }', 'Reservation'),
   ('POST', 'api/reservations/{id}/status', perm('reservations'), '{ "status": "confirmed" } · pending · confirmed · cancelled · seated', 'Reservation'),
   ('GET', 'api/restaurants/{storeId}/floor', PUB, '—', 'PublicSalon[] — tables a guest may book'),
   ('POST', 'api/restaurants/{storeId}/reserve', PUB, 'PublicReserveRequest', 'Reservation'),
 ]),
 ('customers', 'Customers', 'The store\u2019s own customer cards, the ones a CRM wants.', [
   ('GET', 'api/storecustomers?search=&take=100', perm('customers.view'), '—', 'StoreCustomer[]'),
   ('GET', 'api/storecustomers/{id}', perm('customers.view'), '—', 'StoreCustomer'),
   ('POST', 'api/storecustomers', perm('customers'), '{ "name", "phone", "address", "notes", "lat", "lng" }', 'StoreCustomer'),
   ('PUT', 'api/storecustomers/{id}', perm('customers'), 'same body', 'StoreCustomer'),
   ('DELETE', 'api/storecustomers/{id}', perm('customers'), '—', '200'),
 ]),
 ('team', 'Team, staff, attendance, payroll', 'Logins and roles, employees, clock-ins and salaries.', [
   ('GET', 'api/team', perm('team'), '—', 'StoreMember[] — logins and their roles'),
   ('POST', 'api/team', perm('team'), 'SaveStoreMemberRequest', 'StoreMember'),
   ('GET', 'api/team/roles', perm('team'), '—', 'StoreRoleDef[] — custom roles and their permissions'),
   ('GET', 'api/staff?status=active', perm('staff'), '—', 'StaffPage — employees, salaries, documents'),
   ('GET', 'api/staff/payroll?year=2026&month=9', perm('payroll'), '—', 'Payroll'),
   ('POST', 'api/staff/payroll/pay', perm('payroll'), 'PaySalaryRequest', '200'),
   ('GET', 'api/staff/salary-report?from=&to=', perm('payroll'), '—', 'SalaryReport'),
   ('GET', 'api/attendance/me', ANY, '—', 'MyAttendance'),
   ('POST', 'api/attendance/in', ANY, '{ "lat", "lng", "accuracy" }', 'MyAttendance'),
   ('POST', 'api/attendance/out', ANY, 'same', 'MyAttendance'),
   ('GET', 'api/attendance/report?from=&to=&userId=', perm('staff'), '—', 'AttendanceReport'),
 ]),
 ('money', 'Bills, purchases, stock, contracts, discounts', 'The back office.', [
   ('GET', 'api/bills?status=all&category=all', perm('bills'), '—', 'StoreBillPage'),
   ('POST', 'api/bills', perm('bills'), 'SaveStoreBillRequest', '200'),
   ('GET', 'api/bills/report?from=&to=', perm('bills'), '—', 'BillReport — by category, month, company'),
   ('GET', 'api/materials?category=all', perm('inventory'), '—', 'StoreMaterials — raw materials and stock'),
   ('GET', 'api/materials/purchases?skip=0&take=30', perm('inventory'), '—', 'MaterialPurchasePage — supplier invoices'),
   ('POST', 'api/materials/purchases', perm('inventory'), 'SavePurchaseRequest', 'MaterialPurchase'),
   ('GET', 'api/materials/suppliers', perm('inventory'), '—', 'Supplier[]'),
   ('GET', 'api/materials/consumption?period=week', perm('inventory'), '—', 'ConsumptionReport'),
   ('GET', 'api/materials/recipes', perm('inventory'), '—', 'AllRecipes'),
   ('GET', 'api/contracts', perm('contracts'), '—', 'Contract[]'),
   ('POST', 'api/contracts', perm('contracts'), 'SaveContractRequest', 'Contract'),
   ('POST', 'api/contracts/{id}/email', perm('contracts'), '—', 'Contract — emails it to the customer'),
   ('GET', 'api/promotions', perm('discounts'), '—', 'PromotionPage'),
   ('POST', 'api/promotions', perm('discounts'), 'SavePromotionRequest', 'Promotion'),
   ('POST', 'api/promotions/check', PUB, '{ "restaurantId", "code", "subtotal", "itemCount", "orderType" }', 'PromotionCheck — what a code is worth'),
 ]),
 ('guests', 'Surveys, support, chat', 'What guests say, and the line to OrderOrange.', [
   ('GET', 'api/surveys', perm('surveys'), '—', 'Survey[]'),
   ('GET', 'api/surveys/{id}/results', perm('surveys'), '—', 'SurveyResults'),
   ('GET', 'api/tablechats', OWNER, '—', 'open table conversations'),
   ('POST', 'api/tablechats/{tableId}', OWNER, 'SendTableChatRequest', 'TableChatMessage'),
   ('GET', 'api/support/tickets?status=all', perm('support'), '—', 'SupportPage'),
   ('POST', 'api/support/tickets', perm('support'), 'CreateTicketRequest', 'SupportTicketDetail'),
 ]),
 ('public', 'Public, no token', 'What the customer app reads. Useful for your own website or app.', [
   ('GET', 'api/restaurants/{id}', PUB, '—', '{ info, categories[] } — the store and its whole menu'),
   ('GET', 'api/restaurants/by-slug/{slug}', PUB, '—', '{ id }'),
   ('GET', 'api/restaurants?search=&cuisineId=', PUB, '—', 'RestaurantCard[]'),
   ('GET', 'api/restaurants/search?query=kebab', PUB, '—', '{ restaurants[], dishes[], didYouMean }'),
   ('GET', 'api/restaurants/{id}/reviews', PUB, '—', 'Review[]'),
   ('GET', 'api/media/dish/{id} · api/media/logo/{id}', PUB, '—', 'image bytes'),
   ('GET', 'api/orders/verify/{code}', PUB, '—', 'PublicBill — the QR on a printed receipt'),
   ('POST', 'api/contact', PUB, '{ "email", "phone", "title", "text" }', '{ "ok": true } · 100 per day'),
 ]),
]

OBJECTS = [
 ('MenuItem', 'GET api/menu · POST/PUT api/menu/items', [
   ('id, categoryId', 'int', 'Ids are stable; use them as your external keys.'),
   ('name, description', 'string', 'The canonical text. Prefer <code>names</code> / <code>descriptions</code> below.'),
   ('names, descriptions', 'object', 'Per language: <code>{ "en": "…", "ar": "…", "fa": "…" }</code>. Fourteen codes: en ar fa ur hi de es fr it ja pt ru tr zh.'),
   ('price', 'decimal', 'OMR with three decimals, e.g. <code>4.500</code>.'),
   ('discountPercent', 'decimal', '0 to 100. <code>finalPrice</code> is computed by the apps.'),
   ('isAvailable', 'bool', 'Sold out or not, today.'),
   ('isPopular, inStoreOnly', 'bool', 'Popular badge · hide from the public menu but keep on POS and table QR.'),
   ('availableDays', 'string', 'Comma list of weekdays, lower-case three letters: <code>"fri,sat"</code>. Empty = every day.'),
   ('availableFromMinutes, availableToMinutes', 'int?', 'Minutes after midnight in the store\u2019s clock: 720 = 12:00, 1380 = 23:00. Null = all day.'),
   ('leadTimeDays', 'int', 'Must be ordered this many days ahead (cakes, catering).'),
   ('status', 'int', '0 pending approval · 1 approved · 2 rejected. Only approved items can be ordered.'),
   ('photo, photos, mainPhoto', 'string / string[] / int', 'Data URLs on write; on read the customer API returns <code>api/media/…</code> URLs.'),
   ('ingredients, unit, sortOrder', 'string, string, int', 'Recipe link to raw materials; unit of sale; position in the category.'),
 ]),
 ('Order', 'GET api/orders/… · POST api/orders/counter', [
   ('id, number', 'int, string', '<code>number</code> is what the guest and the receipt show, e.g. <code>MF-755259</code>. Use it as the idempotency key in accounting.'),
   ('status', 'int', '0 Pending · 1 Accepted · 2 Preparing · 3 Ready · 4 PickedUp · 5 OnTheWay · 6 Delivered · 7 Cancelled · 8 Rejected'),
   ('orderType', 'int', '0 Delivery · 1 Pickup (also dine-in and counter sales; see <code>tableName</code>)'),
   ('paymentMethod', 'int', '0 Cash · 1 Card · 2 Card online'),
   ('placedAt, scheduledFor', 'datetime', 'Store local time (Oman, UTC+4), ISO 8601.'),
   ('customerId, customerName, customerPhone', 'int, string', 'The buyer; <code>Walk-in</code> for anonymous counter sales.'),
   ('items[]', 'array', '<code>{ id, name, unitPrice, quantity, notes, lineTotal }</code>'),
   ('subtotal, discount, deliveryFee, taxPercent, taxAmount, total', 'decimal', 'Money in OMR. Tax is Oman VAT, 5 % by default.'),
   ('isPaid, paymentRef', 'bool, string?', 'Settled or not, and the reference from the till.'),
   ('tableName', 'string?', 'Set for dine-in and table QR orders.'),
   ('history[]', 'array', '<code>{ status, at, by }</code> — every status change, who did it.'),
   ('deliveryAddress, deliveryLat, deliveryLng, courierName, courierPhone', '…', 'Delivery details when it is one.'),
 ]),
 ('PlaceCounterOrderRequest', 'POST api/orders/counter', [
   ('items[]', 'array', '<code>{ menuItemId, quantity, notes? }</code> — approved, available items only.'),
   ('storeCustomerId', 'int', 'A customer card id, or <code>0</code> for a walk-in.'),
   ('paymentMethod', 'int', '0 cash · 1 card · 2 card online'),
   ('markPaid', 'bool', 'Settle it immediately.'),
   ('tableId', 'int?', 'Makes it a dine-in order on that table.'),
   ('counterSale', 'bool', 'Sold across the counter, nothing delivered.'),
   ('placedAt', 'datetime?', 'Back-date up to 31 days (imports); never in the future.'),
   ('notes, addressOverride', 'string?', 'Free text; a one-off delivery address.'),
 ]),
 ('Reservation', 'GET/POST api/reservations', [
   ('id, tableId, tableName, tableIds', 'int, int, string, string', 'One table, or a comma list when a party holds several.'),
   ('name, phone, guests', 'string, string, int', 'Who and how many.'),
   ('at, durationMinutes', 'datetime, int', 'Start and length (default 90).'),
   ('status', 'string', '<code>pending</code> · <code>confirmed</code> · <code>cancelled</code> · <code>seated</code> — seating opens a tab on the table.'),
   ('price', 'decimal', 'Quoted booking price, frozen at booking time.'),
 ]),
 ('StoreCustomer', 'GET/POST api/storecustomers', [
   ('id, name, phone, address, notes', '…', 'The card as the store keeps it.'),
   ('orderCount, lastOrderAt', 'int, datetime?', 'Kept by OrderOrange as orders come in.'),
   ('lat, lng', 'double?', 'Pinned delivery location.'),
 ]),
 ('MyRestaurant', 'GET/PUT api/restaurants/mine', [
   ('id, name, names, slug', '…', 'Public address is <code>orderorange.com/&lt;slug&gt;</code>.'),
   ('isOpen, isApproved', 'bool', 'Open now · listed to customers.'),
   ('taxPercent, deliveryFee, minOrder, avgPrepMinutes', 'decimal / int', 'Checkout rules.'),
   ('allowsPickup, selfDelivery, onlineReservations', 'bool', 'What the store offers.'),
   ('cuisineId, cuisineIds, storeType', 'int', 'storeType: 0 restaurant · 1 grocery · 2 pharmacy · 3 flowers · 4 shop'),
   ('phone, whatsapp, email, website, instagram, area, street, lat, lng', '…', 'Contact and place.'),
 ]),
]

PERMS = 'pos · orders · orders.cancel · tables · tables.view · chat.guest · chat.team · menu · menu.view · inventory · inventory.view · suppliers · suppliers.view · invoice.cancel · invoice.edit · reports · bills · payments · payroll · discounts · contracts · reservations · deliveries · customers · customers.view · staff · settings · team · surveys · calendar · support'

# ------------------------------------------------------------------ page
CSS = r'''
:root{--bg:#FFFFFF;--bg2:#FBF8F3;--sur:#FFFFFF;--ink:#15110D;--ink2:#4F463D;--mut:#8E8274;--line:#EEE6DB;--acc:#FF5A00;--accd:#C44300;--code:#1C1712;--codei:#F3E8DA;--ok:#1B8A4C;--get:#1B8A4C;--post:#C44300;--put:#7C3AED;--del:#B91C1C;--shadow:0 24px 60px rgba(70,35,5,.10)}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]){--bg:#15110D;--bg2:#1E1813;--sur:#1E1813;--ink:#F7F1E8;--ink2:#D6CCBE;--mut:#9A8E7F;--line:#33291F;--acc:#FF7A2A;--accd:#FF9A4D;--code:#0F0C09;--codei:#F3E8DA;--shadow:0 24px 60px rgba(0,0,0,.4)}}
:root[data-theme="dark"]{--bg:#15110D;--bg2:#1E1813;--sur:#1E1813;--ink:#F7F1E8;--ink2:#D6CCBE;--mut:#9A8E7F;--line:#33291F;--acc:#FF7A2A;--accd:#FF9A4D;--code:#0F0C09;--codei:#F3E8DA;--shadow:0 24px 60px rgba(0,0,0,.4)}
*{box-sizing:border-box}html{scroll-behavior:smooth;scroll-padding-top:84px}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 Manrope,system-ui,"Segoe UI",Arial,sans-serif;-webkit-font-smoothing:antialiased}
a{color:var(--accd)}code,pre{font-family:"JetBrains Mono",Consolas,"Cascadia Mono",Menlo,monospace}
.top{position:sticky;top:0;z-index:5;background:color-mix(in srgb,var(--bg) 80%,transparent);backdrop-filter:blur(14px);border-bottom:1px solid var(--line)}
.top .in{max-width:1280px;margin:0 auto;padding:10px 20px;display:flex;align-items:center;gap:12px}
.top img{width:32px;height:32px;border-radius:9px;flex:0 0 32px}.top b{font-size:15px;white-space:nowrap}.top .sp{flex:1}
.top a.btn{text-decoration:none;font:800 12.5px Manrope,sans-serif;color:var(--bg);background:var(--ink);padding:9px 14px;border-radius:999px;white-space:nowrap}
.top a.lnk{text-decoration:none;font:700 12.5px Manrope,sans-serif;color:var(--ink2);padding:8px 10px;border-radius:10px}.top a.lnk:hover{background:var(--bg2)}
.hero{background:linear-gradient(160deg,#1C1712,#2A1F16 60%,#4A2410);color:#fff;padding:56px 20px 44px}
.hero .in{max-width:1280px;margin:0 auto;display:grid;grid-template-columns:1.1fr .9fr;gap:40px;align-items:center}
.eyebrow{display:inline-flex;align-items:center;gap:8px;font:800 11px/1 Manrope,sans-serif;letter-spacing:1.9px;text-transform:uppercase;color:#FF9A4D}.eyebrow::before{content:"";width:18px;height:2px;background:#FF9A4D;border-radius:2px}
.hero h1{font:800 clamp(34px,4.6vw,54px)/1.02 Manrope,sans-serif;letter-spacing:-1.8px;margin:14px 0 14px;max-width:16ch;text-wrap:balance}.hero h1 span{color:#FF9A4D}
.hero p{font-size:17px;line-height:1.6;color:#D6CCBE;max-width:56ch;margin:0 0 22px}
.hero .row{display:flex;gap:10px;flex-wrap:wrap}.hero .b{display:inline-flex;align-items:center;gap:8px;text-decoration:none;font:800 14px Manrope,sans-serif;padding:12px 18px;border-radius:999px;border:1px solid rgba(255,255,255,.25);color:#fff}.hero .b.p{background:var(--acc);border-color:var(--acc)}
.hero .facts{display:flex;gap:26px;flex-wrap:wrap;margin-top:26px}.hero .facts b{display:block;font-size:22px}.hero .facts span{font-size:11px;letter-spacing:1.4px;text-transform:uppercase;color:#9A8E7F}
.hero .cover{border-radius:22px;overflow:hidden;box-shadow:0 40px 90px rgba(0,0,0,.45);border:1px solid rgba(255,255,255,.12)}.hero .cover img{display:block;width:100%;height:auto}
.wrap{max-width:1280px;margin:0 auto;padding:0 20px;display:grid;grid-template-columns:250px minmax(0,1fr);gap:40px}
nav.side{position:sticky;top:76px;align-self:start;max-height:calc(100vh - 90px);overflow:auto;padding:22px 0 30px;font-size:13.5px}
nav.side h5{font:800 10.5px/1 Manrope,sans-serif;letter-spacing:1.6px;text-transform:uppercase;color:var(--mut);margin:18px 0 8px}
nav.side a{display:block;color:var(--ink2);text-decoration:none;padding:5px 10px;border-radius:8px;border-inline-start:2px solid transparent}nav.side a:hover{background:var(--bg2)}nav.side a.on{color:var(--accd);border-color:var(--acc);background:var(--bg2)}
main{min-width:0;padding:22px 0 80px}
section{padding:28px 0;border-bottom:1px solid var(--line)}section h2{font:800 28px/1.15 Manrope,sans-serif;letter-spacing:-.8px;margin:0 0 6px}section .lead{color:var(--ink2);margin:0 0 18px;max-width:72ch}
h3{font:800 18px/1.2 Manrope,sans-serif;margin:26px 0 8px}h4{font:800 14px/1.2 Manrope,sans-serif;margin:18px 0 6px;color:var(--ink2)}
p{max-width:72ch}
.steps{display:grid;grid-template-columns:repeat(3,1fr);gap:12px;margin:14px 0 6px}.step{background:var(--bg2);border:1px solid var(--line);border-radius:16px;padding:14px 16px}.step em{display:inline-grid;place-items:center;width:26px;height:26px;border-radius:50%;background:var(--acc);color:#fff;font:800 12px Manrope,sans-serif;font-style:normal}.step b{display:block;margin:8px 0 4px}.step span{font-size:13.5px;color:var(--ink2)}
.code{margin:12px 0;border-radius:14px;overflow:hidden;background:var(--code);color:var(--codei);border:1px solid #2A2119}
.code-h{display:flex;align-items:center;justify-content:space-between;padding:8px 14px;font:700 11px/1 Manrope,sans-serif;letter-spacing:1.2px;text-transform:uppercase;color:#9A8E7F;background:#241C15}
.code pre{margin:0;padding:14px 16px;overflow:auto;font-size:12.5px;line-height:1.55;tab-size:2}
.copy{font:700 11px Manrope,sans-serif;color:#F7F1E8;background:#3A2D22;border:0;border-radius:8px;padding:5px 9px;cursor:pointer}.copy:hover{background:var(--acc)}
.tabs{margin:12px 0}.tab-h{display:flex;gap:4px;flex-wrap:wrap}.tab{font:800 12.5px Manrope,sans-serif;color:var(--ink2);background:var(--bg2);border:1px solid var(--line);border-bottom:0;border-radius:10px 10px 0 0;padding:8px 14px;cursor:pointer}.tab.on{background:var(--code);color:#F7F1E8;border-color:#2A2119}
.tab-p{display:none}.tab-p.on{display:block}.tab-p .code{margin-top:0;border-radius:0 14px 14px 14px}
.tbl{overflow-x:auto;border:1px solid var(--line);border-radius:14px;margin:10px 0}table{border-collapse:collapse;width:100%;font-size:13.5px}th{text-align:start;font:800 11px/1 Manrope,sans-serif;letter-spacing:1.2px;text-transform:uppercase;color:var(--mut);padding:10px 12px;background:var(--bg2);border-bottom:1px solid var(--line)}
td{padding:9px 12px;border-bottom:1px solid var(--line);vertical-align:top}tr:last-child td{border-bottom:0}td code{font-size:12.5px;background:var(--bg2);padding:2px 6px;border-radius:6px;white-space:nowrap}table.f td:first-child{white-space:nowrap}
.m{display:inline-block;font:800 10.5px/1 Manrope,sans-serif;letter-spacing:.8px;padding:5px 8px;border-radius:7px;color:#fff;min-width:52px;text-align:center}.m.get{background:var(--get)}.m.post{background:var(--post)}.m.put{background:var(--put)}.m.delete{background:var(--del)}
.tag{display:inline-block;font:700 11px Manrope,sans-serif;background:var(--bg2);border:1px solid var(--line);border-radius:999px;padding:2px 9px;color:var(--ink2);white-space:nowrap}.tag.pub{background:#E9F7EE;border-color:#BFE7CD;color:#1B8A4C}
@media (prefers-color-scheme:dark){:root:not([data-theme="light"]) .tag.pub{background:#163A24;border-color:#1F5B34;color:#8CE0A9}}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:14px}.card{background:var(--sur);border:1px solid var(--line);border-radius:16px;padding:16px 18px}.card b{display:block;margin-bottom:4px}.card span{font-size:13.5px;color:var(--ink2)}
.flow{display:flex;flex-wrap:wrap;gap:8px;align-items:center;margin:12px 0}.flow i{font-style:normal;font:800 12.5px Manrope,sans-serif;background:var(--bg2);border:1px solid var(--line);border-radius:999px;padding:7px 12px}.flow i em{font-style:normal;color:var(--accd);margin-inline-end:6px}.flow s{text-decoration:none;color:var(--mut)}
.note{background:var(--bg2);border-inline-start:3px solid var(--acc);border-radius:12px;padding:12px 16px;margin:12px 0;font-size:14px;color:var(--ink2)}.note b{color:var(--ink)}
.recipe{border:1px solid var(--line);border-radius:18px;padding:18px 20px;margin:14px 0;background:var(--sur)}.recipe h3{margin-top:0}.recipe .why{color:var(--ink2);font-size:14px;margin:0 0 6px}
.ft{border-top:1px solid var(--line);padding:26px 20px;color:var(--mut);font-size:13px;text-align:center}.ft a{color:var(--accd)}
.tmb{display:none}
@media (max-width:1000px){.hero .in{grid-template-columns:1fr}.wrap{grid-template-columns:1fr}nav.side{position:static;max-height:none;display:none}nav.side.open{display:block}.tmb{display:inline-flex;align-items:center;justify-content:center;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--sur);cursor:pointer;flex:0 0 36px}.steps,.grid2{grid-template-columns:1fr}.top a.lnk{display:none}}
'''

JS = r'''
document.querySelectorAll('[data-copy]').forEach(function(b){b.addEventListener('click',function(){var t=b.closest('.code').querySelector('code').innerText;navigator.clipboard.writeText(t).then(function(){b.textContent='Copied';setTimeout(function(){b.textContent='Copy'},1400)})})});
document.querySelectorAll('.tabs').forEach(function(t){t.querySelectorAll('.tab').forEach(function(x){x.addEventListener('click',function(){t.querySelectorAll('.tab').forEach(function(y){y.classList.toggle('on',y===x)});t.querySelectorAll('.tab-p').forEach(function(p){p.classList.toggle('on',p.dataset.pane===x.dataset.tab)})})})});
var links=[].slice.call(document.querySelectorAll('nav.side a')),secs=links.map(function(a){return document.querySelector(a.getAttribute('href'))});
function spy(){var y=window.scrollY+120,i=-1;secs.forEach(function(s,k){if(s&&s.offsetTop<=y)i=k});links.forEach(function(a,k){a.classList.toggle('on',k===i)})}
window.addEventListener('scroll',spy,{passive:true});spy();
var tmb=document.getElementById('tmb');if(tmb){tmb.addEventListener('click',function(){document.querySelector('nav.side').classList.toggle('open')})}
'''

def nav():
    items = [('#start', 'Quick start'), ('#auth', 'Authentication'), ('#conventions', 'Conventions')]
    h = '<nav class="side" id="side"><h5>Start</h5>' + ''.join(f'<a href="{a}">{e(t)}</a>' for a, t in items)
    h += '<h5>Reference</h5>' + ''.join(f'<a href="#{i}">{e(t)}</a>' for i, t, _, _ in REF)
    h += '<h5>Details</h5><a href="#objects">Objects and enums</a><a href="#lifecycle">Order lifecycle</a>'
    h += '<h5>Integrate</h5><a href="#integrations">CRM and accounting</a><a href="#errors">Errors and limits</a><a href="#help">Help</a></nav>'
    return h

def ref_sections():
    h = ''
    for i, t, lead, rows in REF:
        h += f'<section id="{i}"><h2>{e(t)}</h2><p class="lead">{lead}</p>' + table(rows) + '</section>'
    return h

def objects():
    h = '<section id="objects"><h2>Objects and enums</h2><p class="lead">The shapes you will read and write most. Property names are camelCase in JSON; money is OMR with three decimals; dates are ISO 8601 in the store\u2019s local time (Oman, UTC+4).</p>'
    for name, where, rows in OBJECTS:
        h += f'<h3>{e(name)} <small style="font:600 12px Manrope,sans-serif;color:var(--mut)">{e(where)}</small></h3>' + fields(rows)
    h += '</section>'
    return h

ld = [
 {"@context": "https://schema.org", "@type": "TechArticle", "headline": "OrderOrange API reference", "name": TITLE, "description": DESC, "url": URL,
  "image": "https://partner.orderorange.com/guide/og-api.jpg", "inLanguage": "en", "dateModified": "2026-09-18", "proficiencyLevel": "Beginner",
  "author": {"@type": "Organization", "name": "OrderOrange", "url": "https://www.orderorange.com/"}, "publisher": {"@type": "Organization", "name": "OrderOrange"},
  "about": {"@type": "SoftwareApplication", "name": "OrderOrange", "applicationCategory": "BusinessApplication", "operatingSystem": "Web"}},
 {"@context": "https://schema.org", "@type": "BreadcrumbList", "itemListElement": [
   {"@type": "ListItem", "position": 1, "name": "OrderOrange", "item": "https://www.orderorange.com/"},
   {"@type": "ListItem", "position": 2, "name": "Partner Guide", "item": "https://partner.orderorange.com/guide.html"},
   {"@type": "ListItem", "position": 3, "name": "API reference", "item": URL}]},
 {"@context": "https://schema.org", "@type": "FAQPage", "mainEntity": [
   {"@type": "Question", "name": "How do I authenticate with the OrderOrange API?", "acceptedAnswer": {"@type": "Answer", "text": "POST api/auth/login with the owner or team member login and password; the response carries a JWT in the token field. Send it as Authorization: Bearer on every call. Tokens last 12 hours, or 30 days with remember: true."}},
   {"@type": "Question", "name": "Can I connect my CRM or accounting software to OrderOrange?", "acceptedAnswer": {"@type": "Answer", "text": "Yes. Read invoices from api/invoices and api/invoices/daily for accounting, customers from api/storecustomers for a CRM, and write back with POST. There are no webhooks yet; poll the live board every few seconds and use the invoice number as your idempotency key."}},
   {"@type": "Question", "name": "Is the OrderOrange API free?", "acceptedAnswer": {"@type": "Answer", "text": "Yes. Every partner account can use the API with its own login; there is no separate API plan."}}]},
]
HEAD = (f'<!doctype html>\n<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        f'<title>{e(TITLE)}</title><meta name="description" content="{e(DESC)}"><meta name="robots" content="index,follow,max-image-preview:large">'
        f'<link rel="canonical" href="{URL}"><link rel="icon" type="image/svg+xml" href="_content/OrderOrange.ClientCore/logo.svg?v=4"><link rel="apple-touch-icon" sizes="180x180" href="guide/apple-touch-icon.png"><meta name="theme-color" content="#FF5A00">'
        f'<meta property="og:type" content="article"><meta property="og:site_name" content="OrderOrange"><meta property="og:title" content="{e(TITLE)}"><meta property="og:description" content="{e(DESC)}"><meta property="og:url" content="{URL}">'
        f'<meta property="og:image" content="https://partner.orderorange.com/guide/og-api.jpg"><meta property="og:image:width" content="1200"><meta property="og:image:height" content="630">'
        f'<meta name="twitter:card" content="summary_large_image"><meta name="twitter:title" content="{e(TITLE)}"><meta name="twitter:description" content="{e(DESC)}"><meta name="twitter:image" content="https://partner.orderorange.com/guide/og-api.jpg">'
        + ''.join('<script type="application/ld+json">' + json.dumps(x, ensure_ascii=False) + '</script>' for x in ld) +
        '<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Manrope:wght@500;600;700;800&family=JetBrains+Mono:wght@400;600&display=swap">'
        f'<style>{CSS}</style></head><body>')

page = HEAD + f'''
<div class="top"><div class="in"><button type="button" class="tmb" id="tmb" aria-label="Menu">☰</button><img src="guide/apple-touch-icon.png" alt=""><b>OrderOrange · API</b><span class="sp"></span>
<a class="lnk" href="#start">Quick start</a><a class="lnk" href="#auth">Auth</a><a class="lnk" href="#orders">Orders</a><a class="lnk" href="#integrations">Integrate</a><a class="lnk" href="guide.html">Partner guide</a>
<a class="btn" href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Get a free account</a></div></div>

<div class="hero"><div class="in"><div>
<span class="eyebrow">Developer API · v1</span>
<h1>Everything the partner app does, <span>your code can do too.</span></h1>
<p>One REST API over HTTPS with a Bearer token: read and write your menu, orders, tables, reservations, customers, bills and reports. Connect a CRM, an accounting package, a kitchen screen or your own app. Free with every partner account.</p>
<div class="row"><a class="b p" href="#start">Start in three calls</a><a class="b" href="#integrations">Connect CRM or accounting</a></div>
<div class="facts"><div><b>100+</b><span>endpoints</span></div><div><b>JSON</b><span>over HTTPS</span></div><div><b>14</b><span>languages in names</span></div><div><b>0 OMR</b><span>API fee</span></div></div>
</div><div class="cover"><img src="guide/og-api.jpg" alt="OrderOrange API" width="1200" height="630"></div></div></div>

<div class="wrap">{nav()}<main>

<section id="start"><h2>Quick start</h2><p class="lead">Base URL <code>https://api.orderorange.com</code>. Three calls: sign in, read your restaurant, write an order. Every example on this page was run against a real store.</p>
<div class="steps"><div class="step"><em>1</em><b>Sign in, keep the token</b><span>POST your partner login; the answer carries a JWT that lasts 12 hours, or 30 days with <code>remember</code>.</span></div>
<div class="step"><em>2</em><b>Read with GET</b><span>Add <code>Authorization: Bearer …</code>. Everything is scoped to the store the token belongs to.</span></div>
<div class="step"><em>3</em><b>Write with POST or PUT</b><span>Send JSON with <code>Content-Type: application/json</code>. Most writes answer <code>204</code>; creations answer the created object.</span></div></div>
{tabs([('curl', code('bash', LOGIN_CURL, '1 · sign in') + code('json', LOGIN_RES, 'response') + code('bash', GET_CURL, '2 · GET your restaurant') + code('json', GET_RES, 'response') + code('bash', POST_CURL, '3 · POST a counter sale') + code('json', POST_RES, 'response')),
       ('JavaScript', code('js', JS_QUICK, 'fetch')), ('C#', code('csharp', CS_QUICK, 'HttpClient')), ('Python', code('python', PY_QUICK, 'requests'))])}
</section>

<section id="auth"><h2>Authentication</h2><p class="lead">The API uses the same logins as the partner app. A token is a store: it carries the restaurant it works in, the role, and the permissions.</p>
<h3>Getting a token</h3>
{table([('POST', 'api/auth/login', PUB, '{ "email": "login", "password": "…", "remember": true }', 'LoginResponse — token, userId, restaurantId, storeRole, perms'),
        ('POST', 'api/auth/switch-store/{restaurantId}', OWNER, '—', 'LoginResponse with a new token for another business you own or work in'),
        ('GET', 'api/restaurants/my-stores', OWNER, '—', 'the businesses this login may switch to')])}
<div class="note"><b>Lifetime.</b> 12 hours, or 30 days with <code>remember: true</code>. There is no refresh endpoint: sign in again when you get <code>401</code>. Keep the token server-side; never ship it inside a browser page or a mobile app.</div>
<h3>Roles and permissions</h3>
<p>Owners can do everything in their store. A team member gets a <code>storeRole</code> (owner, manager, cashier, kitchen, waiter) or a custom role, and the token then carries a <code>perms</code> list. Each write endpoint names the permission it needs; the reference below shows it as <span class="tag">token · menu</span>. A call without the permission answers <code>403 {{ "code": "perm.denied" }}</code>.</p>
<div class="note"><b>Recommended for integrations:</b> create a team member for the integration in <em>Team</em>, give it a custom role with only the permissions it needs (for accounting: <code>reports</code>; for a CRM: <code>customers</code>; for a kitchen screen: <code>orders</code>), and sign that member in from your server.</div>
<p>All permission keys: <code style="white-space:normal">{e(PERMS)}</code></p>
</section>

<section id="conventions"><h2>Conventions</h2>
<div class="grid2">
<div class="card"><b>JSON, camelCase</b><span>Requests and responses are JSON; property names are camelCase (<code>menuItemId</code>). Unknown properties are ignored.</span></div>
<div class="card"><b>Money and tax</b><span>Decimals in Omani rial with three places (<code>4.500</code>). Tax is Oman VAT, <code>taxPercent</code> 5 by default, and is on every order and invoice.</span></div>
<div class="card"><b>Dates</b><span>ISO 8601 in the store\u2019s local time, Oman (UTC+4): <code>2026-09-18T01:05:48</code>. Send the same shape.</span></div>
<div class="card"><b>Languages</b><span>Names come as dictionaries keyed by language code (<code>names.ar</code>), fourteen codes. Ask for one language with <code>?lang=ar</code> on the menu.</span></div>
<div class="card"><b>Paging</b><span>Lists that can grow take <code>skip</code> and <code>take</code> (default 25 to 100). Reports take <code>period</code> or <code>from</code> and <code>to</code> dates.</span></div>
<div class="card"><b>Status codes</b><span><code>200</code> with a body · <code>204</code> done, no body · <code>400</code> validation · <code>401</code> sign in again · <code>403</code> permission · <code>404</code> not yours or gone · <code>429</code> slow down.</span></div>
<div class="card"><b>Ids are stable</b><span>Store, item, order and customer ids never change; keep them as external keys. The order <code>number</code> (<code>MF-755259</code>) is what people see on receipts.</span></div>
<div class="card"><b>Store scope</b><span>Partner endpoints never take a store id: the token decides. To work on a second business, call <code>switch-store</code> and use the new token.</span></div>
</div></section>

{ref_sections()}

{objects()}

<section id="lifecycle"><h2>Order lifecycle</h2><p class="lead">Every order walks the same road; each step is one POST and is written into <code>history[]</code> with who did it.</p>
<div class="flow"><i><em>0</em>Pending</i><s>→</s><i><em>1</em>Accepted</i><s>→</s><i><em>2</em>Preparing</i><s>→</s><i><em>3</em>Ready</i><s>→</s><i><em>4</em>PickedUp</i><s>→</s><i><em>5</em>OnTheWay</i><s>→</s><i><em>6</em>Delivered</i></div>
<div class="flow"><i><em>8</em>Rejected</i><span style="color:var(--mut)">from Pending, with a reason the guest sees</span></div>
<div class="flow"><i><em>7</em>Cancelled</i><span style="color:var(--mut)">by the guest or the store, with an optional reason</span></div>
<p>Counter sales and dine-in orders end at <code>Ready</code> and are settled with <code>paid</code>; deliveries your own people take out go through <code>courier</code> and <code>self-delivered</code>. A rejected or cancelled order stays in history and in the cancelled-invoices feed.</p>
<h3>Add a product with a schedule</h3>
<p>The same product can be limited to weekdays and hours: minutes after midnight, store clock. This one is sold Fridays and Saturdays from 12:00 to 23:00.</p>
{code('bash', ITEM_POST, 'POST api/menu/items')}{code('json', ITEM_RES, 'response · status 0 = waiting for approval')}
</section>

<section id="integrations"><h2>Connect a CRM or accounting software</h2><p class="lead">There is no separate integration product: the API is the integration. Four patterns cover almost everything; each one below is a working sketch.</p>
<div class="recipe"><h3>Accounting: every invoice, nightly</h3><p class="why">Zoho Books, QuickBooks, Xero, Odoo, Sage or an Excel-based bookkeeper. Read one day of invoices with lines and tax and create them on the other side; use the invoice number as the idempotency key so a rerun never duplicates.</p>
{code('python', ACCOUNTING_PY, 'python · nightly job')}
<p>Prefer files? <code>POST api/export/xlsx</code> returns the same Excel sheet the app\u2019s Excel button makes, for any report.</p></div>
<div class="recipe"><h3>CRM: customers both ways</h3><p class="why">HubSpot, Zoho CRM, Salesforce, Bitrix24 or your own. Customer cards carry name, phone, address, order count and last visit; a lead created in the CRM becomes a store customer with one POST.</p>
{code('js', CRM_JS, 'javascript')}</div>
<div class="recipe"><h3>Orders into an ERP or a kitchen screen</h3><p class="why">No webhooks yet, so poll the live board every few seconds; it is small and fast. New orders are <code>status 0</code>; accept them from your side if you want the board and your system to agree.</p>
{code('js', KDS_JS, 'javascript · poll the board')}</div>
<div class="recipe"><h3>Menu from a central system</h3><p class="why">A chain that keeps its menu in a head-office system pushes it to each branch: switch store, then <code>PUT api/menu/items/{{id}}</code> per product with prices, availability and the weekday and hour schedule. New products go through OrderOrange approval once; edits apply immediately.</p></div>
<div class="grid2">
<div class="card"><b>Idempotency</b><span>Use <code>order.number</code> for sales and <code>id</code> for everything else. Re-running a sync must find, not create.</span></div>
<div class="card"><b>Polling budget</b><span>Board every 5 s, customers and invoices every few minutes or nightly. Keep one token per integration and reuse it until <code>401</code>.</span></div>
<div class="card"><b>Several branches</b><span>Sign in once, list <code>my-stores</code>, and <code>switch-store</code> into each branch in turn; every call after that is scoped to that branch.</span></div>
<div class="card"><b>Time zone</b><span>All timestamps are Oman local time. Convert once on your side and keep it consistent in the accounting entries.</span></div>
</div></section>

<section id="errors"><h2>Errors and limits</h2>
<p>Errors carry a stable <code>code</code> the apps translate and an English <code>message</code>:</p>
{code('json', '{ "code": "auth.bad", "message": "Invalid username or password." }', '401')}
<p>Validation failures on malformed JSON use the standard ASP.NET problem shape (<code>400</code> with <code>errors</code> per field).</p>
{table([('POST', 'api/auth/login', '—', 'wrong password', '401 auth.bad'), ('ANY', 'partner endpoint', '—', 'missing or expired token', '401'), ('ANY', 'partner endpoint', '—', 'role lacks the permission', '403 perm.denied'),
        ('POST', 'api/contact', '—', 'more than 100 messages a day, or 5 from one address', '429 daily · ip'), ('POST', 'api/chat/{{orderId}}', '—', 'more than 12 messages a minute', '429'), ('POST', 'api/export/xlsx', '—', 'more than 20 000 rows', '400')],
       head=('Method', 'Where', '', 'When', 'Answer'))}
<p>There is no global rate limit today. Be a good neighbour: poll the board no faster than every 5 seconds, and batch nightly jobs.</p>
</section>

<section id="help"><h2>Help</h2>
<p>Something missing, or you want a webhook for your case? Open a ticket from <em>Support</em> in the partner app, or write to <a href="mailto:info@orderorange.com">info@orderorange.com</a>. WhatsApp: <a href="https://wa.me/96871322207">+968 7132 2207</a>.</p>
<p>The step-by-step product guide with 39 real screens is at <a href="guide.html">partner.orderorange.com/guide.html</a>.</p>
</section>

</main></div>
<div class="ft">OrderOrange API · reference updated 18 Sep 2026 · <a href="https://www.orderorange.com/">orderorange.com</a> · <a href="guide.html">Partner guide</a> · <a href="https://partner.orderorange.com/register">Open your business</a></div>
<script>{JS}</script></body></html>
'''
io.open(os.path.join(WWW, 'api.html'), 'w', encoding='utf-8', newline='').write(page)
print('api.html', round(len(page) / 1024), 'KB,', sum(len(r) for *_, r in REF), 'endpoints')
