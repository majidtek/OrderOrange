import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# 1. chapter opener bands (gradient in the chapter tint)
rep("""    h = (f'<section id="{id}" style="--tint:{TINT[id]}"><div class="sec-head"><span class="wm">{n:02d}</span>'
         f'<span class="eyebrow">Chapter {n:02d} · {len(items)} screens · {read_min[id]} min read</span><h2>{html.escape(title)}</h2><p class="sub">{html.escape(SUB[id])}</p></div>')""",
    """    h = (f'<section id="{id}" style="--tint:{TINT[id]}" data-title="{html.escape(title)}"><div class="band"><span class="wm">{n:02d}</span><div>'
         f'<span class="eyebrow">Chapter {n:02d} · {len(items)} screens · {read_min[id]} min read</span><h2>{html.escape(title)}</h2><p class="sub">{html.escape(SUB[id])}</p></div>'
         f'<ul class="band-list">' + ''.join(f'<li><a href="#{id}-{j+1}">{html.escape(tt)}</a></li>' for j, (_, tt, _) in enumerate(items)) + '</ul></div>')""")
rep('<section id="start" style="--tint:#FF5A00"><div class="sec-head">', '<section id="start" style="--tint:#FF5A00" data-title="The basics"><div class="sec-head">')
rep('<section id="faq" style="--tint:#475569"><div class="sec-head">', '<section id="faq" style="--tint:#475569" data-title="Questions"><div class="sec-head">')
rep(".sec-head{position:relative;display:grid;gap:6px;margin:40px 0 10px;padding-top:18px}",
    """.sec-head{position:relative;display:grid;gap:6px;margin:40px 0 10px;padding-top:18px}
.band{position:relative;overflow:hidden;margin:44px 0 6px;padding:28px 26px 22px;border-radius:26px;color:#fff;background:linear-gradient(135deg,color-mix(in srgb,var(--tint) 88%,#000) 0%,var(--tint) 55%,color-mix(in srgb,var(--tint) 70%,#fff) 100%);display:grid;gap:16px}
.band::after{content:"";position:absolute;width:380px;height:380px;right:-120px;top:-160px;border-radius:50%;background:rgba(255,255,255,.18);filter:blur(40px)}
.band .eyebrow{color:rgba(255,255,255,.85)}.band .eyebrow::before{background:rgba(255,255,255,.85)}
.band h2{color:#fff;margin-top:6px}.band .sub{color:rgba(255,255,255,.82)}
.band .wm{position:absolute;right:18px;top:-8px;font:800 150px/1 Manrope,sans-serif;letter-spacing:-8px;color:#fff;opacity:.12;pointer-events:none}
.band-list{list-style:none;margin:0;padding:0;display:flex;flex-wrap:wrap;gap:6px;position:relative}
.band-list a{display:inline-block;text-decoration:none;color:#fff;font:700 12px Manrope,sans-serif;background:rgba(255,255,255,.16);border:1px solid rgba(255,255,255,.28);border-radius:999px;padding:6px 11px;backdrop-filter:blur(4px);transition:background .15s,transform .15s}
.band-list a:hover{background:rgba(255,255,255,.3);transform:translateY(-1px)}""")

# 2. live position in the top bar
rep('<b>OrderOrange · Partner Guide</b>', '<b>OrderOrange · Partner Guide</b><span class="where" id="where"><i></i><span>Introduction</span><em>0%</em></span>')
rep(".top .links{margin-inline-start:auto;display:none;gap:4px}",
    """.top .where{display:none;align-items:center;gap:8px;margin-inline-start:14px;font:700 12px Manrope,sans-serif;color:var(--muted);padding-inline-start:14px;border-inline-start:1px solid var(--line)}
.top .where i{width:8px;height:8px;border-radius:50%;background:var(--tint,#FF5A00);box-shadow:0 0 0 4px color-mix(in srgb,var(--tint,#FF5A00) 18%,#fff)}
.top .where em{font-style:normal;font-variant-numeric:tabular-nums;color:var(--ink-2);background:var(--bg-2);border-radius:999px;padding:3px 8px}
.top .links{margin-inline-start:auto;display:none;gap:4px}""")
rep("  .top .links{display:flex}", "  .top .links{display:flex}\n  .top .where{display:inline-flex}")

