# -*- coding: utf-8 -*-
"""The API reference was reachable only from one screen deep in Setup. Link it from the
top bar, the drawer menu, the basics and the footer. Idempotent."""
import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
s = io.open('gen3.py', encoding='utf-8').read()
API = 'https://partner.orderorange.com/api.html'

# 1. top bar (shown from 901px up)
a = "'<nav class=\"links\"><a href=\"#sell\">Selling</a><a href=\"#orders\">Orders</a><a href=\"#menu\">Menu</a><a href=\"#money\">Money</a><a href=\"#team\">Team</a><a href=\"#reports\">Reports</a></nav>'"
b = "'<nav class=\"links\"><a href=\"#sell\">Selling</a><a href=\"#orders\">Orders</a><a href=\"#menu\">Menu</a><a href=\"#money\">Money</a><a href=\"#team\">Team</a><a href=\"#reports\">Reports</a><a href=\"api.html\">API</a></nav>'"
if 'href=\\"api.html\\">API</a></nav>' not in s and '<a href="api.html">API</a></nav>' not in s:
    assert s.count(a) == 1, ('topbar', s.count(a)); s = s.replace(a, b)

# 2. footer, Product column
a2 = '<li><a href="https://api.orderorange.com/api/till/download">Till app for Windows</a></li>'
b2 = a2 + f'<li><a href="{API}">Developer API</a></li>'
if 'Developer API</a></li>' not in s:
    assert s.count(a2) == 1, ('footer', s.count(a2)); s = s.replace(a2, b2)

# 3. the basics: one more card, with the link in the text
a3 = " ('Large restaurants and chains',"
b3 = (" ('Connect your own systems', 'Everything the app does is also a REST API: menus, orders, tables, reservations, customers, bills and reports, with the same login. "
      f'Accounting packages, CRMs, kitchen screens and head-office systems connect through it. The full reference with live examples is at <a href="{API}">partner.orderorange.com/api.html</a>.\'),\n'
      " ('Large restaurants and chains',")
if "'Connect your own systems'" not in s:
    assert s.count(a3) == 1, ('intro', s.count(a3)); s = s.replace(a3, b3)

# 4. drawer menu: a standalone entry under the chapters
a4 = "<li class=\"tn\" style=\"--tint:#FF5A00\"><a href=\"#start\" class=\"tn-h\" data-q=\"the basics sign in role languages bell search till\"><em>00</em><span>The basics</span></a></li>"
b4 = a4 + f"<li class=\"tn\" style=\"--tint:#7C3AED\"><a href=\"{API}\" class=\"tn-h\" data-q=\"api rest integration crm accounting developer\"><em>{{}}</em><span>Developer API</span></a></li>".replace('{}', '&lt;/&gt;')
if 'Developer API</span></a></li>' not in s:
    assert s.count(a4) == 1, ('tree', s.count(a4)); s = s.replace(a4, b4)

io.open('gen3.py', 'w', encoding='utf-8', newline='').write(s)
print('api links: topbar, footer, basics, drawer')
