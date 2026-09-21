# OrderOrange — operations runbook

This is how the live system is built, deployed, backed up and kept healthy. Everything here
assumes the Windows Server that hosts orderorange.com (IIS + Kestrel), SQL Server and MongoDB.

## 1. The stacks

| | Production | Staging |
|---|---|---|
| API (Kestrel exe) | `https://127.0.0.1:8520` (loopback, self-signed `oo-loopback`) and `https://orderorange.com:8500` (till) | `https://127.0.0.1:8620`, public `https://orderorange.com:8600` |
| API proxy (IIS, YARP) | `https://api.orderorange.com` → `127.0.0.1:8520` | — |
| Customer site (IIS) | `https://www.orderorange.com` | `https://orderorange.com:9443` |
| Partner host (IIS) | `https://partner.orderorange.com` | `https://orderorange.com:9444` |
| Admin host (IIS) | `https://admin.orderorange.com` | `https://orderorange.com:9445` |
| Delivery (IIS) | `https://delivery.orderorange.com` | — |
| SQL database | `OrderOrange` (pre-launch data kept as `OrderOrangeTest`) | `OrderOrangeStaging` |
| Mongo database | `orderorange` (pre-launch `wajibat_test`) | `orderorange_staging` |
| Port 80 | `OrderOrange.HttpsRedirect` → 301 https | |

Cloudflare fronts every public hostname (proxy on, TLS at the edge and again at origin). The
API reads the visitor's address from `CF-Connecting-IP`.

## 2. Publish → deploy

Each host is published to a temporary folder and robocopied into its IIS site, **excluding
`appsettings.json`** (the real configuration lives only on the server) and taking a backup first.

```powershell
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet publish OrderOrange.ApiServer\OrderOrange.ApiServer.csproj   -c Release -o D:\OrderOrange\_apitmp
& $dotnet publish OrderOrange.PartnerHost\OrderOrange.PartnerHost.csproj -c Release -o D:\OrderOrange\_hosttmp      # builds RestaurantWeb (WASM) into it
& $dotnet publish OrderOrange.ClientWeb\OrderOrange.ClientWeb.csproj    -c Release -o D:\OrderOrange\_clientwebtmp
& $dotnet publish OrderOrange.AdminHost\OrderOrange.AdminHost.csproj    -c Release -o D:\OrderOrange\_adminhosttmp
& $dotnet publish OrderOrange.DeliveryWeb\OrderOrange.DeliveryWeb.csproj -c Release -o D:\OrderOrange\_deliverytmp
```

