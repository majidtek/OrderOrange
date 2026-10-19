import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# A real tree: everything open, dashed connector lines, chevron toggles only, current screen marked
old_start = s.index(".tree{list-style:none;margin:12px 0 0;padding:0;display:grid;gap:2px}")
old_end = s.index(".tree .hide{display:none!important}")
s = s[:old_start] + """.tree{list-style:none;margin:12px 0 0;padding:0;display:grid;gap:1px}
.tree ul{list-style:none;margin:2px 0 4px 17px;padding:0 0 0 14px;border-left:1.5px dashed color-mix(in srgb,var(--tint) 35%,var(--line));display:grid;gap:1px}
.tn.closed > ul{display:none}
.tn-h{display:flex;align-items:center;gap:8px;text-decoration:none;padding:7px 8px;border-radius:11px;color:var(--ink);font:700 13px Manrope,sans-serif;transition:background .15s,color .15s}
.tn-h em{font-style:normal;width:10px;height:10px;border-radius:3px;background:var(--tint);flex:none;box-shadow:0 0 0 3px color-mix(in srgb,var(--tint) 18%,#fff);font-size:0}
.tn-h span{flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.tn-h b{font:800 10.5px Manrope,sans-serif;color:var(--muted);background:var(--bg-2);border-radius:999px;padding:3px 7px}
.tn-h .tw{width:22px;height:22px;border-radius:7px;display:grid;place-items:center;flex:none;color:var(--muted);transition:background .15s}
.tn-h .tw::before{content:"";width:6px;height:6px;border-right:2px solid currentColor;border-bottom:2px solid currentColor;transform:rotate(45deg) translate(-1px,-1px);transition:transform .2s}
.tn.closed > .tn-h .tw::before{transform:rotate(-45deg) translate(-1px,1px)}
.tn-h .tw:hover{background:var(--bg-2);color:var(--ink)}
.tn-h:hover,.tn.on > .tn-h{background:color-mix(in srgb,var(--tint) 9%,#fff);color:var(--tint)}
.tree ul a{display:flex;align-items:center;gap:8px;text-decoration:none;font:600 12.5px/1.3 Manrope,sans-serif;color:var(--ink-2);padding:5px 8px;border-radius:9px;position:relative;transition:background .15s,color .15s}
.tree ul a::before{content:"";position:absolute;left:-14px;top:50%;width:10px;height:1.5px;background:color-mix(in srgb,var(--tint) 35%,var(--line))}
.tree ul a::after{content:"";width:5px;height:5px;border-radius:50%;background:var(--line);flex:none;order:-1;transition:background .15s,transform .15s}
.tree ul a:hover,.tree ul a.on{color:var(--tint);background:color-mix(in srgb,var(--tint) 8%,#fff)}
.tree ul a.on::after{background:var(--tint);transform:scale(1.4)}
.tree ul a.on{font-weight:800}
""" + s[old_end:]
# chevron toggles; clicking the chapter title navigates
rep("if(tree){[].slice.call(tree.querySelectorAll('.tn-h')).forEach(function(h){h.addEventListener('click',function(e){var li=h.parentElement;if(li.querySelector('ul')){var wasOpen=li.classList.contains('open');[].slice.call(tree.querySelectorAll('.tn.open')).forEach(function(o){if(o!==li)o.classList.remove('open')});li.classList.toggle('open',!wasOpen)}})});",
    "if(tree){[].slice.call(tree.querySelectorAll('.tn-h .tw')).forEach(function(tw){tw.addEventListener('click',function(e){e.preventDefault();e.stopPropagation();tw.closest('.tn').classList.toggle('closed')})});")
rep("li.classList.toggle('open',!!q&&hit)})", "if(q&&hit)li.classList.remove('closed')})")
rep("tops.forEach(function(li,k){li.classList.toggle('on',k===cur);if(k===cur&&!tq.value)li.classList.add('open')})", "tops.forEach(function(li,k){li.classList.toggle('on',k===cur)})")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
