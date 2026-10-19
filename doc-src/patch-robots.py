import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
q = io.open('seo.py', encoding='utf-8').read()
old = "Allow: /guide/\\nDisallow: /\\n"      # inside seo.py these are literal backslash-n escapes
new = "Allow: /guide/\\nAllow: /api.html\\nDisallow: /\\n"
if 'Allow: /api.html' not in q:
    assert q.count(old) == 1, q.count(old)
    q = q.replace(old, new)
    io.open('seo.py', 'w', encoding='utf-8', newline='').write(q)
print('robots allow api.html:', 'Allow: /api.html' in q)
