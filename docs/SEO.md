# OrderOrange — SEO reference

How search engines see orderorange.com, where each piece lives in the code, what is on
production today, and the checklist for adding a page. Companion to the [README](../README.md)
and [OPERATIONS](OPERATIONS.md).

## 1. Where the SEO lives

| Piece | File | Notes |
|---|---|---|
| Head per request (title, description, canonical, hreflang, Open Graph, Twitter, JSON-LD) | `OrderOrange.ClientWeb/Components/App.razor` | The **single SEO brain**. `<PageTitle>` inside interactive Blazor components never reaches a crawler, so everything a crawler needs is written here, server-side, from the request path. |
| Route slugs | `OrderOrange.ClientWeb/Seo/SeoSlugs.cs` | `Hub = "/food-delivery"`, `Pos = "/pos"`. Pages, sitemap, crawler footer and breadcrumbs all read these constants, so a rename is one edit plus a redirect. |
| Known app routes | `AppRoutes` set in `App.razor` | A single lowercase path segment that is **not** in this set is treated as a store slug (`orderorange.com/{slug}`). Add every new client route here or it becomes a shop lookup. |
| Sitemap | `OrderOrange.ClientWeb/Program.cs` → `/sitemap.xml` | Generated from `GET api/restaurants/sitemap` (real stores only: `Seo:RealStoreMinId`, `Seo:SitemapMinMenuItems`), cached 1 h, with 14 `hreflang` alternates per URL. |
| Robots | `OrderOrange.ClientWeb/wwwroot/robots.txt` | Disallows every private route (checkout, orders, profile, sign-in, track, bill, reserve, tab-invoice, contract, chat, `_blazor`). Keep in sync with `AppRoutes`. |
| Crawler pre-render | `App.razor` (`IsCrawler`) | Known bots get the fully rendered body; `curl -A Googlebot` shows what Google sees. |
| Copy | `OrderOrange.ClientCore/Localization/*.json` — keys `seo.*`, `pos.*`, `browse.*` | 14 languages; `?lang=xx` selects the language and the head. |
| Partner guide | generated `OrderOrange.RestaurantWeb/wwwroot/guide*.html` | Served at `www.orderorange.com/guide` through the ClientWeb proxy (see §5). Titles/descriptions per language are set in the generator's `seo.py`. |
| Redirects | `OrderOrange.ClientWeb/Program.cs`, `OrderOrange.PartnerHost/Program.cs`, `OrderOrange.HttpsRedirect` | Old addresses 301 to the current ones (see §4). |

## 2. Indexable pages

