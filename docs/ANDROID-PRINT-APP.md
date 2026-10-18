---
name: orderorange-android-print
description: Build the OrderOrange Android print app ("Android till") that runs on an E-POS Android terminal, prints the cashier receipt on the terminal's built-in printer and kitchen/bar tickets on Wi-Fi ESC/POS printers, following the store's OrderOrange print routing, receipt design and kitchen design. Use when working on the Android printing app, ESC/POS raster printing, or porting the Windows till (LocalHandler) printing to Android.
---

# OrderOrange Android print app

## Goal

A shop uses OrderOrange only on an Android POS terminal (no Windows PC). The terminal has a
**built-in 58/80 mm thermal printer** and there are **Wi-Fi printers** (kitchen, bar). The app:

1. Signs in with the shop's business account (role RestaurantOwner).
2. Polls the live order board and open tables.
3. Prints **kitchen/bar tickets** to the right Wi-Fi printer per item (OrderOrange print routing).
4. Prints the **cashier receipt** on the built-in printer (or any printer with role Cashier).
5. Uses the store's own **receipt design** and **kitchen design** that the owner set in the partner
   app (partner.orderorange.com → Invoice design → Receipt / Kitchen).

It is the Android twin of the Windows till **LocalHandler** (`D:\OrderOrange\LocalHandler` on the
server, also in the repo). Mirror its behaviour; don't invent new rules.

## Hardware facts (from the user's photos)

- Terminal: "E-POS" Android POS, built-in printer settings show print density 100,
  font **GB18030**, code page **PC437**. These code pages have **no Arabic/Persian/Urdu glyphs**,
  so text mode cannot print our menus. **Always print as an image (ESC/POS raster `GS v 0`).**
- Kitchen/bar printers: ESC/POS thermal printers on **Wi-Fi**, raw TCP port **9100**.
  - Print the self-test page (hold FEED while switching on) to see the IP.
  - Ask the shop to give each printer a fixed IP (DHCP reservation on the router).
- Built-in printer model/SDK is unknown. Ask the user for Settings → About device (model,
  manufacturer, Android version). Ways to reach it, in order of preference:
  1. Vendor print SDK (AAR/JAR from the manufacturer, e.g. a `PrinterManager`/`printBitmap` API).
  2. ESC/POS over an internal port: `127.0.0.1:9100`, a serial device (`/dev/ttyS*`), or USB
     (UsbManager, the printer shows as a USB device class 7).
  3. Android Print Service (last resort, slow, no auto-print).
  Build a `IPrinterTransport` interface so each of these is one class.

## Stack (recommended)

- **.NET 10 MAUI, Android target** (`net10.0-android`), C#. Same language as the rest of
  OrderOrange, and it can reference **OrderOrange.Shared** (DTOs + `KitchenTicket` builder) directly.
- Code: private repo **github.com/majidtek/OrderOrange** (`gh` signed in as majidtek). Clone it,
  then create the app as a new project, e.g. `OrderOrange.AndroidTill/`, referencing
  `../OrderOrange.Shared/OrderOrange.Shared.csproj`.
- If Shared can't be referenced as-is (a server-only package), copy only the needed DTO files
  rather than changing Shared's targets.
- Work on a branch. Don't push to `main` or open PRs without asking Majed.

## API (production base `https://api.orderorange.com/`)

The base URL must be a setting, so the app can be pointed at staging for tests. Every call
except login sends `Authorization: Bearer <token>`. JSON is camelCase (System.Text.Json web
defaults) and enums are numbers unless the server says otherwise; reuse the Shared DTOs.

| Purpose | Call | Returns |
|---|---|---|
| Sign in | `POST api/auth/login` body `LoginRequest(Email, Password, Remember)`; Email is the username | `LoginResponse` (Token, RestaurantId, RestaurantName, Role…). Refuse unless Role == RestaurantOwner |
| Stores of this account | `GET api/restaurants/my-stores` | `List<StoreSummaryDto>` |
| Switch store | `POST api/auth/switch-store/{restaurantId}` | new `LoginResponse`; swap the token |
| Live orders | `GET api/orders/restaurant/board` | `List<OrderDto>` (new, cooking, ready, on the way) |
| Open tables (rounds) | `GET api/storetabs` | `List<StoreTabDto>` with Lines; each line has its own id |
| Store profile | `GET api/restaurants/mine` | `MyRestaurantDto` (name, address, phone, VAT, KitchenLanguage…) |
| Receipt design | `GET api/restaurants/mine/receipt` | `ReceiptDesignDto` |
| Kitchen design | `GET api/restaurants/mine/kitchen-design` | `KitchenDesignDto` (TextSize, NumberSize, ShowStation, ShowTime, ShowTable, ShowCustomer, ShowNotes, ShowItemNotes, UpperCase, Separator, Header, Footer) |
| Print routing | `GET api/restaurants/mine/print-routes` | `List<PrintRouteDto(MenuItemId, Station)>`; Station is "kitchen" or "bar". **Unrouted items go to the kitchen.** |

Reference implementation: `LocalHandler/Services/ApiService.cs` (SignIn, SwitchStore, Board,
tabs, designs, routes), `RestaurantsController.cs` (~lines 1933–2071) for the design/route endpoints.

**API only:** never connect to SQL or MongoDB. The app talks to the API and nothing else.

## Printing rules (copy of LocalHandler `MainWindow.RefreshOrdersAsync`)

- Poll every **PollSeconds** (default 15 s) while the app runs. Use a foreground service with a
  notification so Android doesn't kill it, and keep the Wi-Fi awake.
