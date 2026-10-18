# -*- coding: utf-8 -*-
"""The guide's menu, search palette and scroll-spy all died the moment an outside link
joined the contents.

document.querySelector('https://partner.orderorange.com/api.html') is not a selector, it
is a SyntaxError, and it was thrown at the top level of the page script — so every line
after it, including the line that wires the three-line button to the drawer, never ran.
On a phone that reads as "the menu does nothing".

Every place that turns a link into the section it points at now asks first whether the
link points inside this page at all. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

p = 'gen3.py'
s = io.open(p, encoding='utf-8').read()

HELPER = ("function inPage(h){try{return h&&h.charAt(0)==='#'?document.querySelector(h):null}catch(e){return null}}"
          "var links=[].slice.call(document.querySelectorAll('.toc a'));")
old = "var links=[].slice.call(document.querySelectorAll('.toc a'));"
if 'function inPage(' not in s:
    assert s.count(old) == 1, ('toc links', s.count(old))
    s = s.replace(old, HELPER)

pairs = [
    ("var secs=links.map(function(a){return document.querySelector(a.getAttribute('href'))});",
     "var secs=links.map(function(a){return inPage(a.getAttribute('href'))});"),
    ("var subT=sub.map(function(a){return document.querySelector(a.getAttribute('href'))});",
     "var subT=sub.map(function(a){return inPage(a.getAttribute('href'))});"),
    ("var s=document.querySelector(li.querySelector('.tn-h').getAttribute('href'));",
     "var s=inPage(li.querySelector('.tn-h').getAttribute('href'));"),
]
done = 0
for old, new in pairs:
    if new in s:
        continue
    assert s.count(old) == 1, (old[:50], s.count(old))
    s = s.replace(old, new)
    done += 1

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('gen3.py: %d href lookups now ignore links that leave the page' % done)
