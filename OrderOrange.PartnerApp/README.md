# OrderOrange Partner app (.NET MAUI Blazor Hybrid, Android)

The partner portal (`OrderOrange.RestaurantWeb`) as a native Android app **with its own
printers built in**: no RawBT, no separate till app.

- **The portal**: every page, layout and service under `OrderOrange.RestaurantWeb/Components`
  and `/Services` is compiled into this app by file link, so a change to a page reaches the
  browser portal and the app alike. Nothing is copied.
- **The printers** (`Till/`): the Android till's engine, running inside the app on the
  partner's own sign-in. Kitchen/bar tickets per product route, the designed receipt when the
  bill is due, open-table rounds and edits, web sales closed between polls. A foreground
  service keeps it printing with the screen off and after a reboot.

## How printing works

| Event | What prints, where |
|---|---|
| An order appears on the board | Kitchen/bar ticket on the kitchen and bar printers, per the portal's **Print routing** map (unrouted products: kitchen, switchable) |
| A round is rung onto an open table / a line is edited | Round ticket ("1 → 3", "removed") on the kitchen/bar printers |
| The bill is due (dine-in/counter paid, delivery/pickup ready) | The designed receipt on the **Cashier** printer |
| An invoice is settled in the portal and Settings has **Print direct** on | The receipt at once on the Cashier printer (and not again by the poll) |
| **Print** on a page that shows a receipt | That receipt, as drawn on screen, on the Cashier printer |

Printers are set up in the app: the orange printer button (bottom corner of every page) opens
**Printers**: Find printers (the terminal's own "InnerPrinter", paired Bluetooth printers, USB,
and every ESC/POS printer on the shop Wi-Fi), add/edit, test print, cut command, copies, the
auto-print switches, background permissions, the last jobs and the log.

Everything is printed as an image (ESC/POS raster), so Arabic and Persian print correctly on any
thermal printer.

## What differs from the browser portal

| Browser portal (WebAssembly) | This app |
|---|---|
| `Program.cs` fetches `/config` from the host | `AppConfig.cs` (API, customer site, partner host) |
| Language files over HTTP (`i18n/*.json`) | Same files packed into the app (`Services/PackageTranslationLoader.cs`) |
| `/placesearch`, `/maplink` answered by the partner host | `wwwroot/index.html` routes them to `Services/HostBridge.cs`, which forwards them to the partner host |
| `Components/Routes.razor` | `Components/HybridRoot.razor`: languages first, the same router, and the printer wiring |
| `window.print()` / RawBT | `wwwroot/index.html` + `Services/PrintBridge.cs` → the app's Cashier printer |

`OrderOrange.ClientCore` and `OrderOrange.Shared` are referenced as projects, unchanged.

## Build

Needs the .NET 10 SDK with the Android/MAUI workloads.

```bash
dotnet build OrderOrange.PartnerApp/OrderOrange.PartnerApp.csproj -f net10.0-android
```

```bash
dotnet publish OrderOrange.PartnerApp/OrderOrange.PartnerApp.csproj -f net10.0-android -c Release
```

The APK lands in `bin/Release/net10.0-android/publish/com.orderorange.partner-Signed.apk`
(debug-signed; pass a release keystore with `-p:AndroidKeyStore=true …`, kept outside the repo).

## Keep in step

- New portal pages and services need nothing: the links are globs.
- When `OrderOrange.RestaurantWeb/wwwroot/index.html` gains a script or stylesheet, add it to
  `wwwroot/index.html` here too.
- A new package reference in `OrderOrange.RestaurantWeb.csproj` must be added here as well.
- `Till/` was ported from `OrderOrange.AndroidTill` (1.4.0). A printing fix there should be
  carried over here (same file names under `Till/`).

## Known gaps

- A4 report printing (`window.print()` on pages without a receipt) does nothing in the Android WebView.
- Web Push and Google sign-in are browser-only; partners sign in with username and password.
- Microphone / location prompts (voice assistant, "use my location") are not wired yet.
- The printer engine works for the store owner's account (role RestaurantOwner), as the till does.
