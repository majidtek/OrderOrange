import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# 1. crop the POS frame: the desktop POS shot is mostly empty below the dish grid
rep("hero_side = shot(ns['F']('0010_'), 'POS', '/pos')", "hero_side = shot(pos_key, 'POS', '/pos')")
rep("def sec(n, id, title, items):", """def crop_top(key, keep=0.56):
    src = [f for f in glob.glob('frame_*.png') if f[:-4] == key][0]
    im = Image.open(src).convert('RGB'); im = im.crop((0, 0, im.width, int(im.height * keep)))
    if im.width > 1000: im = im.resize((1000, int(im.height * 1000 / im.width)), Image.LANCZOS)
    b = io.BytesIO(); im.save(b, 'JPEG', quality=82, optimize=True)
    img[key + '_crop'] = (base64.b64encode(b.getvalue()).decode(), im.width, im.height)
    return key + '_crop'
pos_key = crop_top(ns['F']('0010_'))

def sec(n, id, title, items):""")
# the POS article uses the cropped frame too
rep("    for j, (k, t, d) in enumerate(items):\n        path, acts = META[t]",
    "    for j, (k, t, d) in enumerate(items):\n        if t == 'POS': k = pos_key\n        path, acts = META[t]")

# 2. tree menu with search: chapters expand into their screens
rep("toc = '<a href=\"#start\" style=\"--tint:#FF5A00\"><em>00</em>The basics</a>' + toc + '<a href=\"#faq\" style=\"--tint:#475569\"><em>?</em>Questions</a>'",
"""def tree_item(i, n, t, items, tint):
    kids = ''.join(f'<li><a href="#{i}-{j+1}" data-q="{html.escape((t + " " + tt + " " + " ".join(META[tt][1])).lower())}">{html.escape(tt)}</a></li>' for j, (_, tt, _) in enumerate(items))
    return (f'<li class="tn" style="--tint:{tint}"><a href="#{i}" class="tn-h" data-q="{html.escape(t.lower())}"><em>{n}</em><span>{html.escape(t)}</span>'
            f'<b>{len(items)}</b><i class="tw"></i></a><ul>{kids}</ul></li>')
tree = ('<div class="tsearch"><svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>'
        '<input id="tq" type="search" placeholder="Search the guide…" autocomplete="off"><kbd>/</kbd></div><ul class="tree">'
        + '<li class="tn" style="--tint:#FF5A00"><a href="#start" class="tn-h" data-q="the basics sign in role languages bell search till"><em>00</em><span>The basics</span></a></li>'
        + ''.join(tree_item(i, f'{n+1:02d}', t, items, TINT[i]) for n, (i, t, items) in enumerate(parts))
        + '<li class="tn" style="--tint:#475569"><a href="#faq" class="tn-h" data-q="questions faq"><em>?</em><span>Questions</span></a></li></ul><div class="tnone">No page matches. Try another word.</div>')
toc = '<a href="#start" style="--tint:#FF5A00"><em>00</em>The basics</a>' + toc + '<a href="#faq" style="--tint:#475569"><em>?</em>Questions</a>'""")
# screens get anchors for the tree
rep("h += (f'<article class=\"{\"flip\" if j % 2 else \"\"}\"><div class=\"txt\">", "h += (f'<article id=\"{id}-{j+1}\" class=\"{\"flip\" if j % 2 else \"\"}\"><div class=\"txt\">")
# layout: tree on desktop, chips on phones
rep("f'<div class=\"wrap\"><nav class=\"toc\">{toc}</nav><main>{body}</main></div>\\n'",
    "f'<div class=\"wrap\"><nav class=\"toc\">{toc}</nav><aside class=\"side\">{tree}</aside><main>{body}</main></div>\\n'")