Deploy pattern for an IIS site (the same for staging with the staging site names):

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'; $site = 'C:\inetpub\OrderOrangeClient'; $pool = 'OrderOrangeClient'
robocopy $site "D:\OrderOrange\_backup\OrderOrangeClient-live-$stamp" /E /XD logs          # backup
& "$env:windir\System32\inetsrv\appcmd.exe" stop  apppool "/apppool.name:$pool"
robocopy 'D:\OrderOrange\_clientwebtmp' $site /E /XF appsettings.json appsettings.Development.json /XD logs /R:3 /W:2
& "$env:windir\System32\inetsrv\appcmd.exe" start apppool "/apppool.name:$pool"
Invoke-WebRequest 'https://www.orderorange.com/' | Select-Object StatusCode                   # smoke
```

The API is not an IIS site: stop the `OrderOrange.ApiServer` process (or its watchdog task),
robocopy `_apitmp` over `D:\OrderOrange\publish-api` (again excluding `appsettings.json`),
start it again, and hit `api/restaurants?take=1` to prove it is up.

Order of operations for a change that touches `OrderOrange.Shared`: API first, then the apps
(DTO changes are additive so old apps keep working during the minute in between).

Static-asset versions: `food.css?v=NNN` and the `mf-*.js?v=N` tags in each app's
`index.html` / `App.razor` must be bumped with every change or browsers keep the old file.
Partner `index.html` also carries `tablet.css?v=` / `tablet.js?v=` for the legacy till.

## 3. Staging first

Deploy to staging, verify (curl the page as `Googlebot` for server-rendered output; drive
Chrome headlessly over CDP for interactive checks), then deploy to production the same way.
Staging `appsettings.json` files differ from production (ports, database names,
`Guide:Source` / `Guide:ShortBase`) and persist because deploys exclude them.

## 4. The partner guide

Source and generator live outside the repo (`doc-src/gen3.py → host.py → seo.py`); the output
is `OrderOrange.RestaurantWeb/wwwroot/guide.html` (+ `guide.xx.html`, `guide/*.jpg`,
`robots.txt`, `sitemap.xml`). After regenerating, publish + deploy **PartnerHost**. The customer
site serves the guide at `www.orderorange.com/guide` by fetching it from the partner host with
the `X-Guide-Proxy` header (10-minute cache) and rewriting canonical / hreflang links; the old
`partner.orderorange.com/guide*.html` addresses 301 there.

## 5. TLS

* Public certificate: Let's Encrypt SAN (`orderorange.com`, `www`, `partner`, `admin`, `api`,
  `delivery`) issued by win-acme into the IIS `WebHosting` store. The API's public `:8500`
  endpoint loads it by subject; **restart the API after a renewal**.
* Loopback: self-signed `CN=oo-loopback` (SAN `localhost`, `127.0.0.1`) in `LocalMachine\My`,
  trusted via `LocalMachine\Root`, valid to 2031. Recreate with `New-SelfSignedCertificate` if
  it ever expires.
* No plain HTTP anywhere since 2026-09-02.

## 6. Backups & data

* Every deploy backs up the live folder to `D:\OrderOrange\_backup\<site>-live-<stamp>`.
* SQL: regular `.bak` of `OrderOrange`; Mongo: `mongodump` of `orderorange`.
* Data changes go **through the API**, never SQLCMD or direct Mongo edits. New Mongo
  collections take integer `_id`s from `counters`; never create an index on `_id`.
* New SQL tables: `EnsureCreated` does not migrate — create the table by hand (script it) when
  an entity is added.

## 7. After a reboot

The API's watchdog task is "run only when logged on"; after a reboot the API may be down and
LocalDB can wedge if production and staging start at once. Start the production API first,
check `api/restaurants?take=1`, then staging. (See the reboot notes in the ops memory.)

## 8. Third parties

* **Google** sign-in (`Google:ClientId`), **Gmail SMTP** for the emailed code (app password).
* **Web Push** (VAPID keys in `Push:*`) for partner notifications; a crossed bell in the app
  means the browser denied permission.
* **Instagram**: each store connects its own long-lived token (`storeSocial`); publishing goes
  container → poll → `media_publish`; the picture must have a public URL. Never poll Meta in a
  background loop; the scheduler only publishes what is due.
* **WhatsApp Cloud API**: WABA and phone number configured on the API; templates are per WABA.
* **Cloudflare**: proxy on for every hostname; purge cache if a static file will not update.

## 9. Monitoring

* Admin → **Activity**: presence board (Active now / App open / recent), page-view feed with IP.
* Admin → **Visitors**, **Bot chats**, **Contact messages**, **Support**.
* Partner → **Team activity**, **Notifications**.
* Google Search Console: the `orderorange.com` domain property; sitemap
  `https://www.orderorange.com/sitemap.xml` (34 URLs at the time of writing).

## 10. Health checks (copy-paste)

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://api.orderorange.com/api/restaurants?take=1     # 200
curl -s -o /dev/null -w "%{http_code}\n" https://www.orderorange.com/                          # 200
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" http://orderorange.com/               # 301 → https
curl -s -A Googlebot https://www.orderorange.com/pos | grep -o "<title>[^<]*"                  # Free POS System …
curl -s https://www.orderorange.com/sitemap.xml | grep -c "<loc>"                              # ≥ 30
curl -s -o /dev/null -w "%{http_code}\n" https://www.orderorange.com/guide/ar                  # 200
```