| Page | Address | Title (en) | Structured data |
|---|---|---|---|
| Home | `/` | OrderOrange — Food Delivery App & Free Restaurant POS | Organization, WebSite (SearchAction), SoftwareApplication (the POS rides the home page too) |
| Food hub | `/food-delivery` | Food Delivery — Order from Restaurants Near You \| OrderOrange | WebPage, BreadcrumbList, FAQPage (browse.q1…) |
| Cuisine | `/cuisines/{slug}` | {Cuisine} restaurants — order online, delivery or pickup | WebPage, ItemList |
| Area | `/areas/{slug}` | Restaurants in {Area} — food delivery near you | WebPage, ItemList |
| POS product site | `/pos` | Free POS System for Restaurants, Cafés & Shops \| OrderOrange | SoftwareApplication (`isAccessibleForFree: true`, `areaServed: Worldwide`, `offers` 0 USD "Free plan", `featureList` from the page's `Blocks`), WebPage, FAQPage (12 questions), BreadcrumbList |
| Partner guide | `/guide`, `/guide/{lang}` | Free Restaurant POS & QR Ordering — OrderOrange Partner Guide | SoftwareApplication, WebPage, BreadcrumbList, FAQPage (23 questions) |
| Store page | `/{slug}` (canonical) or `/restaurant/{id}` | {Store} — order online \| OrderOrange | Restaurant / Store (address, geo, telephone, servesCuisine, menu items with offers) |

Every page carries `<link rel="alternate" hreflang="xx">` for all 14 languages plus `x-default`,
`rel="canonical"` (the slug form for stores), `og:*` and `twitter:*` with a raster image
(`/og-default.jpg`, `/og/{id}.jpg` per store — share crawlers render no JS/SVG).

Private / transactional routes are `noindex` via robots and are not in the sitemap. On the
partner host everything except the guide, the API docs and the sitemap is `X-Robots-Tag: noindex`.

## 3. Titles and descriptions

Targets: title ≤ 60 characters, description 150–160. The English set:

| Key | Value |
|---|---|
| `seo.siteTitle` | OrderOrange — Food Delivery App & Free Restaurant POS |
| `seo.siteBlurb` | Order food online from restaurants, cafés, markets and pharmacies near you — delivery or pickup, in 14 languages. Free for restaurants, no commission, free POS. |
| `seo.hubTitle` / `seo.hubDesc` | Food Delivery — Order from Restaurants Near You / Restaurants, cafés, markets and pharmacies that deliver or offer pickup through OrderOrange… |
| `seo.posTitle` / `seo.posDesc` | Free POS System for Restaurants, Cafés & Shops / Free POS for restaurants, cafés, bakeries, markets and shops: till on any tablet, kitchen display, QR menu, online orders with 0% commission. No monthly fee. Any currency, 14 languages. |
| `seo.cuisineTitle`, `seo.areaTitle`, `seo.storeDesc` | patterned with `{0}` |

Arabic and Persian are written by hand; the other eleven carry their own translations.

**House rules for copy**

* Say **restaurants, cafés and shops** — never "restaurants" alone.
* Do **not** tie the product to a country or city in titles, descriptions, headings, FAQ answers, structured data or URLs. Real store addresses and cuisine names (e.g. "Omani Traditional") are data and stay.
* Lead with the word **free** for the POS ("free POS system", "no monthly fee, no setup fee, no commission").
* Prices in structured data for the POS are `0` in USD; store prices use the store's own currency.

## 4. Addresses and redirects (keep forever)

| Old | → New | Where |
|---|---|---|
| `/pos-system-oman`, `/pos-system` | `/pos` | ClientWeb `Program.cs` (301, query kept) |
| `/food-delivery-oman` | `/food-delivery` | ClientWeb `Program.cs` |
| `partner.orderorange.com/guide.html`, `/guide.{xx}.html`, `/guide` | `www.orderorange.com/guide`, `/guide/{xx}` | PartnerHost middleware (301) unless the request carries `X-Guide-Proxy` |
| `www.orderorange.com/guide/en` | `/guide` | ClientWeb |
| `orderorange.com/*` (apex), `http://*` | `https://www.orderorange.com/*` | apex→www 301 (port-aware), `OrderOrange.HttpsRedirect` on :80 |
| `/restaurant/{id}`, `/store/{id}`, `/p/{id}`, `?id=` | canonical `/{slug}` | `App.razor` canonical tag (no redirect — the page renders, canonical points at the slug) |

Links placed inside Blazor components should use a cross-origin form (`https://orderorange.com/guide`) when a full page load is wanted; same-origin links are intercepted by the Blazor router.

## 5. The partner guide

Generated outside the repo by `doc-src/gen3.py → host.py → seo.py` into
`OrderOrange.RestaurantWeb/wwwroot/` (`guide.html`, `guide.xx.html`, `guide/*.jpg`, `robots.txt`,
`sitemap.xml` for the partner host). The customer site serves it at `/guide` by fetching
`Guide:Source` + `/guide[.xx].html` with the `X-Guide-Proxy` header, rewriting
`partner.orderorange.com/guide*.html` → `www.orderorange.com/guide[/xx]` and relative `guide/`
image paths → `/guide/`, caching 10 min (`/guide/{file}.{ext}` proxies the images, 6 h).
The partner host's own sitemap lists only `api.html`; the guide is in the customer sitemap.

## 6. Search Console (state on 2026-09-21)

* Property: **Domain** `orderorange.com` (covers www, partner, api…).
* Sitemaps: `https://www.orderorange.com/sitemap.xml` — **Success, 34 URLs** (home, hub, `/pos`, `/guide`, cuisines, areas, stores). `https://partner.orderorange.com/sitemap.xml` — fetchable again (robots now allows it), lists `api.html` only. `/pos` and `/guide` were once submitted *as sitemaps* by mistake — they are pages; remove such rows.
* Requested indexing by hand for `/pos`, `/guide`, `/food-delivery` after each rename.

## 7. Off-site (owner's checklist)

1. Google Search Console — done; re-request indexing after big copy changes.
2. Bing Webmaster Tools — submit the same sitemap.
3. Google Business Profile for OrderOrange (software) → link to `/pos`.
4. Instagram bio link → `https://orderorange.com/pos` (captions are not clickable; posts say "link in bio"). Account: **@orderorange.blog**.
5. Backlinks: software directories (Capterra, G2, GetApp, Product Hunt) and every partner's "Order online" link pointing at their orderorange.com page.
6. Regular content: one Instagram post a week plus short articles on `/pos` topics.
7. `Organization.sameAs` in the JSON-LD lists the Instagram account and the GitHub organisation (`https://github.com/majidtek`); add new official profiles there.

## 8. Checklist for a new public page

1. Add the route constant to `SeoSlugs` and the segment to `AppRoutes` in `App.razor`.
2. Add `seo.<page>Title` / `seo.<page>Desc` keys to all 14 locale files (title ≤ 60, description ≤ 160; en/ar/fa by hand).
3. Write the head branch in `App.razor` (title, description, canonical, JSON-LD if the page is an entity).
4. Add the URL to the sitemap in `ClientWeb/Program.cs` with `Alternates(...)`.
5. Link it from the crawler footer and from at least one visible page.
6. Deploy to staging, `curl -A Googlebot` the page and parse the `<script type="application/ld+json">` blocks (Razor writes the type as `ld&#x2B;json`; the JSON itself is fine), then production.
7. Search Console → URL inspection → Request indexing.

## 9. Quick checks

```bash
curl -s -A Googlebot https://www.orderorange.com/pos | grep -o "<title>[^<]*\|<h1[^>]*>[^<]*"
curl -s https://www.orderorange.com/sitemap.xml | grep -c "<loc>"
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" https://www.orderorange.com/pos-system-oman
curl -s -o /dev/null -w "%{http_code} %{redirect_url}\n" https://partner.orderorange.com/guide.html
curl -s https://www.orderorange.com/robots.txt | head
```
