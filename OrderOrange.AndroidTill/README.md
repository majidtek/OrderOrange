# OrderOrange Android Till

The Android twin of the Windows till (`LocalHandler`): runs on an Android POS terminal, polls the
store's live order board and open tables, prints kitchen/bar tickets on Wi-Fi ESC/POS printers and
the cashier receipt on the terminal's built-in printer — using the store's own receipt design,
kitchen design and print routing from the partner app.

## How it works

| Piece | File | Notes |
|---|---|---|
| Poll loop + printing rules | `Services/TillEngine.cs` | Port of `LocalHandler/MainWindow.RefreshOrdersAsync`: kitchen ticket when an order first appears, cashier bill when it is due (dine-in/counter → paid; delivery/pickup → Ready or later), closed tables print only the lines the kitchen has not had, open-table rounds print as they are rung. A failed print is **not** marked printed; it retries next poll and shows a red banner. |
| API | `Services/ApiService.cs` | Same calls as the Windows till; a 401 triggers one silent re-login with the saved credentials. |
| Receipt HTML | `Services/HtmlReceipt.cs` | Verbatim copy of the Windows till's builder (same output as the partner ReceiptView). |
| Kitchen ticket HTML | `OrderOrange.Shared/KitchenTicket.cs` | Shared with the partner designer and the Windows till. |
| Round diff | `Services/TabRounds.cs` | Copy of the Windows till's. |
| HTML → bitmap | `Printing/HtmlRenderer.cs` | Off-screen Android WebView, zoomed so the ticket's CSS width fills 576 (80 mm) or 384 (58 mm) dots. |
| Bitmap → ESC/POS | `Printing/EscPos.cs` | 1-bit (threshold 160 or Floyd–Steinberg), `GS v 0` raster in 256-row bands, feed, optional partial cut. Text mode is never used (no Arabic in PC437/GB18030). |
| Transports | `Printing/Transports.cs` | `IPrinterTransport`: TCP 9100, USB (printer class 7, permission dialog), serial node, and **Built-in** which tries USB → 127.0.0.1:9100 → serial candidates and remembers what worked. |
| Keep-alive | `Platforms/Android/TillForegroundService.cs` | Foreground service with notification, partial wake lock, Wi-Fi lock. |
| Persistence | `Models/AppSettings.cs` | One JSON blob in Preferences (printers, per-store printed/receipted/seen-line ids pruned to 2000, memos). Token and password in SecureStorage. |
| UI | `Pages/*.cs` | C#-only pages: Login, Main (status, banner, last 20 jobs, store switcher), Printers, Printer edit (with test print), Settings, Device probe, Log. English / Arabic / Persian in `Localization/L.cs`. |

## Build

Needs .NET 10 SDK with the Android/MAUI workloads (Visual Studio 2026 with the .NET MAUI workload installs them).

```bash
dotnet build OrderOrange.AndroidTill/OrderOrange.AndroidTill.csproj -f net10.0-android -c Release
```

The APK lands in `OrderOrange.AndroidTill/bin/Release/net10.0-android/com.orderorange.till-Signed.apk`
(debug-signed; a release keystore is passed with `-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=…`
and is kept outside the repo).

Run on a terminal with USB debugging on:

```bash
dotnet build OrderOrange.AndroidTill/OrderOrange.AndroidTill.csproj -f net10.0-android -t:Run
```

or `adb install -r <apk>`.

## First use on a terminal

1. Sign in with the business account (role RestaurantOwner). Point the server field at staging
   for tests (`https://orderorange.com:8600/`).
2. Printers → **Find printers**: the app lists the paired Bluetooth devices (on Sunmi-style
   terminals such as the E-POS the built-in printer is the paired device **"InnerPrinter"**;
   Android 12+ asks for the Nearby devices permission once), checks the terminal itself (USB
   printer-class devices, an internal print server on 127.0.0.1:9100, writable serial nodes) and
   sweeps the Wi-Fi for anything answering on TCP 9100, then lists what it found. **Use** opens
   the form pre-filled (InnerPrinter/built-in → Cashier, 58 mm, cut off; network or other
   Bluetooth → Kitchen, 80 mm). `Printing/PrinterDiscovery.cs`, `Pages/DiscoverPage.cs`; the IP
   field also has **Find on the network…**, the Bluetooth connection has **Choose a paired
   device…** (a printer must be paired in Android Settings → Connected devices first). Typing an
   IP by hand still works (Add printer). Use **Test print** — the page has English, Arabic,
   Persian and a QR, so a missing font or wrong width shows at once.
3. **Product printers** (home screen): the Windows till's routing grid — one row per product,
   one tick per printer on this terminal, a category row ticks the whole category, search box
   on top. Saved on the terminal (`AppSettings.ProductPrinters` / `CategoryPrinters`; the
   printers every product of a category shares become the category rule for dishes added
   later). Unticked products follow the web map at partner → Product printers (Kitchen / Bar /
   both). `Pages/RoutingPage.cs`, `TillEngine.PrintersFor`.
4. If the built-in printer does not answer, open Settings → Device & printer probe and send the
   report (model, USB devices, serial nodes, loopback port): a vendor SDK can then be added as one
   more transport. If the terminal's ROM has no Arabic font, drop `arabic.ttf` into
   `Resources/Raw/` and rebuild.

## Staging test list

- Ring an order in the partner POS → kitchen ticket once (bar items on the bar printer, unrouted items on the kitchen).
- Mark it paid/ready → receipt once.
- Restart the app → nothing reprints.
- Add a round to an open table → only the new lines print; edit a line → "1 → 3" ticket.
- Unplug a kitchen printer → red banner, ticket retried on the next poll, printed once it is back.
