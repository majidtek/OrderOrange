"""Hosted copy of the partner guide: the artifact keeps its images inline (one self-contained
file), but the copy served from partner.orderorange.com gets every screenshot moved out into
wwwroot/guide/<hash>.jpg so the page itself is ~1 MB of text and the pictures load lazily as
the reader scrolls. Run after gen3.py."""
import io, os, re, base64, hashlib, glob
os.chdir(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join('src', 'orderorange-partner-guide.html')   # the artifact body (copied here from the session scratchpad, 2026-10-15)
WWW = r'D:\OrderOrange\OrderOrange.RestaurantWeb\wwwroot'
IMG = os.path.join(WWW, 'guide')
os.makedirs(IMG, exist_ok=True)
page = io.open(SRC, encoding='utf-8').read()
for f in glob.glob(os.path.join(IMG, '*.jpg')): os.remove(f)   # names carry the hash; stale ones go
from PIL import Image as _Im
_Im.open(os.path.join('src', 'orderorange-app-icon-1024.png')).resize((180, 180), _Im.LANCZOS).save(os.path.join(IMG, 'apple-touch-icon.png'))
n = 0
def swap(m):
    global n
    data = base64.b64decode(m.group(2))
    name = hashlib.sha1(data).hexdigest()[:12] + '.jpg'
    with open(os.path.join(IMG, name), 'wb') as fh: fh.write(data)
    n += 1
    return f'src="guide/{name}"'
page = re.sub(r'src="data:image/(jpeg|jpg);base64,([A-Za-z0-9+/=]+)"', swap, page)
# every screenshot decodes off the main thread and only when it is about to be seen
page = page.replace('loading="lazy">', 'loading="lazy" decoding="async">')
# the artifact viewer wraps the page in its own <html>/<head>/<body>; the hosted copy needs them itself
if not page.lstrip().startswith('<!doctype'):
    page = ('<!doctype html>\n<html lang="en"><head><meta charset="utf-8">'
            '<meta name="viewport" content="width=device-width,initial-scale=1">'
            # the same tab icon as the app itself, plus a home-screen icon for phones
            '<link rel="icon" type="image/svg+xml" href="_content/OrderOrange.ClientCore/logo.svg?v=4">'
            '<link rel="apple-touch-icon" sizes="180x180" href="guide/apple-touch-icon.png">'
            '<meta name="theme-color" content="#FF5A00">' + page)
    cut = page.index('</style>') + len('</style>')
    page = page[:cut] + '\n</head><body>' + page[cut:] + '\n</body></html>\n'
out = os.path.join(WWW, 'guide.html')
io.open(out, 'w', encoding='utf-8', newline='').write(page)
print('hosted guide:', round(len(page) / 1e6, 2), 'MB html,', n, 'images in', IMG,
      round(sum(os.path.getsize(f) for f in glob.glob(os.path.join(IMG, '*.jpg'))) / 1e6, 2), 'MB')
