import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# phones: the same tree menu lives in a drawer that slides in from the side; a menu button in the bar opens it
rep(""" '<button class="tsb" id="tsb" aria-label="Search the guide">""",
    """ '<button class="tmb" id="tmb" aria-label="Menu"><i></i><i></i><i></i></button>'
 '<button class="tsb" id="tsb" aria-label="Search the guide">""")
s = s.replace(".side{display:none}", """.side{display:none}
.tmb{width:36px;height:36px;border-radius:50%;border:1px solid var(--line);background:var(--surface);display:grid;place-items:center;gap:0;cursor:pointer;margin-inline-start:auto;margin-inline-end:8px;padding:0}
.tmb i{display:block;width:16px;height:2px;background:var(--ink);border-radius:2px;margin:2px 0}
.tmb i:nth-child(2){width:11px}
.dback{position:fixed;inset:0;background:rgba(20,14,9,.45);backdrop-filter:blur(3px);z-index:18;display:none}
.dback.on{display:block}
.side.drawer{display:block;position:fixed;top:0;bottom:0;right:0;width:min(320px,86vw);background:var(--surface);z-index:19;padding:16px 14px 24px;overflow:auto;box-shadow:-24px 0 60px rgba(20,14,9,.25);transform:translateX(105%);transition:transform .28s cubic-bezier(.16,1,.3,1)}
.side.drawer.on{transform:none}
.side.drawer .dhead{display:flex;align-items:center;gap:10px;margin-bottom:10px}
.side.drawer .dhead b{font:800 14px Manrope,sans-serif;flex:1}
.side.drawer .dhead button{width:32px;height:32px;border-radius:50%;border:1px solid var(--line);background:#fff;font:800 16px/30px Manrope,sans-serif;cursor:pointer}""", 1)
rep("  .top .tsb{margin-inline-start:0}", "  .top .tsb{margin-inline-start:0}\n  .tmb{display:none}\n  .side.drawer{position:static;width:auto;transform:none;box-shadow:none;padding:0;background:none;display:block}\n  .side.drawer .dhead,.dback{display:none!important}")
# the tree gets a drawer header on phones (hidden on desktop by the rule above)
rep("""f'<div class="wrap"><nav class="toc">{toc}</nav><aside class="side">{tree}</aside><main>{body}</main></div>\\n'""",
    """f'<div class="dback" id="dback"></div><div class="wrap"><nav class="toc">{toc}</nav><aside class="side drawer" id="side"><div class="dhead"><span class="mark">O</span><b>Guide menu</b><button type="button" id="dclose" aria-label="Close">×</button></div>{tree}</aside><main>{body}</main></div>\\n'""")
# drawer behaviour: menu button opens, backdrop/close/any link click closes
rep("var cb=document.getElementById('cbtn'),",
    "var side=document.getElementById('side'),dback=document.getElementById('dback'),tmb=document.getElementById('tmb');function dOpen(){side.classList.add('on');dback.classList.add('on');document.body.style.overflow='hidden'}function dClose(){side.classList.remove('on');dback.classList.remove('on');document.body.style.overflow=''}if(tmb){tmb.addEventListener('click',dOpen)}dback.addEventListener('click',dClose);document.getElementById('dclose').addEventListener('click',dClose);side.addEventListener('click',function(e){var a=e.target.closest('a');if(a&&a.getAttribute('href')&&a.getAttribute('href').charAt(0)==='#'&&!e.target.closest('.tw'))dClose()});document.addEventListener('keydown',function(e){if(e.key==='Escape'&&side.classList.contains('on'))dClose()}); "
    "var cb=document.getElementById('cbtn'),")
# on phones the search box inside the drawer should filter the tree itself, not open the palette
rep("var ts=document.querySelector('.tsearch');if(ts&&matchMedia('(max-width:899px)').matches){ts.addEventListener('click',openPal)}", "")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
