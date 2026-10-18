import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# footer markup: brand, chapter map, product links, help, bottom bar
rep(""" '<footer><span>OrderOrange · Partner app guide · September 2026</span><span>Numbers shown are demo data</span></footer>\\n'""",
    """ '<footer class="ft"><div class="ft-in">'
 '<div class="ft-brand"><span class="mark">O</span><b>OrderOrange</b><p>One app for the counter, the kitchen, the floor and the books. Built for restaurants and shops in Oman.</p>'
 '<div class="ft-badges"><span>14 languages</span><span>Phone · tablet · desktop</span><span>Arabic &amp; Persian tickets</span></div></div>'
 f'<div class="ft-col"><h4>Chapters</h4><ul>{ftmap}</ul></div>'
 '<div class="ft-col"><h4>Product</h4><ul><li><a href="https://partner.orderorange.com" target="_blank" rel="noopener">Partner app</a></li><li><a href="https://www.orderorange.com" target="_blank" rel="noopener">Customer website</a></li><li><a href="https://api.orderorange.com/api/till/download">Till app for Windows</a></li><li><a href="https://partner.orderorange.com/register" target="_blank" rel="noopener">Join as a partner</a></li></ul></div>'
 '<div class="ft-col"><h4>Help</h4><ul><li><a href="#faq">Frequently asked</a></li><li><a href="#start">Before you start</a></li><li><a href="https://partner.orderorange.com/support" target="_blank" rel="noopener">Support desk</a></li><li><a href="mailto:info@orderorange.com">info@orderorange.com</a></li><li><a href="https://www.orderorange.com" target="_blank" rel="noopener">orderorange.com</a></li></ul></div>'
 '</div><div class="ft-bar"><span>© 2026 OrderOrange · Partner app guide, edition 1</span><span>Screens and numbers are demo data from Saffron Grill House</span><a class="ft-mail" href="mailto:info@orderorange.com">info@orderorange.com</a><a class="ft-top" href="#top" id="totop">Back to top ↑</a></div></footer>\\n'""")
rep("body = intro_html + ", "ftmap = ''.join(f'<li><a href=\"#{i}\">{html.escape(t)}</a></li>' for i, t, _ in parts)\nbody = intro_html + ")
rep('<div class="progress" id="prog"></div>', '<div class="progress" id="prog"></div><a id="top"></a>')

# footer style: dark band, warm glow, four columns collapsing to one
old_a = s.index("footer{max-width:1220px;margin:0 auto;padding:18px 22px 48px;")
old_b = s.index("\n", old_a)
s = s[:old_a] + """.ft{margin-top:20px;background:linear-gradient(180deg,#1A130D,#120D09);color:#fff;position:relative;overflow:hidden}
.ft::before{content:"";position:absolute;width:640px;height:640px;left:-200px;top:-360px;border-radius:50%;background:rgba(255,122,42,.22);filter:blur(80px)}
.ft::after{content:"";position:absolute;inset:0;background-image:radial-gradient(rgba(255,255,255,.06) 1px,transparent 1px);background-size:22px 22px;pointer-events:none}
.ft-in{position:relative;max-width:1220px;margin:0 auto;padding:54px 22px 30px;display:grid;grid-template-columns:1fr;gap:30px;z-index:1}
.ft-brand{display:grid;gap:12px;max-width:34ch;justify-items:start}
.ft-brand .mark{width:40px;height:40px;border-radius:12px;font-size:18px}
.ft-brand b{font:800 20px Manrope,sans-serif;letter-spacing:-.4px}
.ft-brand p{margin:0;color:rgba(255,255,255,.72);font-size:14.5px;line-height:1.6}
.ft-badges{display:flex;flex-wrap:wrap;gap:6px}
.ft-badges span{font:700 11px Manrope,sans-serif;color:rgba(255,255,255,.85);border:1px solid rgba(255,255,255,.18);background:rgba(255,255,255,.06);border-radius:999px;padding:5px 10px}
.ft-col h4{margin:0 0 12px;font:800 11px/1 Manrope,sans-serif;letter-spacing:1.6px;text-transform:uppercase;color:#FF9A4D}
.ft-col ul{list-style:none;margin:0;padding:0;display:grid;gap:8px}
.ft-col a{text-decoration:none;color:rgba(255,255,255,.78);font:600 14px Manrope,sans-serif;transition:color .15s,transform .15s;display:inline-block}
.ft-col a:hover{color:#fff;transform:translateX(3px)}
.ft-bar{position:relative;z-index:1;max-width:1220px;margin:0 auto;padding:18px 22px 34px;border-top:1px solid rgba(255,255,255,.12);display:flex;flex-wrap:wrap;gap:10px 22px;align-items:center;font:600 12.5px Manrope,sans-serif;color:rgba(255,255,255,.55)}
.ft-mail{text-decoration:none;color:rgba(255,255,255,.85);font:700 12.5px Manrope,sans-serif}.ft-mail:hover{color:#FF9A4D}
.ft-top{margin-inline-start:auto;text-decoration:none;color:#fff;background:rgba(255,255,255,.1);border:1px solid rgba(255,255,255,.18);border-radius:999px;padding:8px 14px;font:800 12.5px Manrope,sans-serif;transition:background .15s}
.ft-top:hover{background:var(--accent)}
@media (min-width:760px){.ft-in{grid-template-columns:1.4fr 1fr 1fr 1fr;gap:40px}}""" + s[old_b:]
# the old light footer rule inside the end-card block no longer applies; keep the end card above the footer tight
rep(".end{max-width:1220px;margin:10px auto 0;padding:0 22px 40px}", ".end{max-width:1220px;margin:10px auto 0;padding:0 22px 54px}")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
