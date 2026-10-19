"""Full translation of the hosted partner guide (Majed 2026-10-15: "guide.html is not full
multilanguage" — until now only the hero and the meta tags were translated and the body
stayed English).

Run AFTER host.py and BEFORE seo.py:
  * reads wwwroot/guide.html (the pristine English page),
  * pulls out every piece of visible English (text nodes plus title/placeholder/aria-label/alt
    attributes, skipping <script>, <style>, <svg>, <kbd>, <code>),
  * translates what the per-language cache i18n/<lang>.json does not have yet, with xAI Grok
    (the same key the assistants use, read from the API's appsettings — never printed),
  * and exposes translate_page(html, lang) which seo.py calls for every variant.

Re-running costs nothing for text that was translated before: only new or changed English
goes to the model. Delete an entry from the cache to have it redone.
"""
import io, os, re, sys, json, html, time, threading
from concurrent.futures import ThreadPoolExecutor
import urllib.request

os.chdir(os.path.dirname(os.path.abspath(__file__)))
WWW = r'D:\OrderOrange\OrderOrange.RestaurantWeb\wwwroot'
CACHE_DIR = 'i18n'
os.makedirs(CACHE_DIR, exist_ok=True)

LANGS = {
    'ar': 'Arabic (Modern Standard, Arabic script)',
    'fa': 'Persian / Farsi (Persian script with ی and ک, Iranian usage)',
    'ur': 'Urdu (Urdu script)',
    'hi': 'Hindi (Devanagari script)',
    'de': 'German', 'es': 'Spanish', 'fr': 'French', 'it': 'Italian', 'ja': 'Japanese',
    'pt': 'Portuguese (European)', 'ru': 'Russian', 'tr': 'Turkish', 'zh': 'Simplified Chinese',
}

# Things that are the same in every language — never sent to the model.
KEEP = {'OrderOrange', 'Ctrl+K', 'Esc', 'K', 'Ctrl', 'Saffron Grill House', 'QR', 'POS', 'KDS', 'API', 'PDF', 'Excel',
        'WhatsApp', 'Instagram', 'Google', 'Android', 'iOS', 'Windows', 'OMR', 'x', 'X'}
ATTRS = ('title', 'placeholder', 'aria-label', 'alt', 'data-tip')
SKIP_TAGS = ('script', 'style', 'svg', 'kbd', 'code', 'noscript', 'select', 'option')   # option = the language picker's own names

_tag = re.compile(r'(<[^>]+>)')
_attr = re.compile(r'\b(' + '|'.join(ATTRS) + r')="([^"]*)"')
_latin = re.compile(r'[A-Za-z]')


def _norm(s):
    return re.sub(r'\s+', ' ', s).strip()


def _translatable(s):
    s = _norm(s)
    if len(s) < 2 or not _latin.search(s) or s in KEEP: return False
    if re.fullmatch(r'[\W\d_]*[A-Za-z][\W\d_]*', s): return False      # a lone letter
    if re.fullmatch(r'https?://\S+|[\w.-]+@[\w.-]+', s): return False  # links, addresses
    return True


def walk(page):
    """Yields (kind, text) for every translatable piece, in page order.
    kind = 'text' (a text node) or 'attr' (an attribute value)."""
    skip = []
    for tok in _tag.split(page):
        if not tok: continue
        if tok.startswith('<'):
            m = re.match(r'<(/?)([a-zA-Z0-9]+)', tok)
            if m:
                name = m.group(2).lower()
                if name in SKIP_TAGS:
                    if m.group(1):
                        if skip and skip[-1] == name: skip.pop()
                    elif not tok.endswith('/>'):
                        skip.append(name)
            if not skip or tok.startswith('<' + '/'):
                for am in _attr.finditer(tok):
                    v = html.unescape(am.group(2))
                    if _translatable(v): yield 'attr', _norm(v)
            continue
        if skip: continue
        v = html.unescape(tok)
        if _translatable(v): yield 'text', _norm(v)


def segments(page):
    seen, out = set(), []
    for _, s in walk(page):
        if s not in seen:
            seen.add(s); out.append(s)
    return out


# ---------------------------------------------------------------- the model
def _grok_config():
    cfg = json.load(io.open(r'D:\OrderOrange\publish-api\appsettings.json', encoding='utf-8-sig'))
    g = cfg.get('Grok', {})
    return g.get('ApiKey', ''), g.get('Model') or 'grok-4.7', g.get('Endpoint') or 'https://api.x.ai/v1/responses'