- **Order type:**
  - Dine-in: `TableName` not empty, or `DeliveryAddress` starts with "Dine-in".
  - Counter: `DeliveryAddress == "Counter"`.
  - Delivery: `OrderType == Delivery`.
  - Otherwise Pickup.
- **Kitchen ticket**: print once, when an order id first appears on the board.
  - Split the items by station using print routes.
  - Send one ticket per station that has items, to every enabled printer with that role.
- **Cashier receipt**: print once, when the bill is due:
  - dine-in / counter: when `IsPaid`
  - delivery / pickup: when status is **Ready or later** (LocalHandler's `ReadyForReceipt`).
- **Closed table** (dine-in and IsPaid, or `PaymentRef` starts with `"TAB-"`): its rounds were
  already printed from the open tab, so don't print the full kitchen ticket again. Print to the
  kitchen only the lines not yet sent, then the receipt.
- **Open-table rounds**: on each poll, diff the line ids from `api/storetabs` against the ids
  already seen. Print the new lines to the kitchen/bar straight away as a round ticket.
- **Persistence, per store:** keep three sets in app storage (Preferences/SQLite), pruned to the
  last ~2000 ids:
  - printed order ids
  - receipted order ids
  - seen tab-line ids
- **First run / first sign-in to a store**: mark everything currently on the board and in the
  tabs as already printed, so nothing old reprints.
- A failed print must **not** be marked printed. Retry on the next poll and show a red banner
  ("Kitchen printer 192.168.1.50 not reachable").

## Rendering (images, not text)

1. Build the ticket **HTML** with the same code the till uses:
   - Kitchen: `OrderOrange.Shared.KitchenTicket.Html(order, items, paperWidth 58|80, design,
     stationName, nameOverrides, changes, TicketStrings.For(lang))` (in `Shared/KitchenTicket.cs`).
     Item names in the store's `KitchenLanguage`; `KitchenTicket.Line(Id, Quantity, Name, Notes)`
     for round lines.
   - Receipt: `LocalHandler/Services/HtmlReceipt.Build(order, StoreProfile, ReceiptDesignDto)`.
     It lives in the WPF project, so copy it into the Android project (or move it to Shared in its
     own commit, without changing its output). The receipt must look like the partner app's
     ReceiptView/Invoice design.
2. Render the HTML to a Bitmap with an **offscreen Android WebView**.
   - Width = printer dots: **80 mm → 576 px**, **58 mm → 384 px**.
   - Wait for `onPageFinished` plus a short delay for the logo, then draw the content height.
   - Arabic/Persian then just works (WebView shapes RTL text).
3. Convert to 1-bit:
   - grayscale, then threshold ~160, or Floyd–Steinberg for logos
   - width a multiple of 8
4. Send ESC/POS:
   - `ESC @` to initialise
   - `GS v 0 m xL xH yL yH data` in bands of ≤ 255–1000 rows (some printers choke on one huge image)
   - feed 3–4 lines
   - `GS V 66 0` (partial cut). The built-in printer may have no cutter, so make cutting a setting.
5. **Copies** per printer setting. Kitchen design TextSize/NumberSize are already applied in the
   HTML; don't scale again.

## Printers screen (settings UI)

Each printer has:
- Name
- Connection: Built-in | Wi-Fi (IP + port 9100) | USB
- Role: Cashier | Kitchen | Bar
- Paper: 58 | 80
- Copies
- Cut (on/off)
- Enabled
- A **Test print** button, which prints an image ticket with Arabic + English + a QR, so code-page
  problems show at once

Global settings:
- API base URL
- AutoPrintReceipt, AutoPrintKitchen
- PollSeconds
- Reprint last receipt / last kitchen ticket buttons
- Sign out

Main screen: store name, connection status, the last 20 print jobs (time, order no, printer, ok/failed).

UI languages: English first, Arabic + Persian at least (OrderOrange's rule: **English is the main
language**). Strings can come from `OrderOrange.ClientCore/Localization/*.json` if useful.

## Build & test on this PC

- Install: .NET 10 SDK + `dotnet workload install maui-android`, Android SDK/JDK (Visual Studio
  2022/2026 with the .NET MAUI workload is easiest).
- Enable Developer options + USB debugging on the terminal, connect by USB, then:
  `dotnet build -t:Run -f net10.0-android`
  or install the APK with `adb install -r`.
- Test a Wi-Fi printer from the PC before writing app code: send a raw ESC/POS test to
  `IP:9100` (a tiny C# console or `ncat`).
- Test with a **staging store**, not a live shop:
  - Ring an order in the partner POS.
  - Mark it paid/ready.
  - Check: kitchen ticket once, receipt once.
  - Restart the app: no reprints.
  - Add a round to an open table: only the new lines print.
  - Unrouted items go to the kitchen.
  - Bar items go to the bar.
- Release: signed APK (keystore kept outside the repo); later, OTA like the Windows till
  (`api/till/version` + download) if Majed wants it.

## Rules for the session

- **Never repeat passwords, tokens or keystore passwords in chat**, and never commit them;
  credentials are typed into the app by the user.
- **Server changes:** if the API needs a change (e.g. a printed-ack endpoint), deploy it to
  **staging only**. Production waits for Majed's explicit "deploy / in production" order.
  The server (VPS) deploy steps are in `docs/OPERATIONS.md` in the repo.
- Never run the full test suite against production.
- Report in text, no screenshots unless asked.
