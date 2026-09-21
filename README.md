# OrderOrange

**A free POS system for restaurants, cafés and shops — and the food-ordering marketplace that comes with it.**
Any country, any currency, fourteen languages. Live at [orderorange.com](https://www.orderorange.com) ·
[orderorange.com/pos](https://www.orderorange.com/pos) · [partner.orderorange.com](https://partner.orderorange.com) ·
[Partner guide](https://www.orderorange.com/guide) · [API docs](https://partner.orderorange.com/api.html)

One API, one shared menu, and five front ends:

| App | Project | Who | Runtime | Live |
|---|---|---|---|---|
| Customer site | `OrderOrange.ClientWeb` | People ordering food, guests scanning a table QR | Blazor **Server** (crawler-friendly, SEO) | https://www.orderorange.com |
| Partner app (POS) | `OrderOrange.RestaurantWeb` served by `OrderOrange.PartnerHost` | Restaurant / café / shop owners and their staff | Blazor **WebAssembly** | https://partner.orderorange.com |
| Admin | `OrderOrange.AdminWeb` served by `OrderOrange.AdminHost` | Platform administrators | Blazor WebAssembly | https://admin.orderorange.com |
| Delivery | `OrderOrange.DeliveryWeb` | Riders | Blazor Server | https://delivery.orderorange.com |
| Legacy tablet till | `RestaurantWeb/wwwroot/tablet/` | Waiters on old Android tablets / iPads | jQuery 1.12 + ES5, no framework | https://partner.orderorange.com/tablet |
| Till companion | `LocalHandler/` | The counter PC | WPF (.NET) — receipt printing, pay terminal | installed per shop, auto-updates from `api/till/version` |

All of them talk to **`OrderOrange.ApiServer`** (ASP.NET Core Web API, .NET 10) behind **`OrderOrange.ApiProxy`** (YARP) at https://api.orderorange.com.

---

## Table of contents

1. [What it does](#what-it-does)
2. [Architecture](#architecture)
3. [Projects](#projects)
4. [Data](#data)
5. [Getting started (development)](#getting-started-development)
6. [Configuration](#configuration)
7. [Authentication & permissions](#authentication--permissions)
8. [Localization](#localization)
9. [The API](#the-api)
10. [Front-end conventions](#front-end-conventions)
11. [SEO](#seo)
12. [Deployment & operations](#deployment--operations)
13. [Testing](#testing)
14. [Repository layout & what is not in git](#repository-layout--what-is-not-in-git)

---

## What it does

### For a business (partner app)

* **Point of sale** — tables, rooms and floor plan, walk-in / counter sales, open tabs, cash & card, split by rounds, keyboard shortcuts and a code box (`5*3` rings No. 5 three times), a command palette, a POS assistant that understands plain sentences in several languages, and a lighter tablet screen for old devices.
* **Kitchen display** — dark ticket board fed by orders and table rounds; tickets age, turn amber and red, cooks tick lines and bump tickets.
* **Menu** — categories, products, options, photos, prices, discounts, availability windows, in-store-only items, names and descriptions in 14 languages, menu templates, a lazy "light" menu for the POS.
* **QR ordering & table chat** — a QR code per table opens the menu on the guest's phone; orders land on that table's bill; the guest can message the counter.
* **Online ordering** — the same menu becomes a shop page on the customer site with delivery, pickup, rider tracking, reservations, coupons and promotions, reviews, surveys.
* **Printing** — ESC/POS receipts via the LocalHandler companion, product printers (drinks to the bar, grills to the kitchen), an invoice designer, labels and barcodes, a signed bill-verification QR.
* **Stock** — raw materials, recipes (consumption per sale), purchasing by barcode, suppliers and payments to them, receiving stock, paying later / instalments, waste write-offs, a **warehouses map** (drag, link, transfers) and a central warehouse for branches.
* **People** — staff with a permission matrix (`Perm` catalogue, enforced server-side by `[RequirePerm]`), GPS attendance and leave, payroll, team chat, team activity board, calendar with reminders.
* **Customers** — customer list, discount codes with rules, **loyalty** (points, tiers, welcome gift, live board), surveys, table bookings.
* **Money** — invoices with an immutable audit trail (cancel / edit-as-replace), bills and expenses, contracts (signed public page + PDF), daily / product / consumption / salary reports, Excel export and A4 print for every report.
* **Marketing** — Instagram posting with the owner's own token, scheduled posts, a menu poster generator, today's dishes on WhatsApp.
* **Several businesses under one login**, each with its own stock, staff and reports.
* **A developer API** with tokens, documented at `/api.html`.

### For customers (customer site)

Home with cuisines, areas, promos, favourites and "order again"; store pages with the menu in the visitor's language; dish sheet, basket, checkout (addresses, coupons, cash/card); live tracking; order history and reviews; reservations; a chat assistant that can take an order by text or voice; table ordering from a QR scan; public bill and contract pages; sign-in by Google or an emailed code (no passwords on production).

### For the platform (admin)

Dashboard and insights, restaurant approvals and commission, users of all roles, orders, coupons and cuisines, product review queue, support desk, contact messages, bot-chat transcripts, visitor analytics, an activity board that separates "active now" from "app open in the background", and a "joins" bell that pushes when a business, customer or rider signs up.

---

## Architecture

```
                 Cloudflare (proxy, TLS)
                        │
   ┌────────────────────┼─────────────────────────────────────────┐
   │ IIS on Windows Server                                        │
   │                                                              │
   │  www.orderorange.com      OrderOrange.ClientWeb  (Blazor Server, SEO brain in App.razor)
   │  partner.orderorange.com  OrderOrange.PartnerHost → serves RestaurantWeb (WASM) + /tablet + /api.html
   │  admin.orderorange.com    OrderOrange.AdminHost   → serves AdminWeb (WASM)
   │  delivery.orderorange.com OrderOrange.DeliveryWeb (Blazor Server)
   │  api.orderorange.com      OrderOrange.ApiProxy    (YARP) ──► https://127.0.0.1:8520
   │  :80                      OrderOrange.HttpsRedirect (301 to https)
   │                                                              │
   │  OrderOrange.ApiServer (Kestrel exe, watchdog task)          │
   │     ├─ SQL Server (EF Core)  users, stores, orders, invoices, staff, attendance, materials…
   │     └─ MongoDB               catalog (menus), chats, KDS tickets, loyalty, warehouses,
   │                              promotions, surveys, support, Instagram, presence, bot logs…
   └──────────────────────────────────────────────────────────────┘
```

* **Shared code** — `OrderOrange.Shared` (DTOs, enums, `Perm` catalogue) and `OrderOrange.ClientCore` (the `ApiClient`, state, localization catalogue, shared components, `food.css` and the `mf-*.js` helpers) are referenced by every front end. `OrderOrange.ClientCore.Server` holds the server-only bits.
* **Two persistence stores** — SQL Server holds the relational core (EF Core, `AppDbContext`, `EnsureCreated`; new tables are created by hand in SQL). Everything added since mid-2025 lives in **MongoDB** as one class per feature (`*Store.cs` in `ApiServer/Services`): integer `_id`s come from a `counters` collection so paging by id keeps working; `_id` is never indexed explicitly.
* **Media** — customer payloads carry `api/media/*` URLs instead of base64; `Media:PublicBase` must be a Cloudflare-proxied 443 host.
* **Background work** — only the Instagram scheduler (`BackgroundService`, 60 s tick) and the presence heartbeat run in the background. There are deliberately **no** background pollers against Meta's Graph API.
* **Time** — WebAssembly has no time-zone data, so its `DateTime.Now` is UTC. Any comparison against a server timestamp goes through a `ServerNow` the API sends (KDS, loyalty, admin activity).

---

## Projects

| Project | Kind | Notes |
|---|---|---|
| `OrderOrange.Shared` | class library | DTOs, enums, `Perm`, `SeoSlugs`-style constants. Additive changes only — every app deserialises these. |
| `OrderOrange.ApiServer` | ASP.NET Core Web API | Controllers per feature (`Controllers/`), stores and services (`Services/`), entities (`Models/Entities.cs`), `Program.cs` (auth, Kestrel endpoints, presence middleware, YARP-facing headers). |
| `OrderOrange.ApiProxy` | ASP.NET Core + YARP | `api.orderorange.com` → loopback API; passes `CF-Connecting-IP`. |
| `OrderOrange.HttpsRedirect` | ASP.NET Core | Port 80 → 301 https. |
| `OrderOrange.ClientCore` | Razor class library | `ApiClient`, `AppState`, `LanguageService` + `TranslationCatalog`, `Fmt`, shared dialogs, `wwwroot/food.css`, `wwwroot/mf-*.js`, `Localization/*.json`. |
| `OrderOrange.ClientCore.Server` | class library | Server-side helpers for the Blazor Server apps. |
| `OrderOrange.ClientWeb` | Blazor Server | Customer site. `Components/App.razor` writes SEO head + JSON-LD per request; `Program.cs` has the sitemap, the `/guide` proxy and the legacy-URL redirects. |
| `OrderOrange.RestaurantWeb` | Blazor WebAssembly | Partner app. `wwwroot/tablet/` is the legacy jQuery till, `wwwroot/guide.html` the generated partner guide, `wwwroot/api.html` the API docs. |
| `OrderOrange.PartnerHost` | ASP.NET Core | Hosts RestaurantWeb; serves the guide files to the customer site's `/guide` proxy and redirects the old guide addresses. |
| `OrderOrange.AdminWeb` / `OrderOrange.AdminHost` | Blazor WASM + host | Admin panel. |
| `OrderOrange.DeliveryWeb` | Blazor Server | Rider app. |
| `OrderOrange.Tests` | xUnit | 44 test classes against an in-memory `ApiFactory` (auth, availability windows, bill verification, promotions, admin allow-list…). **Never run the suite against a live database.** |
| `LocalHandler` | WPF | The till companion: prints the bill from `api/restaurants/mine/receipt`, sends amounts to a pay terminal, self-updates from `api/till/version`. |
| `food-images/`, `docs/` | assets | Seed pictures, guide images, the partner-panel Word guide. |

---

## Data

**SQL Server** (`AppDbContext`): `Users` (roles: Customer, RestaurantOwner, Driver, Administrator; `LastSeenAt` stamped by the API on any authenticated call), `Restaurants` (a *store* of any `StoreType` — Restaurant, Shop…; `IsOpen`, `IsApproved`, slug, cuisines, location), `MenuItems` (a search mirror of the Mongo catalogue — run `POST api/admin/restaurants/{id}/menu/sync-search` after onboarding a real store), `Orders` / `OrderItems`, `Invoices` + `InvoiceAudits`, `StoreStaff`, `StaffAttendances`, `StaffLeaves`, `StoreCalendarEvents`, `ActivityLogs` (page views with IP), `Materials` / `ProductMaterials`, coupons, reviews, addresses, drivers…

**MongoDB** (database `orderorange`): `menuCategories` / `menuItems` (the real catalogue), `counters`, `chat`, `tableChats`, `communityChat`, `kdsTickets`, `loyaltyPrograms` / `loyaltyMembers` / `loyaltyEvents`, `warehouses` / `warehouseBalances`, `wasteLog`, `storePromotions`, `surveys` / `surveyResponses`, `supportTickets` / `supportFiles`, `storeSocial` / `storeSocialQueue` (Instagram), `storePrefs` (currency symbol etc.), `botChats`, `contactMessages`, `adminAlerts`, `presence` (last API heartbeat: address + app).

Rule of the house: **go through the API, never the database** — no SQLCMD, no direct Mongo edits for data work.

---

## Getting started (development)

Prerequisites: .NET 10 SDK, SQL Server (LocalDB is enough), MongoDB 8 on `localhost:27017`, Python 3 (only for the guide generator).

```bash
git clone https://github.com/<you>/OrderOrange.git
cd OrderOrange

# 1. configuration — copy every example and fill in the placeholders
for p in OrderOrange.ApiServer OrderOrange.ClientWeb OrderOrange.PartnerHost OrderOrange.AdminHost OrderOrange.DeliveryWeb OrderOrange.ApiProxy OrderOrange.HttpsRedirect; do
  cp $p/appsettings.example.json $p/appsettings.json
done
# ApiServer: ConnectionStrings:Default, Mongo:ConnectionString, Seed:Scale=Demo (a populated world), Kestrel endpoints
# ClientWeb / PartnerHost / AdminHost / DeliveryWeb: Api:BaseUrl → the API you just configured

# 2. run the API (creates the schema; Seed:Scale=Demo fills it with demo stores)
dotnet run --project OrderOrange.ApiServer

# 3. run the apps you need (each on its own port, see Properties/launchSettings.json)
dotnet run --project OrderOrange.ClientWeb
dotnet run --project OrderOrange.PartnerHost     # partner app (builds RestaurantWeb into it)
dotnet run --project OrderOrange.AdminHost
dotnet run --project OrderOrange.DeliveryWeb

# 4. tests (in-memory; never point them at a real database)
dotnet test OrderOrange.Tests
```

Demo accounts exist only when `Seed:Scale` is `Demo` (an owner, a driver, an admin, a customer); production has no password accounts — sign-in is Google or an emailed code, and `Auth:AllowPasswordLogin` is the emergency back door.

---

## Configuration

Every project ships an `appsettings.example.json` with the real shape and placeholders; the real `appsettings.json` files are git-ignored. The important keys (API):

| Key | Meaning |
|---|---|
| `ConnectionStrings:Default` | SQL Server. |
| `Mongo:ConnectionString`, `Mongo:Database` | MongoDB. |
| `Kestrel:Endpoints:*` | Public `:8500` (till) and loopback `:8520` (IIS apps + proxy), both TLS; certificate subjects are read from the Windows store. |
| `Seed:Scale` | `Production` = schema only; `Demo` = populated development world. |
| `Google:ClientId` | Google sign-in (public). |
| `Smtp:*` | The emailed sign-in code (Gmail needs an app password). |
| `Auth:AllowPasswordLogin`, `Auth:MaxRegistrationsPerIpPerDay` | Password back door, registration throttle. |
| `Admin:AllowedEmails` | Who may sign in as Administrator. |
| `Media:PublicBase` | Public origin for `api/media/*` URLs (must be Cloudflare-proxied on 443). |
| `Suggested:RestaurantIds` | Shops featured on the customer home. |
| `Home:MinItemPrice` | Hides auto-created cheap items (water, bags) from the home page. |
| `Seo:RealStoreMinId`, `Seo:SitemapMinMenuItems` | Which stores the sitemap lists. |
| `Bill:Secret` | HMAC for the signed bill / contract pages — must match across apps. |
| `Push:*` (VAPID) | Web Push keys for partner notifications. |
| `Guide:Source` (ClientWeb), `Guide:ShortBase` (PartnerHost) | Where the customer site fetches the partner guide from, and where the partner host redirects the old guide addresses to (defaults are production; staging points them at itself). |

Currency is **per store** (`storePrefs`), tax rate, opening hours and delivery areas too — nothing in the code assumes a country.

---

## Authentication & permissions

* JWT bearer tokens from `POST api/auth/login` (email/username + password where allowed), `api/auth/google`, or the emailed code flow. `POST api/auth/switch-store/{id}` re-issues a token for another business of the same owner.
* Roles: `Customer`, `RestaurantOwner`, `Driver`, `Administrator`.
* Fine-grained partner permissions live in `OrderOrange.Shared.Perm` (menu, invoices, invoice.cancel/edit, calendar, settings, surveys…), assigned per staff member, and enforced with `[RequirePerm(Perm.X)]` on the API. The partner UI hides what the user may not do; the server is the authority.
* Developer API tokens are documented at `partner.orderorange.com/api.html` (generated by `doc-src/api-gen.py`).

---

## Localization

Fourteen languages: `en ar fa ur hi tr de fr es it pt ru ja zh`. One JSON file per language in `OrderOrange.ClientCore/Localization/` (UTF-8 with BOM, flat `"key": "value"` lines — keep the hand format), served to the browser apps as `/i18n/<code>.json`. `LanguageService` / `TranslationCatalog` fall back to English. Arabic, Persian and Urdu are right-to-left (`MudRTLProvider`); Persian and Arabic digits are drawn by `mf-lang.js`.

Store data is multilingual too: names and descriptions carry a per-language dictionary (`Names`, `Descriptions`), with `NameFor(locale)` falling back to Arabic, then the canonical value. The legacy tablet page has its own dictionaries (`tablet/tablet.js` for en/ar, `tablet/i18n.js` for the other twelve).

---

## The API

Base: `https://api.orderorange.com/` (staging `https://orderorange.com:8600/`). JSON, bearer tokens, `[Authorize]` per controller. Highlights:

| Area | Endpoints |
|---|---|
| Auth | `POST api/auth/login`, `api/auth/google`, `api/auth/email-code`, `POST api/auth/switch-store/{id}`, `GET api/auth/my-stores` |
| Menu | `GET api/menu?light=true` (outline without photos), `GET api/menu/categories/{id}/items` (one group with photos), `GET api/menu/items/{id}/photos`, category/item CRUD (`Perm.Menu`) |
| Orders | `POST api/orders`, `POST api/orders/counter` (walk-in / table sale from the till), status transitions, `api/orders/{id}/paid` |
| Invoices | `GET api/invoices?days=&take=`, `GET api/invoices/{id}`, `POST api/invoices/{id}/replace` (audited edit), cancel |
| Tables & tabs | `api/storerooms`, `api/storetables`, `api/storetabs` (open bills) |
| Customers | `api/storecustomers`, `api/loyalty/*` (board, lookup, earn, redeem, live) |
| Stock | `api/materials`, purchases, suppliers, `api/warehouses/*` (position, link, central/stock, central/transfer), `api/waste` |
| People | `api/staff`, `api/attendance`, `api/team`, `api/teamactivity`, `api/calendar` |
| Store | `GET/PUT api/restaurants/mine`, `PUT api/restaurants/mine/hours`, `POST api/restaurants/mine/toggle-open`, logo, photos, slug, QR text, receipt |
| Marketing | `api/instagram` (connect, post, schedule), `api/promotions`, `api/surveys`, `api/reviews` |
| Public | `api/restaurants` (browse, sitemap), `api/track/*` (page views), `api/contact`, `api/bills/{id}` (signed), `api/media/*` |
| Admin | `api/admin/*` (restaurants, users, orders, reports, activity, presence, alerts, support, bot chats, visitors) |
| Till | `api/till/version`, `api/till/download` (LocalHandler auto-update), `api/export/xlsx` |

Full, verified reference with examples: https://partner.orderorange.com/api.html

---

## Front-end conventions

* **MudBlazor 8.15** everywhere; date pickers go through `MfDatePicker` / `MfDateRangePicker` (never `MudDatePicker` directly). MudBlazor's physical margins (`mr-*`) do not flip in RTL — use logical CSS.
* **`food.css` is layered**: it is appended to, versioned (`?v=NNN` in each app's `index.html` / `App.razor`), and the last block wins. Bump the version on every change or browsers keep the old file.
* **Print CSS** must be `:has()`-scoped or every other page prints blank. **Report export** is one `<ReportExport>` component + `POST api/export/xlsx`.
* **Blazor Server pages** must never compare `DateTime.Now` with server stamps in WASM; ask the API for `ServerNow`.
* **Customer-site links** rendered inside Blazor components are intercepted by the router — cross-origin links (e.g. `https://orderorange.com/guide`, which 301s to `www`) are used where a full page load is wanted.
* **Legacy tablet page** (`wwwroot/tablet/`): ES5 only, jQuery 1.12, floats/inline-block layout, `-webkit-` twins for every transform/animation.

---

## SEO

* `ClientWeb/Components/App.razor` is the SEO brain: per-request `<title>`, description, canonical, `hreflang` for 14 languages (`?lang=xx`), Open Graph, and JSON-LD (Organization, WebSite, Restaurant/Store, FAQPage, BreadcrumbList, `SoftwareApplication` for the POS — `isAccessibleForFree: true`, `areaServed: Worldwide`).
* Landing pages: `/` (home), `/food-delivery` (hub; cuisines under `/cuisines/{slug}`, areas under `/areas/{slug}`), `/pos` (the POS product site with its own layout, nav, hero slideshow, free-vs-paid table, 12 FAQs), `/guide` (the partner guide, proxied from the partner host; `/guide/{lang}` for the other languages), one page per store at `/{slug}`.
* Old addresses keep working with 301s: `/pos-system-oman` → `/pos-system` → `/pos`, `/food-delivery-oman` → `/food-delivery`, `partner.orderorange.com/guide*.html` → `www.orderorange.com/guide[/xx]`.
* `/sitemap.xml` is generated from the API (real stores only) and lists every landing page with its language alternates; `robots.txt` disallows every private route. Titles are ≤ 60 characters, descriptions ≈ 150–160.
* The partner guide is generated: `doc-src/gen3.py → host.py → seo.py` (screens, HOW/NOTES/FAQ, per-language heads, robots, sitemap) into `RestaurantWeb/wwwroot/guide*.html`.
* House style: copy names **restaurants, cafés and shops** — never "restaurants" alone — and does not tie the product to one country.

---

## Deployment & operations

See [docs/OPERATIONS.md](docs/OPERATIONS.md) for the step-by-step runbook. In short:

* Publish with `dotnet publish -c Release -o <tmp>` per host, then robocopy into the IIS site folder (excluding `appsettings.json`, which lives only on the server), recycling the app pool. The API runs as a Kestrel exe with a watchdog scheduled task.
* **Staging first, then production**: staging is a full second stack (API `:8600`, customer `:9443`, partner `:9444`, admin `:9445`, its own SQL database and Mongo database).
* Every deploy takes a backup of the live folder first (`_backup/<site>-live-<stamp>`).
* TLS: no plain HTTP anywhere; Let's Encrypt SAN certificate via win-acme; a self-signed loopback certificate for `127.0.0.1:8520`; restart the API after renewal.
* Observability: admin **Activity** (presence + page views), **Visitors**, **Bot chats**, **Contact messages**, **Support**; partner **Team activity**.

---

## Testing

`dotnet test OrderOrange.Tests` — xUnit over an in-memory API host (`ApiFactory`). Covers auth and admin allow-list, availability windows, bill verification, promotions engine (23 cases), and more. The suite seeds its own data; keep it away from any real database.

UI checks are done headlessly with Chrome over CDP (real mouse events, not `jQuery.trigger`): sign in, tap, read the DOM, screenshot — the scripts used during development live outside the repository.

---

## Repository layout & what is not in git

```
OrderOrange.slnx                      solution
OrderOrange.*/                        projects (see table above)
LocalHandler/                         WPF till companion
docs/                                 partner-panel Word guide, guide images, seed docs
food-images/                          seed pictures
README.md, docs/OPERATIONS.md         this documentation
.gitignore                            keeps the items below out
```

Never committed: `appsettings.json` (all projects — use the `.example.json`), `PASSWORDS.txt`, certificates, `token.json`, `bin/ obj/ .vs/`, publish outputs (`_*tmp/`, `publish-*/`), backups, logs, zips, and the generated guide language pages / sitemap (rebuilt by the generator).

---

## License

Proprietary — © OrderOrange. All rights reserved.