# 3. Ctrl+K command palette (built from the tree data)
rep('<div class="progress" id="prog"></div>\\n\'', """<div class="progress" id="prog"></div>\\n'
 '<div class="pal" id="pal"><div class="pal-box"><div class="pal-in"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg><input id="palq" placeholder="Jump to a page, a chapter or a question…" autocomplete="off"><kbd>Esc</kbd></div><ul class="pal-list" id="pallist"></ul><div class="pal-foot"><span>↑↓ move</span><span>↵ open</span><span>Ctrl+K anywhere</span></div></div></div>\\n'""")
rep(".tnone{display:none;font:600 12.5px Manrope,sans-serif;color:var(--muted);padding:10px 12px}",
    """.tnone{display:none;font:600 12.5px Manrope,sans-serif;color:var(--muted);padding:10px 12px}
.pal{position:fixed;inset:0;background:rgba(20,14,9,.5);backdrop-filter:blur(8px);z-index:30;display:none;align-items:flex-start;justify-content:center;padding:10vh 16px 16px}
.pal.on{display:flex}
.pal-box{width:min(640px,100%);background:#fff;border-radius:20px;box-shadow:0 40px 90px rgba(0,0,0,.35);overflow:hidden;animation:rise .25s cubic-bezier(.16,1,.3,1) both}
.pal-in{display:flex;align-items:center;gap:10px;padding:14px 16px;border-bottom:1px solid var(--line);color:var(--muted)}
.pal-in input{flex:1;border:0;outline:0;font:600 16px Manrope,sans-serif;color:var(--ink);background:none;min-width:0}
.pal-in kbd{font:700 10.5px Manrope,sans-serif;color:var(--muted);border:1px solid var(--line);border-radius:6px;padding:3px 7px;background:var(--bg-2)}
.pal-list{list-style:none;margin:0;padding:8px;max-height:52vh;overflow:auto}
.pal-list li a{display:flex;align-items:center;gap:12px;text-decoration:none;padding:10px 12px;border-radius:12px;color:var(--ink)}
.pal-list li a em{font-style:normal;font:800 10.5px/1 Manrope,sans-serif;color:#fff;background:var(--tint);border-radius:999px;padding:5px 7px;min-width:28px;text-align:center}
.pal-list li a b{font:700 14px Manrope,sans-serif}.pal-list li a small{color:var(--muted);font-size:12px;margin-inline-start:auto;white-space:nowrap}
.pal-list li.sel a,.pal-list li a:hover{background:color-mix(in srgb,var(--tint) 9%,#fff)}
.pal-foot{display:flex;gap:16px;padding:10px 16px;border-top:1px solid var(--line);font:600 11.5px Manrope,sans-serif;color:var(--muted)}
.tsearch{cursor:text}""")

# 4. cursor spotlight in the hero
rep(".hero .in{position:relative;", ".hero .spot{position:absolute;width:520px;height:520px;border-radius:50%;background:radial-gradient(circle,rgba(255,140,60,.28),rgba(255,140,60,0) 60%);pointer-events:none;transform:translate(-50%,-50%);left:70%;top:40%;transition:left .25s ease-out,top .25s ease-out;filter:blur(10px)}\n.hero .in{position:relative;")
rep("'<div class=\"hero\"><div class=\"in\">", "'<div class=\"hero\"><div class=\"spot\" id=\"spot\"></div><div class=\"in\">")

