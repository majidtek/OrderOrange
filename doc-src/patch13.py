import io, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
p = 'gen3.py'; s = io.open(p, encoding='utf-8').read()

# Lightbox v3: the image is sized by width and the stage scrolls natively (phones pan with one finger,
# desktop drags with the mouse or uses the scrollbars); pinch and wheel change the width around the focal point.
a = s.index(".lb{position:fixed;inset:0;background:rgba(20,14,9,.9)")
b = s.index(".lb .cap{")
b = s.index("\n", b) + 1
s = s[:a] + """.lb{position:fixed;inset:0;background:rgba(20,14,9,.92);backdrop-filter:blur(6px);z-index:20;display:none}
.lb.on{display:block}
.lb .stage2{position:absolute;inset:0;overflow:auto;touch-action:pan-x pan-y;cursor:grab;-webkit-overflow-scrolling:touch;scrollbar-width:thin;scrollbar-color:rgba(255,255,255,.35) transparent}
.lb .stage2.drag{cursor:grabbing}
.lb .box{min-width:100%;min-height:100%;display:flex;align-items:center;justify-content:center;padding:70px 16px 80px;box-sizing:border-box}
.lb img{display:block;border-radius:10px;box-shadow:0 30px 80px rgba(0,0,0,.5);background:#fff;max-width:none;flex:none;-webkit-user-drag:none;user-select:none}
.lb .x{position:fixed;top:14px;right:14px;width:40px;height:40px;border-radius:50%;background:#fff;color:var(--ink);border:0;font:800 18px/40px Manrope,sans-serif;cursor:pointer;z-index:2}
.lb .ctl{position:fixed;left:50%;bottom:16px;transform:translateX(-50%);display:flex;gap:6px;background:rgba(255,255,255,.12);border:1px solid rgba(255,255,255,.2);border-radius:999px;padding:6px;z-index:2;backdrop-filter:blur(8px)}
.lb .ctl button{width:38px;height:38px;border-radius:50%;border:0;background:#fff;color:var(--ink);font:800 16px Manrope,sans-serif;cursor:pointer}
.lb .ctl button.w{width:auto;padding:0 14px;font-size:12.5px}
.lb .cap{position:fixed;left:0;right:0;top:18px;text-align:center;color:#fff;font:700 13px Manrope,sans-serif;opacity:.85;pointer-events:none;padding:0 70px;z-index:2}
""" + s[b:]