rep(".wrap{max-width:1220px;margin:0 auto;padding:40px 22px 60px;display:grid;grid-template-columns:1fr;gap:34px}",
    """.wrap{max-width:1220px;margin:0 auto;padding:40px 22px 60px;display:grid;grid-template-columns:1fr;gap:34px}
.side{display:none}
.tsearch{display:flex;align-items:center;gap:8px;background:var(--surface);border:1px solid var(--line);border-radius:14px;padding:9px 12px;color:var(--muted);box-shadow:0 8px 20px rgba(70,35,5,.05);transition:border-color .15s,box-shadow .15s}
.tsearch:focus-within{border-color:#FFB48A;box-shadow:0 0 0 4px rgba(255,90,0,.10)}
.tsearch input{flex:1;min-width:0;border:0;outline:0;background:none;font:600 13.5px Manrope,sans-serif;color:var(--ink)}
.tsearch input::placeholder{color:var(--muted);font-weight:500}
.tsearch kbd{font:700 10.5px Manrope,sans-serif;color:var(--muted);border:1px solid var(--line);border-radius:6px;padding:2px 6px;background:var(--bg-2)}
.tree{list-style:none;margin:12px 0 0;padding:0;display:grid;gap:2px}
.tree ul{list-style:none;margin:0;padding:2px 0 6px 30px;display:none;gap:1px}
.tn.open > ul{display:grid}
.tn-h{display:flex;align-items:center;gap:9px;text-decoration:none;padding:8px 10px;border-radius:12px;color:var(--ink-2);font:700 13px Manrope,sans-serif;transition:background .15s,color .15s}
.tn-h em{font-style:normal;font:800 10.5px/1 Manrope,sans-serif;color:#fff;background:var(--tint);border-radius:999px;padding:5px 7px;min-width:26px;text-align:center}
.tn-h span{flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.tn-h b{font:800 10.5px Manrope,sans-serif;color:var(--muted);background:var(--bg-2);border-radius:999px;padding:3px 7px}
.tn-h .tw{width:8px;height:8px;border-right:2px solid var(--muted);border-bottom:2px solid var(--muted);transform:rotate(-45deg);transition:transform .2s;margin-inline-end:4px}
.tn.open > .tn-h .tw{transform:rotate(45deg)}
.tn-h:hover,.tn.on > .tn-h{background:color-mix(in srgb,var(--tint) 10%,#fff);color:var(--tint)}
.tree ul a{display:block;text-decoration:none;font:600 12.5px/1.3 Manrope,sans-serif;color:var(--muted);padding:6px 10px;border-radius:9px;position:relative}
.tree ul a::before{content:"";position:absolute;left:-14px;top:50%;width:6px;height:6px;border-radius:50%;background:var(--line);transform:translateY(-50%)}
.tree ul a:hover,.tree ul a.on{color:var(--tint);background:color-mix(in srgb,var(--tint) 8%,#fff)}
.tree ul a.on::before{background:var(--tint)}
.tree .hide{display:none!important}
.tree mark{background:color-mix(in srgb,var(--tint) 22%,#fff);color:inherit;border-radius:3px;padding:0 1px}
.tnone{display:none;font:600 12.5px Manrope,sans-serif;color:var(--muted);padding:10px 12px}
.tnone.show{display:block}""")
rep("  .wrap{grid-template-columns:232px minmax(0,1fr);gap:48px}\n  .toc{position:sticky;top:96px;align-self:start;flex-direction:column;overflow:visible;margin:0;padding:0;background:none;backdrop-filter:none;gap:4px}\n  .toc a{border:0;background:none;padding:9px 10px;border-radius:12px;color:var(--muted)}\n  .toc a.on,.toc a:hover{background:color-mix(in srgb,var(--tint) 10%,#fff);color:var(--tint)}",
    "  .wrap{grid-template-columns:264px minmax(0,1fr);gap:44px}\n  .toc{display:none}\n  .side{display:block;position:sticky;top:84px;align-self:start;max-height:calc(100vh - 100px);overflow:auto;scrollbar-width:thin;padding-right:4px}")
# search + tree behaviour
rep("var lb=document.createElement('div');", """var tq=document.getElementById('tq'),tree=document.querySelector('.tree'),none=document.querySelector('.tnone');
if(tree){[].slice.call(tree.querySelectorAll('.tn-h')).forEach(function(h){h.addEventListener('click',function(e){var li=h.parentElement;if(li.querySelector('ul')){var wasOpen=li.classList.contains('open');[].slice.call(tree.querySelectorAll('.tn.open')).forEach(function(o){if(o!==li)o.classList.remove('open')});li.classList.toggle('open',!wasOpen)}})});
function filt(){var q=(tq.value||'').trim().toLowerCase();var any=false;[].slice.call(tree.querySelectorAll('.tn')).forEach(function(li){var head=li.querySelector('.tn-h');var kids=[].slice.call(li.querySelectorAll('ul a'));var hit=false;kids.forEach(function(a){var ok=!q||(a.getAttribute('data-q')||'').indexOf(q)>=0;a.classList.toggle('hide',!ok);if(ok)hit=true;var txt=a.textContent;a.innerHTML=q&&ok&&txt.toLowerCase().indexOf(q)>=0?txt.replace(new RegExp('('+q.replace(/[.*+?^${}()|[\\]\\\\]/g,'\\\\$&')+')','ig'),'<mark>$1</mark>'):txt});var hh=!q||(head.getAttribute('data-q')||'').indexOf(q)>=0;var show=hh||hit;li.classList.toggle('hide',!show);if(show)any=true;li.classList.toggle('open',!!q&&hit)});none.classList.toggle('show',!any)}
tq.addEventListener('input',filt);document.addEventListener('keydown',function(e){if(e.key==='/'&&document.activeElement!==tq&&!/input|textarea/i.test(document.activeElement.tagName)){e.preventDefault();tq.focus()}if(e.key==='Escape'&&document.activeElement===tq){tq.value='';filt();tq.blur()}});
var sub=[].slice.call(tree.querySelectorAll('ul a'));var subT=sub.map(function(a){return document.querySelector(a.getAttribute('href'))});
function onTree(){var y=window.scrollY+160,i=-1;subT.forEach(function(s,k){if(s&&s.offsetTop<=y)i=k});sub.forEach(function(a,k){a.classList.toggle('on',k===i)});var tops=[].slice.call(tree.querySelectorAll('.tn'));var cur=-1;tops.forEach(function(li,k){var s=document.querySelector(li.querySelector('.tn-h').getAttribute('href'));if(s&&s.offsetTop<=y)cur=k});tops.forEach(function(li,k){li.classList.toggle('on',k===cur);if(k===cur&&!tq.value)li.classList.add('open')})}
window.addEventListener('scroll',onTree,{passive:true});onTree();}
var lb=document.createElement('div');""")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