def _ask(key, model, endpoint, lang, items):
    system = (
        f"You are a professional translator localising the user guide of OrderOrange, a restaurant point-of-sale and "
        f"ordering platform, from English into {LANGS[lang]}.\n"
        "You receive a JSON array of strings. Reply with ONLY a JSON array of strings: the same length, the same order, "
        "each item the translation of the input item. No prose, no code fence, no numbering.\n"
        "Rules: natural, concise, professional wording a restaurant owner would read; keep the meaning exactly. "
        "Keep unchanged: brand and product names (OrderOrange, Saffron Grill House, Instagram, WhatsApp, Google, Excel, PDF), "
        "abbreviations POS, QR, KDS, API, VAT, keyboard keys (Ctrl+K, Esc), numbers, prices, currency codes, URLs, e-mail "
        "addresses, emoji and the characters → ↗ · … ↑↓ ↵. If an item contains HTML tags such as <b>…</b>, <i>…</i> or "
        "<a …>…</a>, keep every tag exactly as it is and translate only the text between tags. Keep the same ending "
        "punctuation. Never leave an item empty and never merge or split items."
    )
    body = {
        'model': model,
        'input': [{'role': 'system', 'content': system}, {'role': 'user', 'content': json.dumps(items, ensure_ascii=False)}],
        'max_output_tokens': 8000, 'temperature': 0.2, 'reasoning': {'effort': 'low'},
    }
    req = urllib.request.Request(endpoint, data=json.dumps(body).encode('utf-8'),
                                 headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=180) as r:
        data = json.loads(r.read().decode('utf-8'))
    text = ''
    for item in data.get('output', []):
        for c in item.get('content', []) or []:
            if c.get('type') == 'output_text': text += c.get('text', '')
    s, e = text.find('['), text.rfind(']')
    if s < 0 or e <= s: raise ValueError('no JSON array in reply')
    arr = json.loads(text[s:e + 1])
    if not isinstance(arr, list) or len(arr) != len(items): raise ValueError(f'got {len(arr) if isinstance(arr, list) else "?"} items for {len(items)}')
    out = []
    for src, dst in zip(items, arr):
        dst = (dst if isinstance(dst, str) else '').strip()
        if not dst: raise ValueError('empty translation')
        out.append(dst)
    return out


def _load(lang):
    p = os.path.join(CACHE_DIR, f'{lang}.json')
    return json.load(io.open(p, encoding='utf-8')) if os.path.exists(p) else {}


def _save(lang, cache):
    p = os.path.join(CACHE_DIR, f'{lang}.json')
    io.open(p, 'w', encoding='utf-8', newline='\n').write(json.dumps(cache, ensure_ascii=False, indent=1, sort_keys=True))


def fill(lang, segs, workers=6, batch=20):
    key, model, endpoint = _grok_config()
    if not key: raise SystemExit('Grok:ApiKey is empty in publish-api appsettings')
    cache = _load(lang)
    todo = [s for s in segs if s not in cache]
    if not todo:
        print(f'{lang}: cache complete ({len(cache)})'); return cache
    lock = threading.Lock()
    done = [0]; failed = [0]

    def run(chunk):
        for attempt in range(3):
            try:
                res = _ask(key, model, endpoint, lang, chunk)
                with lock:
                    for s, d in zip(chunk, res): cache[s] = d
                    done[0] += len(chunk)
                    if done[0] % 100 < len(chunk): _save(lang, cache)
                return
            except Exception as ex:
                err = ex
                time.sleep(2 + 3 * attempt)
        if len(chunk) > 1:
            half = len(chunk) // 2
            run(chunk[:half]); run(chunk[half:])
        else:
            with lock: failed[0] += 1
            print(f'  {lang}: FAILED "{chunk[0][:60]}": {err}', file=sys.stderr)

    chunks = [todo[i:i + batch] for i in range(0, len(todo), batch)]
    print(f'{lang}: translating {len(todo)} segments in {len(chunks)} batches…', flush=True)
    t0 = time.time()
    with ThreadPoolExecutor(max_workers=workers) as ex:
        list(ex.map(run, chunks))
    _save(lang, cache)
    print(f'{lang}: done {done[0]}, failed {failed[0]}, {time.time() - t0:.0f}s, cache {len(cache)}', flush=True)
    return cache


# ---------------------------------------------------------------- applying a cache to a page
def translate_page(page, lang):
    cache = _load(lang)
    if not cache: return page
    # A half-translated page is worse than an English one: only apply a cache that covers
    # (nearly) everything on the page — a run that is still in progress is left alone.
    segs = segments(page)
    covered = sum(1 for s in segs if s in cache)
    if covered < 0.95 * len(segs):
        print(f'i18n: {lang} cache covers {covered}/{len(segs)} — page left in English')
        return page
    skip = []
    out = []
    for tok in _tag.split(page):
        if not tok: continue
        if tok.startswith('<'):
            m = re.match(r'<(/?)([a-zA-Z0-9]+)', tok)
            if m:
                name = m.group(2).lower()
                if name in SKIP_TAGS:
                    if m.group(1):
                        if skip and skip[-1] == name: skip.pop()
                    elif not tok.endswith('/>'):
                        skip.append(name)
            if not skip:
                def rep(am):
                    v = html.unescape(am.group(2))
                    if _translatable(v) and _norm(v) in cache:
                        return f'{am.group(1)}="{html.escape(cache[_norm(v)], quote=True)}"'
                    return am.group(0)
                tok = _attr.sub(rep, tok)
            out.append(tok); continue
        if skip: out.append(tok); continue
        v = html.unescape(tok)
        if _translatable(v) and _norm(v) in cache:
            lead = tok[:len(tok) - len(tok.lstrip())]
            trail = tok[len(tok.rstrip()):]
            out.append(lead + html.escape(cache[_norm(v)], quote=False) + trail)
        else:
            out.append(tok)
    return ''.join(out)


if __name__ == '__main__':
    src = io.open(os.path.join(WWW, 'guide.html'), encoding='utf-8').read()
    segs = segments(src)
    print(f'{len(segs)} English segments, {sum(len(s) for s in segs)} chars')
    only = [a for a in sys.argv[1:] if a in LANGS] or list(LANGS)
    for lang in only:
        fill(lang, segs)
