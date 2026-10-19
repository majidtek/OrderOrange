"""Self-host the web fonts (Majed 2026-10-16: the Persian store name showed in a system font on
his phone — Google Fonts had not loaded, so the page kept the fallback). Downloads the
@font-face CSS Google serves to a modern browser, saves every .woff2 next to the apps and
writes wwwroot/fonts.css with the same unicode-range subsets pointing at /fonts/<file>.
Re-run to refresh. Apps link fonts.css instead of fonts.googleapis.com."""
import io, os, re, urllib.request, hashlib

FAMILIES = 'family=Inter:wght@400;600;700;800&family=Cairo:wght@400;600;800&family=Vazirmatn:wght@400;600;800&family=Noto+Sans+Devanagari:wght@400;600;800'
URL = 'https://fonts.googleapis.com/css2?' + FAMILIES + '&display=swap'
UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36'
OUT_DIR = r'D:\OrderOrange\OrderOrange.ClientCore\wwwroot\fonts'
OUT_CSS = r'D:\OrderOrange\OrderOrange.ClientCore\wwwroot\fonts.css'
os.makedirs(OUT_DIR, exist_ok=True)

req = urllib.request.Request(URL, headers={'User-Agent': UA})
css = urllib.request.urlopen(req, timeout=60).read().decode('utf-8')
blocks = re.findall(r'(/\* (\w[\w-]*) \*/\s*@font-face \{[^}]*\})', css)
out = ['/* Self-hosted copies of the Google Fonts faces (tools/fetch-fonts.py). Arabic → Cairo,',
       '   Persian/Urdu → Vazirmatn, Hindi → Noto Sans Devanagari; same unicode-range subsets as Google. */']
n = 0
for block, subset in blocks:
    fam = re.search(r"font-family: '([^']+)'", block).group(1)
    weight = re.search(r'font-weight: (\d+)', block).group(1)
    src = re.search(r'url\((https://[^)]+\.woff2)\)', block).group(1)
    data = urllib.request.urlopen(urllib.request.Request(src, headers={'User-Agent': UA}), timeout=60).read()
    name = f"{fam.replace(' ', '')}-{weight}-{subset}-{hashlib.sha1(data).hexdigest()[:8]}.woff2"
    with open(os.path.join(OUT_DIR, name), 'wb') as fh: fh.write(data)
    block = re.sub(r'url\(https://[^)]+\.woff2\)', f'url(fonts/{name})', block)
    out.append(block)
    n += 1
io.open(OUT_CSS, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')
total = sum(os.path.getsize(os.path.join(OUT_DIR, f)) for f in os.listdir(OUT_DIR))
print(f'{n} faces, {total / 1024:.0f} KB in {OUT_DIR}; css -> {OUT_CSS}')