old_js_a = s.index("var lb=document.createElement('div');")
end_marker = "if(e.key==='0')fit()});})();"
old_js_b = s.index(end_marker)
new_js = ("var lb=document.createElement('div');lb.className='lb';lb.innerHTML='<div class=\\\"stage2\\\"><div class=\\\"box\\\"><img alt=\\\"\\\"></div></div><button class=\\\"x\\\" aria-label=\\\"Close\\\">×</button><div class=\\\"cap\\\"></div><div class=\\\"ctl\\\"><button data-z=\\\"-\\\" aria-label=\\\"Zoom out\\\">−</button><button data-z=\\\"0\\\" class=\\\"w\\\">Fit</button><button data-z=\\\"+\\\" aria-label=\\\"Zoom in\\\">+</button></div>';document.body.appendChild(lb);"
          "var st=lb.querySelector('.stage2'),li=lb.querySelector('img'),cap=lb.querySelector('.cap');var Z={s:1,base:1,w:1000,h:800};"
          "function setW(){li.style.width=Math.round(Z.w*Z.s)+'px';li.style.height='auto'}"
          "function fit(){Z.w=li.naturalWidth||1000;Z.h=li.naturalHeight||800;Z.base=Math.min((st.clientWidth-32)/Z.w,(st.clientHeight-150)/Z.h,1);Z.s=Z.base;setW();st.scrollLeft=0;st.scrollTop=0}"
          "function zoomAt(f,cx,cy){var ns=Math.min(Math.max(Z.base,Z.s*f),Z.base*6);if(ns===Z.s)return;var r=li.getBoundingClientRect();if(cx==null){cx=innerWidth/2;cy=innerHeight/2}var px=(cx-r.left)/r.width,py=(cy-r.top)/r.height;Z.s=ns;setW();var r2=li.getBoundingClientRect();st.scrollLeft+=(r2.left+px*r2.width)-cx;st.scrollTop+=(r2.top+py*r2.height)-cy}"
          "function close(){lb.classList.remove('on');document.body.style.overflow=''}"
          "function openLb(im){li.src=im.src;cap.textContent=im.alt+' · pinch or scroll to zoom, drag to move';lb.classList.add('on');document.body.style.overflow='hidden';if(li.complete&&li.naturalWidth)fit();else li.onload=fit}"
          "[].slice.call(document.querySelectorAll('.shot img')).forEach(function(im){im.addEventListener('click',function(){openLb(im)})});"
          "lb.querySelector('.x').addEventListener('click',close);lb.querySelector('.ctl').addEventListener('click',function(e){var b=e.target.closest('button');if(!b)return;var z=b.getAttribute('data-z');if(z==='+')zoomAt(1.5);else if(z==='-')zoomAt(1/1.5);else fit()});"
          "st.addEventListener('wheel',function(e){if(e.ctrlKey||Z.s<=Z.base||Math.abs(e.deltaY)>=Math.abs(e.deltaX)){e.preventDefault();zoomAt(e.deltaY<0?1.15:1/1.15,e.clientX,e.clientY)}},{passive:false});"
          "var ptrs={},drag=null,pinch=null,lastTap=0;"
          "st.addEventListener('pointerdown',function(e){ptrs[e.pointerId]={x:e.clientX,y:e.clientY};var ks=Object.keys(ptrs);if(ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];pinch={d:Math.hypot(a.x-b.x,a.y-b.y),s:Z.s};drag=null}else if(e.pointerType==='mouse'){drag={x:e.clientX,y:e.clientY,sl:st.scrollLeft,stp:st.scrollTop,moved:false};st.classList.add('drag');st.setPointerCapture(e.pointerId)}});"
          "st.addEventListener('pointermove',function(e){if(!ptrs[e.pointerId])return;ptrs[e.pointerId]={x:e.clientX,y:e.clientY};var ks=Object.keys(ptrs);if(pinch&&ks.length===2){var a=ptrs[ks[0]],b=ptrs[ks[1]];var d=Math.hypot(a.x-b.x,a.y-b.y);var target=pinch.s*(d/pinch.d);zoomAt(target/Z.s,(a.x+b.x)/2,(a.y+b.y)/2)}else if(drag){var dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.abs(dx)+Math.abs(dy)>4)drag.moved=true;st.scrollLeft=drag.sl-dx;st.scrollTop=drag.stp-dy}});"
          "function up(e){delete ptrs[e.pointerId];st.classList.remove('drag');if(Object.keys(ptrs).length<2)pinch=null;if(drag&&!drag.moved){var now=Date.now();if(now-lastTap<320){if(Z.s>Z.base*1.05)fit();else zoomAt(2.5,e.clientX,e.clientY)}lastTap=now}drag=null}"
          "st.addEventListener('pointerup',up);st.addEventListener('pointercancel',up);"
          "var tapT=0;st.addEventListener('touchend',function(e){if(e.touches.length)return;var now=Date.now();if(now-tapT<320&&e.changedTouches.length===1){var t=e.changedTouches[0];if(Z.s>Z.base*1.05)fit();else zoomAt(2.5,t.clientX,t.clientY);e.preventDefault()}tapT=now},{passive:false});"
          "window.addEventListener('resize',function(){if(lb.classList.contains('on'))fit()});"
          "document.addEventListener('keydown',function(e){if(!lb.classList.contains('on'))return;if(e.key==='Escape')close();if(e.key==='+'||e.key==='=')zoomAt(1.5);if(e.key==='-')zoomAt(1/1.5);if(e.key==='0')fit()});})();")
s = s[:old_js_a] + new_js + s[old_js_b + len(end_marker):]
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('patched')
