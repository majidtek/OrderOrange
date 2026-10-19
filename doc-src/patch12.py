import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()
def rep(a, b, n=1):
    global s
    assert s.count(a) == n, (a[:70], s.count(a)); s = s.replace(a, b)

# lightbox: proper zoom + pan (wheel, pinch, drag, double-tap), with +/−/reset controls
a = s.index(".lb{position:fixed;inset:0;background:rgba(20,14,9,.86)")
b = s.index(".lb .cap{")
b = s.index("\n", b) + 1
s = s[:a] + """.lb{position:fixed;inset:0;background:rgba(20,14,9,.9);backdrop-filter:blur(6px);z-index:20;display:none;overflow:hidden;touch-action:none;user-select:none}
.lb.on{display:block}
.lb .stage2{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;cursor:grab}
.lb .stage2.drag{cursor:grabbing}
.lb img{max-width:96vw;max-height:88vh;border-radius:12px;box-shadow:0 30px 80px rgba(0,0,0,.5);background:#fff;transform-origin:0 0;will-change:transform;pointer-events:none}
.lb .x{position:fixed;top:14px;right:14px;width:40px;height:40px;border-radius:50%;background:#fff;color:var(--ink);border:0;font:800 18px/40px Manrope,sans-serif;cursor:pointer;z-index:2}
.lb .ctl{position:fixed;left:50%;bottom:16px;transform:translateX(-50%);display:flex;gap:6px;background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.2);border-radius:999px;padding:6px;z-index:2;backdrop-filter:blur(8px)}
.lb .ctl button{width:38px;height:38px;border-radius:50%;border:0;background:#fff;color:var(--ink);font:800 16px Manrope,sans-serif;cursor:pointer}
.lb .ctl button.w{width:auto;padding:0 14px;font-size:12.5px}
.lb .cap{position:fixed;left:0;right:0;top:18px;text-align:center;color:#fff;font:700 13px Manrope,sans-serif;opacity:.85;pointer-events:none;padding:0 70px}
""" + s[b:]