# 5. JS: where-indicator, palette, spotlight, screenshot drift  (one line, appended to the existing script literal)
rep("var lb=document.createElement('div');",
    "var where=document.getElementById('where'),secsAll=[].slice.call(document.querySelectorAll('section[data-title]'));function whereFn(){var y=window.scrollY+120,cur=null;secsAll.forEach(function(s){if(s.offsetTop<=y)cur=s});var d=document.documentElement,m=d.scrollHeight-d.clientHeight,pc=Math.round(m>0?window.scrollY/m*100:0);if(where){where.querySelector('span').textContent=cur?cur.getAttribute('data-title'):'Introduction';where.querySelector('em').textContent=pc+'%';where.style.setProperty('--tint',cur?getComputedStyle(cur).getPropertyValue('--tint'):'#FF5A00')}}window.addEventListener('scroll',whereFn,{passive:true});whereFn(); "
    "var pal=document.getElementById('pal'),palq=document.getElementById('palq'),pallist=document.getElementById('pallist');var entries=[];[].slice.call(document.querySelectorAll('.tree .tn')).forEach(function(li){var h=li.querySelector('.tn-h');var tint=li.style.getPropertyValue('--tint');entries.push({href:h.getAttribute('href'),num:h.querySelector('em').textContent,title:h.querySelector('span').textContent,sub:'Chapter',q:h.getAttribute('data-q')||'',tint:tint});[].slice.call(li.querySelectorAll('ul a')).forEach(function(a){entries.push({href:a.getAttribute('href'),num:h.querySelector('em').textContent,title:a.textContent,sub:h.querySelector('span').textContent,q:a.getAttribute('data-q')||'',tint:tint})})});var sel=0;function render(){var q=(palq.value||'').trim().toLowerCase();var hits=entries.filter(function(e){return !q||e.q.indexOf(q)>=0||e.title.toLowerCase().indexOf(q)>=0}).slice(0,12);sel=Math.min(sel,Math.max(0,hits.length-1));pallist.innerHTML=hits.map(function(e,i){return '<li class=\"'+(i===sel?'sel':'')+'\" style=\"--tint:'+e.tint+'\"><a href=\"'+e.href+'\"><em>'+e.num+'</em><b>'+e.title+'</b><small>'+e.sub+'</small></a></li>'}).join('')||'<li><a><b>No match</b></a></li>'}function openPal(){pal.classList.add('on');palq.value='';sel=0;render();setTimeout(function(){palq.focus()},30)}function closePal(){pal.classList.remove('on')}"
    "palq.addEventListener('input',function(){sel=0;render()});palq.addEventListener('keydown',function(e){var n=pallist.querySelectorAll('li').length;if(e.key==='ArrowDown'){sel=(sel+1)%n;render();e.preventDefault()}else if(e.key==='ArrowUp'){sel=(sel-1+n)%n;render();e.preventDefault()}else if(e.key==='Enter'){var a=pallist.querySelector('li.sel a');if(a&&a.getAttribute('href')){closePal();location.hash=a.getAttribute('href')}}else if(e.key==='Escape'){closePal()}});pal.addEventListener('click',function(e){if(e.target===pal)closePal();var a=e.target.closest('a');if(a&&a.getAttribute('href')){closePal()}});document.addEventListener('keydown',function(e){if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='k'){e.preventDefault();pal.classList.contains('on')?closePal():openPal()}});var ts=document.querySelector('.tsearch');if(ts&&matchMedia('(max-width:899px)').matches){ts.addEventListener('click',openPal)} "
    "var spot=document.getElementById('spot');if(spot&&!rm&&matchMedia('(hover:hover)').matches){var heroEl=document.querySelector('.hero');heroEl.addEventListener('mousemove',function(e){var r=heroEl.getBoundingClientRect();spot.style.left=(e.clientX-r.left)+'px';spot.style.top=(e.clientY-r.top)+'px'})} "
    "var shotsAll=[].slice.call(document.querySelectorAll('main .shot img'));function drift(){if(rm)return;shotsAll.forEach(function(im){var r=im.getBoundingClientRect();if(r.top<innerHeight&&r.bottom>0){im.style.transform='translateY('+((r.top+r.height/2-innerHeight/2)/innerHeight*-10)+'px)'}})}window.addEventListener('scroll',drift,{passive:true});drift(); "
    "var lb=document.createElement('div');")
rep("article:hover .shot img{transform:scale(1.015)}", "article:hover .shot img{filter:saturate(1.05)}")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
