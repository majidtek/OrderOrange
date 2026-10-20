# OrderOrange Partner app (.NET MAUI Blazor Hybrid — Android + Windows)

The partner portal (`OrderOrange.RestaurantWeb`) as a native app for Android tablets/POS terminals
and Windows PCs, **with the shop's printers built in** — no RawBT, no separate till app. It replaces
the separate Android till in partner Settings.

- **The portal**: every page, layout and service under `OrderOrange.RestaurantWeb/Components`
  and `/Services` is compiled into this app by file link, so a change to a page reaches the
  browser portal and the app alike. Nothing is copied.
- **The printers** (`Till/`): the Android till's engine, running inside the app on the partner's
  own sign-in: kitchen/bar tickets per product route, the designed receipt when the bill is due,
  open-table rounds and edits, web sales closed between polls.

## How printing works

| Event | What prints, where |
|---|---|
| An order appears on the board | Kitchen/bar ticket on every printer that ticked the product in its **Products this printer prints** list (products no printer ticked: the Kitchen printers, switchable) |
| A round is rung onto an open table / a line is edited | Round ticket ("1 → 3", "removed") on the kitchen/bar printers |
| The bill is due (dine-in/counter paid, delivery/pickup ready) | The designed receipt on the **Cashier** printer |
| An invoice is settled in the portal and Settings has **Print direct** on | The receipt at once on the Cashier printer (and not again by the poll) |
| **Print** on a page that shows a receipt | That receipt, as drawn on screen, on the Cashier printer (no cashier printer set up → the system print dialog) |

Printers are set up in **Settings → Printers** (inside the app the "OrderOrange Partner app" card
opens the Printers screen): find printers, add/edit, test print, cut command, copies, **the products
each printer prints** (pick the printer, then tick its categories/products), the auto-print
switches, the last jobs and the log. The portal's old "Product printers" page is gone.

| | Android | Windows |
|---|---|---|
| Connections | the terminal's own printer ("InnerPrinter"), Bluetooth, Wi-Fi/LAN (TCP 9100), USB, serial | Wi-Fi/LAN (TCP 9100), any printer installed in Windows (raw ESC/POS through the spooler) |
| Drawing tickets | off-screen Android WebView (works with the screen off) | inside the app's own WebView2 (`ooRenderHtml` in `wwwroot/index.html`) |
| Keeps printing | foreground service, battery optimisation off, restart after reboot | while the app is open |

Everything is printed as an image (ESC/POS raster), so Arabic and Persian print correctly on any
thermal printer.

## What differs from the browser portal

| Browser portal (WebAssembly) | This app |
|---|---|
| `Program.cs` fetches `/config` from the host | `AppConfig.cs` (API, customer site, partner host) |
| Language files over HTTP (`i18n/*.json`) | Same files packed into the app (`Services/PackageTranslationLoader.cs`) |
| `/placesearch`, `/maplink` answered by the partner host | `wwwroot/index.html` routes them to `Services/HostBridge.cs`, which forwards them to the partner host |
| `Components/Routes.razor` | `Components/HybridRoot.razor`: languages first, the same router, the printer wiring |
| `window.print()` / RawBT | `wwwroot/index.html` + `Services/PrintBridge.cs` → the app's Cashier printer |
| Settings shows "download the app" | `HostInfo.IsNativeApp` (ClientCore) → Settings shows "Printers" |

## Build

```bash
dotnet publish OrderOrange.PartnerApp/OrderOrange.PartnerApp.csproj -f net10.0-android -c Release
```

```bash
dotnet publish OrderOrange.PartnerApp/OrderOrange.PartnerApp.csproj -f net10.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -o _release/win-x64
```

The APK is `bin/Release/net10.0-android/publish/com.orderorange.partner-Signed.apk` (debug-signed).
Zip the Windows folder; it runs on any Windows 10/11 PC without installing .NET (`-r` is not used
because it leaks into the Android restore; `RuntimeIdentifierOverride` is the MAUI way).

## Publish (partner Settings download)

Copy the APK / zip to the VPS, then:

```powershell
powershell -File D:\OrderOrange\tools\publish-partner-app.ps1 -Platform android -File C:\Windows\Temp\com.orderorange.partner-Signed.apk -Version 1.0.0 -Notes "..."
```

```powershell
powershell -File D:\OrderOrange\tools\publish-partner-app.ps1 -Platform windows -File C:\Windows\Temp\OrderOrangePartner-windows.zip -Version 1.0.0 -Notes "..."
```

Add `-StagingOnly` to try it on staging first. Served by `api/till/partner/{android|windows}/version|download`.

## Keep in step

- New portal pages and services need nothing: the links are globs.
- When `OrderOrange.RestaurantWeb/wwwroot/index.html` gains a script or stylesheet, add it to
  `wwwroot/index.html` here too.
- A new package reference in `OrderOrange.RestaurantWeb.csproj` must be added here as well.
- `Till/` was ported from `OrderOrange.AndroidTill` (1.4.0); a printing fix there belongs here too.

## Known gaps

- A4 report printing on Android (`window.print()` without a receipt on the page) does nothing in the WebView.
- Web Push and Google sign-in are browser-only; partners sign in with username and password.
- The printer engine works for the store owner's account (role RestaurantOwner), as the till does.