old_js_a = s.index("var lb=document.createElement('div');")
old_js_b = s.index("document.addEventListener('keydown',function(e){if(e.key==='Escape')close()});})();")
new_js = ("var lb=document.createElement('div');lb.className='lb';lb.innerHTML='<div class=\\\"stage2\\\"><img alt=\\\"\\\"></div><button class=\\\"x\\\" aria-label=\\\"Close\\\">×</button><div class=\\\"cap\\\"></div><div class=\\\"ctl\\\"><button data-z=\\\"-\\\" aria-label=\\\"Zoom out\\\">−</button><button data-z=\\\"0\\\" class=\\\"w\\\">Fit</button><button data-z=\\\"+\\\" aria-label=\\\"Zoom in\\\">+</button></div>';document.body.appendChild(lb);"
          "var st=lb.querySelector('.stage2'),li=lb.querySelector('img'),cap=lb.querySelector('.cap');var Z={s:1,x:0,y:0,base:1,w:0,h:0};"
          "function apply(){li.style.transform='translate('+Z.x+'px,'+Z.y+'px) scale('+Z.s+')'}"
          "function fit(){var vw=innerWidth,vh=innerHeight;var w=li.naturalWidth||1,h=li.naturalHeight||1;Z.base=Math.min((vw*.96)/w,(vh*.88)/h,1);li.style.maxWidth='none';li.style.maxHeight='none';li.style.width=w+'px';li.style.height=h+'px';Z.w=w;Z.h=h;Z.s=Z.base;Z.x=(vw-w*Z.s)/2;Z.y=(vh-h*Z.s)/2;apply()}"
          "function clampZ(){var vw=innerWidth,vh=innerHeight,W=Z.w*Z.s,H=Z.h*Z.s;if(W<=vw)Z.x=(vw-W)/2;else Z.x=Math.min(0,Math.max(vw-W,Z.x));if(H<=vh)Z.y=(vh-H)/2;else Z.y=Math.min(0,Math.max(vh-H,Z.y))}"
          "function zoomAt(f,cx,cy){var ns=Math.min(Math.max(Z.base,Z.s*f),Z.base*6);if(cx==null){cx=innerWidth/2;cy=innerHeight/2}Z.x=cx-(cx-Z.x)*(ns/Z.s);Z.y=cy-(cy-Z.y)*(ns/Z.s);Z.s=ns;clampZ();apply()}"
          "function close(){lb.classList.remove('on');document.body.style.overflow=''}"
          "function openLb(im){li.src=im.src;cap.textContent=im.alt;lb.classList.add('on');document.body.style.overflow='hidden';if(li.complete&&li.naturalWidth)fit();else li.onload=fit}"
          "[].slice.call(document.querySelectorAll('.shot img')).forEach(function(im){im.addEventListener('click',function(){openLb(im)})});"
          "lb.querySelector('.x').addEventListener('click',close);lb.querySelector('.ctl').addEventListener('click',function(e){var b=e.target.closest('button');if(!b)return;var z=b.getAttribute('data-z');if(z==='+')zoomAt(1.5);else if(z==='-')zoomAt(1/1.5);else fit()});"
          "st.addEventListener('wheel',function(e){e.preventDefault();zoomAt(e.deltaY<0?1.15:1/1.15,e.clientX,e.clientY)},{passive:false});"
          "var ptrs={},drag=null,lastTap=0,pinch=null;st.addEventListener('pointerdown',function(e){ptrs[e.pointerId]={x:e.clientX,y:e.clientY};st.setPointerCapture(e.pointerId);var ks=Object.keys(ptrs);if(ks.length===1){drag={x:e.clientX,y:e.clientY,ox:Z.x,oy:Z.y,moved:false};st.classList.add('drag')}else if(ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];pinch={d:Math.hypot(a.x-b.x,a.y-b.y),s:Z.s,cx:(a.x+b.x)/2,cy:(a.y+b.y)/2};drag=null}});"
          "st.addEventListener('pointermove',function(e){if(!ptrs[e.pointerId])return;ptrs[e.pointerId]={x:e.clientX,y:e.clientY};var ks=Object.keys(ptrs);if(pinch&&ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];var d=Math.hypot(a.x-b.x,a.y-b.y);var f=(d/pinch.d)*pinch.s/Z.s;zoomAt(f,pinch.cx,pinch.cy)}else if(drag){var dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>4)drag.moved=true;Z.x=drag.ox+dx;Z.y=drag.oy+dy;clampZ();apply()}});"
          "function up(e){delete ptrs[e.pointerId];st.classList.remove('drag');if(Object.keys(ptrs).length<2)pinch=null;if(drag&&!drag.moved){var now=Date.now();if(now-lastTap<320){if(Z.s>Z.base*1.05)fit();else zoomAt(2.5,e.clientX,e.clientY)}else if(Z.s<=Z.base*1.05&&e.pointerType==='mouse'){zoomAt(2,e.clientX,e.clientY)}lastTap=now}drag=null}"
          "st.addEventListener('pointerup',up);st.addEventListener('pointercancel',up);"
          "window.addEventListener('resize',function(){if(lb.classList.contains('on'))fit()});"
          "document.addEventListener('keydown',function(e){if(!lb.classList.contains('on'))return;if(e.key==='Escape')close();if(e.key==='+'||e.key==='=')zoomAt(1.5);if(e.key==='-')zoomAt(1/1.5);if(e.key==='0')fit()});})();")
s = s[:old_js_a] + new_js + s[old_js_b + len("document.addEventListener('keydown',function(e){if(e.key==='Escape')close()});})();"):]
# the old close-on-backdrop click is gone (the stage is the pan surface); the × button and Esc close it
rep(".shot img{cursor:zoom-in}", ".shot img{cursor:zoom-in}\n.lb .cap small{display:block;font-weight:600;opacity:.7;margin-top:2px}")
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
