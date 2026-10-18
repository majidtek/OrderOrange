import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# mobile top bar: logo + title, a round search button, a short "Open app" pill; chapter links and the position readout stay desktop-only
rep(""" '<a class="cta" href="https://partner.orderorange.com" target="_blank" rel="noopener">Open partner app</a></div></div>\\n'""",
    """ '<button class="tsb" id="tsb" aria-label="Search the guide"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg></button>'
 '<a class="cta" href="https://partner.orderorange.com" target="_blank" rel="noopener"><span>Open partner app</span><i>Open app</i></a></div></div>\\n'""")
rep(".top .cta{margin-inline-start:auto;text-decoration:none;font:800 12.5px Manrope,sans-serif;color:#fff;background:var(--ink);padding:9px 14px;border-radius:999px;white-space:nowrap}",
    """.top .cta{text-decoration:none;font:800 12.5px Manrope,sans-serif;color:#fff;background:var(--ink);padding:9px 14px;border-radius:999px;white-space:nowrap}
.top .cta i{font-style:normal}.top .cta span{display:none}
.top .tsb{margin-inline-start:auto;width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);color:var(--ink-2);display:grid;place-items:center;cursor:pointer;margin-inline-end:8px}
.top .tsb:hover{border-color:var(--accent);color:var(--accent-deep)}
.top .in{padding:10px 14px}
.top b{font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0}""")
rep("  .top .links{display:flex}\n  .top .where{display:inline-flex}",
    "  .top .links{display:flex}\n  .top .where{display:inline-flex}\n  .top .in{padding:12px 22px}\n  .top b{font-size:15px}\n  .top .tsb{margin-inline-start:0}\n  .top .cta span{display:inline}.top .cta i{display:none}")
# the search button opens the palette
rep("var ts=document.querySelector('.tsearch');if(ts&&matchMedia('(max-width:899px)').matches){ts.addEventListener('click',openPal)}",
    "var ts=document.querySelector('.tsearch');if(ts&&matchMedia('(max-width:899px)').matches){ts.addEventListener('click',openPal)}var tsb=document.getElementById('tsb');if(tsb){tsb.addEventListener('click',openPal)}")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
